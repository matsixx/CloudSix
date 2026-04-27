using CloudSix.Source;
using Comfort.Common;
using EFT;
using EFT.Rendering.Clouds;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CloudSix.Patches
{
    internal class SpawnClouds : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BloodOnScreen), nameof(BloodOnScreen.Start));
        }

        static readonly string[] _indoorScenes = { "factory", "laboratory", "labyrinth" };

        [PatchPrefix]
        static void Prefix(BloodOnScreen __instance)
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            Player player = gameWorld?.MainPlayer;

            if (player == null || player is HideoutPlayer)
                return;

            string currentMap = player.Location;
            if (_indoorScenes.Any(s => currentMap.Contains(s, StringComparison.OrdinalIgnoreCase)))
                return;

            CloudRenderer.CleanupClouds();
            CloudRenderer.LoadCloudPrefab();
            CloudRenderer.InstantiateCloudPrefab();
        }
    }
}
