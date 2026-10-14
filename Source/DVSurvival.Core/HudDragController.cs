using System;

namespace DVSurvival.Core
{
    // No Unity objects, allocations, strings, timers or disk writes in the drag loop.
    public sealed class HudDragController
    {
        public const float HoldSeconds = 2f;
        private const float ArmTolerancePixels = 8f;
        private float pressX, pressY, pressTime;
        private HudRect original;
        public int Panel { get; private set; } = -1;
        public bool IsDragging { get; private set; }
        public bool HasMoved { get; private set; }
        public HudRect Rect { get; private set; }
        public float HoldProgress(float now)
        { return Panel < 0 ? 0 : Math.Max(0, Math.Min(1, (now - pressTime) / HoldSeconds)); }

        public void Begin(int panel, HudRect rect, float x, float y, float now)
        {
            Cancel();
            if (panel < 0 || panel >= HudPlacement.PanelCount || !rect.Contains(x, y)) return;
            Panel = panel; original = Rect = rect; pressX = x; pressY = y; pressTime = now;
        }

        public bool Move(float x, float y, float now, float screenWidth, float screenHeight)
        {
            if (Panel < 0) return false;
            float dx = x - pressX, dy = y - pressY;
            if (!IsDragging)
            {
                if (now - pressTime < HoldSeconds)
                {
                    if (dx * dx + dy * dy > ArmTolerancePixels * ArmTolerancePixels) Cancel();
                    return false;
                }
                IsDragging = true;
            }
            var next = HudPlacement.Clamp(new HudRect(original.X + dx, original.Y + dy,
                original.Width, original.Height), screenWidth, screenHeight);
            if (next.X == Rect.X && next.Y == Rect.Y) return false;
            Rect = next; HasMoved = true;
            return true;
        }

        public void Cancel() { Panel = -1; IsDragging = HasMoved = false; }
    }
}
