using System;

namespace DVSurvival.Core
{
    public sealed class HudPresentationRevision
    {
        private bool initialized;
        private int health, hunger, hydration, rest, body, air, flags;
        public bool Observe(SurvivalState state, float ambient, bool numbers, bool warnings, bool russian)
        {
            // Half-step buckets also invalidate at exact rounding ties across Mono/.NET versions.
            int h = (int)Math.Floor(state.Health * 2f), f = (int)Math.Floor(state.Hunger * 2f);
            int w = (int)Math.Floor(state.Hydration * 2f), r = (int)Math.Floor(state.Rest * 2f);
            int b = (int)Math.Floor(state.BodyTemperatureCelsius * 20f), a = (int)Math.Floor(ambient * 20f);
            int mask = (numbers ? 1 : 0) | (warnings ? 2 : 0) | (russian ? 4 : 0) |
                (state.Health < 40 ? 8 : 0) | (state.Hunger < 25 ? 16 : 0) |
                (state.Hydration < 30 ? 32 : 0) | (state.Rest < 30 ? 64 : 0) |
                (state.LowRestGameHours >= 24 ? 128 : 0) | (state.LowRestGameHours >= 48 ? 256 : 0) |
                (state.BodyTemperatureCelsius < 35 ? 512 : 0) | (state.BodyTemperatureCelsius > 38.5f ? 1024 : 0) |
                (ambient > 55 ? 2048 : 0) | (state.Health <= 10 ? 4096 : 0) |
                (state.BodyTemperatureCelsius < 34 || state.BodyTemperatureCelsius > 40 ? 8192 : 0);
            bool changed = !initialized || health != h || hunger != f || hydration != w || rest != r || body != b || air != a || flags != mask;
            initialized = true; health = h; hunger = f; hydration = w; rest = r; body = b; air = a; flags = mask;
            return changed;
        }
    }
}
