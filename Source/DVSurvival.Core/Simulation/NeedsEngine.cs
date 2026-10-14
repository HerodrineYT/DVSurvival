using System;

namespace DVSurvival.Core.Simulation
{
    // Deterministic pipeline: no Unity, time reads, IO, hidden queues or temporary reference objects.
    public static class NeedsEngine
    {
        public static void Advance(SurvivalState state, in EnvironmentFrame world, in SimulationRules rules, float realSeconds = -1)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            state.Clamp();
            float remaining = world.Hours;
            bool live = realSeconds >= 0 && Numbers.Finite(realSeconds);
            if (remaining <= 0)
            {
                if (!live || realSeconds <= 0) return;
                var field = new ThermalField(world, state.WarmthHours > 0);
                float body = ThermalSystem.Awake(state, field, world, rules, 0, realSeconds);
                VitalitySystem.Integrate(state, rules, 0, body, world.Air, true, realSeconds / 60);
                VitalitySystem.CommitStep(state, 0);
                return;
            }
            while (remaining > 0)
            {
                float step = rules.ConsumptionDisabled
                    ? (SleepDeprivationEffects.HasExhaustion(state) ? Math.Min(remaining, state.ExhaustionHoursRemaining) : remaining)
                    : SleepDeprivationEffects.NextStep(state, remaining);
                var field = new ThermalField(world, state.WarmthHours > 0);
                float needs = rules.ConsumptionDisabled ? 0 : step * rules.NeedsRate;
                state.Hunger -= 100 / rules.HungerHours * needs * (.82f + world.Activity * .70f);
                state.Hydration -= 100 / rules.WaterHours * needs * (.82f + world.Activity * .82f + field.HeatStress * .75f) *
                    (SleepDeprivationEffects.HasExhaustion(state) ? 3 : 1);
                state.Rest -= 100 / rules.RestHours * needs * (.90f + world.Activity * .25f);
                state.CaffeineHours = Math.Max(0, state.CaffeineHours - step); state.WarmthHours = Math.Max(0, state.WarmthHours - step);
                float seconds = realSeconds >= 0 ? realSeconds * step / world.Hours : realSeconds;
                float body = ThermalSystem.Awake(state, field, world, rules, step, seconds);
                VitalitySystem.Integrate(state, rules, step, body, world.Air, true, live ? seconds / 60 : step);
                VitalitySystem.CommitStep(state, step);
                FatigueClock.Advance(state, step, world, !rules.ConsumptionDisabled);
                remaining = Math.Max(0, remaining - step);
            }
        }
        public static SurvivalResultCode Sleep(SurvivalState state, float hours, in EnvironmentFrame world, in SimulationRules rules)
        {
            if (state == null || !Numbers.Finite(hours) || hours < .1f || hours > 24) return SurvivalResultCode.InvalidRequest;
            state.Clamp(); bool wakeThirst = SleepDeprivationEffects.SleepTriggersThirst(state); float remaining = hours;
            while (remaining > 0)
            {
                float step = Math.Min(remaining, .25f);
                if (SleepDeprivationEffects.HasExhaustion(state)) step = Math.Min(step, state.ExhaustionHoursRemaining);
                float needs = rules.ConsumptionDisabled ? 0 : step * rules.NeedsRate;
                state.Hunger -= 100 / rules.HungerHours * needs * .52f;
                state.Hydration -= 100 / rules.WaterHours * needs * .58f * (SleepDeprivationEffects.HasExhaustion(state) ? 3 : 1);
                state.Rest += rules.RestPerHour * step; state.CaffeineHours = 0;
                var field = new ThermalField(world, state.WarmthHours > 0); ThermalSystem.Sleeping(state, field, rules, step);
                float body = state.BodyTemperatureCelsius;
                VitalitySystem.Integrate(state, rules, Math.Min(step, 12), body, world.Air, false);
                if (state.Hunger >= 15 && state.Hydration >= 20 && world.Air <= 55 && body >= 35 && body <= 38.5f) state.Health += step * 1.5f;
                VitalitySystem.CommitStep(state, step); FatigueClock.Advance(state, step, world, false);
                wakeThirst |= SleepDeprivationEffects.SleepTriggersThirst(state); remaining = Math.Max(0, remaining - step);
            }
            if (wakeThirst && !rules.ConsumptionDisabled) state.Hydration = Math.Min(10, state.Hydration);
            state.CoffeeUsesSinceSleep = 0; SleepDeprivationEffects.RecoverAfterFullSleep(state, hours); state.Clamp();
            return SurvivalResultCode.Success;
        }
    }
    internal static class FatigueClock
    {
        internal static void Advance(SurvivalState state, float hours, in EnvironmentFrame world, bool awake)
        {
            if (state.ExhaustionHoursRemaining > 0)
            { ApplyCold(state, world); state.ExhaustionHoursRemaining = Math.Max(0, state.ExhaustionHoursRemaining - hours); }
            if (!awake) return;
            double previous = state.LowRestGameHours; state.LowRestGameHours = Math.Min(144, previous + hours);
            if (previous >= 48 || state.LowRestGameHours < 48) return;
            state.ExhaustionHoursRemaining = 3; state.Hunger = Math.Min(10, state.Hunger);
            state.Hydration = Math.Min(10, state.Hydration); state.Rest = Math.Min(10, state.Rest);
            state.CaffeineHours = state.WarmthHours = 0; ApplyCold(state, world);
        }
        private static void ApplyCold(SurvivalState state, in EnvironmentFrame world)
        { if (world.Winter && world.Air < 0) state.BodyTemperatureCelsius = Math.Min(35, state.BodyTemperatureCelsius); }
    }
}
