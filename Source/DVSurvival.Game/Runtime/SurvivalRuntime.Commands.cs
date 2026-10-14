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
        public void RequestConsume(ProvisionKind kind)
        {
            RequestAction(new SurvivalActionRequest
            {
                RequestId = ++requestSequence,
                Action = SurvivalActionKind.Consume,
                Provision = kind
            });
        }

        public void RequestPhysicalConsume(NativeProvisionToken token, int targetUnits = -1)
        {
            if (token == null || !HasConfirmedLocalState) return;
            pendingRemoval.Clear();
            foreach (var pending in pendingItemUses)
                if (pending.Value == null || pending.Value == token) pendingRemoval.Add(pending.Key);
            for (int i = 0; i < pendingRemoval.Count; i++) pendingItemUses.Remove(pendingRemoval[i]);
            if (pendingItemUses.Count >= 16) { token.Confirm(SurvivalResultCode.RateLimited); return; }
            var id = ++requestSequence;
            pendingItemUses.Add(id, token);
            RequestAction(new SurvivalActionRequest {
                RequestId = id, Action = targetUnits >= 0 ? SurvivalActionKind.ConsumePortion : SurvivalActionKind.ConsumePhysical,
                Provision = token.Kind, Amount = targetUnits, ItemIdentity = token.Identity
            });
        }

        public void OnLocalTrauma(TraumaKind kind, float magnitude, float playerHeightMeters)
        {
            if (!sessionReady || IsRespawning || kind == TraumaKind.None) return;
            RequestAction(new SurvivalActionRequest
            {
                RequestId = ++requestSequence,
                Action = SurvivalActionKind.Trauma,
                Trauma = kind,
                Amount = magnitude,
                SecondaryAmount = playerHeightMeters
            });
        }

        private bool RequestAction(SurvivalActionRequest request)
        {
            if (request != null && (request.Action == SurvivalActionKind.Sleep ||
                request.Action == SurvivalActionKind.SleepWithoutTimeAdvance))
                NativeSleepOriginPatch.CaptureBed(request);
            if (!sessionReady)
            {
                Notify(ModLocalization.Result(SurvivalResultCode.NotReady, string.Empty));
                return false;
            }
            if (network.IsSessionActive && !network.IsSupported)
            {
                Notify(ModLocalization.Result(SurvivalResultCode.UnsupportedMultiplayerVersion, string.Empty));
                return false;
            }
            request.SessionId = network.IsSessionActive && !network.IsAuthority
                ? (lastHostMessage == null ? string.Empty : lastHostMessage.SessionId)
                : (document == null ? string.Empty : document.SessionId);
            var sleepPresentation = NativeSleepOriginPatch.IsAdvancingSleep &&
                (request.Action == SurvivalActionKind.Sleep || request.Action == SurvivalActionKind.SleepWithoutTimeAdvance);
            if (sleepPresentation)
            {
                var previewTuning = settings.ToTuning();
                if (network.IsSessionActive && !network.IsAuthority && lastHostMessage != null)
                    previewTuning.DisableNeedsConsumption = lastHostMessage.DisableNeedsConsumption;
                sleepHudPreview.Begin(request.RequestId, currentState, request.Amount,
                    lastLocalEnvironment, previewTuning, Time.realtimeSinceStartup);
                if (hud != null) hud.RefreshNow();
            }
            if (network.IsSessionActive && !network.IsAuthority)
            {
                var sent = network.SendAction(request);
                if (!sent && sleepPresentation) sleepHudPreview.Clear();
                return sent;
            }
            EnsureLocalAuthorityRecord();
            var localId = network.IsSessionActive ? network.LocalPlayerId : byte.MaxValue;
            ProcessAction(localId, GetLocalPlayerInfo(localId), request);
            return true;
        }

        private void OnActionReceived(SurvivalPlayerInfo player, SurvivalActionRequest request)
        {
            if (!sessionReady || !network.IsAuthority || player == null || request == null) return;
            ProcessAction(player.PlayerId, player, request);
        }

        private void ProcessAction(byte playerId, SurvivalPlayerInfo player, SurvivalActionRequest request)
        {
            SurvivalPlayerRecord record;
            if (request.Protocol != SurvivalConstants.ProtocolVersion)
            {
                SendResult(playerId, null, request.RequestId, SurvivalResultCode.ProtocolMismatch, string.Empty);
                return;
            }
            if (document == null || string.IsNullOrEmpty(request.SessionId) ||
                !string.Equals(request.SessionId, document.SessionId, StringComparison.Ordinal))
            {
                SendResult(playerId, null, request.RequestId, SurvivalResultCode.NotReady, string.Empty);
                return;
            }
            if (!activeRecords.TryGetValue(playerId, out record))
            {
                SendResult(playerId, null, request.RequestId, SurvivalResultCode.NotReady, string.Empty);
                return;
            }
            if (HasPendingNativeSleep(playerId))
            {
                // Preserve action order while the exact world-calendar jump is being matched.
                SendResult(playerId, record.State, request.RequestId,
                    SurvivalResultCode.RateLimited, string.Empty);
                return;
            }
            if (!actionRequests.TryAccept(playerId, request.RequestId))
            {
                SendResult(playerId, record.State, request.RequestId, SurvivalResultCode.InvalidRequest, string.Empty);
                return;
            }
            float nextAllowed;
            if (nextActionTimes.TryGetValue(playerId, out nextAllowed) && Time.realtimeSinceStartup < nextAllowed)
            {
                SendResult(playerId, record.State, request.RequestId, SurvivalResultCode.RateLimited, string.Empty);
                return;
            }
            nextActionTimes[playerId] = Time.realtimeSinceStartup + 0.25f;
            var tuning = settings.CachedTuning;
            var result = SurvivalResultCode.InvalidRequest;
            var status = string.Empty;
            var itemUsedUnits = -1;
            var previousCollapse = record.State.CollapseCount;
            if (request.Action == SurvivalActionKind.Consume)
            {
                result = SurvivalSimulator.Consume(record.State, request.Provision, tuning);
                if (result == SurvivalResultCode.Success) status = ProvisionStatus(request.Provision);
            }
            else if (request.Action == SurvivalActionKind.ConsumePhysical)
            {
                result = PartialProvisionLedger.Supports(request.Provision)
                    ? PartialProvisionLedger.Consume(document.PartialItems, document.ConsumedItems, request.ItemIdentity,
                        1000, record.State, request.Provision, out itemUsedUnits, tuning)
                    : PhysicalItemLedger.Consume(document.ConsumedItems, request.ItemIdentity,
                    record.State, request.Provision, tuning, playerId == lastLocalAuthorityId
                        ? environment.SampleLocal(0f) : GetRemoteEnvironment(player, 0f, 0f));
                if (result == SurvivalResultCode.Success) status = ProvisionStatus(request.Provision);
            }
            else if (request.Action == SurvivalActionKind.ConsumePortion)
            {
                if (!float.IsNaN(request.Amount) && !float.IsInfinity(request.Amount) &&
                    request.Amount >= 0f && request.Amount <= 1000f && request.Amount == (int)request.Amount)
                    result = PartialProvisionLedger.Consume(document.PartialItems, document.ConsumedItems,
                        request.ItemIdentity, (int)request.Amount, record.State, request.Provision, out itemUsedUnits, tuning);
                // No per-sip notification: the HUD and the held item's percentage are sufficient.
            }
            else if (request.Action == SurvivalActionKind.Sleep ||
                request.Action == SurvivalActionKind.SleepWithoutTimeAdvance)
            {
                var nearBed = playerId == lastLocalAuthorityId
                    // The native call site proves local bed use, including just-streamed beds
                    // missing from the 30-second spatial cache. Never trust this for remote players.
                    ? NativeSleepOriginPatch.IsAdvancingSleep || environment.IsLocalNearBed()
                    : environment.IsPlayerNearNativeBed(player, request);
                if (!nearBed)
                {
                    entry.Logger.Warning("Native sleep rejected: player " + playerId + " is not near a known bed.");
                    result = SurvivalResultCode.NotNearBed;
                }
                else if (request.Action == SurvivalActionKind.SleepWithoutTimeAdvance)
                {
                    // No shared calendar jump exists in this MP mode. Apply only this player's
                    // validated bed sleep, without queuing a jump or advancing anyone else.
                    if (PersonalSleepValidator.IsValid(network.IsSessionActive,
                        IsPersonalSleepMode, nearBed, request))
                    {
                        var healthBeforeSleep = record.State.Health;
                        result = SurvivalSimulator.Sleep(record.State, request.Amount,
                            playerId == lastLocalAuthorityId ? environment.SampleLocal(0f)
                                : GetRemoteEnvironment(player, 0f, 0f), tuning);
                        if (result == SurvivalResultCode.Success) status = "sleep_ok";
                        entry.Logger.Log("Personal native sleep without world time advance: player=" +
                            playerId + ", hours=" + request.Amount + ", rest=" + record.State.Rest +
                            ", health=" + healthBeforeSleep + "->" + record.State.Health +
                            ", bodyC=" + record.State.BodyTemperatureCelsius);
                    }
                }
                else
                {
                    var sleepEnvironment = playerId == lastLocalAuthorityId
                        ? environment.SampleLocal(0f)
                        : GetRemoteEnvironment(player, 0f, 0f);
                    if (TryQueueNativeSleep(playerId, request, sleepEnvironment)) return;
                    result = SurvivalResultCode.InvalidRequest;
                }
            }
            else if (request.Action == SurvivalActionKind.Trauma)
            {
                float nextTrauma;
                float dismountMagnitude = 0f;
                if (travelPause.IsPaused(playerId, Time.realtimeSinceStartup))
                    result = SurvivalResultCode.NotReady;
                else if (nextTraumaTimes.TryGetValue(playerId, out nextTrauma) &&
                    Time.realtimeSinceStartup < nextTrauma)
                    result = SurvivalResultCode.RateLimited;
                else if (request.Trauma == TraumaKind.Fall &&
                    (request.Amount > 60f || request.SecondaryAmount < 1.4f ||
                     request.SecondaryAmount > 2.4f))
                    result = SurvivalResultCode.InvalidRequest;
                else if (request.Trauma == TraumaKind.TrainCollision &&
                    (player == null || player.IsOnCar || request.Amount > 300f || request.Amount <= 7f ||
                     !environment.IsPlayerNearMovingTrain(player, 7f)))
                    result = SurvivalResultCode.InvalidRequest;
                else if (request.Trauma == TraumaKind.TrainDismount &&
                    !dismountEvidence.TryTake(playerId, request.Amount, Time.realtimeSinceStartup, out dismountMagnitude))
                    result = SurvivalResultCode.InvalidRequest;
                else
                {
                    nextTraumaTimes[playerId] = Time.realtimeSinceStartup + TraumaCooldownSeconds;
                    result = SurvivalSimulator.ApplyTrauma(record.State, request.Trauma,
                        request.Trauma == TraumaKind.TrainDismount ? dismountMagnitude : request.Amount,
                        request.SecondaryAmount, tuning);
                    if (result == SurvivalResultCode.Success)
                        status = request.Trauma == TraumaKind.Fall ? "fall_damage" : "train_damage";
                }
            }
            else if (request.Action == SurvivalActionKind.SetCabHeater)
            {
                if (!CabHeaterActionValidator.IsValid(player, request))
                    result = SurvivalResultCode.InvalidRequest;
                else
                {
                    var level = CabHeaterSetting.NormalizeSwitch(request.Amount);
                    document.SetCabHeater(player.OccupiedCarId, level);
                    ApplyConfirmedCabHeater(player.OccupiedCarId, level);
                    result = SurvivalResultCode.Success;
                    status = level > 0f ? "cab_heater_on" : "cab_heater_off";
                    nextStateBroadcast = 0f;
                }
            }
            if (ApplyDeathPenalty(record.State, previousCollapse)) status = "death";
            record.LastSeenUtcTicks = DateTime.UtcNow.Ticks;
            if (playerId == lastLocalAuthorityId) SetLocalState(record.State);
            SendResult(playerId, record.State, request.RequestId, result, status, true, player,
                itemUsedUnits >= 0 ? request.ItemIdentity : string.Empty, itemUsedUnits);
            stateDirty = true;
        }

        private static string ProvisionStatus(ProvisionKind kind)
        {
            switch (kind)
            {
                case ProvisionKind.Meal: return "meal_used";
                case ProvisionKind.Water: return "water_used";
                case ProvisionKind.Coffee: return "coffee_used";
                case ProvisionKind.FirstAid: return "first_aid_used";
                case ProvisionKind.HeatPack: return "heat_pack_used";
                case ProvisionKind.CannedFood: return "meal_used";
                case ProvisionKind.Tea: return "tea_used";
                default: return string.Empty;
            }
        }

        public int GetPrice(ProvisionKind kind)
        {
            if (network.IsSessionActive && !network.IsAuthority && lastHostMessage != null)
                return lastHostMessage.GetPrice(kind);
            switch (kind)
            {
                case ProvisionKind.Meal: return ProvisionShopPricing.LocalPrice(settings.MealPrice);
                case ProvisionKind.Water: return ProvisionShopPricing.LocalPrice(settings.WaterPrice);
                case ProvisionKind.Coffee: return ProvisionShopPricing.LocalPrice(settings.CoffeePrice);
                case ProvisionKind.FirstAid: return ProvisionShopPricing.LocalPrice(settings.FirstAidPrice);
                case ProvisionKind.HeatPack: return ProvisionShopPricing.LocalPrice(settings.HeatPackPrice);
                case ProvisionKind.CannedFood: return ProvisionShopPricing.LocalPrice(settings.CannedFoodPrice);
                case ProvisionKind.Tea: return ProvisionShopPricing.LocalPrice(settings.TeaPrice);
                default: return -1;
            }
        }
    }
}
