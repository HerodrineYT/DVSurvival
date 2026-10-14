using System;
using System.Collections;
using DV.UI;
using DV.Utils;
using DVSurvival.Core;
using UnityEngine;

namespace DVSurvival.Mod
{
    internal sealed partial class SurvivalHud : MonoBehaviour
    {
        private SurvivalRuntime runtime;
        private SurvivalModSettings settings;
        private readonly NativeLocoHudVisibility locoHudVisibility = new NativeLocoHudVisibility();
        private GUIStyle labelStyle;
        private GUIStyle plateStyle;
        private GUIStyle hudTextStyle;
        private GUIStyle legacyTitleStyle, legacyWarningStyle;
        private HudArtwork artwork;
        private Font hudFont;
        private static readonly Color Brass = new Color(0.82f, 0.67f, 0.44f);
        private string healthLabel, hungerLabel, hydrationLabel, restLabel;
        private string warningTitle = string.Empty;
        private string warningText = string.Empty;
        private readonly NeedsPresenter presenter = new NeedsPresenter();
        private ProvisionUseAction displayedUse;
        private string useTitle, useHint;
        private float nextUseTextRefresh;
        private bool useTextRussian;
        private string healthText = string.Empty;
        private string hungerText = string.Empty;
        private string hydrationText = string.Empty;
        private string restText = string.Empty;
        private string ambientText = string.Empty;
        private string legacyBodyText = string.Empty;
        private float healthFraction;
        private float hungerFraction;
        private float hydrationFraction;
        private float restFraction;
        private float temperatureFraction;
        private float cachedBodyTemperature = 37f;
        private float nextDisplayRefresh;
        private string toast = string.Empty;
        private float toastUntil;
        private bool toastIsLowRestWarning;
        private string pendingWarning = string.Empty;
        private int pendingWarningPriority;
        private bool pendingWarningIsLowRest;
        private readonly SurvivalWarningTracker warningTracker = new SurvivalWarningTracker();
        private Coroutine collapseCoroutine;

        public void Initialize(SurvivalRuntime survivalRuntime, SurvivalModSettings modSettings)
        {
            runtime = survivalRuntime;
            settings = modSettings;
        }

        public void Notify(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            toast = message;
            toastUntil = Time.realtimeSinceStartup + 4f;
            toastIsLowRestWarning = false;
        }

        public void ResetStateWarnings()
        {
            warningTracker.Reset();
            toast = string.Empty;
            toastUntil = 0f;
            toastIsLowRestWarning = false;
            pendingWarning = string.Empty;
            pendingWarningPriority = 0;
            pendingWarningIsLowRest = false;
            warningText = string.Empty;
            nextDisplayRefresh = 0f;
        }

        public void TriggerCollapse()
        {
            if (collapseCoroutine != null) StopCoroutine(collapseCoroutine);
            collapseCoroutine = StartCoroutine(CollapseFade());
        }

        private void Update()
        {
            var visible = CanDisplay();
            UpdateHudDrag(visible);
            if (!visible) return;
            RefreshDisplayCache();
            FlushPendingWarning();
        }

        private void LateUpdate()
        {
            // DE2 uses IndicatorGaugeLagging.Update. Enforce the final visual pose after its
            // normal Update without touching the source Indicator.Value or train physics.
            FatigueSpeedometer.LateTick();
        }

        private bool CanDisplay()
        {
            // Native world-loaded remains true during fast travel. Check the loading overlay as
            // well, including the period before a respawn starts the native travel coroutine.
            return runtime != null && settings != null && runtime.IsSessionReady &&
                WorldStreamingInit.IsLoaded && !UnloadWatcher.isUnloading &&
                !LoadingScreenManager.IsLoading && !FastTravelController.IsFastTravelling &&
                !runtime.IsHomeTravelPending && !locoHudVisibility.BlocksSurvivalHud();
        }

