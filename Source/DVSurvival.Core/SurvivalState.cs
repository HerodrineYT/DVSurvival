using System;

namespace DVSurvival.Core
{
    [Serializable]
    public sealed class SurvivalState
    {
        public int Version = SurvivalConstants.SaveVersion;
        public float Hunger = 100f;
        public float Hydration = 100f;
        public float Rest = 100f;
        public float Health = 100f;
        public float FirstAidSecondsRemaining;
        public float BodyTemperatureCelsius = 37f;
        public float CaffeineHours;
        public int CoffeeUsesSinceSleep;
        public float WarmthHours;
        // Awake game-calendar hours since the last full native sleep (bed rest pauses the clock). The historical field name
        // is retained so existing saves and multiplayer state packets remain migratable.
        public double LowRestGameHours;
        public float ExhaustionHoursRemaining;
        public int Meals = 2;
        public int Water = 2;
        public int Coffee = 1;
        public int FirstAid = 1;
        public int HeatPacks = 1;
        public uint Revision;
        public uint CollapseCount;
        public double SimulatedGameHours;

        public static SurvivalState CreateDefault(SurvivalTuning tuning)
        {
            var value = new SurvivalState();
            if (tuning != null)
            {
                value.Meals = tuning.StartingMeals;
                value.Water = tuning.StartingWater;
                value.Coffee = tuning.StartingCoffee;
                value.FirstAid = tuning.StartingFirstAid;
                value.HeatPacks = tuning.StartingHeatPacks;
            }
            value.Clamp();
            return value;
        }

        public SurvivalState Clone()
        {
            var copy = new SurvivalState();
            CopyTo(copy);
            return copy;
        }

        public void CopyTo(SurvivalState target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.Version = Version;
            target.Hunger = Hunger;
            target.Hydration = Hydration;
            target.Rest = Rest;
            target.Health = Health;
            target.FirstAidSecondsRemaining = FirstAidSecondsRemaining;
            target.BodyTemperatureCelsius = BodyTemperatureCelsius;
            target.CaffeineHours = CaffeineHours;
            target.CoffeeUsesSinceSleep = CoffeeUsesSinceSleep;
            target.WarmthHours = WarmthHours;
            target.LowRestGameHours = LowRestGameHours;
            target.ExhaustionHoursRemaining = ExhaustionHoursRemaining;
            target.Meals = Meals;
            target.Water = Water;
            target.Coffee = Coffee;
            target.FirstAid = FirstAid;
            target.HeatPacks = HeatPacks;
            target.Revision = Revision;
            target.CollapseCount = CollapseCount;
            target.SimulatedGameHours = SimulatedGameHours;
        }

        public int GetProvisionCount(ProvisionKind kind)
        {
            switch (kind)
            {
                case ProvisionKind.Meal: return Meals;
                case ProvisionKind.Water: return Water;
                case ProvisionKind.Coffee: return Coffee;
                case ProvisionKind.FirstAid: return FirstAid;
                case ProvisionKind.HeatPack: return HeatPacks;
                default: return 0;
            }
        }

        public bool TryChangeProvisionCount(ProvisionKind kind, int delta)
        {
            var current = GetProvisionCount(kind);
            if (kind == ProvisionKind.None || delta == 0 || current + delta < 0 || current + delta > 999)
                return false;
            switch (kind)
            {
                case ProvisionKind.Meal: Meals += delta; break;
                case ProvisionKind.Water: Water += delta; break;
                case ProvisionKind.Coffee: Coffee += delta; break;
                case ProvisionKind.FirstAid: FirstAid += delta; break;
                case ProvisionKind.HeatPack: HeatPacks += delta; break;
                default: return false;
            }
            Revision++;
            return true;
        }

        public bool IsValid()
        {
            return Version >= 1 && Version <= SurvivalConstants.SaveVersion &&
                IsFiniteInRange(Hunger, 0f, 100f) &&
                IsFiniteInRange(Hydration, 0f, 100f) &&
                IsFiniteInRange(Rest, 0f, 100f) &&
                IsFiniteInRange(Health, 0f, 100f) &&
                IsFiniteInRange(FirstAidSecondsRemaining, 0f, FirstAidRecovery.DurationSeconds) &&
                IsFiniteInRange(BodyTemperatureCelsius, 30f, 43f) &&
                IsFiniteInRange(CaffeineHours, 0f, 48f) &&
                CoffeeUsesSinceSleep >= 0 && CoffeeUsesSinceSleep <= CoffeeTolerance.MaximumTrackedCups &&
                IsFiniteInRange(WarmthHours, 0f, 48f) &&
                !double.IsNaN(LowRestGameHours) && !double.IsInfinity(LowRestGameHours) &&
                LowRestGameHours >= 0d &&
                LowRestGameHours <= SleepDeprivationEffects.MaximumLowRestHours &&
                IsFiniteInRange(ExhaustionHoursRemaining, 0f, 3f) &&
                Meals >= 0 && Meals <= 999 && Water >= 0 && Water <= 999 &&
                Coffee >= 0 && Coffee <= 999 && FirstAid >= 0 && FirstAid <= 999 &&
                HeatPacks >= 0 && HeatPacks <= 999 &&
                !double.IsNaN(SimulatedGameHours) && !double.IsInfinity(SimulatedGameHours) &&
                SimulatedGameHours >= 0d;
        }

        public void Clamp()
        {
            Version = SurvivalConstants.SaveVersion;
            Hunger = ClampFinite(Hunger, 0f, 100f, 100f);
            Hydration = ClampFinite(Hydration, 0f, 100f, 100f);
            Rest = ClampFinite(Rest, 0f, 100f, 100f);
            Health = ClampFinite(Health, 0f, 100f, 100f);
            FirstAidSecondsRemaining = ClampFinite(FirstAidSecondsRemaining, 0f, FirstAidRecovery.DurationSeconds, 0f);
            BodyTemperatureCelsius = ClampFinite(BodyTemperatureCelsius, 30f, 43f, 37f);
            CaffeineHours = ClampFinite(CaffeineHours, 0f, 48f, 0f);
            CoffeeUsesSinceSleep = Clamp(CoffeeUsesSinceSleep, 0, CoffeeTolerance.MaximumTrackedCups);
            WarmthHours = ClampFinite(WarmthHours, 0f, 48f, 0f);
            LowRestGameHours = double.IsNaN(LowRestGameHours) || double.IsInfinity(LowRestGameHours)
                ? 0d : Math.Max(0d, Math.Min(SleepDeprivationEffects.MaximumLowRestHours,
                    LowRestGameHours));
            ExhaustionHoursRemaining = ClampFinite(ExhaustionHoursRemaining, 0f, 3f, 0f);
            Meals = Clamp(Meals, 0, 999);
            Water = Clamp(Water, 0, 999);
            Coffee = Clamp(Coffee, 0, 999);
            FirstAid = Clamp(FirstAid, 0, 999);
            HeatPacks = Clamp(HeatPacks, 0, 999);
            if (double.IsNaN(SimulatedGameHours) || double.IsInfinity(SimulatedGameHours) ||
                SimulatedGameHours < 0d)
                SimulatedGameHours = 0d;
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

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
