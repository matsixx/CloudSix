using CloudSix.Source;
using Comfort.Common;
using EFT;
using EFT.Rendering.Clouds;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace CloudSix.Patches
{
    internal class SpawnClouds : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BloodOnScreen), nameof(BloodOnScreen.Start));
        }

        [PatchPrefix]
        static void Prefix(BloodOnScreen __instance)
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            Player player = gameWorld?.MainPlayer;

            if (player == null || player is HideoutPlayer)
                return;

            CloudRenderer.CleanupClouds();
            CloudRenderer.LoadCloudPrefab();
            CloudRenderer.InstantiateCloudPrefab();
        }
    }
}
