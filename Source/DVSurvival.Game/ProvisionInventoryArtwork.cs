using System;
using System.Collections.Generic;
using System.IO;
using DVSurvival.Core;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DVSurvival.Mod
{
    internal static class ProvisionInventoryArtwork
    {
        private static readonly Sprite[,,] sprites = new Sprite[2, 2, (int)ProvisionKind.Tea + 1];

        public static void Load(List<Object> assets)
        {
            for (int language = 0; language < 2; language++)
            {
                string resource = "DVSurvival.Items.inventory-" + (language == 0 ? "ru" : "en") + ".png";
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = resource };
                assets.Add(texture);
                using (var input = typeof(ProvisionInventoryArtwork).Assembly.GetManifestResourceStream(resource))
                    {
                    if (input == null) throw new InvalidOperationException("Missing inventory model atlas: " + resource);
                    if (!ImageConversion.LoadImage(texture, EmbeddedBytes.Read(input), true))
                        throw new InvalidOperationException("Invalid inventory model atlas: " + resource);
                }
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                for (int ghost = 0; ghost < 2; ghost++)
                    for (int index = 0; index < ProvisionCatalog.Count; index++)
                    {
                        var kind = ProvisionCatalog.At(index);
                        var sprite = Sprite.Create(texture, new Rect(index * 256, ghost == 0 ? 256 : 0, 256, 256),
                            new Vector2(.5f, .5f), 256f, 0, SpriteMeshType.FullRect);
                        assets.Add(sprite);
                        sprites[language, ghost, (int)kind] = sprite;
                    }
            }
        }

        public static Sprite Get(ProvisionKind kind, bool ghost)
        {
            int index = (int)kind;
            return !ProvisionCatalog.IsSupported(kind) ? null : sprites[ModLocalization.IsRussian ? 0 : 1, ghost ? 1 : 0, index];
        }
        public static void Clear() { Array.Clear(sprites, 0, sprites.Length); }

        public static void Resolve(InventoryItemSpec spec, bool ghost, ref Sprite result)
        {
            ProvisionKind kind;
            if (!CustomProvisionCatalog.TryKind(spec, out kind)) return;
            var sprite = Get(kind, ghost);
            if (sprite != null) result = sprite;
        }
    }

    // Native inventory/hotbar/container UI uses these getters, including restored/legacy instances.
    // Only our items are changed; no UI replacement or scene searches.
    [HarmonyPatch(typeof(InventoryItemSpec), nameof(InventoryItemSpec.ItemIconSprite), MethodType.Getter)]
    internal static class ProvisionInventoryModelPatch
    {
        private static void Postfix(InventoryItemSpec __instance, ref Sprite __result)
        { ProvisionInventoryArtwork.Resolve(__instance, false, ref __result); }
    }
    [HarmonyPatch(typeof(InventoryItemSpec), nameof(InventoryItemSpec.ItemIconSpriteSimple), MethodType.Getter)]
    internal static class ProvisionSimpleModelPatch
    {
        private static void Postfix(InventoryItemSpec __instance, ref Sprite __result)
        { ProvisionInventoryArtwork.Resolve(__instance, false, ref __result); }
    }
    [HarmonyPatch(typeof(InventoryItemSpec), nameof(InventoryItemSpec.ItemIconSpriteDropped), MethodType.Getter)]
    internal static class ProvisionDroppedModelPatch
    {
        private static void Postfix(InventoryItemSpec __instance, ref Sprite __result)
        { ProvisionInventoryArtwork.Resolve(__instance, true, ref __result); }
    }
}
