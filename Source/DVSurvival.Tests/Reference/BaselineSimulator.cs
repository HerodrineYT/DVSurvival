// Frozen 0.1.2 oracle captured before the rewrite; compiled only in tests, never shipped as runtime code.
using System;
using DVSurvival.Core;

namespace DVSurvival.Tests.Reference
{
    internal static class BaselineSimulator
    {
        private const float ThermalThresholdDamagePerHour = 1f;

        public static void Advance(SurvivalState state, SurvivalEnvironment environment, SurvivalTuning tuning,
            float realSeconds = -1f)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            state.Clamp();
            // Normal samples are valid, read-only inputs. Only invalid external inputs need an owned clamped copy.
            if (!environment.IsValid()) { environment = environment.Clone(); environment.Clamp(); }
            tuning.Clamp();
            var hours = environment.GameHours;
            if (hours <= 0f) return;

            var remaining = hours;
            while (remaining > 0f)
            {
                // Pausing the awake clock must also bypass its threshold step calculation:
                // near a boundary it would repeatedly produce the same tiny step.
                var step = tuning.DisableNeedsConsumption
                    ? (SleepDeprivationEffects.HasExhaustion(state)
                        ? Math.Min(remaining, state.ExhaustionHoursRemaining) : remaining)
                    : SleepDeprivationEffects.NextStep(state, remaining);
                AdvanceStep(state, environment, tuning, step, realSeconds >= 0 ? realSeconds * step / hours : realSeconds);
                if (tuning.DisableNeedsConsumption)
                    SleepDeprivationEffects.AdvanceSleepClock(state, step, environment);
                else
                    SleepDeprivationEffects.AdvanceClock(state, step, environment);
                remaining = Math.Max(0, remaining - step);
            }
        }

        private static void AdvanceStep(SurvivalState state, SurvivalEnvironment environment, SurvivalTuning tuning,
            float hours, float realSeconds)
        {

            var needsHours = tuning.DisableNeedsConsumption ? 0f : hours * tuning.NeedsRateMultiplier;
            var activity = environment.Activity;
            var thermal = ComputeThermal(environment, state, tuning);
            state.Hunger -= 100f / tuning.HungerHoursFromFull * needsHours * (0.82f + activity * 0.70f);
            state.Hydration -= 100f / tuning.HydrationHoursFromFull * needsHours *
                (0.82f + activity * 0.82f + thermal.HeatStress * 0.75f) * (SleepDeprivationEffects.HasExhaustion(state) ? 3f : 1f);
            state.Rest -= 100f / tuning.RestHoursFromFull * needsHours * (0.90f + activity * 0.25f);
            state.CaffeineHours = Math.Max(0f, state.CaffeineHours - hours);
            state.WarmthHours = Math.Max(0f, state.WarmthHours - hours);

            var previousBodyTemperature = state.BodyTemperatureCelsius;
            var thermalStep = realSeconds >= 0f && !float.IsInfinity(realSeconds)
                ? Math.Min(realSeconds, 30f) / tuning.ThermalTimeConstantSeconds
                : hours / tuning.ThermalTimeConstantHours;
            var alpha = 1f - (float)Math.Exp(-thermalStep * environment.ThermalRecoveryMultiplier);
            state.BodyTemperatureCelsius += (thermal.TargetCoreTemperature - state.BodyTemperatureCelsius) * alpha;
            var healthTemperature = state.BodyTemperatureCelsius;
            if (realSeconds >= 0f && !float.IsInfinity(realSeconds))
            {
                // A one-second cooling tick must not move an already visible 38.5 C just
                // below the boundary before damage is evaluated. Use the unsafe endpoint
                // for live ticks; lumped simulation still evaluates its final temperature.
                if (previousBodyTemperature >= 38.5f && previousBodyTemperature > healthTemperature)
                    healthTemperature = previousBodyTemperature;
                else if (previousBodyTemperature < 35f && previousBodyTemperature < healthTemperature)
                    healthTemperature = previousBodyTemperature;
            }
            ApplyHealthChange(state, hours, tuning, healthTemperature, environment.AmbientTemperatureCelsius);
            state.SimulatedGameHours += hours;
            state.Revision++;
            state.Clamp();
            RecoverAfterCollapse(state);
        }

        public static SurvivalResultCode ConsumePhysical(SurvivalState state, ProvisionKind provision, SurvivalTuning tuning,
            SurvivalEnvironment environment = null)
        {
            if (state == null || tuning == null || provision < ProvisionKind.Meal || provision > ProvisionKind.HeatPack)
                return SurvivalResultCode.InvalidRequest;
            var previous = state.GetProvisionCount(provision);
            state.TryChangeProvisionCount(provision, 1 - previous);
            var result = Consume(state, provision, tuning);
            state.TryChangeProvisionCount(provision, previous - state.GetProvisionCount(provision));
            return result;
        }

