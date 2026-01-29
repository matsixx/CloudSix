using CloudSix.Source;
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
        public static bool cleanerRan = false;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CharacterControllerSpawner), nameof(CharacterControllerSpawner.Spawn));
        }

        [PatchPostfix]
        static void Postfix(CharacterControllerSpawner __instance)
        {
            EFT.Player player = __instance.transform.root.GetComponent<EFT.Player>();

            if (player is not HideoutPlayer)
            {
                CloudRenderer.LoadCloudPrefab();
                CloudRenderer.InstantiateCloudPrefab();
            }
        }
    }
}
