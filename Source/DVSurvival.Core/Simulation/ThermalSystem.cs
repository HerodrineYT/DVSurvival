using System;

namespace DVSurvival.Core.Simulation
{
    public readonly struct ThermalField
    {
        public readonly float Apparent, HeatStress, Target;
        public ThermalField(in EnvironmentFrame world, bool heatPack)
        {
            float air = world.Air + world.Shelter;
            if (world.Air < 16) air -= Math.Min(10, world.Wind * .62f) * world.Exposure;
            air -= world.Rain * 7 * world.Exposure;
            air += world.Activity * 2.2f;
            Apparent = air;
            float cold = Numbers.Range((12 - air) / 30, 0, 1, 0) * (.35f + world.Exposure * .65f);
            HeatStress = Numbers.Range((air - 30) / 20, 0, 1, 0) * (.55f + world.Exposure * .45f);
            Target = 37 - cold * 4.2f + HeatStress * 3 + world.Activity * .15f;
            if (heatPack) Target = Math.Max(37, Target);
        }
    }
    internal static class ThermalSystem
    {
        internal static float Awake(SurvivalState state, in ThermalField field, in EnvironmentFrame world,
            in SimulationRules rules, float hours, float seconds)
        {
            float before = state.BodyTemperatureCelsius;
            bool live = seconds >= 0 && Numbers.Finite(seconds);
            float step = live ? Math.Min(seconds, 30) / rules.ThermalSeconds : hours / rules.ThermalHours;
            state.BodyTemperatureCelsius = Numbers.Relax(before, field.Target, step * world.Recovery);
            if (live && ((before >= 38.5f && before > state.BodyTemperatureCelsius) ||
                         (before < 35 && before < state.BodyTemperatureCelsius))) return before;
            return state.BodyTemperatureCelsius;
        }
        internal static void Sleeping(SurvivalState state, in ThermalField field, in SimulationRules rules, float hours)
        {
            float target = Math.Max(35.5f, Math.Min(37.2f, field.Target + .4f));
            state.BodyTemperatureCelsius = Numbers.Relax(state.BodyTemperatureCelsius, target, hours / Math.Max(.25f, rules.ThermalHours));
        }
    }
}