        private void OnGUI()
        {
            // Hide the entire overlay, including warnings and notifications, before any GUI work.
            if (!CanDisplay()) return;
            var repaint = Event.current == null || Event.current.type == EventType.Repaint;
            if (!repaint) return;
            EnsureStyles();
            if (healthLabel == null) RefreshDisplayCache(true);
            var oldMatrix = GUI.matrix;
            // A shared percentage for all styles; panel contents retain their own proportions.
            var scale = CurrentHudScale();
            try
            {
                if (settings.ShowHud && repaint) DrawLayout(scale);
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                if (repaint && !string.IsNullOrEmpty(toast) && Time.realtimeSinceStartup < toastUntil)
                {
                    var width = Math.Min(620f, Screen.width / scale - 40f);
                    DrawToast(new Rect((Screen.width / scale - width) * 0.5f, 24f, width, 46f), toast);
                }
            }
            finally { GUI.matrix = oldMatrix; }
            var use = ProvisionUseAction.Active;
            if (use != null)
            {
                var box = new Rect((Screen.width - 280f) / 2f, Screen.height * 0.72f, 280f, 62f);
                var russian = ModLocalization.IsRussian;
                if (displayedUse != use || useTextRussian != russian || Time.realtimeSinceStartup >= nextUseTextRefresh)
                {
                    displayedUse = use; useTextRussian = russian; nextUseTextRefresh = Time.realtimeSinceStartup + .1f;
                    useTitle = ModLocalization.Provision(use.Kind) + (use.IsPortion ? " — " +
                        ModLocalization.Number(use.Progress * 100f, "F1") + "%" : string.Empty);
                    useHint = use.IsPortion ? ModLocalization.Text("Удерживайте ЛКМ — есть / пить", "Hold LMB to eat / drink") :
                        ModLocalization.Text("ПКМ / убрать предмет — отмена", "RMB / put item away to cancel");
                }
                DrawProvisionStatus(box, useTitle, use.Progress, useHint);
            }
        }


        private void DrawPlate(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, plateStyle);
        }

        private IEnumerator CollapseFade()
        {
            ScreenFade.Fade(Color.black, 0.45f);
            yield return new WaitForSecondsRealtime(1.8f);
            ScreenFade.Fade(Color.clear, 1.0f);
            collapseCoroutine = null;
        }

        public void RefreshNow() { RefreshDisplayCache(true); }

        private void RefreshDisplayCache(bool force = false)
        {
            using (new SurvivalProfiler("DVSurvival.HUD.Refresh"))
            {
            var now = Time.realtimeSinceStartup;
            if (!force && now < nextDisplayRefresh) return;
            nextDisplayRefresh = now + 0.25f;
            var state = runtime == null ? null : runtime.HudState;
            if (state == null) return;
            var warningEvents = warningTracker.Observe(state);
            // Threshold warnings live on the affected gauge; only sleep explanations use toasts.
            if (settings.ShowStatusWarnings) QueueStateWarning(warningEvents &
                (SurvivalWarningEvent.LowRestOneDay | SurvivalWarningEvent.ExhaustionSoon |
                 SurvivalWarningEvent.ExhaustionStarted | SurvivalWarningEvent.HallucinationSoon |
                 SurvivalWarningEvent.HallucinationStarted | SurvivalWarningEvent.LowRestRecovered));
            healthFraction = state.Health / 100f;
            hungerFraction = state.Hunger / 100f;
            hydrationFraction = state.Hydration / 100f;
            restFraction = state.Rest / 100f;
            cachedBodyTemperature = state.BodyTemperatureCelsius;
            temperatureFraction = Mathf.InverseLerp(34f, 40f, cachedBodyTemperature);
            needFractions[0] = healthFraction; needFractions[1] = hungerFraction;
            needFractions[2] = hydrationFraction; needFractions[3] = restFraction;
            var ambient = runtime.CurrentEnvironment == null ? 0f : runtime.CurrentEnvironment.AmbientTemperatureCelsius;
            presenter.Capture(state, ambient, settings.ShowNumericValues, settings.ShowStatusWarnings);
            healthLabel = presenter.Names[0]; hungerLabel = presenter.Names[1];
            hydrationLabel = presenter.Names[2]; restLabel = presenter.Names[3];
            healthText = presenter.Numbers[0]; hungerText = presenter.Numbers[1];
            hydrationText = presenter.Numbers[2]; restText = presenter.Numbers[3];
            for (int i = 0; i < 4; i++) { needNames[i] = presenter.Names[i]; needValues[i] = presenter.Alerts[i]; }
            ambientText = presenter.Ambient; compactBodyText = presenter.Body; legacyBodyText = presenter.LegacyBody;
            warningTitle = presenter.WarningTitle; warningText = string.Empty;
            }
        }

