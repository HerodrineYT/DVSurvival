using System;

namespace DVSurvival.Core.Simulation
{
    // Value inputs are detached from reusable world buffers; simulation never owns Unity objects.
    public readonly struct EnvironmentFrame
    {
        public readonly float Hours, Air, Rain, Wind, Exposure, Activity, Shelter, Recovery;
        public readonly bool Winter;
        public EnvironmentFrame(SurvivalEnvironment source)
        {
            Hours = Numbers.Range(source.GameHours, 0, 24, 0);
            Air = Numbers.Range(source.AmbientTemperatureCelsius, -100, 1500, 18);
            Rain = Numbers.Range(source.RainIntensity, 0, 1, 0);
            Wind = Numbers.Range(source.WindSpeedMetersPerSecond, 0, 80, 0);
            Exposure = Numbers.Range(source.Exposure, 0, 1, 1);
            Activity = Numbers.Range(source.Activity, 0, 1, 0);
            Shelter = Numbers.Range(source.ShelterWarmthCelsius, -20, 30, 0);
            Recovery = Numbers.Range(source.ThermalRecoveryMultiplier, .25f, 5, 1);
            Winter = source.IsWinter;
        }
    }
    public readonly struct SimulationRules
    {
        public readonly bool ConsumptionDisabled;
        public readonly float NeedsRate, DamageRate, HungerHours, WaterHours, RestHours, RestPerHour;
        public readonly float ThermalHours, ThermalSeconds, RecoveryPerHour, CoffeeHours, HeatPackHours;
        public SimulationRules(SurvivalTuning settings)
        {
            ConsumptionDisabled = settings.DisableNeedsConsumption;
            NeedsRate = Numbers.Range(settings.NeedsRateMultiplier, .1f, 5, 1);
            DamageRate = Numbers.Range(settings.DamageMultiplier, 0, 5, 1);
            HungerHours = Numbers.Range(settings.HungerHoursFromFull, 2, 96, 18);
            WaterHours = Numbers.Range(settings.HydrationHoursFromFull, 2, 72, 12);
            RestHours = Numbers.Range(settings.RestHoursFromFull, 2, 96, 18);
            RestPerHour = Numbers.Range(settings.RestoredPerSleepHour, 1, 30, 12.5f);
            ThermalHours = Numbers.Range(settings.ThermalTimeConstantHours, .1f, 12, 1.5f);
            ThermalSeconds = Numbers.Range(settings.ThermalTimeConstantSeconds, 10, 3600, 180);
            RecoveryPerHour = Numbers.Range(settings.PassiveHealthRecoveryPerHour, 0, 10, 1.25f);
            CoffeeHours = Numbers.Range(settings.CoffeeDurationHours, .25f, 24, 4);
            HeatPackHours = Numbers.Range(settings.HeatPackDurationHours, .25f, 24, 4);
        }
    }
    internal static class Numbers
    {
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static float Range(float value, float low, float high, float fallback) =>
            Finite(value) ? Math.Max(low, Math.Min(high, value)) : fallback;
        internal static float Deficit(float value, float threshold) => Math.Max(0, Math.Min(1, (threshold - value) / threshold));
        internal static float Relax(float value, float target, float step) => value + (target - value) * (1f - (float)Math.Exp(-step));
    }
}
