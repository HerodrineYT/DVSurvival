// Offline command recorder for the actual production layout methods; no Unity runtime required.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DVSurvival.Core;
using UnityEngine;
using Xunit;

namespace UnityEngine
{
    internal struct Rect
    {
        public float x, y, width, height;
        public float yMax => y + height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }
    internal struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
    }
    internal static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Clamp01(float x) => Math.Clamp(x, 0, 1);
    }
    internal static class Screen { public static float height = 260; public const float width = 1280; }
    internal sealed class GUIStyle
    {
        public string align = "left";
        public int size = 12;
        public string font = "Arial";
    }
    internal static class GUI
    {
        [ThreadStatic] public static List<object> Commands;
        [ThreadStatic] public static float OffsetX, OffsetY, Scale;
        [ThreadStatic] public static int Panel;
        public static Rect Transform(Rect rect) => new Rect(OffsetX+rect.x*Scale, OffsetY+rect.y*Scale,
            rect.width*Scale, rect.height*Scale);
        public static void Label(Rect rect, string text, GUIStyle style)
        {
            Commands.Add(new { kind = "text", panel=Panel, rect=Transform(rect), text,
                style=new { style.align, style.font, size=style.size*Scale, bold=style.size==13 } });
        }
    }
}
namespace DVSurvival.Mod
{
    internal sealed class PreviewSettings { public int HudStyle; }
    internal sealed class HudArtwork
    {
        public void DrawIcon(Rect rect, int index, Color color) => GUI.Commands.Add(new { kind = "icon", panel=GUI.Panel, rect=GUI.Transform(rect), index, color });
        public void DrawThermal(Rect rect) => GUI.Commands.Add(new { kind = "thermal", panel=GUI.Panel, rect=GUI.Transform(rect) });
        public void DrawRing(Rect rect, float value, Color color) => GUI.Commands.Add(new { kind = "ring", panel=GUI.Panel, rect=GUI.Transform(rect), value, color });
        public void DrawDial(Rect rect, float value, Color color) => GUI.Commands.Add(new { kind = "dial", panel=GUI.Panel, rect=GUI.Transform(rect), value, color });
    }
    internal sealed partial class SurvivalHud
    {
        private readonly PreviewSettings settings = new PreviewSettings();
        private readonly HudArtwork artwork = new HudArtwork();
        private static readonly Color Brass = new Color(.82f, .67f, .44f);
        private readonly GUIStyle labelStyle = new GUIStyle { align = "center", font = "Georgia" };
        private readonly GUIStyle hudTextStyle = new GUIStyle { font = "Georgia" };
        private readonly GUIStyle legacyTitleStyle = new GUIStyle { font = "Georgia", size = 14 };
        private readonly GUIStyle legacyWarningStyle = new GUIStyle { font = "Georgia", size = 11 };
        private string warningTitle = "Требуется внимание", warningText = "";
        private string ambientText = "Воздух: -3.1 °C";
        private string legacyBodyText = "Температура тела 37.0 °C";
        private float cachedBodyTemperature = 37, temperatureFraction = .5f;
        private static void DrawSolid(Rect rect, Color color) => GUI.Commands.Add(new { kind = "solid", panel=GUI.Panel, rect=GUI.Transform(rect), color });
        private void DrawPlate(Rect rect) => GUI.Commands.Add(new { kind = "plate", panel=GUI.Panel, rect=GUI.Transform(rect), scale=GUI.Scale });

        internal List<object> RecordOverlays(int style)
        {
            GUI.Commands = new List<object>();
            GUI.OffsetX=GUI.OffsetY=0; GUI.Scale=1; GUI.Panel=-1;
            settings.HudStyle = style;
            modernCenter = new GUIStyle { align = "center" };
            modernOverlayTitle = new GUIStyle { align = "center", size = 14 };
            brassOverlayTitle = new GUIStyle { align = "center", size = 14, font = "Georgia" };
            DrawToast(new Rect(330, 24, 620, 46), "Сон восстановлен / Rest restored");
            DrawProvisionStatus(new Rect(500, 500, 280, 62), "Кофе — 63,5%", .635f, "Удерживайте ЛКМ — есть / пить");
            return GUI.Commands;
        }