        public static SurvivalResultCode Consume(SurvivalState state, ProvisionKind provision,
            SurvivalTuning tuning)
        {
            if (state == null || tuning == null || provision == ProvisionKind.None)
                return SurvivalResultCode.InvalidRequest;
            state.Clamp();
            tuning.Clamp();
            if (state.GetProvisionCount(provision) <= 0) return SurvivalResultCode.NoStock;

            switch (provision)
            {
                case ProvisionKind.Meal:
                    if (state.Hunger >= 98f) return SurvivalResultCode.NotNeeded;
                    state.Hunger = Math.Min(100f, state.Hunger + 45f);
                    break;
                case ProvisionKind.Water:
                    if (state.Hydration >= 98f && state.BodyTemperatureCelsius <= 36f) return SurvivalResultCode.NotNeeded;
                    state.Hydration = Math.Min(100f, state.Hydration + 50f);
                    if (state.BodyTemperatureCelsius > 36f)
                        state.BodyTemperatureCelsius = Math.Max(36f, state.BodyTemperatureCelsius - 0.2f);
                    break;
                case ProvisionKind.Coffee:
                    if (state.Rest >= 98f && state.Hydration >= 98f && state.CaffeineHours > 0.5f && state.BodyTemperatureCelsius >= 37f)
                        return SurvivalResultCode.NotNeeded;
                    state.Rest = Math.Min(100f, state.Rest + 16f * GetCoffeeRestMultiplier(state));
                    CoffeeTolerance.RegisterCup(state);
                    state.Hydration = Math.Min(100f, state.Hydration + 15f);
                    state.CaffeineHours = Math.Max(state.CaffeineHours, tuning.CoffeeDurationHours);
                    if (state.BodyTemperatureCelsius < 37f)
                        state.BodyTemperatureCelsius = Math.Min(37f, state.BodyTemperatureCelsius + 0.2f);
                    break;
                case ProvisionKind.FirstAid:
                    if (state.Health >= 99f || state.FirstAidSecondsRemaining > 0f) return SurvivalResultCode.NotNeeded;
                    state.FirstAidSecondsRemaining = FirstAidRecovery.DurationSeconds;
                    break;
                case ProvisionKind.HeatPack:
                    if (state.WarmthHours > tuning.HeatPackDurationHours * 0.75f && Math.Abs(state.BodyTemperatureCelsius - 37f) < 0.01f)
                        return SurvivalResultCode.NotNeeded;
                    state.WarmthHours = Math.Max(state.WarmthHours, tuning.HeatPackDurationHours);
                    state.BodyTemperatureCelsius = 37f;
                    break;
                default:
                    return SurvivalResultCode.InvalidRequest;
            }

            state.TryChangeProvisionCount(provision, -1);
            state.Clamp();
            return SurvivalResultCode.Success;
        }