        private void QueueStateWarning(SurvivalWarningEvent events)
        {
            if (events == SurvivalWarningEvent.None) return;
            if ((events & SurvivalWarningEvent.LowRestRecovered) != 0)
                ClearLowRestWarnings();
            if ((events & SurvivalWarningEvent.HallucinationStarted) != 0)
            {
                QueueWarning(ModLocalization.Text(
                    "Из-за галлюцинаций спидометр сначала показывает скорость в 1,5 раза меньше. Искажение усиливается каждые игровые сутки.",
                    "Hallucinations initially make the speedometer read 1.5 times lower. The distortion increases every game day."), 100, true);
                return;
            }
            if ((events & SurvivalWarningEvent.ExhaustionStarted) != 0)
            {
                QueueWarning(ModLocalization.Text(
                    "Началось сильное истощение от недосыпа. Жажда временно усилилась.",
                    "Severe exhaustion from sleep deprivation has begun. Thirst is temporarily stronger."), 90, true);
                return;
            }
            if ((events & (SurvivalWarningEvent.Hypothermia | SurvivalWarningEvent.Hyperthermia |
                SurvivalWarningEvent.CriticalHealth)) != 0)
            {
                QueueWarning(ModLocalization.Text(
                    "Опасное состояние организма — здоровье продолжит снижаться.",
                    "Dangerous physical condition — health will continue to fall."), 80);
                return;
            }
            if ((events & (SurvivalWarningEvent.Starving | SurvivalWarningEvent.Dehydrated |
                SurvivalWarningEvent.ExtremeFatigue)) != 0)
            {
                QueueWarning(ModLocalization.Text(
                    "Критическая потребность: срочно восстановите отмеченные показатели.",
                    "Critical need: restore the marked values immediately."), 70);
                return;
            }
            if ((events & SurvivalWarningEvent.HallucinationSoon) != 0)
            {
                QueueWarning(ModLocalization.Text(
                    "Сильный недосып может вызвать галлюцинации и исказить спидометр.",
                    "Severe sleep deprivation can cause hallucinations and distort the speedometer."), 65, true);
                return;
            }
            if ((events & SurvivalWarningEvent.ExhaustionSoon) != 0)
            {
                QueueWarning(ModLocalization.Text(
                    "Недосып усиливается. Скоро наступит сильное истощение.",
                    "Sleep deprivation is worsening. Severe exhaustion will begin soon."), 60, true);
                return;
            }
            var runEvents = SurvivalWarningEvent.RunHealth | SurvivalWarningEvent.RunHunger |
                SurvivalWarningEvent.RunHydration | SurvivalWarningEvent.RunRest;
            if ((events & runEvents) != 0)
            {
                QueueWarning(ModLocalization.Text(
                    "Бег недоступен: один или несколько показателей слишком низкие.",
                    "Running is unavailable: one or more needs are too low."), 50);
                return;
            }
            if ((events & SurvivalWarningEvent.LowRestOneDay) != 0)
            {
                QueueWarning(ModLocalization.Text(
                    "Вы давно не высыпались. Хорошо отдохните, чтобы избежать последствий.",
                    "You have not rested properly for a long time. Sleep well to avoid consequences."), 40, true);
                return;
            }
            if ((events & SurvivalWarningEvent.LowRestRecovered) != 0)
                QueueWarning(ModLocalization.Text(
                    "Вы выспались: последствия недосыпа сняты.",
                    "You are well rested: the effects of sleep deprivation are gone."), 20, true);
        }

