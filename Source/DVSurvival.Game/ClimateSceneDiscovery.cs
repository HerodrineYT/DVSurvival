using System;
using System.Collections.Generic;
using DV;
using DV.Simulation.Cars;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DVSurvival.Mod
{
    internal static class ClimateSceneDiscovery
    {
        private static bool listening, initialized;
        private static float nextReconciliation;
        private static readonly List<GameObject> roots = new List<GameObject>(256);
        private static readonly Queue<Transform> pending = new Queue<Transform>();
        private static readonly HashSet<int> queuedRoots = new HashSet<int>();
        private static readonly List<PostProcessingVolumeAOController> offices = new List<PostProcessingVolumeAOController>();
        private static readonly List<TutorialPlayerDetector> detectors = new List<TutorialPlayerDetector>();
        private static readonly List<FireboxSimController> fireboxes = new List<FireboxSimController>();
        private static readonly List<Shop> shops = new List<Shop>();
        private static readonly List<BedSleeping> beds = new List<BedSleeping>();

        internal static void Start()
        {
            if (listening) return;
            listening = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        internal static void Stop() { SceneManager.sceneLoaded -= OnSceneLoaded; listening = false; Reset(); }
        internal static void Reset() { initialized = false; pending.Clear(); queuedRoots.Clear(); roots.Clear(); }

        internal static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            nextReconciliation = Time.realtimeSinceStartup + 180f;
            // One bootstrap per world/session. Native callbacks handle subsequent spawns.
            foreach (var value in UnityEngine.Object.FindObjectsOfType<Shop>()) SurvivalWorldRegistry.Register(value);
            foreach (var value in UnityEngine.Object.FindObjectsOfType<BedSleeping>()) SurvivalWorldRegistry.Register(value);
            foreach (var value in UnityEngine.Object.FindObjectsOfType<PostProcessingVolumeAOController>()) StationOfficeVolumes.Register(value);
            foreach (var value in UnityEngine.Object.FindObjectsOfType<TutorialPlayerDetector>()) StationOfficeVolumes.Register(value);
            foreach (var value in UnityEngine.Object.FindObjectsOfType<FireboxSimController>()) FireboxRegistry.Register(value);
            QueueLoadedScenes(); // Includes inactive streamed components missed by bootstrap.
        }

        internal static void Tick()
        {
            EnsureInitialized();
            using (new SurvivalProfiler("DVSurvival.World.Discovery"))
            {
                if (pending.Count == 0 && Time.realtimeSinceStartup >= nextReconciliation)
                {
                    nextReconciliation = Time.realtimeSinceStartup + 180f;
                    queuedRoots.Clear();
                    QueueLoadedScenes();
                }
                // Reconciliation is spread across frames, never a periodic global Find.
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                var budget = System.Diagnostics.Stopwatch.Frequency / 2000; // 0.5 ms, checked every 8 nodes.
                for (int n = 0; n < 64 && pending.Count != 0; n++)
                {
                    if (n != 0 && n % 8 == 0 && System.Diagnostics.Stopwatch.GetTimestamp() - started >= budget) break;
                    var node = pending.Dequeue();
                    if (node == null) continue;
                    try
                    {
                        node.GetComponents(offices); foreach (var v in offices) StationOfficeVolumes.Register(v);
                        node.GetComponents(detectors); foreach (var v in detectors) StationOfficeVolumes.Register(v);
                        node.GetComponents(fireboxes); foreach (var v in fireboxes) FireboxRegistry.Register(v);
                        node.GetComponents(shops); foreach (var v in shops) SurvivalWorldRegistry.Register(v);
                        node.GetComponents(beds); foreach (var v in beds) SurvivalWorldRegistry.Register(v);
                        for (int i = 0; i < node.childCount; i++) pending.Enqueue(node.GetChild(i));
                    }
                    catch (Exception error) { ClimateRegistrationErrors.Report(error); }
                }
                offices.Clear(); detectors.Clear(); fireboxes.Clear(); shops.Clear(); beds.Clear();
            }
        }
        private static void QueueLoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) QueueScene(SceneManager.GetSceneAt(i));
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { QueueScene(scene); }
        private static void QueueScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            try
            {
                if (roots.Capacity < scene.rootCount) roots.Capacity = scene.rootCount;
                scene.GetRootGameObjects(roots);
                foreach (var root in roots)
                    if (root != null && queuedRoots.Add(root.GetInstanceID())) pending.Enqueue(root.transform);
            }
            catch (Exception error) { ClimateRegistrationErrors.Report(error); }
            finally { roots.Clear(); }
        }
    }
}
