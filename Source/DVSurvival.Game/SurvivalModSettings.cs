using System;
using DVSurvival.Core;
using UnityEngine;
using UnityModManagerNet;

namespace DVSurvival.Mod
{
    [Serializable]
    public sealed class SurvivalModSettings : UnityModManager.ModSettings
    {
        public string IdentityId = string.Empty;
        public bool ShowHud = true;
        public bool ShowNumericValues = true;
        public bool ShowStatusWarnings = true;
        public bool UseDvSeasonsTemperature = true;
        public float HudScale = 1f;
        public int HudStyle = HudLayout.Legacy;
        public float CompactHudScale = 1.75f;
        public HudPanelPosition[] HudPanelPositions = HudPlacement.ValidatePositions(null);
        public bool HudLegacyRestored = false;
        public bool DisableNeedsConsumption = false;
        public float NeedsRateMultiplier = 1f;
        public float DamageMultiplier = 1f;
        public float TemperatureChangeMultiplier = 1.5f;
        public float HungerHoursFromFull = 18f;
        public float HydrationHoursFromFull = 12f;
        public float RestHoursFromFull = 18f;
        public float ShopUseDistance = 18f;
        public float BedValidationDistance = 12f;
        public int MealPrice = 90;
        public int WaterPrice = 40;
        public int CoffeePrice = 75;
        public int FirstAidPrice = 350;
        public int HeatPackPrice = 120;
        public int CannedFoodPrice = 125;
        public int TeaPrice = 55;

        public bool EnsureIdentity()
        {
            Guid parsed;
            if (Guid.TryParse(IdentityId, out parsed) && parsed != Guid.Empty) return false;
            IdentityId = Guid.NewGuid().ToString("D");
            return true;
        }

        [NonSerialized] private SurvivalTuning cachedTuning;
        [NonSerialized] private SurvivalTuning rulesSource;
        [NonSerialized] private DVSurvival.Core.Simulation.SimulationRules cachedRules;
        [System.Xml.Serialization.XmlIgnore]
        public DVSurvival.Core.Simulation.SimulationRules CachedRules
        {
            get
            {
                var source = CachedTuning;
                if (!ReferenceEquals(source, rulesSource))
                {
                    cachedRules = new DVSurvival.Core.Simulation.SimulationRules(source);
                    rulesSource = source;
                }
                return cachedRules;
            }
        }
        // Compare the small set of public UMM fields: covers UI changes and external settings edits.
        // ToTuning still returns an owned copy for HUD prediction and other mutating consumers.
        [System.Xml.Serialization.XmlIgnore]
        public SurvivalTuning CachedTuning
        {
            get
            {
                var t = cachedTuning;
                if (t == null || t.DisableNeedsConsumption != DisableNeedsConsumption ||
                    t.NeedsRateMultiplier != NeedsRateMultiplier || t.DamageMultiplier != DamageMultiplier ||
                    t.ThermalTimeConstantHours != 1.5f / TemperatureChangeMultiplier ||
                    t.HungerHoursFromFull != HungerHoursFromFull || t.HydrationHoursFromFull != HydrationHoursFromFull ||
                    t.RestHoursFromFull != RestHoursFromFull || t.MealPrice != MealPrice ||
                    t.WaterPrice != WaterPrice || t.CoffeePrice != CoffeePrice ||
                    t.FirstAidPrice != FirstAidPrice || t.HeatPackPrice != HeatPackPrice ||
                    t.CannedFoodPrice != CannedFoodPrice || t.TeaPrice != TeaPrice)
                    cachedTuning = ToTuning();
                return cachedTuning;
            }
        }

        public SurvivalTuning ToTuning()
        {
            Clamp();
            var value = new SurvivalTuning
            {
                DisableNeedsConsumption = DisableNeedsConsumption,
                NeedsRateMultiplier = NeedsRateMultiplier,
                DamageMultiplier = DamageMultiplier,
                ThermalTimeConstantHours = 1.5f / TemperatureChangeMultiplier,
                ThermalTimeConstantSeconds = 180f / TemperatureChangeMultiplier,
                HungerHoursFromFull = HungerHoursFromFull,
                HydrationHoursFromFull = HydrationHoursFromFull,
                RestHoursFromFull = RestHoursFromFull,
                MealPrice = MealPrice,
                WaterPrice = WaterPrice,
                CoffeePrice = CoffeePrice,
                FirstAidPrice = FirstAidPrice,
                HeatPackPrice = HeatPackPrice,
                CannedFoodPrice = CannedFoodPrice,
                TeaPrice = TeaPrice,
                StartingMeals = 0, StartingWater = 0, StartingCoffee = 0, StartingFirstAid = 0, StartingHeatPacks = 0
            };
            value.Clamp();
            return value;
        }

        public void Clamp()
        {
            EnsureIdentity();
            HudScale = ClampFinite(HudScale, 0.65f, 1.75f, 1f);
            HudStyle = HudLayout.Validate(HudStyle);
            CompactHudScale = ClampFinite(CompactHudScale, 0.75f, 3f, 1.75f);
            HudPanelPositions = HudPlacement.ValidatePositions(HudPanelPositions);
            // One-time return to the requested original HUD; later selections are preserved.
            if (!HudLegacyRestored) { HudStyle = HudLayout.Legacy; HudLegacyRestored = true; }
            NeedsRateMultiplier = ClampFinite(NeedsRateMultiplier, 0.25f, 3f, 1f);
            DamageMultiplier = ClampFinite(DamageMultiplier, 0f, 5f, 1f);
            TemperatureChangeMultiplier = ClampFinite(TemperatureChangeMultiplier, 0.25f, 5f, 1.5f);
            HungerHoursFromFull = ClampFinite(HungerHoursFromFull, 4f, 48f, 18f);
            HydrationHoursFromFull = ClampFinite(HydrationHoursFromFull, 3f, 36f, 12f);
            RestHoursFromFull = ClampFinite(RestHoursFromFull, 4f, 48f, 18f);
            ShopUseDistance = ClampFinite(ShopUseDistance, 5f, 50f, 18f);
            BedValidationDistance = ClampFinite(BedValidationDistance, 5f, 30f, 12f);
            MealPrice = ClampInt(MealPrice, 0, 1000000);
            WaterPrice = ClampInt(WaterPrice, 0, 1000000);
            CoffeePrice = ClampInt(CoffeePrice, 0, 1000000);
            FirstAidPrice = ClampInt(FirstAidPrice, 0, 1000000);
            HeatPackPrice = ClampInt(HeatPackPrice, 0, 1000000);
            CannedFoodPrice = ClampInt(CannedFoodPrice, 0, 1000000);
            TeaPrice = ClampInt(TeaPrice, 0, 1000000);
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Clamp();
            Save(this, modEntry);
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
