using CloudSix.Source;
using EFT.EnvironmentEffect;
using EFT.Rendering.Clouds;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

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
        static bool Prefix(EnvironmentManager __instance)
        {
            DisableEyeAdaptation.IsEnabled = CloudConfig.EyeAdaptation.Value;

            if (!IsEnabled)
                return true;
            
            if (__instance.EnableLongShadowsCorrection)
            {
                QualitySettings.shadowDistance = __instance.method_2() * __instance.Single_0;
            }

            __instance.PrismExposureOffset = 0.23f;
            __instance.PrismExposureSpeed = 0f;
            
            return false;
        }
    }
}
