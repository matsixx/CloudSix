using EFT.EnvironmentEffect;
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
            return AccessTools.Method(typeof(Class1821), nameof(Class1821.RenderClouds));
        }

        [PatchPrefix]
        static bool Prefix(Class1821 __instance)
        {
            return false;
        }
    }
    internal class DisableEyeAdaptation : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EnvironmentManager), nameof(EnvironmentManager.Update));
        }

        public static bool IsEnabled = true;

        [PatchPrefix]
        static void Prefix(EnvironmentManager __instance)
        {
            if (!IsEnabled)
                return;

            __instance.PrismExposureSpeed = 0f;
        }
    }
}