        internal List<object> Record(int style, bool warning, bool english, HudPanelPosition[] positions=null)
        {
            GUI.Commands = new List<object>();
            settings.HudStyle = style;
            Screen.height = 720;
            modernLeft = new GUIStyle(); modernCenter = new GUIStyle { align = "center" };
            modernNumber = new GUIStyle { align = "center", size = 13 }; modernSmall = new GUIStyle { size = 11 };
            compactBodyText = english ? "Body: 37.0 °C" : "Тело: 37.0 °C";
            legacyBodyText = english ? "Body temperature 37.0 °C" : "Температура тела 37.0 °C";
            ambientText = english ? "Ambient: -3.1 °C" : "Воздух: -3.1 °C";
            warningTitle = english ? "Needs attention" : "Требуется внимание";
            warningText = warning ? (english ? "Health" : "Здоровье") : "";
            var names = english ? new[] { "Health", "Food", "Water", "Rest" } : new[] { "Здоровье", "Сытость", "Вода", "Сон" };
            var values = new[] { .85f, .72f, .64f, .9f };
            for (var i = 0; i < 4; i++) { needNames[i] = names[i]; needFractions[i] = values[i]; needValues[i] = (values[i] * 100).ToString("F0") + "%"; }
            if (warning)
            {
                needFractions[0] = .1f;
                needValues[0] = NeedAlertText.Format(new SurvivalState { Health = 10 }, 0, "10%", true);
            }
            positions=HudPlacement.ValidatePositions(positions);
            float scale=HudLayout.FitScale(style,1,Screen.width,Screen.height);
            for(int panel=0;panel<HudPlacement.PanelCount;panel++)
            {
                var rect=HudPlacement.Resolve(positions[HudPlacement.Index(style,panel)],
                    HudPlacement.Default(style,panel,scale,Screen.width,Screen.height),Screen.width,Screen.height);
                GUI.OffsetX=rect.X; GUI.OffsetY=rect.Y; GUI.Panel=panel;
                GUI.Scale=scale*(panel==HudPlacement.Needs?HudLayout.ContentScale(style):1);
                DrawHudPanel(panel,HudLayout.Validate(style));
            }
            return GUI.Commands;
        }
    }
}
namespace DVSurvival.Tests
{
    public sealed class HudRenderHarnessTests
    {
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void ProductionLayoutsShowAllSixValuesAndRemainInBounds(int style)
        {
            foreach (var english in new[] { false, true })
            foreach (var warning in new[] { false, true })
            {
                var commands = new Mod.SurvivalHud().Record(style, warning, english);
                var json = JsonSerializer.Serialize(commands, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true });
                using (var document = JsonDocument.Parse(json))
                {
                var labels = document.RootElement.EnumerateArray().Where(x => x.GetProperty("kind").GetString() == "text")
                    .Select(x => x.GetProperty("text").GetString()).ToArray();
                foreach (var value in new[] { warning ? "<color=#FF5555>! 10%</color>" : "85%", "72%", "64%", "90%" }) Assert.Contains(value, labels);
                Assert.Contains(labels, x => x.Contains("37.0")); Assert.Contains(labels, x => x.Contains("-3.1"));
                Assert.DoesNotContain("Needs attention", labels);
                Assert.DoesNotContain("Требуется внимание", labels);
                foreach (var command in document.RootElement.EnumerateArray())
                {
                    var rect = command.GetProperty("rect");
                    Assert.InRange(rect.GetProperty("x").GetSingle(), 0, Screen.width);
                    Assert.True(rect.GetProperty("x").GetSingle() + rect.GetProperty("width").GetSingle() <= Screen.width);
                    Assert.InRange(rect.GetProperty("y").GetSingle(), 0, Screen.height - 20);
                    Assert.True(rect.GetProperty("y").GetSingle() + rect.GetProperty("height").GetSingle() <= Screen.height - 20);
                }
                var output = Environment.GetEnvironmentVariable("DVSURVIVAL_HUD_PREVIEW_DIR");
                if (!string.IsNullOrEmpty(output))
                {
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output, $"style-{style}-{(english ? "en" : "ru")}{(warning ? "-alert" : "")}.json"), json);
                }
                }
            }
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void NumericValuesNeverCoverVisualIndicators(int style)
        {
            var commands = new Mod.SurvivalHud().Record(style, false, false);
            var json = JsonSerializer.Serialize(commands, new JsonSerializerOptions { IncludeFields = true });
            using (var document = JsonDocument.Parse(json))
            {
                var all = document.RootElement.EnumerateArray().ToArray();
                var values = all.Where(x => x.GetProperty("kind").GetString() == "text" &&
                    (x.GetProperty("text").GetString().EndsWith("%") ||
                     x.GetProperty("text").GetString().Contains("37.0") ||
                     x.GetProperty("text").GetString().Contains("-3.1"))).ToArray();
                var visuals = all.Where(x =>
                {
                    var kind = x.GetProperty("kind").GetString();
                    if (kind == "ring" || kind == "dial" || kind == "thermal" || kind == "icon") return true;
                    if (kind != "solid") return false;
                    var rect = x.GetProperty("rect");
                    var height = rect.GetProperty("height").GetSingle();
                    var width = rect.GetProperty("width").GetSingle();
                    return (height <= 10f && width <= 150f) ||
                        (width <= 2.1f && height <= 12.1f);
                }).ToArray();
                // The supplied classic-HUD reference places the numeric label rect exactly at
                // the dial edge. Georgia's built-in top inset leaves the visible glyphs clear.
                var clearance = style == HudLayout.Legacy ? 0f : 10f;
                foreach (var value in values)
                foreach (var visual in visuals)
                {
                    // Rings intentionally contain the percentage. The dedicated test below
                    // checks its placement inside the hollow center and below the icon.
                    var percentageInRing = (style == 0 || style == 4) && value.GetProperty("text").GetString().EndsWith("%");
                    if (percentageInRing && (visual.GetProperty("kind").GetString() == "ring" ||
                        visual.GetProperty("kind").GetString() == "dial")) continue;
                    Assert.False(IntersectsWithClearance(value.GetProperty("rect"),
                            visual.GetProperty("rect"), percentageInRing ? 1f : clearance),
                        $"Numeric text overlaps or violates the intended clearance from {visual.GetProperty("kind").GetString()} in style {style}.");
                }
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(0, true)]
        [InlineData(4, false)]
        [InlineData(4, true)]
        public void RingsAndBrassMoveTogetherTowardsFixedLabels(int style, bool english)
        {
            var commands = new Mod.SurvivalHud().Record(style, false, english);
            using (var document = JsonDocument.Parse(JsonSerializer.Serialize(commands,
                new JsonSerializerOptions { IncludeFields = true })))
            {
            var all = document.RootElement.EnumerateArray().ToArray();
            var rings = all.Where(x => x.GetProperty("kind").GetString() == (style == 0 ? "ring" : "dial")).ToArray();
            var numbers = all.Where(x => x.GetProperty("kind").GetString() == "text" &&
                x.GetProperty("text").GetString().EndsWith("%")).ToArray();
            var icons = all.Where(x => x.GetProperty("kind").GetString() == "icon" &&
                x.GetProperty("index").GetInt32() < 4).ToArray();
            Assert.Equal(4, numbers.Length);
            var names = english ? new[] { "Health", "Food", "Water", "Rest" } :
                new[] { "Здоровье", "Сытость", "Вода", "Сон" };
            for (var i = 0; i < 4; i++)
            {
                var root=HudPlacement.Default(style,HudPlacement.Needs,1,Screen.width,Screen.height);
                var s=HudLayout.ContentScale(style);
                var dx = root.X + (9f + 66f * i)*s;
                var y = root.Y;
                AssertRect(rings[i].GetProperty("rect"), dx, y + (style == 0 ? 30f : 33f)*s, 62f*s, 62f*s);
                AssertRect(icons[i].GetProperty("rect"), dx + 21f*s, y + (style == 0 ? 41f : 46f)*s, 20f*s, 20f*s);
                AssertRect(numbers[i].GetProperty("rect"), dx + 11f*s, y + (style == 0 ? 62f : 67f)*s, 40f*s, 16f*s);
                var name = all.Single(x => x.GetProperty("kind").GetString() == "text" &&
                    x.GetProperty("text").GetString() == names[i]).GetProperty("rect");
                AssertRect(name, dx - 2f*s, y + (style == 0 ? 102f : 105f)*s, 66f*s, (style == 0 ? 16f : 17f)*s);
                Assert.Equal(10f*s, name.GetProperty("y").GetSingle() -
                    rings[i].GetProperty("rect").GetProperty("y").GetSingle() - 62f*s,3);
                Assert.Equal("center", numbers[i].GetProperty("style").GetProperty("align").GetString());
                Assert.Equal((style == 0 ? 13 : 12)*s, numbers[i].GetProperty("style").GetProperty("size").GetSingle(),3);
            }
            }
        }

        [Fact]
        public void LegacyLayoutMatchesSuppliedReferenceAndKeepsDecorationsFixed()
        {
            var commands = new Mod.SurvivalHud().Record(HudLayout.Legacy, false, false);
            var json = JsonSerializer.Serialize(commands, new JsonSerializerOptions { IncludeFields = true });
            using (var document = JsonDocument.Parse(json))
            {
                var all = document.RootElement.EnumerateArray().ToArray();
                var dials = all.Where(x => x.GetProperty("kind").GetString() == "dial")
                    .OrderBy(x => x.GetProperty("rect").GetProperty("x").GetSingle()).ToArray();
                Assert.Equal(4, dials.Length);

                var percentages = all.Where(x => x.GetProperty("kind").GetString() == "text" &&
                    x.GetProperty("text").GetString().EndsWith("%"))
                    .OrderBy(x => x.GetProperty("rect").GetProperty("x").GetSingle()).ToArray();
                Assert.Equal(4, percentages.Length);

                var categoryNames = new[] { "Здоровье", "Сытость", "Вода", "Сон" };
                var labels = all.Where(x => x.GetProperty("kind").GetString() == "text" &&
                    categoryNames.Contains(x.GetProperty("text").GetString()))
                    .OrderBy(x => x.GetProperty("rect").GetProperty("x").GetSingle()).ToArray();
                Assert.Equal(4, labels.Length);

                var needPlates = all.Where(x => x.GetProperty("kind").GetString() == "plate" &&
                    x.GetProperty("rect").GetProperty("width").GetSingle() == 80f)
                    .OrderBy(x => x.GetProperty("rect").GetProperty("x").GetSingle()).ToArray();
                var needIcons = all.Where(x => x.GetProperty("kind").GetString() == "icon" &&
                    x.GetProperty("index").GetInt32() >= 0 && x.GetProperty("index").GetInt32() <= 3)
                    .OrderBy(x => x.GetProperty("rect").GetProperty("x").GetSingle()).ToArray();
                Assert.Equal(4, needPlates.Length);
                Assert.Equal(4, needIcons.Length);

                for (var i = 0; i < 4; i++)
                {
                    var dial = dials[i].GetProperty("rect");
                    var percentage = percentages[i].GetProperty("rect");
                    var label = labels[i].GetProperty("rect");
                    AssertRect(needPlates[i].GetProperty("rect"), 28f + 86f * i, 516f, 80f, 134f);
                    AssertRect(dial, 28f + 86f * i, 516f, 80f, 80f);
                    AssertRect(needIcons[i].GetProperty("rect"), 52f + 86f * i, 538f, 32f, 32f);
                    AssertRect(percentage, 32f + 86f * i, 596f, 72f, 18f);
                    AssertRect(label, 30f + 86f * i, 610f, 76f, 19f);
                    Assert.Equal(0f, percentage.GetProperty("y").GetSingle() -
                        (dial.GetProperty("y").GetSingle() + dial.GetProperty("height").GetSingle()));
                    Assert.Equal(14f, label.GetProperty("y").GetSingle() -
                        percentage.GetProperty("y").GetSingle());
                }

                var ambient = all.Single(x => x.GetProperty("kind").GetString() == "text" &&
                    x.GetProperty("text").GetString().Contains("-3.1"));
                var body = all.Single(x => x.GetProperty("kind").GetString() == "text" &&
                    x.GetProperty("text").GetString().Contains("37.0"));
                var thermal = all.Single(x => x.GetProperty("kind").GetString() == "thermal");
                var ambientPlate = all.Single(x => x.GetProperty("kind").GetString() == "plate" &&
                    x.GetProperty("rect").GetProperty("width").GetSingle() == 338f);
                var bodyPlate = all.Single(x => x.GetProperty("kind").GetString() == "plate" &&
                    x.GetProperty("rect").GetProperty("width").GetSingle() == 312f);
                var bodyIcon = all.Single(x => x.GetProperty("kind").GetString() == "icon" &&
                    x.GetProperty("index").GetInt32() == 4);
                var thermalMarker = all.Single(x => x.GetProperty("kind").GetString() == "icon" &&
                    x.GetProperty("index").GetInt32() == 7);
                AssertRect(ambientPlate.GetProperty("rect"), 28f, 652f, 338f, 36f);
                AssertRect(ambient.GetProperty("rect"), 42f, 657f, 310f, 25f);
                AssertRect(bodyPlate.GetProperty("rect"), 484f, 626f, 312f, 62f);
                AssertRect(bodyIcon.GetProperty("rect"), 496f, 637f, 34f, 34f);
                AssertRect(body.GetProperty("rect"), 542f, 630f, 236f, 20f);
                AssertRect(thermal.GetProperty("rect"), 542f, 668f, 236f, 6f);
                AssertRect(thermalMarker.GetProperty("rect"), 651f, 661f, 18f, 20f);
            }
        }

        private static void AssertRect(JsonElement rect, float x, float y, float width, float height)
        {
            Assert.Equal(x, rect.GetProperty("x").GetSingle(),3);
            Assert.Equal(y, rect.GetProperty("y").GetSingle(),3);
            Assert.Equal(width, rect.GetProperty("width").GetSingle(),3);
            Assert.Equal(height, rect.GetProperty("height").GetSingle(),3);
        }

        private static bool IntersectsWithClearance(JsonElement a, JsonElement b, float clearance)
        {
            var ax=a.GetProperty("x").GetSingle();var ay=a.GetProperty("y").GetSingle();
            var aw=a.GetProperty("width").GetSingle();var ah=a.GetProperty("height").GetSingle();
            var bx=b.GetProperty("x").GetSingle();var by=b.GetProperty("y").GetSingle();
            var bw=b.GetProperty("width").GetSingle();var bh=b.GetProperty("height").GetSingle();
            return ax < bx+bw+clearance && ax+aw > bx-clearance &&
                ay < by+bh+clearance && ay+ah > by-clearance;
        }
    }
}
