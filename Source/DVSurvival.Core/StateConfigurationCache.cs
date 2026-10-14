using System;

namespace DVSurvival.Core
{
    // Config is sent on a reliable full state at hello/change/transaction; telemetry is compact.
    // ReliableUnordered transport can deliver telemetry first. Drop it until its config arrives;
    // the reliable full message carries a state too, so initialization cannot deadlock.
    public sealed class StateConfigurationCache
    {
        private string session;
        private uint revision;
        private bool disabled;
        private int meal, water, coffee, firstAid, heatPack, cannedFood, tea;
        public bool Prepare(SurvivalStateMessage m, bool reliable)
        {
            bool changed = revision == 0 || session != m.SessionId || disabled != m.DisableNeedsConsumption ||
                meal != m.MealPrice || water != m.WaterPrice || coffee != m.CoffeePrice ||
                firstAid != m.FirstAidPrice || heatPack != m.HeatPackPrice ||
                cannedFood != m.CannedFoodPrice || tea != m.TeaPrice;
            if (changed)
            {
                uint next = session == m.SessionId ? revision + 1u : 1u;
                m.ConfigurationRevision = next == 0 ? 1u : next;
                Capture(m);
            }
            m.ConfigurationRevision = revision;
            m.HasConfiguration = reliable || changed;
            return reliable || changed;
        }
        public bool Apply(SurvivalStateMessage m)
        {
            if (m.HasConfiguration)
            {
                if (m.ConfigurationRevision == 0 || m.MealPrice < 0 || m.WaterPrice < 0 || m.CoffeePrice < 0 ||
                    m.FirstAidPrice < 0 || m.HeatPackPrice < 0 || m.CannedFoodPrice < 0 || m.TeaPrice < 0) return false;
                if (session != m.SessionId || m.ConfigurationRevision >= revision) Capture(m);
                // Older reliable receipts remain self-contained. Let runtime sequence checks
                // confirm their actions without rolling back the cached configuration or state.
                else return true;
            }
            if (session != m.SessionId || revision != m.ConfigurationRevision) return false;
            m.DisableNeedsConsumption = disabled;
            m.MealPrice = meal; m.WaterPrice = water; m.CoffeePrice = coffee;
            m.FirstAidPrice = firstAid; m.HeatPackPrice = heatPack;
            m.CannedFoodPrice = cannedFood; m.TeaPrice = tea;
            return true;
        }
        private void Capture(SurvivalStateMessage m)
        {
            session = m.SessionId; revision = m.ConfigurationRevision; disabled = m.DisableNeedsConsumption;
            meal = m.MealPrice; water = m.WaterPrice; coffee = m.CoffeePrice;
            firstAid = m.FirstAidPrice; heatPack = m.HeatPackPrice;
            cannedFood = m.CannedFoodPrice; tea = m.TeaPrice;
        }
        public void Reset() { session = null; revision = 0; }
    }
}
