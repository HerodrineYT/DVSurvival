using System;
using System.Collections.Generic;
using custom_item_mod;
using DV.CabControls.Spec;
using DV.Shops;
using DV.ThingTypes;
using DVSurvival.Core;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DVSurvival.Mod
{
    // CIM owns registration, resource loading, shop layout and inventory specs.
    // Our adapter owns its prefabs; no per-frame discovery or asset generation.
    internal static class CustomProvisionCatalog
    {
        private const string Prefix = "DVSurvival_";
        private static readonly Dictionary<ProvisionKind, CustomItem> items = new Dictionary<ProvisionKind, CustomItem>();
        private static readonly List<Object> assets = new List<Object>();

        public static bool TryGet(ProvisionKind kind, out CustomItem item) { return items.TryGetValue(kind, out item); }

        public static void RefreshPrices(SurvivalRuntime runtime)
        {
            ProvisionModels.RefreshLanguage();
            bool changed = false;
            foreach (var pair in items)
            {
                var price = runtime.GetPrice(pair.Key);
                if (pair.Value.ShopData.basePrice == price) continue;
                pair.Value.ShopData.basePrice = price;
                changed = true;
            }
            var controller = DV.Utils.SingletonBehaviour<GlobalShopController>.Instance;
            if (changed && controller != null) controller.Fire_GlobalShopDataChanged();
        }

        public static bool TryKind(InventoryItemSpec spec, out ProvisionKind kind)
        {
            kind = ProvisionKind.None;
            if (spec == null) return false;
            foreach (var pair in items)
                if (spec.ItemPrefabName == pair.Value.ItemSpec.ItemPrefabName)
                {
                    kind = pair.Key;
                    return true;
                }
            return false;
        }

        public static void Register()
        {
            Clear();
            ProvisionInventoryArtwork.Load(assets);
            var material = ProvisionModels.CreateMaterial(assets);
            for (int i = 0; i < ProvisionCatalog.Count; i++)
            {
                var kind = ProvisionCatalog.At(i);
                var model = ProvisionModels.Create(kind, material, assets);
                var modelBounds = model.GetComponent<MeshFilter>().sharedMesh.bounds;
                var placement = new ShelfDisplayLayout(modelBounds.min.x, modelBounds.min.y, modelBounds.min.z,
                    modelBounds.max.x, modelBounds.max.y, modelBounds.max.z);
                var icon = ProvisionInventoryArtwork.Get(kind, false);
                var ghostIcon = ProvisionInventoryArtwork.Get(kind, true);
                var info = new CustomItemInfo
                {
                    Name = Prefix + kind,
                    Description = ModLocalization.Description(kind),
                    Price = Main.Runtime == null ? ProvisionShopPricing.LocalPrice(100) : Main.Runtime.GetPrice(kind),
                    Amount = 5,
                    // CIM maps X/Y to ShelfItem width/depth, not to model width/height.
                    ShelfBounds = new Vector3(placement.Width, placement.Depth, placement.Height),
                    ShelfRotation = Vector3.zero
                };
                var custom = new CustomItem(info, model, icon, ghostIcon);
                custom.ItemSpec.localizationKeyName = ModLocalization.ProvisionKey(kind);
                custom.ItemSpec.localizationKeyDescription = ModLocalization.DescriptionKey(kind);
                // The inactive parent prevents ControlSpec.Awake until an actual item is instantiated.
                custom.ItemPrefab.SetActive(true);
                var spec = custom.ItemPrefab.GetComponent<Item>();
                // CIM queries active children only; our safe inactive construction needs an explicit collider list.
                var itemColliders = custom.ItemPrefab.GetComponentsInChildren<Collider>(true);
                spec.colliderGameObjects = new GameObject[itemColliders.Length];
                for (int c = 0; c < itemColliders.Length; c++) spec.colliderGameObjects[c] = itemColliders[c].gameObject;
                spec.itemUseApproach = ItemUseApproach.OneShot;
                spec.rigidbodyMass = 0.3f;
                spec.excludeFromStorageSerialization = (StorageType)0;
                custom.ItemPrefab.AddComponent<NativeProvisionToken>().Kind = kind;
                custom.ItemPrefab.AddComponent<ProvisionItemLocalizer>().Kind = kind;
                if (custom.ItemSpec.gameObject != custom.ItemPrefab)
                    custom.ItemSpec.gameObject.AddComponent<ProvisionItemLocalizer>().Kind = kind;
                custom.ItemPrefab.GetComponent<ShopRestocker>().restockOnItemDestroyed = true;
                custom.ItemSpec.PreviewBounds = model.GetComponent<MeshFilter>().sharedMesh.bounds;
                // Native detailed previews must not clone pickup/use/save scripts or physics.
                var preview = new GameObject(Prefix + kind + "_VisualPreview");
                preview.transform.SetParent(custom.ItemPrefab.transform.parent, false);
                preview.AddComponent<MeshFilter>().sharedMesh = model.GetComponent<MeshFilter>().sharedMesh;
                preview.AddComponent<MeshRenderer>().sharedMaterial = material;
                custom.ItemSpec.PreviewPrefab = preview;
                // Shelf display geometry must be active, but remains ordinary non-usable shop stock.
                var shelf = custom.ShopData.shelfItem.transform;
                custom.ShopData.shelfItem.height = placement.Height;
                for (int child = 0; child < shelf.childCount; child++)
                {
                    var display = shelf.GetChild(child);
                    // Only sample geometry exists here; CIM adds the scan tag later. Never move the tag/root.
                    display.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    display.localPosition = new Vector3(placement.OffsetX, placement.OffsetY, placement.OffsetZ);
                    display.gameObject.SetActive(true);
                }
                assets.Add(custom.ItemPrefab.transform.parent.gameObject);
                items.Add(kind, custom);
                ItemModsFinder.CustomItems.Add(custom);
                Object.Destroy(model);
            }
        }

        public static void Clear()
        {
            foreach (var value in items.Values) ItemModsFinder.CustomItems.Remove(value);
            items.Clear();
            for (int i = assets.Count - 1; i >= 0; i--) if (assets[i] != null) Object.Destroy(assets[i]);
            assets.Clear();
            ProvisionModels.Clear();
            ProvisionInventoryArtwork.Clear();
        }

    }

    [HarmonyPatch(typeof(ItemModsFinder), nameof(ItemModsFinder.InitializeItems))]
    internal static class RegisterSurvivalItemsPatch
    {
        private static void Postfix()
        {
            var mod = UnityModManagerNet.UnityModManager.FindMod("DVSurvival");
            if (mod != null && mod.Active) CustomProvisionCatalog.Register();
        }
    }

}