        public static SurvivalResultCode ApplyTrauma(SurvivalState state, TraumaKind kind,
            float magnitude, float playerHeightMeters, SurvivalTuning tuning)
        {
            if (state == null || tuning == null || float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                return SurvivalResultCode.InvalidRequest;
            tuning.Clamp();
            var damage = CalculateTraumaDamage(kind, magnitude, playerHeightMeters) *
                tuning.DamageMultiplier;
            if (damage <= 0f) return SurvivalResultCode.NotNeeded;
            state.Clamp();
            state.Health -= damage;
            state.Revision++;
            state.Clamp();
            RecoverAfterCollapse(state);
            return SurvivalResultCode.Success;
        }

        public static float CalculateTraumaDamage(TraumaKind kind, float magnitude,
            float playerHeightMeters)
        {
            if (float.IsNaN(magnitude) || float.IsInfinity(magnitude) || magnitude < 0f)
                return 0f;
            switch (kind)
            {
                case TraumaKind.Fall:
                    if (float.IsNaN(playerHeightMeters) || float.IsInfinity(playerHeightMeters))
                        return 0f;
                    var height = Math.Max(1.4f, Math.Min(2.4f, playerHeightMeters));
                    var excess = magnitude - height * 1.5f;
                    if (excess <= 0f) return 0f;
                    return Math.Min(95f, 4f + excess * 8.5f + excess * excess * 0.45f);
                case TraumaKind.TrainDismount:
                    return Math.Max(0f, magnitude - 5f);
                case TraumaKind.TrainCollision:
                    var speedExcess = magnitude - 7f;
                    if (speedExcess <= 0f) return 0f;
                    return Math.Min(95f, 6f + speedExcess * 2f + speedExcess * speedExcess * 0.025f);
                default:
                    return 0f;
            }
        }

        public static SurvivalResultCode Sleep(SurvivalState state, float hours,
            SurvivalEnvironment environment, SurvivalTuning tuning)
        {
            if (state == null || environment == null || tuning == null || float.IsNaN(hours) ||
                float.IsInfinity(hours) || hours < 0.1f || hours > 24f)
                return SurvivalResultCode.InvalidRequest;
            state.Clamp();
            // Normal samples are valid, read-only inputs. Only invalid external inputs need an owned clamped copy.
            if (!environment.IsValid()) { environment = environment.Clone(); environment.Clamp(); }
            tuning.Clamp();

            var wakeThirst = SleepDeprivationEffects.SleepTriggersThirst(state);
            var remaining = hours;
            while (remaining > 0)
            {
                // Integrate bed rest in bounded game-time steps: waking hungry/cold must
                // not retroactively cancel healing earned earlier in a long sleep.
                var step = Math.Min(remaining, 0.25f);
                if (SleepDeprivationEffects.HasExhaustion(state))
                    step = Math.Min(step, state.ExhaustionHoursRemaining);
                SleepStep(state, step, environment, tuning);
                SleepDeprivationEffects.AdvanceSleepClock(state, step, environment);
                wakeThirst |= SleepDeprivationEffects.SleepTriggersThirst(state);
                remaining = Math.Max(0, remaining - step);
            }
            if (wakeThirst && !tuning.DisableNeedsConsumption)
                state.Hydration = Math.Min(10, state.Hydration);
            // Only an accepted sleep action resets tolerance, even if rest stays below 70%.
            state.CoffeeUsesSinceSleep = 0;
            // Rest level alone (including coffee) never clears sleep deprivation. Only one
            // completed, full native sleep action does; short sleeps do not accumulate.
            SleepDeprivationEffects.RecoverAfterFullSleep(state, hours);
            state.Clamp();
            return SurvivalResultCode.Success;
        }

        private static void SleepStep(SurvivalState state, float hours, SurvivalEnvironment environment, SurvivalTuning tuning)
        {
            var needsHours = tuning.DisableNeedsConsumption ? 0f : hours * tuning.NeedsRateMultiplier;
            state.Hunger -= 100f / tuning.HungerHoursFromFull * needsHours * 0.52f;
            state.Hydration -= 100f / tuning.HydrationHoursFromFull * needsHours * 0.58f * (SleepDeprivationEffects.HasExhaustion(state) ? 3f : 1f);
            state.Rest += tuning.RestoredPerSleepHour * hours;
            state.CaffeineHours = 0f;

            var thermal = ComputeThermal(environment, state, tuning);
            var sleepTarget = Math.Max(35.5f, Math.Min(37.2f, thermal.TargetCoreTemperature + 0.4f));
            var alpha = 1f - (float)Math.Exp(-hours / Math.Max(0.25f, tuning.ThermalTimeConstantHours));
            state.BodyTemperatureCelsius += (sleepTarget - state.BodyTemperatureCelsius) * alpha;
            ApplyHealthChange(state, Math.Min(hours, 12f), tuning, state.BodyTemperatureCelsius,
                environment.AmbientTemperatureCelsius, false);
            if (state.Hunger >= 15f && state.Hydration >= 20f && environment.AmbientTemperatureCelsius <= 55f &&
                // Match the actual thermal damage thresholds. Sleep can settle at
                // 35.5 C, which used to silently disable all sleep healing.
                state.BodyTemperatureCelsius >= 35f && state.BodyTemperatureCelsius <= 38.5f)
                state.Health += hours * 1.5f;
            state.SimulatedGameHours += hours;
            state.Revision++;
            state.Clamp();
            RecoverAfterCollapse(state);
        }

        public static float GetMovementMultiplier(SurvivalState state)
        {
            if (state == null) return 1f;
            var hydrationPenalty = Deficit(state.Hydration, 35f) * 0.14f;
            var restPenalty = Deficit(state.Rest, 30f) * 0.18f;
            var healthPenalty = Deficit(state.Health, 60f) * 0.23f;
            var hungerPenalty = Deficit(state.Hunger, 25f) * 0.08f;
            var thermalPenalty = 0f;
            if (state.BodyTemperatureCelsius < 35.5f)
                thermalPenalty = Math.Min(0.18f, (35.5f - state.BodyTemperatureCelsius) * 0.08f);
            else if (state.BodyTemperatureCelsius > 38.5f)
                thermalPenalty = Math.Min(0.18f, (state.BodyTemperatureCelsius - 38.5f) * 0.08f);
            return Math.Max(0.55f, 1f - hydrationPenalty - restPenalty - healthPenalty - hungerPenalty - thermalPenalty);
        }

        public static float GetCoffeeRestMultiplier(SurvivalState state)
        {
            return CoffeeTolerance.NextRestMultiplier(state == null ? 0 : state.CoffeeUsesSinceSleep);
        }

        public static bool CanRun(SurvivalState state)
        { return state == null || (state.Health >= 40f && state.Hunger >= 25f && state.Hydration >= 30f && state.Rest >= 30f); }

        public static float GetApparentTemperature(SurvivalState state, SurvivalEnvironment environment,
            SurvivalTuning tuning)
        {
            if (state == null || environment == null || tuning == null) return 18f;
            return ComputeThermal(environment, state, tuning).ApparentTemperature;
        }

        private static ThermalResult ComputeThermal(SurvivalEnvironment source, SurvivalState state,
            SurvivalTuning tuning)
        {
            var environment = source;
            if (!environment.IsValid()) { environment = source.Clone(); environment.Clamp(); }
            var apparent = environment.AmbientTemperatureCelsius + environment.ShelterWarmthCelsius;
            if (environment.AmbientTemperatureCelsius < 16f)
                apparent -= Math.Min(10f, environment.WindSpeedMetersPerSecond * 0.62f) * environment.Exposure;
            apparent -= environment.RainIntensity * 7f * environment.Exposure;
            apparent += environment.Activity * 2.2f;
            var coldStress = Clamp01((12f - apparent) / 30f) * (0.35f + environment.Exposure * 0.65f);
            var heatStress = Clamp01((apparent - 30f) / 20f) * (0.55f + environment.Exposure * 0.45f);
            var target = 37f - coldStress * 4.2f + heatStress * 3.0f + environment.Activity * 0.15f;
            if (state.WarmthHours > 0f) target = Math.Max(37f, target);
            return new ThermalResult(apparent, coldStress, heatStress, target);
        }

        private static void ApplyHealthChange(SurvivalState state, float hours, SurvivalTuning tuning)
        { ApplyHealthChange(state, hours, tuning, state.BodyTemperatureCelsius, 18f); }

        private static void ApplyHealthChange(SurvivalState state, float hours, SurvivalTuning tuning,
            float bodyTemperatureCelsius, float airTemperatureCelsius, bool passiveRecovery = true)
        {
            var damagePerHour = 0f;
            damagePerHour += Deficit(state.Hunger, 15f) * 4f;
            damagePerHour += Deficit(state.Hydration, 20f) * 10f;
            damagePerHour += Deficit(state.Rest, 10f) * 2f;
            if (bodyTemperatureCelsius < 35f)
                damagePerHour += ThermalThresholdDamagePerHour +
                    (35f - bodyTemperatureCelsius) * 6f;
            if (bodyTemperatureCelsius > 38.5f)
                damagePerHour += ThermalThresholdDamagePerHour +
                    (bodyTemperatureCelsius - 38.5f) * 2f;
            if (bodyTemperatureCelsius > 39f)
                damagePerHour += (bodyTemperatureCelsius - 39f) * 5f;
            // Extreme cabin/outdoor heat is harmful even before core temperature
            // has caught up. The authority applies this damage and replicates the
            // resulting health through the normal per-player state packet.
            if (airTemperatureCelsius > 55f)
                damagePerHour += 0.75f + (airTemperatureCelsius - 55f) * 0.12f;
            if (damagePerHour > 0f)
            {
                state.Health -= damagePerHour * hours * tuning.DamageMultiplier;
            }
            else if (passiveRecovery && state.Hunger > 65f && state.Hydration > 65f && state.Rest > 55f &&
                bodyTemperatureCelsius >= 36f && bodyTemperatureCelsius <= 38f)
            {
                state.Health += tuning.PassiveHealthRecoveryPerHour * hours;
            }
        }

        private static void RecoverAfterCollapse(SurvivalState state)
        {
            if (state.Health > 0f) return;
            state.CollapseCount++;
            state.FirstAidSecondsRemaining = 0f;
            state.Health = 25f;
            state.Hunger = Math.Max(25f, state.Hunger);
            state.Hydration = Math.Max(25f, state.Hydration);
            state.Rest = Math.Max(25f, state.Rest);
            state.BodyTemperatureCelsius = Math.Max(35.5f, Math.Min(38.5f, state.BodyTemperatureCelsius));
            state.Revision++;
        }

        private static float Deficit(float value, float threshold)
        {
            if (value >= threshold || threshold <= 0f) return 0f;
            return Clamp01((threshold - value) / threshold);
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }

        private readonly struct ThermalResult
        {
            public ThermalResult(float apparentTemperature, float coldStress, float heatStress,
                float targetCoreTemperature)
            {
                ApparentTemperature = apparentTemperature;
                ColdStress = coldStress;
                HeatStress = heatStress;
                TargetCoreTemperature = targetCoreTemperature;
            }

            public float ApparentTemperature { get; }
            public float ColdStress { get; }
            public float HeatStress { get; }
            public float TargetCoreTemperature { get; }
        }
    }
}
