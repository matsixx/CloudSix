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
using static Class1821;

namespace CloudSix.Patches
{
    internal class CloudShadowsDisable : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Class1819), nameof(Class1819.BakeCloudShadows));
        }

        [PatchPrefix]
        static bool Prefix()
        {
            return false;
        }
    }

    internal class CloudShadowsAllocate : ModulePatch
    {
        public static float capturedShadowSize = 5000f;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Class1819), nameof(Class1819.Allocate));
        }

        [PatchPostfix]
        static void Postfix(Class1819 __instance, CloudLayer cloudLayer)
        {
            CloudRenderer.LoadCloudPrefab();
            CloudRenderer.LoadShadowMaterial();
            if (CloudRenderer.cloudShadowMap == null) return;
            if (!cloudLayer.Boolean_0) return;

            capturedShadowSize = cloudLayer.ShadowSize;

            if (__instance.cloudShadowsRT != null
                && __instance.cloudShadowsRT != CloudRenderer.cloudShadowMap)
            {
                RenderTexture.ReleaseTemporary(__instance.cloudShadowsRT);
            }

            __instance.cloudShadowsRT = CloudRenderer.cloudShadowMap;
        }
    }

    internal class CloudShadowsRelease : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Class1819), nameof(Class1819.Release));
        }

        [PatchPrefix]
        static bool Prefix(Class1819 __instance)
        {
            if (__instance.cloudShadowsRT == CloudRenderer.cloudShadowMap)
            {
                __instance.cloudShadowsRT = null;
            }
            return true;
        }
    }

    internal class CloudShadowsCookieSize : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Class1821), "GetSunLightCookieParameters");
        }

        [PatchPostfix]
        static void Postfix(ref GStruct292 cookieParams, ref bool __result)
        {
            if (CloudRenderer.cloudShadowMap == null) return;
            if (!__result) return;

            cookieParams.Size = new Vector2(5000f, 5000f);
        }
    }
}
