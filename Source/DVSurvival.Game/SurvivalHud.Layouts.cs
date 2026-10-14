using DVSurvival.Core;
using UnityEngine;

namespace DVSurvival.Mod
{
    internal sealed partial class SurvivalHud
    {
        private readonly float[] needFractions = new float[4];
        private readonly string[] needValues = new string[4];
        private readonly string[] needNames = new string[4];
        private static readonly Color[] NeedColors = {
            new Color(0.94f, 0.26f, 0.28f), new Color(1f, 0.63f, 0.18f),
            new Color(0.20f, 0.63f, 1f), new Color(0.62f, 0.43f, 1f) };
        private GUIStyle modernLeft, modernCenter, modernNumber, modernSmall;
        private static readonly Color[] LegacyColors = {
            new Color(.76f, .42f, .35f), new Color(.82f, .60f, .28f),
            new Color(.26f, .67f, .82f), new Color(.55f, .47f, .81f) };
        private string compactBodyText = string.Empty;

        private void DrawHudPanel(int panel, int style)
        {
            if (panel == HudPlacement.Air)
            {
                DrawStyledPanel(new Rect(0, 0, 338, 36));
                GUI.Label(new Rect(14, 5, 310, 25), ambientText, UsesBrassFrame ? hudTextStyle : modernSmall);
                return;
            }
            if (panel == HudPlacement.Body) { DrawBodyPanel(style); return; }
            if (style == HudLayout.Legacy) { DrawLegacyNeeds(0, 0); return; }
            DrawStyledPanel(new Rect(0, 0, HudLayout.Width(style), HudLayout.Height(style)));
            switch (style)
            {
                case 1: DrawHorizontalBars(0, 0); break;
                case 2: DrawRibbon(0, 0); break;
                case 3: DrawMinimal(0, 0); break;
                case 4: DrawCompactBrass(0, 0); break;
                default: DrawModernRings(0, 0); break;
            }
        }

        private bool UsesBrassFrame
        {
            get { var style = HudLayout.Validate(settings.HudStyle); return style == 4 || style == HudLayout.Legacy; }
        }

        private void DrawStyledPanel(Rect rect)
        {
            if (UsesBrassFrame) DrawPlate(rect);
            else FlatPanel(rect);
        }

        private static void FlatPanel(Rect rect)
        {
            DrawSolid(rect, new Color(0.045f, 0.068f, 0.095f, 0.83f));
            DrawSolid(new Rect(rect.x, rect.y, rect.width, 1), new Color(0.45f, 0.58f, 0.70f, 0.45f));
        }

        private void NeedIcon(float x, float y, int i, float size = 20)
        {
            artwork.DrawIcon(new Rect(x, y, size, size), i, NeedColors[i]);
        }

        private static void Progress(Rect rect, float fraction, Color color)
        {
            DrawSolid(rect, new Color(0.19f, 0.23f, 0.28f, 1f));
            DrawSolid(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), color);
        }

        private void DrawBodyPanel(int style)
        {
            DrawStyledPanel(new Rect(0, 0, 312, 62));
            if (style == HudLayout.Legacy)
            {
                artwork.DrawIcon(new Rect(12, 11, 34, 34), 4, Brass);
                GUI.Label(new Rect(58, 4, 236, 20), legacyBodyText, legacyTitleStyle);
                var legacyTrack = new Rect(58, 42, 236, 6);
                artwork.DrawThermal(legacyTrack);
                artwork.DrawIcon(new Rect(legacyTrack.x + legacyTrack.width * temperatureFraction - 9,
                    legacyTrack.y - 7, 18, 20), 7, Color.white);
                return;
            }
            var color = cachedBodyTemperature >= 38.5f ? NeedColors[0] :
                (cachedBodyTemperature < 35f ? NeedColors[2] : new Color(0.4f, 0.9f, 0.8f));
            var inset = style == 0 || style == 3 ? 12f : 41f;
            if (inset > 12) artwork.DrawIcon(new Rect(12, 7, 17, 22), 4, color);
            GUI.Label(new Rect(inset, 6, 300 - inset, 18), compactBodyText,
                UsesBrassFrame ? hudTextStyle : modernSmall);
            var track = new Rect(inset, 42, 300 - inset, 4);
            artwork.DrawThermal(track);
            DrawSolid(new Rect(track.x + track.width * temperatureFraction - 1, 38, 2, 12), Color.white);
        }

