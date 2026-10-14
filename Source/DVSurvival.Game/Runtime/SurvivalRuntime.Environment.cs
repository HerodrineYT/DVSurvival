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
        private void OnEnvironmentReceived(SurvivalPlayerInfo player, SurvivalEnvironmentReport report)
        {
            if (!sessionReady || !network.IsAuthority || player == null || report == null ||
                report.Protocol != SurvivalConstants.ProtocolVersion || report.Environment == null ||
                !report.Environment.IsValid() || document == null ||
                !string.Equals(report.SessionId, document.SessionId, StringComparison.Ordinal))
                return;
            TravelTransitionOrder order;
            if (!travelOrders.TryGetValue(player.PlayerId, out order))
                travelOrders[player.PlayerId] = order = new TravelTransitionOrder();
            // An older telemetry sample may still carry the newest reliable travel transition.
            if (order.Accept(report.TravelRevision, report.IsTravelling))
                travelPause.Update(player.PlayerId, report.IsTravelling, Time.realtimeSinceStartup);
            RemoteEnvironmentRecord existing;
            if (remoteEnvironments.TryGetValue(player.PlayerId, out existing) &&
                report.Sequence <= existing.Sequence)
                return;
            if (existing == null)
            {
                existing = new RemoteEnvironmentRecord(0, 0f, new SurvivalEnvironment());
                remoteEnvironments[player.PlayerId] = existing;
            }
            report.Environment.CopyTo(existing.Environment);
            existing.Environment.GameHours = 0f;
            existing.Environment.Clamp();
            existing.Sequence = report.Sequence;
            existing.ReceivedAt = Time.realtimeSinceStartup;
        }

        private SurvivalEnvironment GetRemoteEnvironment(SurvivalPlayerInfo player, float gameHours,
            float realSeconds)
        {
            RemoteEnvironmentRecord report;
            if (remoteEnvironments.TryGetValue(player.PlayerId, out report) &&
                Time.realtimeSinceStartup - report.ReceivedAt <= EnvironmentReportTimeoutSeconds)
            {
                report.Environment.CopyTo(remoteSample);
                remoteSample.GameHours = gameHours;
                return remoteSample;
            }
            return environment.SampleRemote(player, gameHours, realSeconds);
        }

        private void UpdateLocalEnvironment(bool force = false)
        {
            using (new SurvivalProfiler("DVSurvival.Environment.UpdateLocal"))
            {
            var now = Time.realtimeSinceStartup;
            if (!force && now < nextLocalEnvironmentUpdate) return;
            nextLocalEnvironmentUpdate = now + LocalEnvironmentIntervalSeconds;
            if (PlayerManager.PlayerTransform == null) return;
            EnsureImpactMonitor();
            lastLocalEnvironment = environment.SampleLocal(0f);
            }
        }

        private void EnsureImpactMonitor()
        {
            var player = PlayerManager.PlayerTransform;
            if (player == null) return;
            if (impactMonitor != null && impactPlayerTransform == player && Time.realtimeSinceStartup < nextImpactControllerRetry) return;
            nextImpactControllerRetry = Time.realtimeSinceStartup + 30f;
            var controller = player.GetComponent<CustomFirstPersonController>();
            if (controller == null)
                controller = player.GetComponentInChildren<CustomFirstPersonController>(true);
            if (controller == null)
                controller = player.GetComponentInParent<CustomFirstPersonController>();
            var physicsTransform = controller == null ? player : controller.transform;
            if (impactMonitor != null && impactMonitor.transform == physicsTransform) return;
            DestroyImpactMonitor();
            impactPlayerTransform = player;
            impactMonitor = physicsTransform.gameObject.AddComponent<PlayerImpactMonitor>();
            impactMonitor.Initialize(this, controller);
        }

        private void DestroyImpactMonitor()
        {
            if (impactMonitor == null) return;
            UnityEngine.Object.Destroy(impactMonitor);
            impactMonitor = null;
            impactPlayerTransform = null;
        }

        private void OnPlayerTeleportStarted()
        {
            if (impactMonitor != null) impactMonitor.ResetTracking();
            localTeleportPauseUntil = Time.realtimeSinceStartup + 1f;
            RefreshLocalTravelPause();
        }

        internal void RefreshLocalTravelPause()
        {
            if (!sessionReady) return;
            var now = Time.realtimeSinceStartup;
            var paused = FastTravelController.IsFastTravelling || LoadingScreenManager.IsLoading ||
                homeRespawn.Pending || now < localTeleportPauseUntil;
            var changed = localTravelPaused != paused;
            localTravelPaused = paused;
            travelPause.Update(network.IsSessionActive ? network.LocalPlayerId : byte.MaxValue, paused, now);
            if (changed)
            {
                if (!paused) nextLocalEnvironmentUpdate = 0f;
                SendLocalEnvironmentReport();
            }
        }

        private void SendLocalEnvironmentReport()
        {
            if (!network.IsSessionActive || network.IsAuthority || !network.IsSupported) return;
            nextEnvironmentReport = Time.realtimeSinceStartup + EnvironmentReportIntervalSeconds;
            network.SendEnvironment(new SurvivalEnvironmentReport
            {
                Sequence = ++environmentReportSequence,
                SessionId = lastHostMessage == null ? string.Empty : lastHostMessage.SessionId,
                IsTravelling = localTravelPaused,
                Environment = lastLocalEnvironment == null
                    ? new SurvivalEnvironment() : lastLocalEnvironment.Clone()
            });
        }

        private static bool IsTravelSimulationPaused(byte playerId, HashSet<byte> captured)
        {
            return captured != null && captured.Contains(playerId);
        }
    }
}
