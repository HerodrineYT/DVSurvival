using System.Collections.Generic;
using DV;

namespace DVSurvival.Mod
{
    internal static class SurvivalWorldRegistry
    {
        private static readonly List<Shop> shops = new List<Shop>();
        private static readonly List<BedSleeping> beds = new List<BedSleeping>();
        internal static uint Revision { get; private set; }
        internal static List<Shop> Shops { get { ClimateSceneDiscovery.EnsureInitialized(); Prune(shops); return shops; } }
        internal static List<BedSleeping> Beds { get { ClimateSceneDiscovery.EnsureInitialized(); Prune(beds); return beds; } }
        internal static void Register(Shop value) { if (value != null && !shops.Contains(value)) { shops.Add(value); Revision++; } }
        internal static void Register(BedSleeping value) { if (value != null && !beds.Contains(value)) { beds.Add(value); Revision++; } }
        private static void Prune<T>(List<T> values) where T : UnityEngine.Object
        {
            for (int i = values.Count - 1; i >= 0; i--) if (values[i] == null) { values.RemoveAt(i); Revision++; }
        }
        internal static void Reset() { shops.Clear(); beds.Clear(); Revision++; ClimateSceneDiscovery.Reset(); }
    }
}
