using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DV.Teleporters;
using DV.Utils;
using DVSurvival.Core;
using DVSurvival.Core.Runtime;
using DVSurvival.Core.Simulation;
using UnityEngine;
using UnityModManagerNet;

namespace DVSurvival.Mod
{
    // Native integration boundary. Pure simulation and cadence are owned by Core/Simulation and Core/Runtime.
    internal sealed partial class SurvivalRuntime
    {
        private bool ApplyDeathPenalty(SurvivalState state, uint previousCollapse)
        {
            if (state == null || state.CollapseCount <= previousCollapse) return false;
            var deaths = state.CollapseCount - previousCollapse;
            for (uint index = 0; index < deaths; index++) environment.DeductMoneyUpTo(DeathPenalty);
            return true;
        }

        private void ResetHomeRespawn()
        {
            homeRespawn.Reset();
            respawnTraumaGraceUntil = 0f;
            nextHouseFallbackScan = 0f;
            nextRespawnWarning = 0f;
            cachedHouseDestination = null;
        }

        private void ProcessPendingHomeRespawn()
        {
            if (!homeRespawn.Pending || homeRespawn.Running || Time.realtimeSinceStartup < nextHomeRespawnAttempt) return;
            nextHomeRespawnAttempt = Time.realtimeSinceStartup + HomeRespawnRetrySeconds;
            if (!WorldStreamingInit.IsLoaded || PlayerManager.PlayerTransform == null ||
                SingletonBehaviour<APlayerTeleport>.Instance == null || UnloadWatcher.isUnloading)
                return;
            var travel = SingletonBehaviour<FastTravelController>.Instance;
            if (travel == null || FastTravelController.IsFastTravelling) return;
            Vector3 position;
            Quaternion rotation;
            if (!TryGetHouseRespawnPose(out position, out rotation))
            {
                WarnRespawnDelay("House map destination is not loaded yet.");
                return;
            }
            var controller = impactMonitor == null ? null : impactMonitor.GetComponent<CustomFirstPersonController>();
            if (!homeRespawn.TryStart(controller != null, FastTravelController.IsFastTravelling)) return;
            var revision = homeRespawn.Revision;
            try
            {
                NativeHomeFastTravel.Start(travel, cachedHouseDestination, controller,
                    error => OnHomeFastTravelFinished(revision, error));
            }
            catch (Exception error)
            {
                OnHomeFastTravelFinished(revision, error);
            }
        }

        private void OnHomeFastTravelFinished(uint revision, Exception error)
        {
            // Ignore callbacks from a previous save/session or an superseded death.
            if (!homeRespawn.Finish(revision, error == null)) return;
            if (error != null)
            {
                WarnRespawnDelay("Native home fast travel failed: " + error.Message);
                return;
            }
            respawnTraumaGraceUntil = Time.realtimeSinceStartup + 2f;
            entry.Logger.Log("House respawn completed through native fast travel, without a travel fee.");
            Notify(ModLocalization.Text("Вы очнулись у дома. Здоровье: 25%.", "You woke up at home. Health: 25%."));
            environment.InvalidateSpatialCache();
            if (impactMonitor != null) impactMonitor.ResetTracking();
            UpdateLocalEnvironment(true);
        }

        private void WarnRespawnDelay(string reason)
        {
            if (Time.realtimeSinceStartup < nextRespawnWarning) return;
            nextRespawnWarning = Time.realtimeSinceStartup + 10f;
            entry.Logger.Warning(reason);
            Notify(ModLocalization.Text("Возвращение к дому: ожидаю загрузку места появления.",
                "Returning home: waiting for the respawn location to load."));
        }

        private bool TryGetHouseRespawnPose(out Vector3 position, out Quaternion rotation)
        {
            if (!IsHouseDestination(cachedHouseDestination))
            {
                cachedHouseDestination = null;
                foreach (var destination in FastTravelDestination.ActiveDestinations)
                    if (IsHouseDestination(destination)) { cachedHouseDestination = destination; break; }
                // Map visibility can temporarily remove the real destination from ActiveDestinations.
                // Look only at loaded scene objects, never prefab assets or arbitrary station positions.
                if (cachedHouseDestination == null && Time.realtimeSinceStartup >= nextHouseFallbackScan)
                {
                    nextHouseFallbackScan = Time.realtimeSinceStartup + 5f;
                    foreach (var destination in Resources.FindObjectsOfTypeAll<WorldFastTravelDestination>())
                        if (IsHouseDestination(destination) && destination.gameObject.scene.IsValid() &&
                            destination.gameObject.scene.isLoaded)
                        { cachedHouseDestination = destination; break; }
                }
            }
            if (cachedHouseDestination != null)
            {
                position = cachedHouseDestination.playerTeleportAnchor.position;
                rotation = cachedHouseDestination.playerTeleportAnchor.rotation;
                // A negative coordinate is valid after origin shifting; reject only corrupt coordinates.
                return IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z);
            }
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        private static bool IsHouseDestination(FastTravelDestination destination)
        {
            return destination != null && !destination.IsDynamic &&
                (destination.markerType & FastTravelDestination.MarkerType.House) != 0 &&
                destination.playerTeleportAnchor != null;
        }

        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
