using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using DVLangHelper.Runtime;
using DVSurvival.Core;
using I2.Loc;
using UnityEngine;

namespace DVSurvival.Mod
{
    internal static class ModLocalization
    {
        private static TranslationInjector injector;
        private static readonly Dictionary<string, string> keys = new Dictionary<string, string>(StringComparer.Ordinal);

        public static void Initialize(string modPath)
        {
            if (injector != null) return;
            var path = Path.Combine(modPath, "Localization", "strings.csv");
            if (!File.Exists(path)) throw new FileNotFoundException("Survival translation catalogue is missing.", path);
            injector = new TranslationInjector("DVSurvival");
            injector.AddTranslationsFromCsv(path);
        }

        // Stable keys permit Language Helper overrides without patching the game's localization method.
        public static string Key(string english)
        {
            string key;
            if (keys.TryGetValue(english, out key)) return key;
            uint hash = 2166136261;
            unchecked { foreach (char c in english) hash = (hash ^ c) * 16777619; }
            key = "DVSurvival/" + hash.ToString("x8", CultureInfo.InvariantCulture);
            keys.Add(english, key);
            return key;
        }

        public static bool IsRussian
        {
            get
            {
                try
                {
                    var language = LocalizationManager.CurrentLanguage ?? string.Empty;
                    if (!string.IsNullOrEmpty(language))
                        return language.Equals("Russian", StringComparison.OrdinalIgnoreCase) ||
                            language.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ||
                            language.IndexOf("рус", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                catch
                {
                    // Localization is briefly unavailable during scene changes.
                }
                return Application.systemLanguage == SystemLanguage.Russian;
            }
        }

        public static string Text(string russian, string english)
        {
            if (injector != null)
            {
                var translated = LocalizationManager.GetTranslation(Key(english));
                if (!string.IsNullOrEmpty(translated) && translated != Key(english)) return translated;
            }
            return IsRussian ? russian : english;
        }

        public static string ProvisionKey(ProvisionKind kind)
        {
            switch (kind)
            {
                case ProvisionKind.Meal: return Key("Meal");
                case ProvisionKind.Water: return Key("Water");
                case ProvisionKind.Coffee: return Key("Coffee");
                case ProvisionKind.FirstAid: return Key("First aid");
                case ProvisionKind.HeatPack: return Key("Heat pack");
                case ProvisionKind.CannedFood: return Key("Canned food");
                case ProvisionKind.Tea: return Key("Tea flask");
                default: return Key("Unknown");
            }
        }

        public static string Description(ProvisionKind kind)
        {
            switch (kind)
            {
                case ProvisionKind.Meal: return Text(
                    "До +45% сытости. Ешьте, удерживая ЛКМ (5 с на порцию). Отпустите — остаток сохранится.",
                    "Up to +45% food. Hold LMB to eat (5 s per portion). Release to keep the remainder.");
                case ProvisionKind.Water: return Text(
                    "До +50% воды; охлаждает на 0,2 °C, не ниже 36 °C. Пейте, удерживая ЛКМ (3 с на бутылку). Остаток сохраняется.",
                    "Up to +50% hydration; cools by 0.2 °C, no lower than 36 °C. Hold LMB to drink (3 s per bottle). Remainder is kept.");
                case ProvisionKind.Coffee: return Text(
                    "До +16% сна, +15% воды; +0,2 °C, не выше 37 °C. ЛКМ: пить частями (3 с). Первые 5 чашек: 100%; с 6-й: −5% за чашку до сна. Учёт с первого глотка.",
                    "Up to +16% rest, +15% water; +0.2 °C, max 37 °C. LMB: sip (3 s). First 5 cups: 100%; from cup 6: −5% per cup until sleep. Counts from first sip.");
                case ProvisionKind.FirstAid: return Text(
                    "До +40% здоровья за 20 с. ЛКМ: подготовка 8 с, затем расход целиком. ПКМ/убрать — отмена подготовки. Лечение не складывается.",
                    "Up to +40% health over 20 s. LMB: 8 s preparation, then uses the whole kit. RMB/put away cancels preparation. Healing does not stack.");
                case ProvisionKind.HeatPack: return Text(
                    "Температура тела 37 °C и временная защита от холода. ЛКМ: применение 3 с, расход целиком. ПКМ/убрать — отмена.",
                    "Sets body to 37 °C; temporary cold protection. LMB: apply for 3 s, uses the whole pack. RMB/put away cancels.");
                case ProvisionKind.CannedFood: return Text(
                    "До +60% сытости. Удерживайте ЛКМ, чтобы есть (6 с на банку). Отпустите — остаток сохранится.",
                    "Up to +60% food. Hold LMB to eat (6 s per can). Release to keep the remainder.");
                case ProvisionKind.Tea: return Text(
                    "До +35% воды; согревает на 0,15 °C, не выше 37 °C. ЛКМ: пить частями (3 с). Не заменяет сон. Остаток сохраняется.",
                    "Up to +35% water; warms by 0.15 °C, max 37 °C. LMB: sip (3 s). Does not replace sleep. Remainder is kept.");
                default: return string.Empty;
            }
        }

        public static string DescriptionKey(ProvisionKind kind)
        {
            switch (kind)
            {
                case ProvisionKind.Meal: return Key("Up to +45% food. Hold LMB to eat (5 s per portion). Release to keep the remainder.");
                case ProvisionKind.Water: return Key("Up to +50% hydration; cools by 0.2 °C, no lower than 36 °C. Hold LMB to drink (3 s per bottle). Remainder is kept.");
                case ProvisionKind.Coffee: return Key("Up to +16% rest, +15% water; +0.2 °C, max 37 °C. LMB: sip (3 s). First 5 cups: 100%; from cup 6: −5% per cup until sleep. Counts from first sip.");
                case ProvisionKind.FirstAid: return Key("Up to +40% health over 20 s. LMB: 8 s preparation, then uses the whole kit. RMB/put away cancels preparation. Healing does not stack.");
                case ProvisionKind.HeatPack: return Key("Sets body to 37 °C; temporary cold protection. LMB: apply for 3 s, uses the whole pack. RMB/put away cancels.");
                case ProvisionKind.CannedFood: return Key("Up to +60% food. Hold LMB to eat (6 s per can). Release to keep the remainder.");
                case ProvisionKind.Tea: return Key("Up to +35% water; warms by 0.15 °C, max 37 °C. LMB: sip (3 s). Does not replace sleep. Remainder is kept.");
                default: return ProvisionKey(kind);
            }
        }

        public static string Number(float value, string format)
        {
            return value.ToString(format, IsRussian ? CultureInfo.CurrentCulture : CultureInfo.InvariantCulture);
        }

        public static string Provision(ProvisionKind kind)
        {
            switch (kind)
            {
                case ProvisionKind.Meal: return Text("Паёк", "Meal");
                case ProvisionKind.Water: return Text("Вода", "Water");
                case ProvisionKind.Coffee: return Text("Кофе", "Coffee");
                case ProvisionKind.FirstAid: return Text("Аптечка", "First aid");
                case ProvisionKind.HeatPack: return Text("Грелка", "Heat pack");
                case ProvisionKind.CannedFood: return Text("Консервы", "Canned food");
                case ProvisionKind.Tea: return Text("Термос с чаем", "Tea flask");
                default: return Text("Неизвестно", "Unknown");
            }
        }

        public static string Result(SurvivalResultCode code, string statusKey)
        {
            if (!string.IsNullOrEmpty(statusKey))
            {
                switch (statusKey)
                {
                    case "meal_used": return Text("Вы поели.", "You ate a meal.");
                    case "water_used": return Text("Вы выпили воду.", "You drank water.");
                    case "coffee_used": return Text("Кофе немного отогнал сон.", "Coffee pushed back the fatigue.");
                    case "tea_used": return Text("Вы выпили чай.", "You drank tea.");
                    case "first_aid_used": return Text("Раны обработаны.", "Injuries treated.");
                    case "heat_pack_used": return Text("Грелка начала действовать.", "The heat pack is warming you.");
                    case "purchase_ok": return Text("Покупка добавлена в личный запас.", "Purchase added to your personal supplies.");
                    case "sleep_ok": return Text("Сон восстановил силы.", "Sleep restored your energy.");
                    case "fall_damage": return Text("Падение причинило травму.", "The fall caused an injury.");
                    case "train_damage": return Text("Столкновение с поездом причинило травму.",
                        "The train collision caused an injury.");
                    case "cab_heater_on": return Text("Отопитель кабины включён.",
                        "Cab heater switched on.");
                    case "cab_heater_off": return Text("Отопитель кабины выключен.",
                        "Cab heater switched off.");
                    case "death": return Text(
                        "Вы погибли, потеряли $5000 и очнулись дома с 25% здоровья.",
                        "You died, lost $5000, and woke up at home with 25% health.");
                    case "identity_in_use": return Text("ID игрока уже используется в этой сессии.", "Player identity is already in use in this session.");
                }
            }
            switch (code)
            {
                case SurvivalResultCode.Success: return Text("Готово.", "Done.");
                case SurvivalResultCode.NotReady: return Text("Система выживания ещё загружается.", "Survival system is still loading.");
                case SurvivalResultCode.InvalidRequest: return Text("Некорректное действие.", "Invalid action.");
                case SurvivalResultCode.ProtocolMismatch: return Text("Версии мода у игроков не совпадают.", "Players have incompatible mod versions.");
                case SurvivalResultCode.NoStock: return Text("В личном запасе этого нет.", "You do not have this in your supplies.");
                case SurvivalResultCode.NotNeeded: return Text("Сейчас это не требуется.", "You do not need that now.");
                case SurvivalResultCode.NotNearShop: return Text("Для покупки подойдите к магазину.", "Move closer to a shop to buy this.");
                case SurvivalResultCode.NotNearBed: return Text("Сон подтверждается только рядом с кроватью.", "Sleep is accepted only near a bed.");
                case SurvivalResultCode.NotEnoughMoney: return Text("Недостаточно общих средств.", "Not enough shared money.");
                case SurvivalResultCode.RateLimited: return Text("Слишком много действий подряд.", "Too many actions at once.");
                case SurvivalResultCode.UnsupportedMultiplayerVersion:
                    return Text("Обновите Multiplayer до проверенной версии.", "Update Multiplayer to the tested version.");
                default: return string.Empty;
            }
        }
    }
}
