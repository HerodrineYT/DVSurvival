using System;
using DVSurvival.Core;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace DVSurvival.Mod
{
    public static class Main
    {
        private const string HarmonyId = "Herodrine.DVSurvival";
        private static readonly string[] HudStyleNames = new string[6];
        private static UnityModManager.ModEntry entry;
        private static SurvivalModSettings settings;
        private static Harmony harmony;

        internal static SurvivalRuntime Runtime { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            if (modEntry == null) return false;
            entry = modEntry;
            try
            {
                var itemMod = UnityModManager.FindMod("custom_item_mod");
                if (itemMod == null || !itemMod.Active || itemMod.Version < new Version(0, 2, 0))
                    throw new InvalidOperationException("Custom Item Mod 0.2.0 or newer must be installed and enabled.");
                var languageMod = UnityModManager.FindMod("DVLangHelper");
                if (languageMod == null || !languageMod.Active || languageMod.Version < new Version(1, 2, 1))
                    throw new InvalidOperationException("Language Helper 1.2.1 or newer must be installed and enabled.");
                ModLocalization.Initialize(entry.Path);
                settings = UnityModManager.ModSettings.Load<SurvivalModSettings>(entry) ??
                    new SurvivalModSettings();
                var newIdentity = settings.EnsureIdentity();
                settings.Clamp();
                if (newIdentity) settings.Save(entry);
                Runtime = new SurvivalRuntime(entry, settings);
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(typeof(Main).Assembly);
                entry.OnToggle = OnToggle;
                entry.OnUnload = OnUnload;
                entry.OnUpdate = OnUpdate;
                entry.OnGUI = OnGui;
                entry.OnSaveGUI = OnSaveGui;
                entry.OnSessionStart = OnSessionStart;
                entry.Logger.Log("Survival Needs " + SurvivalConstants.ModVersion +
                    " loaded (network protocol " + SurvivalConstants.ProtocolVersion + ").");
                return true;
            }
            catch (Exception exception)
            {
                entry.Logger.Error("Survival Needs failed to load.");
                entry.Logger.LogException(exception);
                Cleanup();
                return false;
            }
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
        {
            if (!enabled && WorldStreamingInit.IsLoaded)
            {
                modEntry.Logger.Warning("Return to the main menu before disabling Survival Needs (custom item catalogue is in use).");
                return false;
            }
            try
            {
                if (enabled) Runtime.Start(); else Runtime.Stop();
                return true;
            }
            catch (Exception exception)
            {
                modEntry.Logger.LogException(exception);
                return false;
            }
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            if (Runtime != null) Runtime.SaveSettings();
            else if (settings != null) settings.Save(modEntry);
            Cleanup();
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry modEntry, float deltaTime)
        {
            if (Runtime != null) Runtime.Tick(deltaTime);
            FatigueSpeedometer.Tick();
        }

        private static void OnSessionStart(UnityModManager.ModEntry modEntry)
        {
            try { if (Runtime != null) Runtime.OnSessionStart(); }
            catch (Exception exception) { modEntry.Logger.LogException(exception); }
        }

        private static void OnSaveGui(UnityModManager.ModEntry modEntry)
        {
            if (Runtime != null) Runtime.SaveSettings();
            else if (settings != null) settings.Save(modEntry);
        }

        private static void OnGui(UnityModManager.ModEntry modEntry)
        {
            if (settings == null) return;
            settings.Clamp();
            GUILayout.Label(ModLocalization.Text("Survival Needs — персональные потребности",
                "Survival Needs — per-player needs"));
            GUILayout.Label(Runtime == null ? string.Empty : Runtime.NetworkStatus);
            var cabinStatus = Runtime == null ? string.Empty : Runtime.CabinStatus;
            if (!string.IsNullOrEmpty(cabinStatus)) GUILayout.Label(cabinStatus);
            if (Runtime != null && Runtime.CurrentState != null)
            {
                var state = Runtime.CurrentState;
                GUILayout.Label(ModLocalization.Text("Сытость ", "Food ") +
                    ModLocalization.Number(state.Hunger, "F0") + "% | " +
                    ModLocalization.Text("вода ", "water ") + ModLocalization.Number(state.Hydration, "F0") +
                    "% | " + ModLocalization.Text("сон ", "rest ") +
                    ModLocalization.Number(state.Rest, "F0") + "% | " +
                    ModLocalization.Text("здоровье ", "health ") +
                    ModLocalization.Number(state.Health, "F0") + "% | " +
                    ModLocalization.Number(state.BodyTemperatureCelsius, "F1") + " °C");
            }
            GUILayout.Space(8f);
            settings.ShowHud = GUILayout.Toggle(settings.ShowHud,
                ModLocalization.Text("Показывать HUD потребностей", "Show needs HUD"));
            settings.ShowNumericValues = GUILayout.Toggle(settings.ShowNumericValues,
                ModLocalization.Text("Показывать числовые значения", "Show numeric values"));
            settings.ShowStatusWarnings = GUILayout.Toggle(settings.ShowStatusWarnings,
                ModLocalization.Text("Показывать предупреждения о состоянии", "Show condition warnings"));
            settings.UseDvSeasonsTemperature = GUILayout.Toggle(settings.UseDvSeasonsTemperature,
                ModLocalization.Text("Использовать температуру Dynamic Seasons", "Use Dynamic Seasons temperature"));
            GUILayout.Label(ModLocalization.Text("Масштаб HUD: ", "HUD scale: ") +
                ModLocalization.Number(settings.HudScale * 100f, "F0") + "%");
            settings.HudScale = GUILayout.HorizontalSlider(settings.HudScale, 0.65f, 1.75f);
            GUILayout.Label(ModLocalization.Text("Вариант интерфейса (применяется сразу)", "HUD style (applies immediately)"));
            HudStyleNames[0] = ModLocalization.Text("Кольца", "Rings");
            HudStyleNames[1] = ModLocalization.Text("Полосы", "Bars");
            HudStyleNames[2] = ModLocalization.Text("Строка", "Ribbon");
            HudStyleNames[3] = ModLocalization.Text("Минимум", "Minimal");
            HudStyleNames[4] = ModLocalization.Text("Латунь", "Brass");
            HudStyleNames[5] = ModLocalization.Text("Классический (старый)", "Classic (original)");
            settings.HudStyle = GUILayout.SelectionGrid(settings.HudStyle,
                HudStyleNames, 3);
            GUILayout.Label(ModLocalization.Text(
                "Разблокируйте курсор, удерживайте ЛКМ на блоке HUD 2 секунды и перетащите. Отпустите — положение сохранится; Esc — отмена. При 100% ширина блока потребностей одинакова во всех стилях.",
                "Unlock the cursor, hold LMB on a HUD block for 2 seconds, then drag. Release to save; Esc to cancel. At 100%, every style has the same needs-block width."));
            if (GUILayout.Button(ModLocalization.Text("Сбросить расположение текущего HUD", "Reset current HUD positions")))
            {
                for (int panel = 0; panel < HudPlacement.PanelCount; panel++)
                    settings.HudPanelPositions[HudPlacement.Index(settings.HudStyle, panel)].Reset();
                settings.Save(modEntry);
            }
            GUILayout.Space(8f);
            GUILayout.Label(ModLocalization.Text(
                "Следующие параметры задаёт хост и они едины для расчёта всех игроков.",
                "The host controls the following simulation values for all players."));
            settings.DisableNeedsConsumption = GUILayout.Toggle(settings.DisableNeedsConsumption,
                ModLocalization.Text("Отключить расход потребностей", "Disable needs consumption"));
            if (settings.DisableNeedsConsumption)
                GUILayout.Label(ModLocalization.Text(
                    "Сытость, вода и сон не расходуются, новый недосып не накапливается. Восстановление, температура и урон работают как обычно. В MP действует настройка хоста.",
                    "Food, water and rest do not drain; new sleep deprivation does not accumulate. Recovery, temperature and damage work normally. In MP, the host's setting applies."));
            GUILayout.Label(ModLocalization.Text("Скорость потребностей: ", "Needs rate: ") +
                ModLocalization.Number(settings.NeedsRateMultiplier, "F2") + "×");
            settings.NeedsRateMultiplier = GUILayout.HorizontalSlider(settings.NeedsRateMultiplier, 0.25f, 3f);
            GUILayout.Label(ModLocalization.Text("Множитель урона: ", "Damage multiplier: ") +
                ModLocalization.Number(settings.DamageMultiplier, "F2") + "×");
            settings.DamageMultiplier = GUILayout.HorizontalSlider(settings.DamageMultiplier, 0f, 5f);
            GUILayout.Label(ModLocalization.Text(
                "Температурный урон: тело ниже 35 °C или выше 38,5 °C; воздух выше 55 °C. Урон идёт по активному реальному времени, независимо от длительности суток.",
                "Temperature damage: body below 35 °C or above 38.5 °C; air above 55 °C. Damage follows active real time, independently of day length."));
            GUILayout.Label(ModLocalization.Text("Скорость изменения температуры тела: ",
                "Body temperature change rate: ") +
                ModLocalization.Number(settings.TemperatureChangeMultiplier, "F2") + "×");
            settings.TemperatureChangeMultiplier = GUILayout.HorizontalSlider(
                settings.TemperatureChangeMultiplier, 0.25f, 5f);
            GUILayout.Label(ModLocalization.Text("Изменение примерно на 63% пути к равновесию за ",
                "About 63% of the change towards equilibrium in ") +
                ModLocalization.Number(180f / settings.TemperatureChangeMultiplier, "F0") +
                ModLocalization.Text(" с; в тёплых зданиях быстрее. От длительности суток не зависит.",
                    " s; faster in heated buildings. Independent of day length."));
            DrawHoursSlider(ref settings.HungerHoursFromFull, 4f, 48f,
                ModLocalization.Text("Сытость от 100 до 0: ", "Food 100 to 0: "));
            DrawHoursSlider(ref settings.HydrationHoursFromFull, 3f, 36f,
                ModLocalization.Text("Вода от 100 до 0: ", "Water 100 to 0: "));
            DrawHoursSlider(ref settings.RestHoursFromFull, 4f, 48f,
                ModLocalization.Text("Бодрость от 100 до 0: ", "Rest 100 to 0: "));
            GUILayout.Space(6f);
            GUILayout.Label(ModLocalization.Text(
                "Требуются Custom Item Mod 0.2.0 и Language Helper 1.2.1; Multiplayer необязателен. Отсканируйте ценник, оплатите покупку на кассе и заберите предмет. Язык следует языку игры.",
                "Requires Custom Item Mod 0.2.0 and Language Helper 1.2.1; Multiplayer is optional. Scan the tag, pay at the register and collect the item. Language follows the game."));
            GUILayout.Label(ModLocalization.Text(
                "Недосып идёт по календарю мира с последнего полноценного штатного сна: через 48 игровых часов — истощение, через 72 — спидометр показывает скорость в 1,5 раза меньше. Каждые следующие сутки делитель растёт на 0,5, максимум до 3. Кофе, высокий показатель сна и короткие сны отсчёт не останавливают; сбрасывает его один штатный сон не короче 8 часов.",
                "Sleep deprivation follows the world calendar from the last full native sleep: exhaustion begins after 48 game hours, and after 72 the speedometer reads 1.5 times lower. Each following day adds 0.5 to the divisor, up to 3. Coffee, a high rest value and short sleeps do not pause the clock; one native sleep of at least 8 hours resets it."));
            if (Runtime != null && Runtime.CurrentState != null)
            {
                var lowRest = Runtime.CurrentState.LowRestGameHours;
                var displayDivisor = SleepDeprivationEffects.DisplaySpeedDivisor(Runtime.CurrentState);
                if (FatigueSpeedometer.IsTestActive)
                    GUILayout.Label(ModLocalization.Text("Тест активен: показание спидометра временно в 1,5 раза меньше реальной скорости.",
                        "Test active: the speedometer reading is temporarily 1.5 times lower than actual speed."));
                else if (displayDivisor > 1f)
                    GUILayout.Label(ModLocalization.Text("Искажение активно: делитель показания спидометра ×", "Distortion active: speedometer reading divisor ×") +
                        ModLocalization.Number(displayDivisor, "0.0"));
                else if (lowRest >= SleepDeprivationEffects.ExhaustionThresholdHours)
                    GUILayout.Label(ModLocalization.Text("Состояние: истощение.", "Condition: exhaustion."));
                else if (lowRest >= 24d)
                    GUILayout.Label(ModLocalization.Text("Состояние: недосып.", "Condition: sleep deprivation."));
                if (Runtime.IsSessionReady && GUILayout.Button(ModLocalization.Text(
                    "Проверить спидометр ÷1,5 на 15 секунд", "Test speedometer ÷1.5 for 15 seconds")))
                {
                    FatigueSpeedometer.BeginTest(15f);
                    Runtime.Notify(ModLocalization.Text(
                        "Тест спидометра ÷1,5 включён на 15 секунд. Физическая скорость не изменена.",
                        "Speedometer ÷1.5 test enabled for 15 seconds. Physical speed is unchanged."));
                }
            }
            GUILayout.Label(ModLocalization.Text(
                "Бег недоступен: здоровье < 40%, сытость < 25%, вода < 30% или сон < 30%.",
                "Sprinting is disabled below 40% health, 25% food, 30% water or 30% rest."));
            GUILayout.Label(ModLocalization.Text(
                "Кофе: первые 5 чашек дают полный эффект. С шестой эффект на сон уменьшается на 5% от исходного за чашку, до нуля. Новая чашка учитывается с первого глотка. Любой успешный сон восстанавливает эффективность.",
                "Coffee: the first 5 cups have full effect. From cup 6, rest gain drops by 5% of the original per cup, down to zero. Each new cup counts from its first sip. Any successful sleep restores effectiveness."));
            if (Runtime != null && Runtime.CurrentState != null)
                GUILayout.Label(ModLocalization.Text("Эффективность следующего кофе для сна: ", "Next coffee rest effectiveness: ") +
                    ModLocalization.Number(SurvivalSimulator.GetCoffeeRestMultiplier(Runtime.CurrentState) * 100f, "F0") + "%");
        }

        private static void DrawHoursSlider(ref float value, float min, float max, string label)
        {
            GUILayout.Label(label + ModLocalization.Number(value, "F1") +
                ModLocalization.Text(" игровых ч.", " game h"));
            value = GUILayout.HorizontalSlider(value, min, max);
        }

        private static void Cleanup()
        {
            if (Runtime != null)
            {
                Runtime.Dispose();
                Runtime = null;
            }
            CustomProvisionCatalog.Clear();
            if (harmony != null)
            {
                harmony.UnpatchAll(HarmonyId);
                harmony = null;
            }
            if (entry != null)
            {
                entry.OnToggle = null;
                entry.OnUnload = null;
                entry.OnUpdate = null;
                entry.OnGUI = null;
                entry.OnSaveGUI = null;
                entry.OnSessionStart = null;
            }
            entry = null;
            settings = null;
        }
    }
}
