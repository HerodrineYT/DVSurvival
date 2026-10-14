using DVSurvival.Core;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class HudLayoutTests
    {
        [Theory]
        [InlineData(0, 280, 122)]
        [InlineData(1, 292, 116)]
        [InlineData(2, 320, 74)]
        [InlineData(3, 248, 74)]
        [InlineData(4, 280, 140)]
        [InlineData(5, 338, 134)]
        public void FiveCompactLayoutsHaveStableDimensions(int style, float width, float height)
        {
            Assert.Equal(style, HudLayout.Validate(style));
            Assert.Equal(width, HudLayout.Width(style)); Assert.Equal(height, HudLayout.Height(style));
            Assert.Equal(1f, HudLayout.FitScale(style, 1, 1920, 1080));
            Assert.Equal(338f, width * HudLayout.ContentScale(style), 3);
        }
        [Theory]
        [InlineData(-1)]
        [InlineData(6)]
        [InlineData(int.MaxValue)]
        public void InvalidSavedSelectionFallsBackToRings(int style)
        {
            Assert.Equal(0, HudLayout.Validate(style));
            Assert.Equal(280f, HudLayout.Width(style));
        }
        [Theory]
        [InlineData(640, 480)]
        [InlineData(800, 600)]
        [InlineData(1280, 720)]
        [InlineData(1920, 1080)]
        [InlineData(2560, 1440)]
        public void AllStylesFitIncludingWarningAtMaximumScale(float width, float height)
        {
            for (var style = 0; style < HudLayout.Count; style++)
            {
                var scale = HudLayout.FitScale(style, 1.75f, width, height);
                Assert.Equal(HudLayout.FitScale(0, 1.75f, width, height),scale);
                var needs=HudPlacement.Default(style,HudPlacement.Needs,scale,width,height);
                var air=HudPlacement.Default(style,HudPlacement.Air,scale,width,height);
                var body=HudPlacement.Default(style,HudPlacement.Body,scale,width,height);
                foreach(var rect in new[] { needs,air,body })
                {
                    Assert.InRange(rect.X,0,width-rect.Width+.01f);
                    Assert.InRange(rect.Y,0,height-rect.Height+.01f);
                }
                Assert.True(needs.Y+needs.Height<=air.Y+.01f);
                Assert.True(air.X+air.Width<=body.X+.01f);
                Assert.InRange(scale, 0.1f, 1.75f);
            }
        }
        [Fact]
        public void BadScaleIsSanitized()
        {
            Assert.Equal(1f, HudLayout.FitScale(0, float.NaN, 1920, 1080));
            Assert.Equal(1f, HudLayout.FitScale(0, float.PositiveInfinity, 1920, 1080));
            Assert.Equal(0.65f, HudLayout.FitScale(0, -5, 1920, 1080));
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
        public void ObsoleteCompactMultiplierNoLongerChangesUniformPercentage(int style)
        {
            Assert.Equal(1f, HudLayout.FitScale(style, 1, 2560, 1440, 1.75f));
            Assert.Equal(1f, HudLayout.FitScale(style, 1, 2560, 1440, 2f));
            Assert.Equal(1f, HudLayout.FitScale(style, 1, 2560, 1440, 3f));
            var small = HudLayout.FitScale(style, 1.75f, 640, 480, 3f);
            Assert.True(730f * small <= 640.01f);
            Assert.True(HudLayout.Height(style) * HudLayout.ContentScale(style) * small <= 480.01f);
        }

        [Fact]
        public void OriginalHudIgnoresNewLayoutsMultiplier()
        {
            Assert.Equal(5, HudLayout.Validate(HudLayout.Legacy));
            Assert.Equal(1f, HudLayout.FitScale(HudLayout.Legacy, 1, 1920, 1080, 3));
        }
    }
}
