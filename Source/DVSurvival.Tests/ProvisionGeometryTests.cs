using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using DVSurvival.Core;
using DVSurvival.Mod;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class ProvisionGeometryTests
    {
        [Fact]
        public void FirstAidClaspsAndHingesBridgeTheCaseSeamOnOppositeEnds()
        {
            var g=ProvisionGeometry.Create(ProvisionKind.FirstAid);
            foreach(float x in new[] { -.052f,.052f })
            {
                var hardware=Enumerable.Range(0,g.Vertices.Count).Where(i=>
                {
                    int tile=(int)(g.TexCoords[i].U*2048/128);
                    return g.TexCoords[i].V<.5f && (tile==9 || tile==5) &&
                        Math.Abs(g.Vertices[i].X-x)<=.012f;
                }).Select(i=>g.Vertices[i]).ToArray();
                var clasp=hardware.Where(p=>p.Y>.1245f).ToArray();
                Assert.NotEmpty(clasp);
                Assert.Contains(clasp,p=>p.Z<-.009f);
                Assert.Contains(clasp,p=>p.Z>.009f);
                var hinge=hardware.Where(p=>p.Y<.004f).ToArray();
                Assert.NotEmpty(hinge);
                Assert.Contains(hinge,p=>p.Z<-.009f);
                Assert.Contains(hinge,p=>p.Z>.009f);
                Assert.DoesNotContain(hardware,p=>p.Y>.105f && p.Y<.123f && p.Z<-.024f);
            }
            var handle=Enumerable.Range(0,g.Vertices.Count).Where(i=>
                g.TexCoords[i].V<.5f && (int)(g.TexCoords[i].U*2048/128)==9 &&
                Math.Abs(g.Vertices[i].X)<.041f && g.Vertices[i].Y>.119f)
                .Select(i=>g.Vertices[i]).ToArray();
            Assert.NotEmpty(handle);
            Assert.All(handle,p=>Assert.True(p.Z>0,"Handle mounts must not join the opening halves."));
        }

        [Theory]
        [InlineData(ProvisionKind.Meal)]
        [InlineData(ProvisionKind.Water)]
        [InlineData(ProvisionKind.Coffee)]
        [InlineData(ProvisionKind.FirstAid)]
        [InlineData(ProvisionKind.HeatPack)]
        [InlineData(ProvisionKind.CannedFood)]
        [InlineData(ProvisionKind.Tea)]
        public void ModelsHaveBoundedGeometryAndOutwardPrintedLabels(ProvisionKind kind)
        {
            var g = ProvisionGeometry.Create(kind);
            Assert.Equal(g.Vertices.Count, g.TexCoords.Count);
            Assert.Equal(g.Vertices.Count, g.Normals.Count);
            Assert.InRange(g.Triangles.Count / 3, 20, 1600);
            Assert.Equal(0, g.Triangles.Count % 3);
            foreach (var p in g.Vertices)
            {
                Assert.InRange(p.X, -.175f, .175f);
                Assert.InRange(p.Y, 0f, .3f);
                Assert.InRange(p.Z, -.1f, .1f);
            }
            foreach (var uv in g.TexCoords)
            {
                Assert.InRange(uv.U, 0f, 1f);
                Assert.InRange(uv.V, 0f, 1f);
            }
            foreach (var normal in g.Normals)
                Assert.InRange(normal.X*normal.X+normal.Y*normal.Y+normal.Z*normal.Z, .999f, 1.001f);
            foreach (int index in g.Triangles) Assert.InRange(index, 0, g.Vertices.Count - 1);
            for (int i = g.LabelStart; i < g.Triangles.Count; i += 3)
            {
                var a=g.Vertices[g.Triangles[i]]; var b=g.Vertices[g.Triangles[i+1]]; var c=g.Vertices[g.Triangles[i+2]];
                float normalZ=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
                Assert.True(normalZ < 0, "Labels must face -Z, never draw mirrored on the back.");
                var uv=g.TexCoords[g.Triangles[i]];
                int artwork = ProvisionCatalog.ArtworkIndex(kind);
                Assert.InRange(uv.U, artwork/8f, (artwork+1)/8f);
                Assert.InRange(uv.V, .5f, 1f);
                Assert.True(a.Z < 0 && b.Z < 0 && c.Z < 0);
            }
            // Optional offline preview of the exact production geometry, not an in-game capture.
            string output=Environment.GetEnvironmentVariable("DVSURVIVAL_MODEL_PREVIEW_DIR");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output,kind+".json"),JsonSerializer.Serialize(new {
                    name=kind.ToString(), vertices=g.Vertices.Select(p=>new[]{p.X,p.Y,p.Z}),
                    uv=g.TexCoords.Select(t=>new[]{t.U,t.V}), normals=g.Normals.Select(p=>new[]{p.X,p.Y,p.Z}), triangles=g.Triangles, labelStart=g.LabelStart
                }));
            }
        }
    }
}
