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
        public void Notify(string message)
        {
            if (hud != null) hud.Notify(message);
        }

        private void SetLocalState(SurvivalState state)
        {
            if (state == null) return;
            var previousCollapse = currentState == null ? lastCollapseCount : currentState.CollapseCount;
            if (currentState == null) currentState = new SurvivalState();
            state.CopyTo(currentState);
            currentState.Clamp();
            CustomProvisionCatalog.RefreshPrices(this);
            movementMultiplier = SurvivalSimulator.GetMovementMultiplier(currentState);
            if (hasLocalStateSnapshot && currentState.CollapseCount > previousCollapse)
            {
                if (hud != null) hud.TriggerCollapse();
                homeRespawn.Begin();
                RefreshLocalTravelPause();
                nextHomeRespawnAttempt = 0f;
                nextRespawnWarning = Time.realtimeSinceStartup + 10f;
                entry.Logger.Log("Death detected: returning the local player to the marked house.");
                ProcessPendingHomeRespawn();
            }
            hasLocalStateSnapshot = true;
            lastCollapseCount = currentState.CollapseCount;
            if (hud != null) hud.RefreshNow();
        }

        private void CreateHud()
        {
            if (hud != null) return;
            var gameObject = new GameObject("[DVSurvival HUD]");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            hud = gameObject.AddComponent<SurvivalHud>();
            hud.Initialize(this, settings);
            personalItems = gameObject.AddComponent<PersonalProvisionItems>();
            personalItems.Initialize(this);
        }

        private void DestroyHud()
        {
            if (hud == null) return;
            UnityEngine.Object.Destroy(hud.gameObject);
            hud = null;
            personalItems = null;
        }
    }
}
