using System;
using DV;
using DV.Simulation.Cars;
using HarmonyLib;
using UnityEngine;

namespace DVSurvival.Mod
{
    // Registration is observational only: never interrupt the game's initialization.
    internal static class ClimateRegistrationErrors
    {
        private static bool reported;
        internal static void Report(Exception error)
        {
            if (reported) return;
            reported = true;
            Debug.LogWarning("[DVSurvival] Climate registration failed; periodic discovery remains enabled: " + error.Message);
        }
    }

    [HarmonyPatch(typeof(Shop), "Awake")]
    internal static class RegisterShopClimatePatch
    {
        private static void Postfix(Shop __instance)
        {
            try { SurvivalWorldRegistry.Register(__instance); }
            catch (Exception error) { ClimateRegistrationErrors.Report(error); }
        }
    }

    [HarmonyPatch(typeof(BedSleeping), "Start")]
    internal static class RegisterBedClimatePatch
    {
        private static void Postfix(BedSleeping __instance)
        {
            try { SurvivalWorldRegistry.Register(__instance); }
            catch (Exception error) { ClimateRegistrationErrors.Report(error); }
        }
    }

    [HarmonyPatch(typeof(PostProcessingVolumeAOController), "OnEnable")]
    internal static class RegisterOfficeClimatePatch
    {
        private static void Postfix(PostProcessingVolumeAOController __instance)
        {
            try { StationOfficeVolumes.Register(__instance); }
            catch (Exception error) { ClimateRegistrationErrors.Report(error); }
        }
    }

    [HarmonyPatch(typeof(TutorialPlayerDetector), "Awake")]
    internal static class RegisterTutorialOfficeClimatePatch
    {
        private static void Postfix(TutorialPlayerDetector __instance)
        {
            try { StationOfficeVolumes.Register(__instance); }
            catch (Exception error) { ClimateRegistrationErrors.Report(error); }
        }
    }

    [HarmonyPatch(typeof(FireboxSimController), nameof(FireboxSimController.Init))]
    internal static class RegisterFireboxClimatePatch
    {
        private static void Postfix(FireboxSimController __instance)
        {
            try { FireboxRegistry.Register(__instance); }
            catch (Exception error) { ClimateRegistrationErrors.Report(error); }
        }
    }
}
