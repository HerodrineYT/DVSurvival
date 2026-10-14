using System;
using DVSurvival.Core;

namespace DVSurvival.Mod
{
    // Retained presentation: layouts only draw; individual strings update when their own field changes.
    internal sealed class NeedsPresenter
    {
        internal readonly string[] Names = new string[4];
        internal readonly string[] Numbers = new string[4];
        internal readonly string[] Alerts = new string[4];
        private readonly int[] keys = { int.MinValue, int.MinValue, int.MinValue, int.MinValue };
        private readonly bool[] low = new bool[4];
        private int bodyKey = int.MinValue, airKey = int.MinValue;
        private bool initialized, russian, numeric, warnings, hotBody, hotAir, critical;
        internal string Ambient = string.Empty, Body = string.Empty, LegacyBody = string.Empty, WarningTitle = string.Empty;

        internal void Capture(SurvivalState state, float air, bool showNumbers, bool showWarnings)
        {
            bool language = ModLocalization.IsRussian;
            bool all = !initialized || russian != language || numeric != showNumbers || warnings != showWarnings;
            if (!initialized || russian != language)
            {
                Names[0] = ModLocalization.Text("Здоровье", "Health"); Names[1] = ModLocalization.Text("Сытость", "Food");
                Names[2] = ModLocalization.Text("Вода", "Water"); Names[3] = ModLocalization.Text("Сон", "Rest");
            }
            for (int i = 0; i < 4; i++)
            {
                float value = i == 0 ? state.Health : i == 1 ? state.Hunger : i == 2 ? state.Hydration : state.Rest;
                bool alert = i == 0 ? value < 40 : i == 1 ? value < 25 : i == 2 ? value < 30 : value < 30 || state.LowRestGameHours >= 24;
                int key = (int)Math.Floor(value * 2);
                if (!all && key == keys[i] && low[i] == alert) continue;
                Numbers[i] = showNumbers ? ModLocalization.Number(value, "F0") + "%" : string.Empty;
                Alerts[i] = NeedAlertText.Format(state, i, Numbers[i], showWarnings);
                keys[i] = key; low[i] = alert;
            }
            float body = state.BodyTemperatureCelsius;
            int nextBody = (int)Math.Floor(body * 20), nextAir = (int)Math.Floor(air * 20);
            bool bodyAlert = showWarnings && (body > 38.5f || body < 35), airAlert = showWarnings && air > 55;
            if (all || nextBody != bodyKey || bodyAlert != hotBody)
            {
                string number = ModLocalization.Number(body, "F1") + " °C";
                Body = Mark(ModLocalization.Text("Тело: ", "Body: ") + number, bodyAlert);
                LegacyBody = Mark(ModLocalization.Text("Температура тела  ", "Body temperature  ") + number, bodyAlert);
                bodyKey = nextBody; hotBody = bodyAlert;
            }
            if (all || nextAir != airKey || airAlert != hotAir)
            {
                Ambient = Mark(ModLocalization.Text("Воздух: ", "Ambient: ") + ModLocalization.Number(air, "F1") + " °C", airAlert);
                airKey = nextAir; hotAir = airAlert;
            }
            bool nextCritical = state.Health <= 10 || body < 34 || body > 40 || state.LowRestGameHours >= 48;
            if (all || nextCritical != critical)
                WarningTitle = nextCritical ? ModLocalization.Text("Критическое состояние", "Critical condition") : ModLocalization.Text("Требуется внимание", "Needs attention");
            critical = nextCritical; initialized = true; russian = language; numeric = showNumbers; warnings = showWarnings;
        }
        private static string Mark(string value, bool alert) => alert ? "<color=#FF5555>! " + value + "</color>" : value;
    }
}
