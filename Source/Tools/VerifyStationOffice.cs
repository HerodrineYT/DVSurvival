using System;
using System.IO;
using System.Reflection;
using UnityEngine;

// Run in Unity against the built Survival assembly, not a duplicated model.
public static class VerifyStationOffice
{
    public static FakeRuntime Runtime = new FakeRuntime();
    public sealed class FakeRuntime
    {
        public bool HasWeatherTemperature { get { return true; } }
        public FakeState CurrentState { get; set; } = new FakeState();
    }
    public sealed class FakeState
    {
        public float TemperatureCelsius { get; set; }
        public string Current { get; set; } = "Winter";
    }

    public static void Run(string modDirectory)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(modDirectory, "DVSurvival.dll"));
        VerifyTravelModels(assembly);
        var game = Assembly.Load("Assembly-CSharp");
        var detectorType = game.GetType("TutorialPlayerDetector", true);
        var root = new GameObject("Room without office name");
        var unrelated = new GameObject("stationoffice decorative wall");
        var regularOffice = new GameObject("Office_7_interior(Clone)");
        var house = new GameObject("House_interior");
        try
        {
            root.transform.position = new Vector3(10000, 0, 10000);
            root.transform.rotation = Quaternion.Euler(0, 38, 0);
            root.transform.localScale = new Vector3(1.4f, 1, .8f);
            var volume = root.AddComponent<BoxCollider>();
            volume.center = new Vector3(0, 1.5f, 0);
            volume.size = new Vector3(8, 3, 6);
            volume.isTrigger = true;
            volume.enabled = false;
            var detector = root.AddComponent(detectorType);
            var kind = detectorType.GetField("detectorType");
            kind.SetValue(detector, Enum.Parse(kind.FieldType, "StationOffice"));
            ((Behaviour)detector).enabled = false;

            unrelated.transform.position = root.transform.position + Vector3.right * 30;
            var wall = unrelated.AddComponent<BoxCollider>();
            wall.center = new Vector3(0, 1.5f, 0);
            wall.size = new Vector3(8, 3, 6);
            wall.isTrigger = true;
            regularOffice.transform.position = root.transform.position + Vector3.right * 60;
            regularOffice.transform.rotation = Quaternion.Euler(0, -29, 0);
            house.transform.position = root.transform.position + Vector3.right * 100;
            AddOfficeVolumes(game, regularOffice);
            AddOfficeVolumes(game, house);
            Physics.SyncTransforms();

            var settings = Activator.CreateInstance(assembly.GetType("DVSurvival.Mod.SurvivalModSettings", true));
            var heaterType = assembly.GetType("DVSurvival.Mod.CabHeaterSwitchSystem", true);
            var heaters = Activator.CreateInstance(heaterType, new object[] { new Action<string, float>((id, value) => { }) });
            var samplerType = assembly.GetType("DVSurvival.Mod.EnvironmentSampler", true);
            var sampler = Activator.CreateInstance(samplerType, new[] { settings, heaters });
            var provider = Get(sampler, "seasons");
            Set(provider, "probeAttempted", true);
            Set(provider, "runtimeField", typeof(VerifyStationOffice).GetField("Runtime"));
            Set(provider, "currentStateProperty", typeof(FakeRuntime).GetProperty("CurrentState"));
            Set(provider, "temperatureProperty", typeof(FakeState).GetProperty("TemperatureCelsius"));
            Set(provider, "seasonProperty", typeof(FakeState).GetProperty("Current"));
            Set(provider, "weatherTemperatureProperty", typeof(FakeRuntime).GetProperty("HasWeatherTemperature"));
            var sample = samplerType.GetMethod("Sample", BindingFlags.Instance | BindingFlags.NonPublic);
            var environmentType = Assembly.LoadFrom(Path.Combine(modDirectory, "DVSurvival.Core.dll"))
                .GetType("DVSurvival.Core.SurvivalEnvironment", true);
            Func<Vector3, float, object> read = (position, outside) => {
                Runtime.CurrentState.TemperatureCelsius = outside;
                var target = Activator.CreateInstance(environmentType);
                return sample.Invoke(sampler, new object[] { target, position, 0f, false, false, 0f, false, false });
            };

            var inside = root.transform.position;
            var outdoors = root.transform.TransformPoint(new Vector3(5, 0, 0));
            Equal(22, Number(read(inside, -25), "AmbientTemperatureCelsius"), "disabled office volume");
            Equal(22, Number(read(inside, 11), "AmbientTemperatureCelsius"), "cool spring office");
            Equal(30, Number(read(inside, 30), "AmbientTemperatureCelsius"), "office does not cool summer air");
            Equal(-25, Number(read(outdoors, -25), "AmbientTemperatureCelsius"), "outside rotated room");
            Equal(-25, Number(read(unrelated.transform.position, -25), "AmbientTemperatureCelsius"), "named scenery is not a room");
            var officeLeft = regularOffice.transform.TransformPoint(new Vector3(-3, 0, 0));
            var officeRight = regularOffice.transform.TransformPoint(new Vector3(3, 0, 0));
            Equal(22, Number(read(officeLeft, -25), "AmbientTemperatureCelsius"), "native office left volume");
            Equal(22, Number(read(officeRight, -25), "AmbientTemperatureCelsius"), "native office right volume");
            Equal(-25, Number(read(regularOffice.transform.position, -25), "AmbientTemperatureCelsius"), "gap between office volumes is outdoors");
            Equal(-25, Number(read(house.transform.TransformPoint(new Vector3(-3, 0, 0)), -25), "AmbientTemperatureCelsius"), "same AO volume in house is not a station office");
            var roomEnvironment = read(inside, -25);
            Equal(0, Number(roomEnvironment, "RainIntensity"), "sheltered rain");
            Equal(0, Number(roomEnvironment, "WindSpeedMetersPerSecond"), "sheltered wind");
            Equal(.03f, Number(roomEnvironment, "Exposure"), "sheltered exposure");

            root.transform.position += new Vector3(-500, 0, 650);
            Equal(22, Number(read(root.transform.position, -25), "AmbientTemperatureCelsius"), "origin-shifted room");
            Equal(-25, Number(read(inside, -25), "AmbientTemperatureCelsius"), "old origin is outdoors");
            root.SetActive(false);
            Equal(-25, Number(read(root.transform.position, -25), "AmbientTemperatureCelsius"), "unloaded room ignored");
            VerifyStreamingCaches(assembly, game, sampler, read);
            VerifyWorldRegistry(assembly, game, sampler);
            ((IDisposable)heaters).Dispose();
            Debug.Log("STATION_OFFICE_OK: authored AO office boxes, two-room gap, house exclusion, disabled native room, rotated/scaled containment, cold22, warm30, origin shift, unload");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(unrelated);
            UnityEngine.Object.DestroyImmediate(regularOffice); UnityEngine.Object.DestroyImmediate(house);
        }
    }

    private static void VerifyTravelModels(Assembly assembly)
    {
        var models = assembly.GetType("DVSurvival.Mod.ProvisionModels", true);
        var core = Assembly.Load("DVSurvival.Core");
        var kindType = core.GetType("DVSurvival.Core.ProvisionKind", true);
        var assets = new System.Collections.Generic.List<UnityEngine.Object>();
        try
        {
            var material = (Material)models.GetMethod("CreateMaterial").Invoke(null, new object[] { assets });
            if (material.renderQueue != 2000 || material.GetInt("_ZWrite") != 1)
                throw new Exception("Travel model material is not opaque/depth-writing.");
            if (material.GetTexture("_MetallicGlossMap") == null || !material.IsKeywordEnabled("_METALLICGLOSSMAP"))
                throw new Exception("Shared travel material surface map is missing.");
            foreach (string name in new[] { "Meal", "Water", "Coffee", "FirstAid", "HeatPack", "CannedFood", "Tea" })
            {
                var root = (GameObject)models.GetMethod("Create").Invoke(null,
                    new object[] { Enum.Parse(kindType, name), material, assets });
                try
                {
                    if (root.GetComponentsInChildren<Renderer>(true).Length != 1 ||
                        root.GetComponentsInChildren<Collider>(true).Length != 1)
                        throw new Exception("Travel model must have one renderer/collider: " + name);
                    var bounds = root.GetComponent<MeshFilter>().sharedMesh.bounds;
                    if (bounds.size.x <= 0 || bounds.size.x > .18f || bounds.size.y <= 0 ||
                        bounds.size.y > .22f || bounds.size.z <= 0 || bounds.size.z > .11f || bounds.min.y < -.001f)
                        throw new Exception("Travel model dimensions are not compact: " + name);
                    var collider = root.GetComponent<BoxCollider>();
                    if ((collider.size - bounds.size).sqrMagnitude > .000001f)
                        throw new Exception("Collider does not match actual mesh: " + name);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            Debug.Log("TRAVEL_MODELS_OK: seven compact production meshes, one renderer/collider, shared opaque material and native bounds");
        }
        finally
        {
            models.GetMethod("Clear").Invoke(null, null);
            foreach (var asset in assets) if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
        }
    }

    private static void VerifyStreamingCaches(Assembly assembly, Assembly game, object sampler,
        Func<Vector3, float, object> read)
    {
        var offices = assembly.GetType("DVSurvival.Mod.StationOfficeVolumes", true);
        var fireboxes = assembly.GetType("DVSurvival.Mod.FireboxRegistry", true);
        var streamed = new GameObject("Office_3_interior(Clone)");
        var sceneOffice = new GameObject("Office_4_interior(Clone)");
        var loco = new GameObject("climate test firebox");
        try
        {
            var discovery = assembly.GetType("DVSurvival.Mod.ClimateSceneDiscovery", true);
            var officeDeadline = (float)StaticGet(discovery, "nextReconciliation");
            if (officeDeadline - Time.realtimeSinceStartup < 120f) throw new Exception("Scene reconciliation is not throttled.");
            streamed.transform.position = new Vector3(12000, 0, 12000);
            AddOfficeVolumes(game, streamed);
            var controller = streamed.GetComponentInChildren(game.GetType("DV.PostProcessingVolumeAOController"), true);
            // Invoke the same postfix registered on native OnEnable (fixture has no graphics singleton).
            InvokeStatic(assembly.GetType("DVSurvival.Mod.RegisterOfficeClimatePatch"), "Postfix", controller);
            var rooms = (System.Collections.IList)StaticGet(offices, "rooms");
            int count = rooms.Count;
            InvokeStatic(assembly.GetType("DVSurvival.Mod.RegisterOfficeClimatePatch"), "Postfix", controller);
            Equal(count, rooms.Count, "duplicate room registration");
            var inside = streamed.transform.TransformPoint(new Vector3(-3, 0, 0));
            Equal(22, Number(read(inside, -25), "AmbientTemperatureCelsius"), "room registered without world scan");
            streamed.SetActive(false);
            Equal(-25, Number(read(inside, -25), "AmbientTemperatureCelsius"), "inactive cached room");
            streamed.SetActive(true);
            Equal(22, Number(read(inside, -25), "AmbientTemperatureCelsius"), "reactivated cached room");
            Equal(officeDeadline, (float)StaticGet(discovery, "nextReconciliation"), "reads do not rescan the world");

            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            sceneOffice.transform.position = new Vector3(14000, 0, 14000);
            AddOfficeVolumes(game, sceneOffice);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sceneOffice, scene);
            InvokeStatic(assembly.GetType("DVSurvival.Mod.ClimateSceneDiscovery"), "OnSceneLoaded", scene,
                UnityEngine.SceneManagement.LoadSceneMode.Additive);
            DrainDiscovery(discovery);
            Equal(22, Number(read(sceneOffice.transform.TransformPoint(new Vector3(3, 0, 0)), -25),
                "AmbientTemperatureCelsius"), "disabled controller found on sector load");

            var entries = (System.Collections.IList)InvokeStatic(fireboxes, "GetLoaded");
            int before = entries.Count;
            loco.transform.position = new Vector3(16000, 0, 16000);
            var firebox = loco.AddComponent(game.GetType("DV.Simulation.Cars.FireboxSimController"));
            SetPort(firebox, "fireboxCapacityPort", 1f);
            SetPort(firebox, "fireboxContentsPort", .5f);
            SetPort(firebox, "fireboxDoorPort", 0f);
            SetPort(firebox, "combustionRateNormalizedPort", 1f);
            SetPort(firebox, "fireOnPort", 1f);
            var patch = assembly.GetType("DVSurvival.Mod.RegisterFireboxClimatePatch");
            InvokeStatic(patch, "Postfix", firebox);
            InvokeStatic(patch, "Postfix", firebox);
            Equal(before + 1, entries.Count, "one cached firebox per locomotive");
            Equal(-16.6f, Number(read(loco.transform.position, -25), "AmbientTemperatureCelsius"), "nearby closed firebox heat");
            SetPort(firebox, "fireboxDoorPort", 1f);
            Equal(5f, Number(read(loco.transform.position, -25), "AmbientTemperatureCelsius"), "open firebox heat updates without rescan");
            Equal(-25f, Number(read(loco.transform.position + Vector3.right * 8f, -25), "AmbientTemperatureCelsius"), "outside seven metre radius");
            loco.SetActive(false);
            // Reconciliation while pooled must not lose the object for later reuse.
            discovery.GetField("nextReconciliation", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, 0f);
            DrainDiscovery(discovery);
            Equal(-25f, Number(read(loco.transform.position, -25), "AmbientTemperatureCelsius"), "pooled firebox gives no heat");
            loco.SetActive(true);
            Equal(5f, Number(read(loco.transform.position, -25), "AmbientTemperatureCelsius"), "pooled firebox reactivation needs no Init");
            var deadline = (float)StaticGet(discovery, "nextReconciliation");
            for (var i = 0; i < 100; i++) read(loco.transform.position, -25);
            Equal(deadline, (float)StaticGet(discovery, "nextReconciliation"), "no repeated firebox scan during sampling");
            UnityEngine.Object.DestroyImmediate(firebox);
            InvokeStatic(fireboxes, "GetLoaded");
            Equal(before, entries.Count, "destroyed firebox removed");
            UnityEngine.Object.DestroyImmediate(sceneOffice);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            Debug.Log("CLIMATE_CACHE_OK: new sector, lifecycle registration, deduplication, inactive/reactivated rooms and fireboxes, radius/door heat, destroyed entries, throttled reconciliation");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(streamed);
            UnityEngine.Object.DestroyImmediate(sceneOffice);
            UnityEngine.Object.DestroyImmediate(loco);
            InvokeStatic(offices, "Reset"); InvokeStatic(fireboxes, "Reset");
        }
    }

    private static void DrainDiscovery(Type discovery)
    {
        for (int i = 0; i < 10000; i++)
        {
            InvokeStatic(discovery, "Tick");
            if (((System.Collections.ICollection)StaticGet(discovery, "pending")).Count == 0) return;
        }
        throw new Exception("Incremental scene discovery failed to drain.");
    }

    private static void VerifyWorldRegistry(Assembly assembly, Assembly game, object sampler)
    {
        var registry = assembly.GetType("DVSurvival.Mod.SurvivalWorldRegistry", true);
        var discovery = assembly.GetType("DVSurvival.Mod.ClimateSceneDiscovery", true);
        var shopRoot = new GameObject("fixture shop"); shopRoot.SetActive(false);
        var bedRoot = new GameObject("fixture inactive bed"); bedRoot.SetActive(false);
        shopRoot.transform.position = new Vector3(18000, 0, 18000);
        try
        {
            var shop = shopRoot.AddComponent(game.GetType("Shop", true));
            shop.GetType().GetField("itemSpawnTransform").SetValue(shop, shopRoot.transform);
            var bed = bedRoot.AddComponent(game.GetType("DV.BedSleeping", true));
            var shopPatch = assembly.GetType("DVSurvival.Mod.RegisterShopClimatePatch", true);
            var bedPatch = assembly.GetType("DVSurvival.Mod.RegisterBedClimatePatch", true);
            InvokeStatic(shopPatch, "Postfix", shop); InvokeStatic(shopPatch, "Postfix", shop);
            InvokeStatic(bedPatch, "Postfix", bed); InvokeStatic(bedPatch, "Postfix", bed);
            Func<string, System.Collections.IList> entries = name => (System.Collections.IList)
                registry.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null, null);
            var shops = entries("Shops"); var beds = entries("Beds");
            if (!shops.Contains(shop) || !beds.Contains(bed)) throw new Exception("Inactive registered objects lost.");
            int shopCount = shops.Count, bedCount = beds.Count;
            var near = sampler.GetType().GetMethod("IsNear", BindingFlags.Instance | BindingFlags.NonPublic);
            if ((bool)near.Invoke(sampler, new object[] { shopRoot.transform.position, 5f, true }))
                throw new Exception("Inactive shop counted as usable.");
            shopRoot.SetActive(true);
            if (!(bool)near.Invoke(sampler, new object[] { shopRoot.transform.position, 5f, true }))
                throw new Exception("Reactivated shop not usable.");
            shopRoot.SetActive(false);
            InvokeStatic(registry, "Reset");
            InvokeStatic(discovery, "OnSceneLoaded", shopRoot.scene, UnityEngine.SceneManagement.LoadSceneMode.Single);
            DrainDiscovery(discovery);
            if (!entries("Shops").Contains(shop) || !entries("Beds").Contains(bed))
                throw new Exception("Inactive shops/beds not recovered by incremental discovery after reset.");
            UnityEngine.Object.DestroyImmediate(shopRoot); UnityEngine.Object.DestroyImmediate(bedRoot);
            if (entries("Shops").Contains(shop) || entries("Beds").Contains(bed))
                throw new Exception("Destroyed registry entries retained.");
            Debug.Log("WORLD_REGISTRY_OK: shop/bed lifecycle, inactive retention, active shop validation, reset/incremental rediscovery, destroyed pruning");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(shopRoot); UnityEngine.Object.DestroyImmediate(bedRoot);
            InvokeStatic(registry, "Reset");
        }
    }

    private static void SetPort(object target, string name, float value)
    {
        var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        var port = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(field.FieldType);
        field.FieldType.GetField("value", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(port, value);
        field.SetValue(target, port);
    }
    private static object StaticGet(Type type, string name)
    { return type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null); }
    private static object InvokeStatic(Type type, string name, params object[] args)
    { return type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).Invoke(null, args); }

    private static void AddOfficeVolumes(Assembly game, GameObject parent)
    {
        var volumeObject = new GameObject("PostProcOfficeVolumeAO");
        volumeObject.SetActive(false);
        volumeObject.transform.SetParent(parent.transform, false);
        for (int side = -1; side <= 1; side += 2)
        {
            var box = volumeObject.AddComponent<BoxCollider>();
            box.center = new Vector3(side * 3, 1.5f, 0);
            box.size = new Vector3(4, 3, 4);
            box.enabled = false;
        }
        var controller = (Behaviour)volumeObject.AddComponent(game.GetType("DV.PostProcessingVolumeAOController", true));
        controller.enabled = false; // The fixture has no game's graphics preferences singleton.
        volumeObject.SetActive(true);
    }

    private static object Get(object target, string field)
    { return target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    private static void Set(object target, string field, object value)
    { target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value); }
    private static float Number(object target, string field)
    { return (float)target.GetType().GetField(field).GetValue(target); }
    private static void Equal(float expected, float actual, string label)
    { if (Math.Abs(expected - actual) > .001f) throw new Exception(label + ": expected " + expected + ", got " + actual); }
}
