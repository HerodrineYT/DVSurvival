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
        private void OnHelloReceived(SurvivalPlayerInfo player, SurvivalHello hello)
        {
            if (player == null || hello == null || !network.IsAuthority) return;
            if (!sessionReady)
            {
                pendingHellos[player.PlayerId] = new PendingHello(player, hello);
                return;
            }
            BindHello(player, hello);
        }

        private void BindHello(SurvivalPlayerInfo player, SurvivalHello hello)
        {
            Guid identityGuid;
            if (hello.Protocol != SurvivalConstants.ProtocolVersion ||
                !Guid.TryParse(hello.IdentityId, out identityGuid) || identityGuid == Guid.Empty)
            {
                SendResult(player.PlayerId, null, 0, SurvivalResultCode.ProtocolMismatch, string.Empty);
                return;
            }
            var identity = hello.IdentityId;
            var duplicate = false;
            foreach (var pair in activeRecords)
                if (pair.Key != player.PlayerId && string.Equals(pair.Value.IdentityId, identity, StringComparison.OrdinalIgnoreCase))
                { duplicate = true; break; }
            var status = string.Empty;
            if (duplicate)
            {
                identity = identity + "#" + player.PlayerId;
                status = "identity_in_use";
            }
            var record = document.GetOrCreate(identity, player.DisplayName, settings.CachedTuning);
            activeRecords[player.PlayerId] = record;
            stateDirty = true;
            SendResult(player.PlayerId, record.State, 0, SurvivalResultCode.None, status);
        }

        private void ProcessPendingHellos()
        {
            if (!sessionReady || !network.IsAuthority || document == null || pendingHellos.Count == 0) return;
            while (pendingHellos.Count > 0)
            {
                var id = pendingHellos.IdAt(pendingHellos.Count - 1);
                var value = pendingHellos[id];
                pendingHellos.Remove(id);
                BindHello(value.Player, value.Hello);
            }
        }

        private void SendHello()
        {
            if (!sessionReady || !network.IsSessionActive || network.IsAuthority || !network.IsSupported) return;
            network.SendHello(new SurvivalHello
            {
                IdentityId = settings.IdentityId,
                ModVersion = SurvivalConstants.ModVersion
            });
            nextHello = Time.realtimeSinceStartup + HelloRetrySeconds;
        }

        private void SendAllStates()
        {
            using (new SurvivalProfiler("DVSurvival.Network.SendStates"))
            {
            var players = network.GetPlayers();
            Array.Clear(broadcastPlayers, 0, broadcastPlayers.Length);
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null) broadcastPlayers[players[i].PlayerId] = players[i];
            foreach (var pair in activeRecords)
            {
                if (pair.Key == lastLocalAuthorityId) continue;
                var player = broadcastPlayers[pair.Key];
                SendResult(pair.Key, pair.Value.State, 0, SurvivalResultCode.None,
                    string.Empty, false, player);
            }
            }
        }

        private void SendResult(byte playerId, SurvivalState state, uint requestId,
            SurvivalResultCode result, string status, bool reliable = true,
            SurvivalPlayerInfo player = null, string itemIdentity = "", int itemUsedUnits = -1)
        {
            var tuning = settings.CachedTuning;
            if (player == null)
            {
                if (playerId == lastLocalAuthorityId)
                    player = GetLocalPlayerInfo(playerId);
                else if (network.IsSessionActive)
                    player = FindNetworkPlayer(playerId);
            }
            var heaterCarId = player != null && player.OccupiedCarSupportsCabHeater
                ? player.OccupiedCarId ?? string.Empty : string.Empty;
            var message = new SurvivalStateMessage
            {
                Sequence = ++stateSequence,
                RequestId = requestId,
                Result = result,
                SessionId = document == null ? string.Empty : document.SessionId,
                StatusKey = status ?? string.Empty,
                State = state == null ? SurvivalState.CreateDefault(tuning) : state.Clone(),
                DisableNeedsConsumption = tuning.DisableNeedsConsumption,
                MealPrice = GetPrice(ProvisionKind.Meal),
                WaterPrice = GetPrice(ProvisionKind.Water),
                CoffeePrice = GetPrice(ProvisionKind.Coffee),
                FirstAidPrice = GetPrice(ProvisionKind.FirstAid),
                HeatPackPrice = GetPrice(ProvisionKind.HeatPack),
                CannedFoodPrice = GetPrice(ProvisionKind.CannedFood),
                TeaPrice = GetPrice(ProvisionKind.Tea),
                CabHeaterCarId = heaterCarId,
                CabHeaterLevel = document != null ? document.GetCabHeater(heaterCarId) : 0f,
                ItemIdentity = itemIdentity,
                ItemUsedUnits = itemUsedUnits,
            };
            if (playerId == lastLocalAuthorityId)
            {
                ApplyLocalMessage(message, false);
                return;
            }
            network.SendStateToPlayer(playerId, message, reliable);
        }

        private void OnStateReceived(SurvivalStateMessage message)
        {
            HandleNetworkConnectionGeneration();
            if (!sessionReady || network.IsAuthority || message == null ||
                message.Protocol != SurvivalConstants.ProtocolVersion || message.State == null ||
                !message.State.IsValid())
                return;
            if (string.IsNullOrEmpty(message.SessionId) ||
                retiredHostSessions.Contains(message.SessionId))
                return;
            if (lastHostMessage != null &&
                !string.Equals(message.SessionId, lastHostMessage.SessionId,
                    StringComparison.Ordinal))
            {
                // MPAPI's clientbound callback has no sender and its remote wrappers do not expose
                // a reliable host identity. Inside one connection, therefore, an arbitrary new
                // GUID is never adopted: it could be an unseen delayed epoch. Reconnect, world
                // load and local authority transitions explicitly clear lastHostMessage first.
                return;
            }
            if (message.Sequence <= lastReceivedSequence)
            {
                // A newer periodic snapshot may overtake the reliable receipt; never rewind state.
                if (lastHostMessage != null && message.SessionId == lastHostMessage.SessionId)
                {
                    sleepHudPreview.Resolve(message.RequestId);
                    if (hud != null) hud.RefreshNow();
                    ConfirmPhysicalItem(message);
                    if (message.RequestId == pendingCabHeaterRequestId)
                        ConfirmStaleCabHeaterReceipt(message);
                }
                return;
            }
            lastReceivedSequence = message.Sequence;
            if (message.Result != SurvivalResultCode.NotReady &&
                message.Result != SurvivalResultCode.ProtocolMismatch &&
                !string.IsNullOrEmpty(message.SessionId))
                receivedNetworkState = true;
            lastHostMessage = message;
            ApplyLocalMessage(message, true);
        }

        private void HandleNetworkConnectionGeneration()
        {
            var generation = network.ConnectionGeneration;
            if (generation == lastConnectionGeneration) return;
            lastConnectionGeneration = generation;
            if (lastHostMessage != null) RetireHostSession(lastHostMessage.SessionId);
            lastReceivedSequence = 0u;
            receivedNetworkState = false;
            ReleasePendingPhysicalItems();
            RejectPendingCabHeater();
            ResetCabHeaterControls();
            confirmedCabHeaterStates.Clear();
            lastHostMessage = null;
            nextHello = 0f;
        }

        private void BeginAuthorityEpoch()
        {
            if (document == null) return;
            document.SessionId = Guid.NewGuid().ToString("D");
            stateSequence = 0u;
            stateDirty = true;
        }

        private void RetireHostSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || !retiredHostSessions.Add(sessionId)) return;
            retiredHostSessionOrder.Enqueue(sessionId);
            while (retiredHostSessionOrder.Count > 16)
                retiredHostSessions.Remove(retiredHostSessionOrder.Dequeue());
        }

        private void ReleasePendingPhysicalItems()
        {
            foreach (var token in pendingItemUses.Values)
                if (token != null) token.Confirm(SurvivalResultCode.NotReady);
            pendingItemUses.Clear();
        }

        private void ApplyLocalMessage(SurvivalStateMessage message, bool fromNetwork)
        {
            sleepHudPreview.Resolve(message.RequestId);
            SetLocalState(message.State);
            ApplyCabHeaterMessage(message);
            ConfirmPhysicalItem(message);
            if (string.IsNullOrEmpty(message.ItemIdentity) && (message.RequestId != 0 || message.Result != SurvivalResultCode.None ||
                !string.IsNullOrEmpty(message.StatusKey)))
            {
                var text = ModLocalization.Result(message.Result, message.StatusKey);
                if (!string.IsNullOrEmpty(text)) Notify(text);
            }
            if (fromNetwork && message.Result == SurvivalResultCode.ProtocolMismatch)
                receivedNetworkState = false;
        }

        private void ConfirmPhysicalItem(SurvivalStateMessage message)
        {
            NativeProvisionToken token;
            if (message.RequestId == 0 || !pendingItemUses.TryGetValue(message.RequestId, out token)) return;
            pendingItemUses.Remove(message.RequestId);
            if (token != null) token.Confirm(message.Result,
                message.ItemIdentity == token.Identity ? message.ItemUsedUnits : -1);
        }

        private void OnPlayerDisconnected(byte playerId)
        {
            travelPause.Remove(playerId);
            foreach (var paused in calendarTravelPauses.Values) paused.Remove(playerId);
            dismountEvidence.Remove(playerId);
            actionRequests.Remove(playerId);
            activeRecords.Remove(playerId);
            pendingHellos.Remove(playerId);
            nextActionTimes.Remove(playerId);
            nextTraumaTimes.Remove(playerId);
            remoteEnvironments.Remove(playerId);
            travelOrders.Remove(playerId);
            sleepCalendar.DisconnectPlayer(playerId);
            RemovePendingNativeSleeps(playerId);
            environment.ForgetPlayer(playerId);
        }

        private SurvivalPlayerInfo GetLocalPlayerInfo(byte playerId)
        {
            if (network.IsSessionActive)
            {
                var fromNetwork = FindNetworkPlayer(playerId);
                if (fromNetwork != null) return fromNetwork;
            }
            var position = PlayerManager.PlayerTransform == null ? Vector3.zero : PlayerManager.PlayerTransform.position;
            var car = PlayerManager.Car;
            var result = localPlayerSnapshot;
            result.PlayerId = playerId; result.DisplayName = LocalDisplayName();
            result.PositionX = position.x; result.PositionY = position.y; result.PositionZ = position.z;
            result.IsHost = network.IsAuthority; result.IsLoaded = sessionReady; result.IsOnCar = car != null;
            result.OccupiedCarIsLocomotive = car != null && car.IsLoco;
            result.OccupiedCarId = car == null ? string.Empty : car.CarGUID ?? string.Empty;
            result.OccupiedCarSupportsCabHeater = SupportsCabHeater(car);
            return result;
        }

        private SurvivalPlayerInfo FindNetworkPlayer(byte id)
        {
            var players = network.GetPlayers();
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && players[i].PlayerId == id) return players[i];
            return null;
        }

        private string LocalDisplayName()
        {
            if (network.IsSessionActive)
            {
                var local = FindNetworkPlayer(network.LocalPlayerId);
                if (local != null && !string.IsNullOrWhiteSpace(local.DisplayName)) return local.DisplayName;
            }
            return "Local player";
        }
    }
}
