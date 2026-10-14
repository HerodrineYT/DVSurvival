using System.Linq;
using DVSurvival.Core;
using DVSurvival.Mod;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class ShelfDisplayLayoutTests
    {
        [Theory]
        [InlineData(ProvisionKind.Meal)] [InlineData(ProvisionKind.Water)]
        [InlineData(ProvisionKind.Coffee)] [InlineData(ProvisionKind.FirstAid)] [InlineData(ProvisionKind.HeatPack)]
        [InlineData(ProvisionKind.CannedFood)] [InlineData(ProvisionKind.Tea)]
        public void EntireRotatedProductionModelIsInsideReservedShelfSpace(ProvisionKind kind)
        {
            var vertices = ProvisionGeometry.Create(kind).Vertices;
            var layout = new ShelfDisplayLayout(vertices.Min(p => p.X), vertices.Min(p => p.Y), vertices.Min(p => p.Z),
                vertices.Max(p => p.X), vertices.Max(p => p.Y), vertices.Max(p => p.Z));
            Assert.True(layout.Depth <= 0.387f);
            foreach (var p in vertices)
            {
                var x = -p.X + layout.OffsetX; var y = p.Y + layout.OffsetY; var z = -p.Z + layout.OffsetZ;
                Assert.InRange(x, -layout.Width / 2 + 0.029f, layout.Width / 2 - 0.029f);
                Assert.InRange(y, 0.001f, layout.Height);
                Assert.InRange(z, -layout.Depth + 0.039f, -0.039f);
            }
        }
        [Fact]
        public void OffCenterGeometryAlsoCentersOnShelf()
        {
            var layout = new ShelfDisplayLayout(1, 2, 3, 1.2f, 2.3f, 3.1f);
            Assert.Equal(0f, -1.1f + layout.OffsetX, 4);
            Assert.Equal(-layout.Depth / 2, -3.05f + layout.OffsetZ, 4);
            Assert.Equal(.002f, 2 + layout.OffsetY, 4);
        }
    }
}
