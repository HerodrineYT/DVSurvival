using System;
using DVSurvival.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DVSurvival.Mod
{
    internal sealed partial class SurvivalHud
    {
        private readonly HudDragController hudDrag = new HudDragController();
        private readonly HudRect[] panelRects = new HudRect[HudPlacement.PanelCount];
        private int dragStyle;
        private float dragScale, dragScreenWidth, dragScreenHeight;
        private bool originalCustom;
        private float originalX, originalY;

        private float CurrentHudScale()
        { return HudLayout.FitScale(settings.HudStyle, settings.HudScale, Screen.width, Screen.height); }

        private void ResolvePanels(float scale)
        {
            for (int panel = 0; panel < HudPlacement.PanelCount; panel++)
                panelRects[panel] = HudPlacement.Resolve(settings.HudPanelPositions[HudPlacement.Index(settings.HudStyle, panel)],
                    HudPlacement.Default(settings.HudStyle, panel, scale, Screen.width, Screen.height), Screen.width, Screen.height);
        }

        private void UpdateHudDrag(bool visible)
        {
            bool editable = visible && settings.ShowHud && Application.isFocused &&
                Cursor.visible && Cursor.lockState != CursorLockMode.Locked;
            if (!editable) { FinishHudDrag(); return; }
            float scale = CurrentHudScale();
            if (hudDrag.Panel >= 0)
            {
                if (dragStyle != settings.HudStyle || dragScale != scale ||
                    dragScreenWidth != Screen.width || dragScreenHeight != Screen.height)
                { FinishHudDrag(); return; }
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    var original = settings.HudPanelPositions[HudPlacement.Index(dragStyle, hudDrag.Panel)];
                    original.IsCustom = originalCustom; original.X = originalX; original.Y = originalY;
                    hudDrag.Cancel(); return;
                }
                if (!Input.GetMouseButton(0)) { FinishHudDrag(); return; }
                var mouse = Input.mousePosition;
                if (hudDrag.Move(mouse.x, Screen.height - mouse.y, Time.realtimeSinceStartup, Screen.width, Screen.height))
                    HudPlacement.Remember(settings.HudPanelPositions[HudPlacement.Index(dragStyle, hudDrag.Panel)],
                        hudDrag.Rect, Screen.width, Screen.height);
                return;
            }
            if (!Input.GetMouseButtonDown(0) || GUIUtility.hotControl != 0 ||
                (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) return;
            ResolvePanels(scale);
            var pointer = Input.mousePosition;
            for (int panel = HudPlacement.PanelCount - 1; panel >= 0; panel--)
            {
                if (!panelRects[panel].Contains(pointer.x, Screen.height - pointer.y)) continue;
                var position = settings.HudPanelPositions[HudPlacement.Index(settings.HudStyle, panel)];
                originalCustom = position.IsCustom; originalX = position.X; originalY = position.Y;
                dragStyle = settings.HudStyle; dragScale = scale;
                dragScreenWidth = Screen.width; dragScreenHeight = Screen.height;
                hudDrag.Begin(panel, panelRects[panel], pointer.x, Screen.height - pointer.y, Time.realtimeSinceStartup);
                break;
            }
        }

        private void FinishHudDrag()
        {
            bool save = hudDrag.HasMoved;
            hudDrag.Cancel();
            // Exactly one settings write on release/interruption, never during held movement.
            if (!save || runtime == null) return;
            try { runtime.SaveSettings(); }
            catch (Exception error) { Debug.LogWarning("[DVSurvival] Could not save HUD placement: " + error.Message); }
        }

        private void DrawLayout(float scale)
        {
            ResolvePanels(scale);
            var previous = GUI.matrix;
            try
            {
                for (int panel = 0; panel < HudPlacement.PanelCount; panel++)
                {
                    var rect = panelRects[panel];
                    float contentScale = scale * (panel == HudPlacement.Needs ? HudLayout.ContentScale(settings.HudStyle) : 1f);
                    GUI.matrix = Matrix4x4.TRS(new Vector3(rect.X, rect.Y, 0), Quaternion.identity,
                        new Vector3(contentScale, contentScale, 1));
                    DrawHudPanel(panel, HudLayout.Validate(settings.HudStyle));
                }
                if (hudDrag.Panel >= 0)
                {
                    GUI.matrix = Matrix4x4.identity;
                    var rect = panelRects[hudDrag.Panel];
                    var color = hudDrag.IsDragging ? new Color(.4f,.9f,.8f) : Brass;
                    DrawSolid(new Rect(rect.X, rect.Y, rect.Width, 2), color);
                    DrawSolid(new Rect(rect.X, rect.Y + rect.Height - 2, rect.Width, 2), color);
                    DrawSolid(new Rect(rect.X, rect.Y, 2, rect.Height), color);
                    DrawSolid(new Rect(rect.X + rect.Width - 2, rect.Y, 2, rect.Height), color);
                    if (!hudDrag.IsDragging)
                        DrawSolid(new Rect(rect.X, rect.Y + rect.Height - 4,
                            rect.Width * hudDrag.HoldProgress(Time.realtimeSinceStartup), 4), color);
                }
            }
            finally { GUI.matrix = previous; }
        }
    }
}
