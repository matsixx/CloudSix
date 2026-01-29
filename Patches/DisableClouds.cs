using EFT.Rendering.Clouds;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace CloudSix.Patches
{
    internal class DisableClouds : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CloudController), nameof(CloudController.UpdateAmbient));
        }

        [PatchPrefix]
        static bool Prefix(CloudController __instance)
        {
            __instance.enabled = false;
            return false;
        }
    }
}
