using System;

namespace DVSurvival.Core
{
    // Stable numeric IDs are persisted in UMM Settings.xml.
    public static class HudLayout
    {
        public const int Count = 6;
        public const int Legacy = 5;
        public const float ReferenceWidth = 338f;
        public static int Validate(int style) { return style >= 0 && style < Count ? style : 0; }
        public static float Width(int style)
        {
            switch (Validate(style)) { case 1: return 292; case 2: return 320; case 3: return 248; case Legacy: return 338; default: return 280; }
        }
        public static float Height(int style)
        {
            switch (Validate(style)) { case 1: return 116; case 2: return 74; case 3: return 74; case 4: return 140; case Legacy: return 134; default: return 122; }
        }
        public static float ContentScale(int style) { return ReferenceWidth / Width(style); }
        public static float FitScale(int style, float requested, float screenWidth, float screenHeight, float compactScale = 1f)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested)) requested = 1;
            requested = Math.Max(0.65f, Math.Min(1.75f, requested));
            // Same physical needs width for every style; retain the artwork's aspect ratio.
            // The old compact multiplier remains an ignored argument for settings compatibility.
            // Use a common fit limit so changing styles never changes the effective percentage.
            float maxHeight = 0;
            for (int i = 0; i < Count; i++) maxHeight = Math.Max(maxHeight, Height(i) * ContentScale(i));
            return Math.Max(0.1f, Math.Min(requested,
                Math.Min(screenWidth / 730f, screenHeight / (maxHeight + 102f))));
        }
    }
}