        private void QueueWarning(string message, int priority, bool isLowRestWarning = false)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (Time.realtimeSinceStartup >= toastUntil && string.IsNullOrEmpty(pendingWarning))
            {
                toast = message;
                toastUntil = Time.realtimeSinceStartup + 5f;
                toastIsLowRestWarning = isLowRestWarning;
                return;
            }
            if (priority <= pendingWarningPriority) return;
            pendingWarning = message;
            pendingWarningPriority = priority;
            pendingWarningIsLowRest = isLowRestWarning;
        }

        private void ClearLowRestWarnings()
        {
            if (pendingWarningIsLowRest)
            {
                pendingWarning = string.Empty;
                pendingWarningPriority = 0;
                pendingWarningIsLowRest = false;
            }
            if (!toastIsLowRestWarning) return;
            toast = string.Empty;
            toastUntil = 0f;
            toastIsLowRestWarning = false;
        }

        private void FlushPendingWarning()
        {
            if (!settings.ShowStatusWarnings)
            {
                pendingWarning = string.Empty;
                pendingWarningPriority = 0;
                pendingWarningIsLowRest = false;
                return;
            }
            if (string.IsNullOrEmpty(pendingWarning) || Time.realtimeSinceStartup < toastUntil) return;
            toast = pendingWarning;
            toastUntil = Time.realtimeSinceStartup + 5f;
            toastIsLowRestWarning = pendingWarningIsLowRest;
            pendingWarning = string.Empty;
            pendingWarningPriority = 0;
            pendingWarningIsLowRest = false;
        }

        private string FormatNeed(float value)
        {
            return settings.ShowNumericValues ? ModLocalization.Number(value, "F0") + "%" : string.Empty;
        }

        private void EnsureStyles()
        {
            if (labelStyle != null) return;
            artwork = new HudArtwork();
            hudFont = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "DejaVu Serif", "Times New Roman" }, 14);
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                alignment = TextAnchor.MiddleCenter,
                font = hudFont,
                fontStyle = FontStyle.Normal,
                fontSize = 12,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                normal = { textColor = new Color(0.90f, 0.87f, 0.80f) }
            };
            plateStyle = new GUIStyle
            {
                border = new RectOffset(16, 16, 16, 16),
                normal = { background = artwork.Panel }
            };
            hudTextStyle = new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            legacyTitleStyle = new GUIStyle(hudTextStyle) { fontSize = 14, normal = { textColor = Brass } };
            legacyWarningStyle = new GUIStyle(hudTextStyle) { wordWrap = true, fontSize = 11 };
            modernLeft = new GUIStyle(GUI.skin.label) {
                richText = true,
                fontSize = 12, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0), normal = { textColor = new Color(0.93f, 0.96f, 1f) }
            };
            modernCenter = new GUIStyle(modernLeft) { alignment = TextAnchor.MiddleCenter };
            modernNumber = new GUIStyle(modernCenter) { fontStyle = FontStyle.Bold, fontSize = 13 };
            modernSmall = new GUIStyle(modernLeft) { fontSize = 11 };
            modernOverlayTitle = new GUIStyle(modernCenter) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = true };
            brassOverlayTitle = new GUIStyle(labelStyle) { fontSize = 14, fontStyle = FontStyle.Bold, wordWrap = true };
        }

        private static void DrawSolid(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void OnDestroy()
        {
            FinishHudDrag();
            if (artwork != null) artwork.Dispose();
            if (hudFont != null) UnityEngine.Object.Destroy(hudFont);
        }
    }
}
