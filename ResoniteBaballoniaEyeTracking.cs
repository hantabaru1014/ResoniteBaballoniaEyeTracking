using Elements.Core;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using Rug.Osc;
using System.Reflection.Emit;

namespace ResoniteBaballoniaEyeTracking;

public class ResoniteBaballoniaEyeTracking : ResoniteMod
{
    public override string Name => "ResoniteBaballoniaEyeTracking";
    public override string Author => "hantabaru1014";
    public override string Version => "0.2.1";
    public override string Link => "https://github.com/hantabaru1014/ResoniteBaballoniaEyeTracking";

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> ModEnabledKey = new("Enabled", "Mod Enabled", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<float> AlphaKey = new("Alpha", "Eye Swing Multiplier X", () => 1.0f);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<float> BetaKey = new("Beta", "Eye Swing Multiplier Y", () => 1.0f);

    private static ModConfiguration? _config;

    public override void OnEngineInit()
    {
        _config = GetConfiguration();

        new Harmony("net.hantabaru1014.ResoniteBaballoniaEyeTracking").PatchAll();
    }
    
    [Harmony]
    public class BabbleOSC_Driver_Patches
    {
        [HarmonyPatch(typeof(BabbleOSC_Driver), "get_ShouldInitialize")]
        [HarmonyPostfix]
        public static void Postfix_ShouldInitialize(ref bool __result)
        {
            if (_config?.GetValue(ModEnabledKey) ?? false)
            {
                __result = true;
            }
        }

        [HarmonyPatch(typeof(BabbleOSC_Driver), "RegisterInputs")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile_RegisterInputs(IEnumerable<CodeInstruction> original)
        {
            List<CodeInstruction> instructions = new List<CodeInstruction>(original);

            int start = instructions.FindIndex(o => o.opcode == OpCodes.Stfld);

            instructions.InsertRange(start, new CodeInstruction[]{
                new CodeInstruction(OpCodes.Ldarg_1,null),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BaballoniaEyeDriver), "RegisterInputs2"))
            });

            return instructions;
        }

        [HarmonyPatch(typeof(BabbleOSC_Driver), "UpdateInputs")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpile_UpdateInputs(IEnumerable<CodeInstruction> original)
        {
            List<CodeInstruction> instructions = new List<CodeInstruction>(original);

            instructions.InsertRange(0, new CodeInstruction[]{
                new CodeInstruction(OpCodes.Ldarg_1,null),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BaballoniaEyeDriver), "UpdateInputs2"))
            });

            return instructions;
        }

        [HarmonyPatch(typeof(BabbleOSC_Driver), "UpdateData")]
        [HarmonyPrefix]
        public static bool Prefix_UpdateData(OscMessage message)
        {
            if (string.IsNullOrEmpty(message.Address)) return true;

            switch (message.Address)
            {
                case "/LeftEyeX":
                case "/LeftEyeY":
                case "/RightEyeX":
                case "/RightEyeY":
                case "/LeftEyeLid":
                case "/RightEyeLid":
                    BaballoniaEyeDriver.UpdateData2(message);
                    return false; // Skip original method for eye-related messages
                default:
                    return true; // Run original method for mouth-related messages
            }
        }
    }

    public class BaballoniaEyeDriver : OSC_Driver
    {
        private static Eyes? _eyes;

        private static float _leftEyePosX = 0f;
        private static float _leftEyePosY = 0f;
        private static float _rightEyePosX = 0f;
        private static float _rightEyePosY = 0f;
        private static float _leftEyeLid = 0f;
        private static float _rightEyeLid = 0f;

        public override int UpdateOrder => throw new NotImplementedException();

        public static void RegisterInputs2(InputInterface inputInterface)
        {
            _eyes = new Eyes(inputInterface, "Project Babble", false);
        }

        public static void UpdateInputs2(float deltaTime)
        {
            if (_eyes is null) return;
            if (!Engine.Current.InputInterface.VR_Active || !(_config?.GetValue(ModEnabledKey) ?? false))
            {
                _eyes.IsEyeTrackingActive = false;
                return;
            }

            _eyes.IsEyeTrackingActive = true;

            var leftEyeDirection = Project2DTo3D(_leftEyePosX, _leftEyePosY);
            UpdateEye(_eyes.LeftEye, true, leftEyeDirection, _leftEyeLid);
            var rightEyeDirection = Project2DTo3D(_rightEyePosX, _rightEyePosY);
            UpdateEye(_eyes.RightEye, true, rightEyeDirection, _rightEyeLid);
            var combinedDirection = MathX.Average(leftEyeDirection, rightEyeDirection);
            var combinedOpenness = MathX.Average(_leftEyeLid, _rightEyeLid);
            UpdateEye(_eyes.CombinedEye, true, combinedDirection, combinedOpenness);
            _eyes.ComputeCombinedEyeParameters();

            _eyes.ConvergenceDistance = 0f;
            _eyes.Timestamp += deltaTime;
            _eyes.FinishUpdate();
        }

        public static void UpdateEye(Eye eye, bool isTracking, float3 gazeDirection, float openness)
        {
            eye.IsDeviceActive = isTracking;
            eye.IsTracking = isTracking;

            if (isTracking)
            {
                eye.UpdateWithDirection(gazeDirection);
                eye.RawPosition = float3.Zero;
            }

            eye.Openness = openness;
            eye.Squeeze = 0f;
            eye.Frown = 0f;
        }

        public static void UpdateData2(OscMessage message)
        {
            if (string.IsNullOrEmpty(message.Address)) return;

            switch (message.Address)
            {
                case "/LeftEyeX":
                    _leftEyePosX = OSC_Driver.ReadFloat(message);
                    break;
                case "/LeftEyeY":
                    _leftEyePosY = OSC_Driver.ReadFloat(message);
                    break;
                case "/RightEyeX":
                    _rightEyePosX = OSC_Driver.ReadFloat(message);
                    break;
                case "/RightEyeY":
                    _rightEyePosY = OSC_Driver.ReadFloat(message);
                    break;
                case "/LeftEyeLid":
                    _leftEyeLid = OSC_Driver.ReadFloat(message);
                    break;
                case "/RightEyeLid":
                    _rightEyeLid = OSC_Driver.ReadFloat(message);
                    break;
            }
        }

        public static float3 Project2DTo3D(float x, float y)
        {
            var alpha = _config?.GetValue(AlphaKey) ?? 1f;
            var beta = _config?.GetValue(BetaKey) ?? 1f;
            return new float3(MathX.Tan(alpha * x), MathX.Tan(beta * y), 1f).Normalized;
        }

        public override void CollectDeviceInfos(DataTreeList list)
        {
            throw new NotImplementedException();
        }

        public override void UpdateInputs(float deltaTime)
        {
            throw new NotImplementedException();
        }

        protected override void UpdateData(OscMessage message)
        {
            throw new NotImplementedException();
        }
    }
}
