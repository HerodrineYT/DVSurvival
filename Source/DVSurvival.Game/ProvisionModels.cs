using System;
using System.Collections.Generic;
using System.IO;
using DVSurvival.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DVSurvival.Mod
{
    internal static class ProvisionModels
    {
        private static Material shared;
        private static Texture2D russianTexture, englishTexture;
        private static bool wasRussian;
        public static Material CreateMaterial(List<Object> assets)
        {
            var shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Standard item shader is unavailable.");
            russianTexture = LoadTexture(assets, "provisions");
            englishTexture = LoadTexture(assets, "provisions-en");
            var texture = ModLocalization.IsRussian ? russianTexture : englishTexture;
            var material = CreateOpaqueMaterial(shader, texture, assets);
            material.SetTexture("_MetallicGlossMap", LoadTexture(assets, "provisions-materials", true));
            material.SetFloat("_GlossMapScale", 1f);
            material.EnableKeyword("_METALLICGLOSSMAP");
            return material;
        }

        public static void RefreshLanguage()
        {
            if (shared == null) return;
            bool russian = ModLocalization.IsRussian;
            if (wasRussian == russian) return;
            wasRussian = russian;
            shared.mainTexture = russian ? russianTexture : englishTexture;
        }

        public static void Clear() { shared = null; russianTexture = englishTexture = null; }

        private static Texture2D LoadTexture(List<Object> assets, string name, bool linear = false)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = "Survival printed packaging" };
            assets.Add(texture);
            using (var stream = typeof(ProvisionModels).Assembly.GetManifestResourceStream("DVSurvival.Items." + name + ".png"))
            {
                if (stream == null) throw new InvalidOperationException("Provision packaging atlas is missing.");
                if (!ImageConversion.LoadImage(texture, EmbeddedBytes.Read(stream), true))
                    throw new InvalidOperationException("Invalid provision packaging atlas.");
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 2;
            return texture;
        }

        private static Material CreateOpaqueMaterial(Shader shader, Texture2D texture, List<Object> assets)
        {
            // Opaque Standard uses ZTest LEqual, ZWrite On and back-face culling.
            // The printed labels are geometry in this same mesh/material, never an overlay font.
            var material = new Material(shader) { name = "Survival opaque packaging", mainTexture = texture, color = Color.white };
            material.SetFloat("_Mode", 0f);
            material.SetInt("_SrcBlend", 1);
            material.SetInt("_DstBlend", 0);
            material.SetInt("_ZWrite", 1);
            material.SetFloat("_Metallic", .12f);
            material.SetFloat("_Glossiness", .25f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 2000;
            assets.Add(material);
            shared = material;
            wasRussian = ModLocalization.IsRussian;
            return material;
        }

        public static GameObject Create(ProvisionKind kind, Material material, List<Object> assets)
        {
            var data = ProvisionGeometry.Create(kind);
            var vertices = new Vector3[data.Vertices.Count];
            var uv = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var p = data.Vertices[i]; var t = data.TexCoords[i];
                // Geometry is authored at its final physical size, including shelf/preview bounds.
                vertices[i] = new Vector3(p.X, p.Y, p.Z);
                uv[i] = new Vector2(t.U, t.V);
                var normal = data.Normals[i];
                normals[i] = new Vector3(normal.X, normal.Y, normal.Z);
            }
            var mesh = new Mesh { name = "DVSurvival_" + kind, vertices = vertices, uv = uv, normals = normals, triangles = data.Triangles.ToArray() };
            assets.Add(mesh);
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            mesh.UploadMeshData(true);
            var root = new GameObject("DVSurvival_" + kind);
            root.SetActive(false);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = material;
            // One cheap interaction/physics volume, no colliders on decorative parts.
            var collider = root.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = bounds.size;
            return root;
        }
    }
}
