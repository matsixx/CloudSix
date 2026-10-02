using CloudSix.Source;
using EFT.Rendering.Clouds;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using static EFT.Rendering.Clouds.CloudLayerRenderer;

namespace CloudSix.Patches
{
    internal class CloudShadowsDisable : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(PrecomputationData), nameof(PrecomputationData.BakeCloudShadows));
        }

        [PatchPrefix]
        static bool Prefix()
        {
            return false;
        }
    }

    internal class CloudShadowsAllocate : ModulePatch
    {
        // The game-side holder we last wired our RT into. The game re-assigns the sun cookie from it
        // every pre-render, so when OUR map reallocates (Shadow Resolution change destroys + recreates
        // the RT) it must be re-pointed or the game keeps projecting a destroyed texture.
        public static PrecomputationData lastHolder;

        public static void SyncShadowRT()
        {
            if (lastHolder != null && VolCloudRenderer.cloudShadowMap != null
                && lastHolder.cloudShadowsRT != VolCloudRenderer.cloudShadowMap)
            {
                lastHolder.cloudShadowsRT = VolCloudRenderer.cloudShadowMap;
            }
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(PrecomputationData), nameof(PrecomputationData.Allocate));
        }

        [PatchPostfix]
        static void Postfix(PrecomputationData __instance, CloudLayer cloudLayer)
        {
            VolCloudRenderer.LoadCloudPrefab();
            VolCloudRenderer.LoadShadowMaterial();
            if (VolCloudRenderer.cloudShadowMap == null) return;
            //if (!cloudLayer.Boolean_0) return;

            if (__instance.cloudShadowsRT != null
                && __instance.cloudShadowsRT != VolCloudRenderer.cloudShadowMap)
            {
                RenderTexture.ReleaseTemporary(__instance.cloudShadowsRT);
            }

            __instance.cloudShadowsRT = VolCloudRenderer.cloudShadowMap;
            lastHolder = __instance;
        }
    }

    internal class CloudShadowsRelease : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(PrecomputationData), nameof(PrecomputationData.Release));
        }

        [PatchPrefix]
        static bool Prefix(PrecomputationData __instance)
        {
            if (__instance.cloudShadowsRT == VolCloudRenderer.cloudShadowMap)
            {
                __instance.cloudShadowsRT = null;
            }
            if (__instance == CloudShadowsAllocate.lastHolder)
            {
                CloudShadowsAllocate.lastHolder = null;
            }
            return true;
        }
    }

    internal class CloudShadowsCookieSize : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CloudLayerRenderer), "GetSunLightCookieParameters");
        }

        [PatchPostfix]
        static void Postfix(ref CookieParameters cookieParams, ref bool __result)
        {
            if (VolCloudRenderer.cloudShadowMap == null) return;
            if (!__result) return;

            cookieParams.Size = new Vector2(VolCloudRenderer.SunCookieWorldSize, VolCloudRenderer.SunCookieWorldSize);
        }
    }
}
