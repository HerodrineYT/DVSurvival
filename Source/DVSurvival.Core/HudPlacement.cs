using System;

namespace DVSurvival.Core
{
    public struct HudRect
    {
        public float X, Y, Width, Height;
        public HudRect(float x, float y, float width, float height)
        { X = x; Y = y; Width = width; Height = height; }
        public bool Contains(float x, float y)
        { return x >= X && y >= Y && x < X + Width && y < Y + Height; }
    }

    // Local UMM settings only; never replicated with player state or written to world saves.
    [Serializable]
    public sealed class HudPanelPosition
    {
        public bool IsCustom;
        public float X, Y;
        public void Reset() { IsCustom = false; X = Y = 0; }
    }

    public static class HudPlacement
    {
        public const int Needs = 0, Air = 1, Body = 2, PanelCount = 3;
        public const int PositionCount = HudLayout.Count * PanelCount;
        public static int Index(int style, int panel) { return HudLayout.Validate(style) * PanelCount + panel; }

        public static HudPanelPosition[] ValidatePositions(HudPanelPosition[] positions)
        {
            if (positions == null || positions.Length != PositionCount)
            {
                var replacement = new HudPanelPosition[PositionCount];
                if (positions != null) Array.Copy(positions, replacement, Math.Min(positions.Length, replacement.Length));
                positions = replacement;
            }
            for (int i = 0; i < positions.Length; i++)
            {
                if (positions[i] == null) positions[i] = new HudPanelPosition();
                var p = positions[i];
                if (!Finite(p.X) || !Finite(p.Y)) p.Reset();
                p.X = Clamp01(p.X); p.Y = Clamp01(p.Y);
            }
            return positions;
        }

        public static HudRect Default(int style, int panel, float scale, float screenWidth, float screenHeight)
        {
            float width = (panel == Body ? 312f : HudLayout.ReferenceWidth) * scale;
            float height = (panel == Needs ? HudLayout.Height(style) * HudLayout.ContentScale(style) :
                panel == Air ? 36f : 62f) * scale;
            float x = 28f * scale;
            float y = screenHeight - (32f * scale + height);
            if (panel == Needs) y -= 38f * scale;
            if (panel == Body) x = Math.Max((screenWidth - width) * .5f, (28f + 338f + 24f) * scale);
            return Clamp(new HudRect(x, y, width, height), screenWidth, screenHeight);
        }

        public static HudRect Resolve(HudPanelPosition saved, HudRect fallback, float screenWidth, float screenHeight)
        {
            if (saved != null && saved.IsCustom)
            {
                fallback.X = Clamp01(saved.X) * Math.Max(0, screenWidth - fallback.Width);
                fallback.Y = Clamp01(saved.Y) * Math.Max(0, screenHeight - fallback.Height);
            }
            return Clamp(fallback, screenWidth, screenHeight);
        }

        public static void Remember(HudPanelPosition saved, HudRect rect, float screenWidth, float screenHeight)
        {
            rect = Clamp(rect, screenWidth, screenHeight);
            saved.X = screenWidth > rect.Width ? rect.X / (screenWidth - rect.Width) : 0;
            saved.Y = screenHeight > rect.Height ? rect.Y / (screenHeight - rect.Height) : 0;
            saved.IsCustom = true;
        }

        public static HudRect Clamp(HudRect rect, float screenWidth, float screenHeight)
        {
            rect.X = Math.Max(0, Math.Min(Finite(rect.X) ? rect.X : 0, Math.Max(0, screenWidth - rect.Width)));
            rect.Y = Math.Max(0, Math.Min(Finite(rect.Y) ? rect.Y : 0, Math.Max(0, screenHeight - rect.Height)));
            return rect;
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static float Clamp01(float value) { return Finite(value) ? Math.Max(0, Math.Min(1, value)) : 0; }
    }
}
