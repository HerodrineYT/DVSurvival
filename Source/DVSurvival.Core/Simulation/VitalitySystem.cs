using System;

namespace DVSurvival.Core.Simulation
{
    internal static class VitalitySystem
    {
        internal static void Integrate(SurvivalState state, in SimulationRules rules, float hours, float body, float air,
            bool passive, float thermalDuration = -1)
        {
            float rate = Numbers.Deficit(state.Hunger, 15) * 4;
            rate += Numbers.Deficit(state.Hydration, 20) * 10;
            rate += Numbers.Deficit(state.Rest, 10) * 2;
            float thermalRate = 0;
            if (body < 35) thermalRate += 1 + (35 - body) * 6;
            if (body > 38.5f) thermalRate += 1 + (body - 38.5f) * 2;
            if (body > 39) thermalRate += (body - 39) * 5;
            if (air > 55) thermalRate += .75f + (air - 55) * .12f;
            // Live exposure is measured in active real minutes. Calendar-only/sleep calculations
            // retain game-hour integration; changing the day length must not suppress live hazards.
            if (thermalDuration < 0) thermalDuration = hours;
            if (rate > 0 || thermalRate > 0)
                state.Health -= (rate * hours + thermalRate * thermalDuration) * rules.DamageRate;
            else if (passive && state.Hunger > 65 && state.Hydration > 65 && state.Rest > 55 && body >= 36 && body <= 38)
                state.Health += rules.RecoveryPerHour * hours;
        }
        internal static void CommitStep(SurvivalState state, float hours)
        { state.SimulatedGameHours += hours; state.Revision++; state.Clamp(); Respawn(state); }
        internal static void Respawn(SurvivalState state)
        {
            if (state.Health > 0) return;
            state.CollapseCount++; state.FirstAidSecondsRemaining = 0; state.Health = 25;
            state.Hunger = Math.Max(25, state.Hunger); state.Hydration = Math.Max(25, state.Hydration); state.Rest = Math.Max(25, state.Rest);
            state.BodyTemperatureCelsius = Math.Max(35.5f, Math.Min(38.5f, state.BodyTemperatureCelsius)); state.Revision++;
        }
        internal static float Mobility(SurvivalState state)
        {
            if (state == null) return 1;
            float thermal = state.BodyTemperatureCelsius < 35.5f ? Math.Min(.18f, (35.5f - state.BodyTemperatureCelsius) * .08f) :
                state.BodyTemperatureCelsius > 38.5f ? Math.Min(.18f, (state.BodyTemperatureCelsius - 38.5f) * .08f) : 0;
            return Math.Max(.55f, 1 - Numbers.Deficit(state.Hydration, 35) * .14f - Numbers.Deficit(state.Rest, 30) * .18f -
                Numbers.Deficit(state.Health, 60) * .23f - Numbers.Deficit(state.Hunger, 25) * .08f - thermal);
        }
    }
}
