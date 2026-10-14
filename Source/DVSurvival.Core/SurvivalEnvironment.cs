using System;

namespace DVSurvival.Core
{
    [Serializable]
    public sealed class SurvivalEnvironment
    {
        public const float MinimumAirTemperature = -100f;
        public const float MaximumAirTemperature = 1500f;
        public float GameHours;
        public float AmbientTemperatureCelsius = 18f;
        public float RainIntensity;
        public float WindSpeedMetersPerSecond;
        public float Exposure = 1f;
        public float Activity;
        public float ShelterWarmthCelsius;
        public float ThermalRecoveryMultiplier = 1f;
        public bool IsWinter;

        public SurvivalEnvironment Clone()
        {
            var copy = new SurvivalEnvironment();
            CopyTo(copy);
            return copy;
        }

        public void CopyTo(SurvivalEnvironment target)
        {
            target.GameHours = GameHours;
            target.AmbientTemperatureCelsius = AmbientTemperatureCelsius;
            target.RainIntensity = RainIntensity;
            target.WindSpeedMetersPerSecond = WindSpeedMetersPerSecond;
            target.Exposure = Exposure;
            target.Activity = Activity;
            target.ShelterWarmthCelsius = ShelterWarmthCelsius;
            target.ThermalRecoveryMultiplier = ThermalRecoveryMultiplier;
            target.IsWinter = IsWinter;
        }

        public bool IsValid()
        {
            return IsFiniteInRange(GameHours, 0f, 24f) &&
                IsFiniteInRange(AmbientTemperatureCelsius, MinimumAirTemperature, MaximumAirTemperature) &&
                IsFiniteInRange(RainIntensity, 0f, 1f) &&
                IsFiniteInRange(WindSpeedMetersPerSecond, 0f, 80f) &&
                IsFiniteInRange(Exposure, 0f, 1f) &&
                IsFiniteInRange(Activity, 0f, 1f) &&
                IsFiniteInRange(ShelterWarmthCelsius, -20f, 30f) &&
                IsFiniteInRange(ThermalRecoveryMultiplier, 0.25f, 5f);
        }

        public void Clamp()
        {
            GameHours = ClampFinite(GameHours, 0f, 24f, 0f);
            AmbientTemperatureCelsius = ClampFinite(AmbientTemperatureCelsius, MinimumAirTemperature, MaximumAirTemperature, 18f);
            RainIntensity = ClampFinite(RainIntensity, 0f, 1f, 0f);
            WindSpeedMetersPerSecond = ClampFinite(WindSpeedMetersPerSecond, 0f, 80f, 0f);
            Exposure = ClampFinite(Exposure, 0f, 1f, 1f);
            Activity = ClampFinite(Activity, 0f, 1f, 0f);
            ShelterWarmthCelsius = ClampFinite(ShelterWarmthCelsius, -20f, 30f, 0f);
            ThermalRecoveryMultiplier = ClampFinite(ThermalRecoveryMultiplier, 0.25f, 5f, 1f);
        }

        private static bool IsFiniteInRange(float value, float min, float max)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
        }

        private static float ClampFinite(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
