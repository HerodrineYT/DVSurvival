using System;
using System.Collections.Generic;
using DV;
using UnityEngine;

namespace DVSurvival.Mod
{
    // Reuse the game's authored room volumes, not a radius around job terminals.
    internal sealed class StationOfficeVolumes
    {
        private static readonly List<BoxCollider> rooms = new List<BoxCollider>();
        private static readonly List<BoxCollider> scratch = new List<BoxCollider>();

        // Native lifecycle callbacks register streamed rooms immediately. Reconciliation
        // covers already-loaded rooms and third-party components bypassing those callbacks.
        internal static void Register(PostProcessingVolumeAOController controller)
        {
            if (controller == null || !IsOfficeInterior(controller.transform.parent)) return;
            controller.GetComponents(scratch);
            foreach (var volume in scratch)
                if (!rooms.Contains(volume)) rooms.Add(volume);
        }

        internal static void Register(TutorialPlayerDetector detector)
        {
            if (detector == null || detector.detectorType != TutorialPlayerDetector.TutorialPlayerDetectorType.StationOffice)
                return;
            detector.GetComponentsInChildren(true, scratch);
            foreach (var volume in scratch)
                if (!rooms.Contains(volume)) rooms.Add(volume);
        }

        internal static void Reset()
        {
            rooms.Clear();
            scratch.Clear();
        }

        public bool Contains(Vector3 position)
        {
            ClimateSceneDiscovery.EnsureInitialized();
            var inside = false;
            for (var i = rooms.Count - 1; i >= 0; i--)
            {
                var room = rooms[i];
                if (room == null) rooms.RemoveAt(i);
                else if (!inside && Contains(room, position)) inside = true;
            }
            return inside;
        }

        private static bool IsOfficeInterior(Transform root)
        {
            if (root == null) return false;
            string name = root.name;
            if (name.EndsWith("(Clone)", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 7).TrimEnd();
            return name == "MilitaryOffice_interior" ||
                (name.Length == 17 && name.StartsWith("Office_", StringComparison.Ordinal) &&
                 name[7] >= '1' && name[7] <= '7' && name.EndsWith("_interior", StringComparison.Ordinal));
        }

        internal static bool Contains(BoxCollider room, Vector3 position)
        {
            if (room == null || !room.gameObject.activeInHierarchy) return false;
            // ClosestPoint on a disabled collider returns the input position.
            // Test the native box in its local space instead: this also handles
            // rotated offices and origin shifts without a stale world AABB.
            Vector3 relative = room.transform.InverseTransformPoint(position) - room.center;
            Vector3 half = room.size * .5f;
            return Mathf.Abs(relative.x) < Mathf.Abs(half.x) &&
                Mathf.Abs(relative.y) < Mathf.Abs(half.y) &&
                Mathf.Abs(relative.z) < Mathf.Abs(half.z);
        }
    }
}
