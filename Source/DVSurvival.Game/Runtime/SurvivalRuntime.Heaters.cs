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
        private void OnCabHeaterSwitchChanged(string carId, float level)
        {
            if (string.IsNullOrWhiteSpace(carId)) return;
            if (!HasConfirmedLocalState)
            {
                // A newly connected client has no authoritative heater snapshot yet.
                cabHeaters.ApplyConfirmed(carId, 0f);
                return;
            }
            var previous = GetConfirmedCabHeaterState(carId);
            if (pendingCabHeaterRequestId != 0u)
            {
                cabHeaters.ApplyConfirmed(carId, previous);
                return;
            }
            var car = PlayerManager.Car;
            if (!SupportsCabHeater(car) || !string.Equals(car.CarGUID, carId,
                    StringComparison.OrdinalIgnoreCase))
            {
                cabHeaters.ApplyConfirmed(carId, previous);
                return;
            }
            var id = ++requestSequence;
            if (id == 0u) id = ++requestSequence;
            pendingCabHeaterRequestId = id;
            pendingCabHeaterCarId = carId;
            pendingCabHeaterPreviousState = previous;
            pendingCabHeaterExpiresAt = Time.realtimeSinceStartup + 5f;
            if (!RequestAction(new SurvivalActionRequest
            {
                RequestId = id,
                Action = SurvivalActionKind.SetCabHeater,
                Amount = level,
                ItemIdentity = carId
            }))
                RejectPendingCabHeater();
        }

        private void LoadCabHeatersFromDocument()
        {
            if (document == null || document.CabHeaters == null) return;
            foreach (var heater in document.CabHeaters)
                if (heater != null && !string.IsNullOrWhiteSpace(heater.CarId))
                    ApplyConfirmedCabHeater(heater.CarId, heater.EffectiveLevel);
        }

        private void ApplyCabHeaterMessage(SurvivalStateMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.CabHeaterCarId)) return;
            var samePendingCar = pendingCabHeaterRequestId != 0u &&
                string.Equals(message.CabHeaterCarId, pendingCabHeaterCarId,
                    StringComparison.OrdinalIgnoreCase);
            if (samePendingCar)
            {
                if (message.RequestId == pendingCabHeaterRequestId)
                    ClearPendingCabHeater();
                else if (Time.realtimeSinceStartup < pendingCabHeaterExpiresAt)
                    return;
                else
                    ClearPendingCabHeater();
            }
            ApplyConfirmedCabHeater(message.CabHeaterCarId, message.CabHeaterLevel);
        }

        private void ConfirmStaleCabHeaterReceipt(SurvivalStateMessage receipt)
        {
            if (receipt == null || pendingCabHeaterRequestId == 0u ||
                receipt.RequestId != pendingCabHeaterRequestId) return;
            var carId = pendingCabHeaterCarId;
            ClearPendingCabHeater();
            if (lastHostMessage != null && string.Equals(lastHostMessage.CabHeaterCarId,
                    carId, StringComparison.OrdinalIgnoreCase))
                ApplyConfirmedCabHeater(carId, lastHostMessage.CabHeaterLevel);
            else if (string.Equals(receipt.CabHeaterCarId, carId,
                    StringComparison.OrdinalIgnoreCase))
                ApplyConfirmedCabHeater(carId, receipt.CabHeaterLevel);
        }

        private float GetConfirmedCabHeaterState(string carId)
        {
            if (string.IsNullOrWhiteSpace(carId)) return 0f;
            float confirmed;
            if (confirmedCabHeaterStates.TryGetValue(carId, out confirmed)) return confirmed;
            if (network.IsAuthority && document != null) return document.GetCabHeater(carId);
            if (lastHostMessage != null && string.Equals(lastHostMessage.CabHeaterCarId,
                    carId, StringComparison.OrdinalIgnoreCase))
                return lastHostMessage.CabHeaterLevel;
            return 0f;
        }

        private void ApplyConfirmedCabHeater(string carId, float level)
        {
            if (string.IsNullOrWhiteSpace(carId)) return;
            level = CabHeaterSetting.NormalizeSwitch(level);
            confirmedCabHeaterStates[carId] = level;
            cabHeaters.ApplyConfirmed(carId, level);
        }

        private void ResolveExpiredCabHeaterRequest()
        {
            if (pendingCabHeaterRequestId == 0u ||
                Time.realtimeSinceStartup < pendingCabHeaterExpiresAt) return;
            var carId = pendingCabHeaterCarId;
            var previous = pendingCabHeaterPreviousState;
            ClearPendingCabHeater();
            var confirmed = lastHostMessage != null && string.Equals(
                lastHostMessage.CabHeaterCarId, carId, StringComparison.OrdinalIgnoreCase)
                ? lastHostMessage.CabHeaterLevel : previous;
            ApplyConfirmedCabHeater(carId, confirmed);
        }

        private void RejectPendingCabHeater()
        {
            if (pendingCabHeaterRequestId == 0u) return;
            var carId = pendingCabHeaterCarId;
            var previous = pendingCabHeaterPreviousState;
            ClearPendingCabHeater();
            ApplyConfirmedCabHeater(carId, previous);
        }

        private void ClearPendingCabHeater()
        {
            pendingCabHeaterRequestId = 0u;
            pendingCabHeaterCarId = string.Empty;
            pendingCabHeaterPreviousState = 0f;
            pendingCabHeaterExpiresAt = 0f;
        }

        private void ResetCabHeaterControls()
        {
            firstAidAccumulator = 0f;
            sleepHudPreview.Clear();
            dismountEvidence.Clear();
            cabHeaters.Reset();
            environment.InvalidateCabControls();
        }

        private static bool SupportsCabHeater(TrainCar car)
        {
            if (car == null) return false;
            return car.carType == DV.ThingTypes.TrainCarType.LocoShunter ||
                car.carType == DV.ThingTypes.TrainCarType.LocoDH4 ||
                car.carType == DV.ThingTypes.TrainCarType.LocoDM3 ||
                car.carType == DV.ThingTypes.TrainCarType.LocoDiesel;
        }
    }
}
