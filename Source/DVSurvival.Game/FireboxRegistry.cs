using System.Collections.Generic;
using DV.Simulation.Cars;
using UnityEngine;

namespace DVSurvival.Mod
{
    internal static class FireboxRegistry
    {
        private static readonly List<FireboxSimController> entries = new List<FireboxSimController>();

        internal static void Register(FireboxSimController firebox)
        {
            if (firebox != null && !entries.Contains(firebox)) entries.Add(firebox);
        }

        internal static List<FireboxSimController> GetLoaded()
        {
            ClimateSceneDiscovery.EnsureInitialized();
            // Keep inactive pooled locomotives: reactivation need not rerun Init.
            for (var i = entries.Count - 1; i >= 0; i--)
                if (entries[i] == null) entries.RemoveAt(i);
            return entries;
        }

        internal static void Reset() { entries.Clear(); }
    }
}
