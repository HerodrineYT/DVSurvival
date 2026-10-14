using System;

namespace DVSurvival.Core.Simulation
{
    internal static class TraumaSystem
    {
        internal static float Damage(TraumaKind kind, float magnitude, float height)
        {
            if (!Numbers.Finite(magnitude) || magnitude < 0) return 0;
            if (kind == TraumaKind.TrainDismount) return Math.Max(0, magnitude - 5);
            if (kind == TraumaKind.TrainCollision)
            { float excess = magnitude - 7; return excess <= 0 ? 0 : Math.Min(95, 6 + excess * 2 + excess * excess * .025f); }
            if (kind != TraumaKind.Fall || !Numbers.Finite(height)) return 0;
            float fall = magnitude - Numbers.Range(height, 1.4f, 2.4f, 1.8f) * 1.5f;
            return fall <= 0 ? 0 : Math.Min(95, 4 + fall * 8.5f + fall * fall * .45f);
        }
        internal static SurvivalResultCode Apply(SurvivalState state, TraumaKind kind, float magnitude, float height, in SimulationRules rules)
        {
            if (state == null || !Numbers.Finite(magnitude)) return SurvivalResultCode.InvalidRequest;
            float damage = Damage(kind, magnitude, height) * rules.DamageRate;
            if (damage <= 0) return SurvivalResultCode.NotNeeded;
            state.Clamp(); state.Health -= damage; state.Revision++; state.Clamp(); VitalitySystem.Respawn(state);
            return SurvivalResultCode.Success;
        }
    }
}
