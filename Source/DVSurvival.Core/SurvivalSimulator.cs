using System;
using DVSurvival.Core.Simulation;

namespace DVSurvival.Core
{
    // Compatibility facade for native adapters and old saves. New rules live in Simulation/.
    public static class SurvivalSimulator
    {
        public static void Advance(SurvivalState state, SurvivalEnvironment environment, SurvivalTuning tuning, float realSeconds = -1)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            NeedsEngine.Advance(state, new EnvironmentFrame(environment), new SimulationRules(tuning), realSeconds);
        }
        public static SurvivalResultCode Sleep(SurvivalState state, float hours, SurvivalEnvironment environment, SurvivalTuning tuning)
        {
            if (environment == null || tuning == null) return SurvivalResultCode.InvalidRequest;
            return NeedsEngine.Sleep(state, hours, new EnvironmentFrame(environment), new SimulationRules(tuning));
        }
        public static SurvivalResultCode Consume(SurvivalState state, ProvisionKind provision, SurvivalTuning tuning) =>
            tuning == null ? SurvivalResultCode.InvalidRequest : ProvisionSystem.Use(state, provision, new SimulationRules(tuning), false);
        public static SurvivalResultCode ConsumePhysical(SurvivalState state, ProvisionKind provision, SurvivalTuning tuning,
            SurvivalEnvironment environment = null) =>
            tuning == null ? SurvivalResultCode.InvalidRequest : ProvisionSystem.Use(state, provision, new SimulationRules(tuning), true);
        public static SurvivalResultCode ApplyTrauma(SurvivalState state, TraumaKind kind, float magnitude, float playerHeightMeters, SurvivalTuning tuning) =>
            tuning == null ? SurvivalResultCode.InvalidRequest : TraumaSystem.Apply(state, kind, magnitude, playerHeightMeters, new SimulationRules(tuning));
        public static float CalculateTraumaDamage(TraumaKind kind, float magnitude, float playerHeightMeters) =>
            TraumaSystem.Damage(kind, magnitude, playerHeightMeters);
        public static float GetMovementMultiplier(SurvivalState state) => VitalitySystem.Mobility(state);
        public static float GetCoffeeRestMultiplier(SurvivalState state) => CoffeeTolerance.NextRestMultiplier(state == null ? 0 : state.CoffeeUsesSinceSleep);
        public static bool CanRun(SurvivalState state) => state == null ||
            (state.Health >= 40 && state.Hunger >= 25 && state.Hydration >= 30 && state.Rest >= 30);
        public static float GetApparentTemperature(SurvivalState state, SurvivalEnvironment environment, SurvivalTuning tuning) =>
            state == null || environment == null || tuning == null ? 18 : new ThermalField(new EnvironmentFrame(environment), state.WarmthHours > 0).Apparent;
    }
}
