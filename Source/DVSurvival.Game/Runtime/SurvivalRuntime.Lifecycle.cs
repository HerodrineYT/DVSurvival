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
        public void Start()
        {
            if (started || disposed) return;
            started = true;
            ClimateSceneDiscovery.Start();
            WorldStreamingInit.LoadingFinished += OnWorldLoaded;
            UnloadWatcher.UnloadRequested += OnWorldUnloading;
            network.SetEnabled(true);
            CreateHud();
            PlayerManager.PlayerTeleportStarted += OnPlayerTeleportStarted;
            var manager = UnityEngine.Object.FindObjectOfType<SaveGameManager>();
            if (!UnloadWatcher.isUnloading && WorldStreamingInit.IsLoaded && manager != null &&
                manager.data != null)
                OnWorldLoaded();
            entry.Logger.Log("Survival runtime started. " + NetworkStatus);
        }

        public void Stop()
        {
            if (!started) return;
            EndSession();
            started = false;
            ClimateSceneDiscovery.Stop();
            WorldStreamingInit.LoadingFinished -= OnWorldLoaded;
            UnloadWatcher.UnloadRequested -= OnWorldUnloading;
            network.SetEnabled(false);
            PlayerManager.PlayerTeleportStarted -= OnPlayerTeleportStarted;
            DestroyImpactMonitor();
            DestroyHud();
            environment.Reset();
            ResetCabHeaterControls();
            confirmedCabHeaterStates.Clear();
        }

        public void OnSessionStart()
        {
            if (!started || disposed) return;
            TryPrepareSession();
        }

        public void SaveSettings()
        {
            settings.Save(entry);
        }

        public void Dispose()
        {
            if (disposed) return;
            Stop();
            disposed = true;
            network.HelloReceived -= OnHelloReceived;
            network.ActionReceived -= OnActionReceived;
            network.EnvironmentReceived -= OnEnvironmentReceived;
            network.PlayerDisconnected -= OnPlayerDisconnected;
            network.StateReceived -= OnStateReceived;
            network.Dispose();
            cabHeaters.Dispose();
        }

        private void OnWorldLoaded()
        {
            if (!started || disposed) return;
            try
            {
                if (!TryPrepareSession()) return;
                sessionReady = true;
                // PlayerManager clears static listeners when unloading a save.
                PlayerManager.PlayerTeleportStarted -= OnPlayerTeleportStarted;
                PlayerManager.PlayerTeleportStarted += OnPlayerTeleportStarted;
                simulationAccumulator = 0f;
                nextStateBroadcast = Time.realtimeSinceStartup;
                nextSaveCheckpoint = Time.realtimeSinceStartup + SaveCheckpointSeconds;
                nextLocalEnvironmentUpdate = 0f;
                UpdateLocalEnvironment(true);
                RebaseGameCalendar();
                lastAuthority = network.IsAuthority;
                if (network.IsSessionActive && !network.IsAuthority) SendHello();
                else EnsureLocalAuthorityRecord();
                ProcessPendingHellos();
            }
            catch (Exception exception)
            {
                entry.Logger.Warning("Survival session initialization failed: " + exception.Message);
            }
        }

        private bool TryPrepareSession()
        {
            if (UnloadWatcher.isUnloading) return false;
            var manager = UnityEngine.Object.FindObjectOfType<SaveGameManager>();
            if (manager == null || manager.data == null) return false;
            if (manager == saveManager && ReferenceEquals(manager.data, sessionSaveData)) return true;
            if (sessionSaveData != null) EndSession();
            ResetCabHeaterControls();
            confirmedCabHeaterStates.Clear();
            ClearPendingCabHeater();
            saveManager = manager;
            sessionSaveData = manager.data;
            saveManager.OnInternalDataUpdate += OnSaveDataUpdate;
            if (network.IsAuthority)
            {
                document = SurvivalSaveData.Read(sessionSaveData, settings.CachedTuning);
                BeginAuthorityEpoch();
                LoadCabHeatersFromDocument();
            }
            else
                document = null;
            currentState = SurvivalState.CreateDefault(settings.CachedTuning);
            hasLocalStateSnapshot = false;
            if (hud != null) hud.ResetStateWarnings();
            ResetHomeRespawn();
            receivedNetworkState = false;
            lastReceivedSequence = 0;
            lastHostMessage = null;
            retiredHostSessions.Clear();
            retiredHostSessionOrder.Clear();
            RebaseGameCalendar();
            return true;
        }

        private void HandleAuthorityTransition()
        {
            if (lastAuthority == network.IsAuthority) return;
            if (lastAuthority && document != null) PersistAuthorityDocument();
            lastAuthority = network.IsAuthority;
            schedule.Reset();
            activeRecords.Clear();
            actionRequests.Clear();
            pendingItemUses.Clear();
            if (personalItems != null) personalItems.Clear();
            nextActionTimes.Clear();
            nextTraumaTimes.Clear();
            remoteEnvironments.Clear();
            travelOrders.Clear();
            ResetCabHeaterControls();
            travelPause.Reset();
            localTravelPaused = false;
            confirmedCabHeaterStates.Clear();
            ClearPendingCabHeater();
            ResetCalendarReconciliation();
            RebaseGameCalendar();
            receivedNetworkState = false;
            lastReceivedSequence = 0;
            lastHostMessage = null;
            hasLocalStateSnapshot = false;
            if (hud != null) hud.ResetStateWarnings();
            ResetHomeRespawn();
            if (network.IsAuthority)
            {
                document = SurvivalSaveData.Read(sessionSaveData, settings.CachedTuning);
                BeginAuthorityEpoch();
                LoadCabHeatersFromDocument();
                EnsureLocalAuthorityRecord();
            }
            else
            {
                document = null;
                currentState = SurvivalState.CreateDefault(settings.CachedTuning);
                SendHello();
            }
        }

        private void EnsureLocalAuthorityRecord()
        {
            if (!sessionReady || !network.IsAuthority || document == null) return;
            var localId = network.IsSessionActive ? network.LocalPlayerId : byte.MaxValue;
            if (lastLocalAuthorityId != localId)
            {
                SurvivalPlayerRecord old;
                if (activeRecords.TryGetValue(lastLocalAuthorityId, out old) &&
                    string.Equals(old.IdentityId, settings.IdentityId, StringComparison.OrdinalIgnoreCase))
                    activeRecords.Remove(lastLocalAuthorityId);
                lastLocalAuthorityId = localId;
            }
            SurvivalPlayerRecord existing;
            if (activeRecords.TryGetValue(localId, out existing) &&
                string.Equals(existing.IdentityId, settings.IdentityId, StringComparison.OrdinalIgnoreCase))
                return;
            var record = document.GetOrCreate(settings.IdentityId, LocalDisplayName(), settings.CachedTuning);
            activeRecords[localId] = record;
            SetLocalState(record.State);
            stateDirty = true;
        }

        private void OnSaveDataUpdate(SaveGameData data)
        {
            if (!sessionReady || !network.IsAuthority || document == null ||
                !ReferenceEquals(data, sessionSaveData))
                return;
            try
            {
                SurvivalSaveData.Write(data, document, settings.CachedTuning);
                stateDirty = false;
            }
            catch (Exception exception)
            {
                entry.Logger.Warning("Could not add survival data to the game save: " + exception.Message);
            }
        }

        private void Checkpoint()
        {
            using (new SurvivalProfiler("DVSurvival.Save.Checkpoint"))
            {
            WriteToMemorySave();
            }
        }

        private void WriteToMemorySave()
        {
            if (!sessionReady || !network.IsAuthority || document == null || sessionSaveData == null ||
                !stateDirty)
                return;
            PersistAuthorityDocument();
        }

        private void PersistAuthorityDocument()
        {
            if (document == null || sessionSaveData == null || !stateDirty) return;
            try
            {
                SurvivalSaveData.Write(sessionSaveData, document, settings.CachedTuning);
                stateDirty = false;
            }
            catch (Exception exception)
            {
                entry.Logger.Warning("Survival checkpoint failed: " + exception.Message);
            }
        }

        private void OnWorldUnloading()
        {
            if (!started || disposed) return;
            try { EndSession(); }
            catch (Exception exception)
            {
                entry.Logger.Warning("Survival session cleanup failed: " + exception.Message);
            }
        }

        private void EndSession()
        {
            schedule.Reset();
            pendingItemUses.Clear();
            if (personalItems != null) personalItems.Clear();
            actionRequests.Clear();
            if (hud != null) hud.ResetStateWarnings();
            if (sessionSaveData == null && !sessionReady) return;
            if (network.IsAuthority && document != null)
                DrainCalendarOperations(double.MaxValue);
            WriteToMemorySave();
            if (saveManager != null) saveManager.OnInternalDataUpdate -= OnSaveDataUpdate;
            saveManager = null;
            sessionSaveData = null;
            document = null;
            sessionReady = false;
            receivedNetworkState = false;
            lastHostMessage = null;
            retiredHostSessions.Clear();
            retiredHostSessionOrder.Clear();
            activeRecords.Clear();
            pendingHellos.Clear();
            nextActionTimes.Clear();
            nextTraumaTimes.Clear();
            remoteEnvironments.Clear();
            travelOrders.Clear();
            ResetCalendarReconciliation();
            travelPause.Reset();
            localTravelPaused = false;
            localTeleportPauseUntil = 0f;
            simulationAccumulator = 0f;
            nextLocalEnvironmentUpdate = 0f;
            nextEnvironmentReport = 0f;
            nextSessionPrepareAttempt = 0f;
            stateDirty = false;
            hasLocalStateSnapshot = false;
            ResetHomeRespawn();
            nextHomeRespawnAttempt = 0f;
            movementMultiplier = 1f;
            appUtil = null;
            nextAppUtilProbe = 0f;
            environment.Reset();
            ResetCabHeaterControls();
            confirmedCabHeaterStates.Clear();
            ClearPendingCabHeater();
            gameCalendarClock.Reset();
        }

        internal bool IsGamePaused()
        {
            try
            {
                if (appUtil == null && Time.realtimeSinceStartup >= nextAppUtilProbe)
                {
                    nextAppUtilProbe = Time.realtimeSinceStartup + AppUtilProbeIntervalSeconds;
                    appUtil = UnityEngine.Object.FindObjectOfType<DV.AppUtil>();
                }
                return appUtil != null ? appUtil.IsTimePaused : Time.timeScale <= 0f;
            }
            catch { return Time.timeScale <= 0f; }
        }

        private ISurvivalNetworkBridge CreateNetworkBridge(string modPath)
        {
            try
            {
                if (Type.GetType("MPAPI.MultiplayerAPI, MultiplayerAPI", false) == null)
                    return new OfflineSurvivalBridge();
                var adapterPath = Path.Combine(modPath, "DVSurvival.Multiplayer.dll");
                if (!File.Exists(adapterPath))
                {
                    entry.Logger.Warning("DVSurvival.Multiplayer.dll not found; using local mode.");
                    return new OfflineSurvivalBridge();
                }
                var assembly = Assembly.LoadFrom(adapterPath);
                var type = assembly.GetType("DVSurvival.Multiplayer.MultiplayerSurvivalBridge", true);
                return (ISurvivalNetworkBridge)Activator.CreateInstance(type);
            }
            catch (Exception exception)
            {
                entry.Logger.Warning("Multiplayer adapter unavailable; using local mode: " + exception.Message);
                return new OfflineSurvivalBridge();
            }
        }
    }
}
