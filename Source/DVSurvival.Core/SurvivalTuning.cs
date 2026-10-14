using System;

namespace DVSurvival.Core
{
    [Serializable]
    public sealed class SurvivalTuning
    {
        public bool DisableNeedsConsumption = false;
        public float NeedsRateMultiplier = 1f;
        public float DamageMultiplier = 1f;
        public float HungerHoursFromFull = 18f;
        public float HydrationHoursFromFull = 12f;
        public float RestHoursFromFull = 18f;
        public float RestoredPerSleepHour = 12.5f;
        public float ThermalTimeConstantHours = 1.5f;
        public float ThermalTimeConstantSeconds = 180f;
        public float PassiveHealthRecoveryPerHour = 1.25f;
        public float HeatPackWarmthCelsius = 9f;
        public float HeatPackDurationHours = 4f;
        public float CoffeeDurationHours = 4f;
        public int StartingMeals = 2;
        public int StartingWater = 2;
        public int StartingCoffee = 1;
        public int StartingFirstAid = 1;
        public int StartingHeatPacks = 1;
        public int MealPrice = 90;
        public int WaterPrice = 40;
        public int CoffeePrice = 75;
        public int FirstAidPrice = 350;
        public int HeatPackPrice = 120;
        public int CannedFoodPrice = 125;
        public int TeaPrice = 55;

        public int GetPrice(ProvisionKind kind)
        {
            switch (kind)
            {
                case ProvisionKind.Meal: return MealPrice;
                case ProvisionKind.Water: return WaterPrice;
                case ProvisionKind.Coffee: return CoffeePrice;
                case ProvisionKind.FirstAid: return FirstAidPrice;
                case ProvisionKind.HeatPack: return HeatPackPrice;
                case ProvisionKind.CannedFood: return CannedFoodPrice;
                case ProvisionKind.Tea: return TeaPrice;
                default: return -1;
            }
        }

        public void Clamp()
        {
            NeedsRateMultiplier = ClampFinite(NeedsRateMultiplier, 0.1f, 5f, 1f);
            DamageMultiplier = ClampFinite(DamageMultiplier, 0f, 5f, 1f);
            HungerHoursFromFull = ClampFinite(HungerHoursFromFull, 2f, 96f, 18f);
            HydrationHoursFromFull = ClampFinite(HydrationHoursFromFull, 2f, 72f, 12f);
            RestHoursFromFull = ClampFinite(RestHoursFromFull, 2f, 96f, 18f);
            RestoredPerSleepHour = ClampFinite(RestoredPerSleepHour, 1f, 30f, 12.5f);
            ThermalTimeConstantHours = ClampFinite(ThermalTimeConstantHours, 0.1f, 12f, 1.5f);
            ThermalTimeConstantSeconds = ClampFinite(ThermalTimeConstantSeconds, 10f, 3600f, 180f);
            PassiveHealthRecoveryPerHour = ClampFinite(PassiveHealthRecoveryPerHour, 0f, 10f, 1.25f);
            HeatPackWarmthCelsius = ClampFinite(HeatPackWarmthCelsius, 0f, 25f, 9f);
            HeatPackDurationHours = ClampFinite(HeatPackDurationHours, 0.25f, 24f, 4f);
            CoffeeDurationHours = ClampFinite(CoffeeDurationHours, 0.25f, 24f, 4f);
            StartingMeals = ClampInt(StartingMeals, 0, 50);
            StartingWater = ClampInt(StartingWater, 0, 50);
            StartingCoffee = ClampInt(StartingCoffee, 0, 50);
            StartingFirstAid = ClampInt(StartingFirstAid, 0, 50);
            StartingHeatPacks = ClampInt(StartingHeatPacks, 0, 50);
            MealPrice = ClampInt(MealPrice, 0, 1000000);
            WaterPrice = ClampInt(WaterPrice, 0, 1000000);
            CoffeePrice = ClampInt(CoffeePrice, 0, 1000000);
            FirstAidPrice = ClampInt(FirstAidPrice, 0, 1000000);
            HeatPackPrice = ClampInt(HeatPackPrice, 0, 1000000);
            CannedFoodPrice = ClampInt(CannedFoodPrice, 0, 1000000);
            TeaPrice = ClampInt(TeaPrice, 0, 1000000);
        }

        private static float ClampFinite(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }

        private static int ClampInt(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
