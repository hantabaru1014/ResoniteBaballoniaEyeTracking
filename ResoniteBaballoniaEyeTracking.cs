using Elements.Core;
using FrooxEngine;
using ResoniteModLoader;
using Rug.Osc;

namespace ResoniteBaballoniaEyeTracking;

public class ResoniteBaballoniaEyeTracking : ResoniteMod
{
    public override string Name => "ResoniteBaballoniaEyeTracking";
    public override string Author => "hantabaru1014";
    public override string Version => "0.1.0";
    public override string Link => "https://github.com/hantabaru1014/ResoniteBaballoniaEyeTracking";

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<bool> ModEnabledKey = new("Enabled", "Mod Enabled", () => true);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<int> OscPortKey = new("OscPort", "OSC port", () => 8888);
    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<float> AlphaKey = new("Alpha", "Eye Swing Multiplier X", () => 1.0f);

    [AutoRegisterConfigKey]
    private static readonly ModConfigurationKey<float> BetaKey = new("Beta", "Eye Swing Multiplier Y", () => 1.0f);

    private static ModConfiguration? _config;

    public override void OnEngineInit()
    {
        _config = GetConfiguration();

        Engine.Current.RunPostInit(() =>
        {
            try
            {
                Engine.Current.InputInterface.RegisterInputDriver(new BaballoniaEyeOSC_Driver());
            }
            catch (Exception e)
            {
                Error($"Failed to register input driver: {e}");
            }
        });
    }

    public class BaballoniaEyeOSC_Driver : OSC_Driver
    {
        private Eyes? _eyes;

        private float _leftEyePosX = 0f;
        private float _leftEyePosY = 0f;
        private float _rightEyePosX = 0f;
        private float _rightEyePosY = 0f;
        private float _leftEyeLid = 0f;
        private float _rightEyeLid = 0f;

        public override int UpdateOrder => 100;

        public override void CollectDeviceInfos(DataTreeList list)
        {
            var dict = new DataTreeDictionary();
            dict.Add("Name", "Baballonia Eye Tracking");
            dict.Add("Type", "Eye Tracking");
            dict.Add("Model", "Baballonia");
            list.Add(dict);
        }

        public override void RegisterInputs(InputInterface inputInterface)
        {
            _eyes = new Eyes(inputInterface, "Baballonia Eye Tracking", false);
            SetPort(_config?.GetValue(OscPortKey) ?? 8888);
            base.RegisterInputs(inputInterface);
        }

        public override void UpdateInputs(float deltaTime)
        {
            if (_eyes is null) return;
            if (!Engine.Current.InputInterface.VR_Active || !(_config?.GetValue(ModEnabledKey) ?? false))
            {
                _eyes.IsEyeTrackingActive = false;
                return;
            }

            _eyes.IsEyeTrackingActive = true;

            var leftEyeOpenness = 1 - _leftEyeLid;
            var rightEyeOpenness = 1 - _rightEyeLid;
            var leftEyeDirection = Project2DTo3D(_leftEyePosX, _leftEyePosY);
            UpdateEye(_eyes.LeftEye, true, leftEyeDirection, leftEyeOpenness);
            var rightEyeDirection = Project2DTo3D(_rightEyePosX, _rightEyePosY);
            UpdateEye(_eyes.RightEye, true, rightEyeDirection, rightEyeOpenness);
            var combinedDirection = MathX.Average(leftEyeDirection, rightEyeDirection);
            var combinedOpenness = MathX.Average(leftEyeOpenness, rightEyeOpenness);
            UpdateEye(_eyes.CombinedEye, true, combinedDirection, combinedOpenness);
            _eyes.ComputeCombinedEyeParameters();

            _eyes.ConvergenceDistance = 0f;
            _eyes.Timestamp += deltaTime;
            _eyes.FinishUpdate();
        }

        private static void UpdateEye(Eye eye, bool isTracking, float3 gazeDirection, float openness)
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

        protected override void UpdateData(OscMessage message)
        {
            if (string.IsNullOrEmpty(message.Address)) return;

            switch (message.Address)
            {
                case "/LeftEyeX":
                    _leftEyePosX = ReadFloat(message);
                    break;
                case "/LeftEyeY":
                    _leftEyePosY = ReadFloat(message);
                    break;
                case "/RightEyeX":
                    _rightEyePosX = ReadFloat(message);
                    break;
                case "/RightEyeY":
                    _rightEyePosY = ReadFloat(message);
                    break;
                case "/LeftEyeLid":
                    _leftEyeLid = ReadFloat(message);
                    break;
                case "/RightEyeLid":
                    _rightEyeLid = ReadFloat(message);
                    break;
            }
        }
        
        private static float3 Project2DTo3D(float x, float y)
        {
            var alpha = _config?.GetValue(AlphaKey) ?? 1f;
            var beta = _config?.GetValue(BetaKey) ?? 1f;
            return new float3(MathX.Tan(alpha * x), MathX.Tan(beta * y), 1f).Normalized;
        }
    }
}
