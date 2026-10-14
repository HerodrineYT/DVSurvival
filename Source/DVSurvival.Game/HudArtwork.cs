using System;
using System.IO;
using UnityEngine;

namespace DVSurvival.Mod
{
    // All artwork is embedded: a single load per HUD, then fixed atlas lookups on Repaint.
    internal sealed class HudArtwork : IDisposable
    {
        public readonly Texture2D Panel;
        private readonly Texture2D dial;
        private readonly Texture2D rings;
        private readonly Texture2D icons;
        private readonly Texture2D thermal;
        private readonly Rect[] ringUvs = new Rect[101];

        public HudArtwork()
        {
            Panel = Load("panel");
            dial = Load("dial");
            rings = Load("rings");
            icons = Load("icons");
            thermal = Load("thermal");
            for (var i = 0; i <= 100; i++)
                ringUvs[i] = new Rect((i % 11) / 11f, 1f - (i / 11 + 1) / 10f, 1f / 11f, 0.1f);
        }

        public void DrawDial(Rect rect, float value, Color color)
        {
            GUI.DrawTexture(rect, dial);
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(rect, rings, ringUvs[Mathf.Clamp(Mathf.RoundToInt(value * 100f), 0, 100)]);
            GUI.color = old;
        }

        public void DrawIcon(Rect rect, int index, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(rect, icons, new Rect(index / 8f, 0f, 1f / 8f, 1f));
            GUI.color = old;
        }

        public void DrawRing(Rect rect, float value, Color color)
        {
            var old = GUI.color;
            GUI.color = new Color(0.16f, 0.20f, 0.25f, 1f);
            GUI.DrawTextureWithTexCoords(rect, rings, ringUvs[100]);
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(rect, rings, ringUvs[Mathf.Clamp(Mathf.RoundToInt(value * 100f), 0, 100)]);
            GUI.color = old;
        }

        public void DrawThermal(Rect rect)
        {
            GUI.DrawTexture(rect, thermal);
        }

        private static Texture2D Load(string name)
        {
            using (var stream = typeof(HudArtwork).Assembly.GetManifestResourceStream(
                "DVSurvival.Hud." + name + ".png"))
            {
                if (stream == null) throw new InvalidOperationException("Missing HUD artwork: " + name);
                {
                    var bytes = EmbeddedBytes.Read(stream);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(texture, bytes, true))
                    {
                        UnityEngine.Object.Destroy(texture);
                        throw new InvalidOperationException("Invalid HUD artwork: " + name);
                    }
                    texture.name = "DVSurvival HUD " + name;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                    return texture;
                }
            }
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(Panel);
            UnityEngine.Object.Destroy(dial);
            UnityEngine.Object.Destroy(rings);
            UnityEngine.Object.Destroy(icons);
            UnityEngine.Object.Destroy(thermal);
        }
    }
}
