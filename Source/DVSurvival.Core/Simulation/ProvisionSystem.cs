using System;

namespace DVSurvival.Core.Simulation
{
    internal static class ProvisionSystem
    {
        internal static SurvivalResultCode Use(SurvivalState state, ProvisionKind kind, in SimulationRules rules, bool physical)
        {
            if (state == null || !ProvisionCatalog.IsSupported(kind)) return SurvivalResultCode.InvalidRequest;
            state.Clamp();
            if (!physical && state.GetProvisionCount(kind) <= 0) return SurvivalResultCode.NoStock;
            switch (kind)
            {
                case ProvisionKind.Meal:
                    if (state.Hunger >= 98) return SurvivalResultCode.NotNeeded;
                    state.Hunger = Math.Min(100, state.Hunger + 45); break;
                case ProvisionKind.CannedFood:
                    if (state.Hunger >= 98) return SurvivalResultCode.NotNeeded;
                    state.Hunger = Math.Min(100, state.Hunger + 60); break;
                case ProvisionKind.Tea:
                    if (state.Hydration >= 98 && state.BodyTemperatureCelsius >= 37) return SurvivalResultCode.NotNeeded;
                    state.Hydration = Math.Min(100, state.Hydration + 35);
                    if (state.BodyTemperatureCelsius < 37)
                        state.BodyTemperatureCelsius = Math.Min(37, state.BodyTemperatureCelsius + .15f);
                    break;
                case ProvisionKind.Water:
                    if (state.Hydration >= 98 && state.BodyTemperatureCelsius <= 36) return SurvivalResultCode.NotNeeded;
                    state.Hydration = Math.Min(100, state.Hydration + 50);
                    if (state.BodyTemperatureCelsius > 36) state.BodyTemperatureCelsius = Math.Max(36, state.BodyTemperatureCelsius - .2f);
                    break;
                case ProvisionKind.Coffee:
                    if (state.Rest >= 98 && state.Hydration >= 98 && state.CaffeineHours > .5f && state.BodyTemperatureCelsius >= 37) return SurvivalResultCode.NotNeeded;
                    state.Rest = Math.Min(100, state.Rest + 16 * CoffeeTolerance.NextRestMultiplier(state.CoffeeUsesSinceSleep)); CoffeeTolerance.RegisterCup(state);
                    state.Hydration = Math.Min(100, state.Hydration + 15); state.CaffeineHours = Math.Max(state.CaffeineHours, rules.CoffeeHours);
                    if (state.BodyTemperatureCelsius < 37) state.BodyTemperatureCelsius = Math.Min(37, state.BodyTemperatureCelsius + .2f);
                    break;
                case ProvisionKind.FirstAid:
                    if (state.Health >= 99 || state.FirstAidSecondsRemaining > 0) return SurvivalResultCode.NotNeeded;
                    state.FirstAidSecondsRemaining = 20; break;
                case ProvisionKind.HeatPack:
                    if (state.WarmthHours > rules.HeatPackHours * .75f && Math.Abs(state.BodyTemperatureCelsius - 37) < .01f) return SurvivalResultCode.NotNeeded;
                    state.WarmthHours = Math.Max(state.WarmthHours, rules.HeatPackHours); state.BodyTemperatureCelsius = 37; break;
            }
            if (physical) state.Revision++; else state.TryChangeProvisionCount(kind, -1);
            state.Clamp(); return SurvivalResultCode.Success;
        }
    }
}
