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
            return AccessTools.Method(typeof(CloudLayerRenderer), nameof(CloudLayerRenderer.RenderClouds));
        }

        [PatchPrefix]
        static bool Prefix(CloudLayerRenderer __instance)
        {
            return false;
        }
    }
    
    // Hide Tarkov's TOD atmosphere gradient + star dome so our own sky renders in their place. TOD re-enables
    // both renderers every TOD_Sky.LateUpdate, so we disable them in a POSTFIX of that method (guaranteed to
    // run after TOD sets them, and before the render). No-op when Custom Sky is off -> vanilla sky returns.
    internal class DisableTodSky : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TOD_Sky), nameof(TOD_Sky.LateUpdate));
        }

        [PatchPostfix]
        static void Postfix(TOD_Sky __instance)
        {
            if (CloudConfig.CustomSky == null || !CloudConfig.CustomSky.Value)
                return;
            // Only when OUR sky is actually rendering (clouds up in a raid + LUTs baked). TOD_Sky also runs
            // in the menu; without this we'd blank the menu backdrop's sky with nothing behind it.
            if (!SkyAtmosphere.Ready || VolCloudRenderer.lowRenderer == null)
                return;
            var comp = __instance.Components;
            if (comp == null)
                return;
            if (comp.AtmosphereRenderer != null && comp.AtmosphereRenderer.enabled)
                comp.AtmosphereRenderer.enabled = false;
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
                QualitySettings.shadowDistance = __instance.GetLongShadowCorrectionFactor() * __instance.OutdoorShadowDistance;
            }

            __instance.PrismExposureOffset = 0.23f;
            __instance.PrismExposureSpeed = 0f;
            
            return false;
        }
    }
}