        private void DrawModernRings(float x, float y)
        {
            for (var i = 0; i < 4; i++)
            {
                var dx = x + 9 + i * 66;
                artwork.DrawRing(new Rect(dx, y + 30, 62, 62), needFractions[i], NeedColors[i]);
                NeedIcon(dx + 21, y + 41, i);
                // Move the whole indicator together, leaving ten pixels before its name.
                GUI.Label(new Rect(dx + 11, y + 62, 40, 16), needValues[i], modernNumber);
                GUI.Label(new Rect(dx - 2, y + 102, 66, 16), needNames[i], modernCenter);
            }
        }

        private void DrawHorizontalBars(float x, float y)
        {
            for (var i = 0; i < 4; i++)
            {
                var dy = y + 8 + i * 28;
                NeedIcon(x + 10, dy + 1, i);
                GUI.Label(new Rect(x + 39, dy, 83, 22), needNames[i], modernLeft);
                Progress(new Rect(x + 123, dy + 7, 104, 9), needFractions[i], NeedColors[i]);
                GUI.Label(new Rect(x + 239, dy, 45, 22), needValues[i], modernNumber);
            }
        }

        private void DrawRibbon(float x, float y)
        {
            for (var i = 0; i < 4; i++)
            {
                var dx = x + 10 + i * 77;
                NeedIcon(dx, y + 10, i);
                GUI.Label(new Rect(dx + 30, y + 9, 37, 21), needValues[i], modernNumber);
                Progress(new Rect(dx, y + 40, 65, 4), needFractions[i], NeedColors[i]);
                GUI.Label(new Rect(dx, y + 48, 70, 16), needNames[i], modernSmall);
            }
        }

        private void DrawMinimal(float x, float y)
        {
            for (var i = 0; i < 4; i++)
            {
                var dx = x + 10 + i * 59;
                NeedIcon(dx + 16, y + 8, i, 18);
                GUI.Label(new Rect(dx, y + 36, 50, 18), needValues[i], modernCenter);
                Progress(new Rect(dx, y + 64, 50, 3), needFractions[i], NeedColors[i]);
            }
        }

        private void DrawLegacyNeeds(float x, float y)
        {
            for (var i = 0; i < 4; i++)
            {
                var dx = x + i * 86;
                DrawPlate(new Rect(dx, y, 80, 134));
                artwork.DrawDial(new Rect(dx, y, 80, 80), needFractions[i], LegacyColors[i]);
                artwork.DrawIcon(new Rect(dx + 24, y + 22, 32, 32), i, LegacyColors[i]);
                GUI.Label(new Rect(dx + 4, y + 80, 72, 18), needValues[i], labelStyle);
                GUI.Label(new Rect(dx + 2, y + 94, 76, 19), needNames[i], labelStyle);
            }
        }

        private void DrawCompactBrass(float x, float y)
        {
            for (var i = 0; i < 4; i++)
            {
                var dx = x + 9 + i * 66;
                artwork.DrawDial(new Rect(dx, y + 33, 62, 62), needFractions[i], NeedColors[i]);
                NeedIcon(dx + 21, y + 46, i);
                GUI.Label(new Rect(dx + 11, y + 67, 40, 16), needValues[i], labelStyle);
                GUI.Label(new Rect(dx - 2, y + 105, 66, 17), needNames[i], labelStyle);
            }
        }
    }
}
