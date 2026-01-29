using CloudSix.Source;
using EFT;
using EFT.Rendering.Clouds;
using EFT.Weather;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace CloudSix.Patches
{
    internal class DynamicClouds : ModulePatch
    {
        public static bool cleanerRan = false;
        public static Camera fpsCam;
        public static Camera opticCam;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WeatherController), nameof(WeatherController.method_9));
        }

        [PatchPostfix]
        static void Postfix(WeatherController __instance)
        {
            if (CloudRenderer.cloudPrefab == null)
                return;
            
            if (CloudRenderer.lowRenderer == null)
            {
                CloudRenderer.InitializeCloudRenderers();
                if (CloudRenderer.lowRenderer == null)
                    return;
            }

            if (fpsCam == null)
            {
                var mainCam = Camera.main;
                if (mainCam == null)
                    return;

                if (mainCam.name == "FPS Camera")
                    fpsCam = mainCam;
            }

            if (fpsCam == null)
                return;

            CloudRenderer.SetupCloudCommandBuffer(fpsCam);

            CloudRenderer.cloudInstance.transform.position = fpsCam.transform.position;

            if (CloudRenderer.cloudCommandBuffer != null && CloudRenderer.lowRenderer != null && CloudRenderer.lowMaterial != null)
            {
                CloudRenderer.cloudCommandBuffer.Clear();
                CloudRenderer.cloudCommandBuffer.DrawRenderer(CloudRenderer.lowRenderer, CloudRenderer.lowMaterial);
            }

            float cloudiness = __instance.WeatherCurve.Cloudiness;
            float rain = __instance.WeatherCurve.Rain;
            float timeOfDay = GClass4.Instance.Cycle.Hour;
            Vector2 windVector = __instance.WeatherCurve.Wind;

            CustomCloudController.UpdateWindSystem(windVector);

            // Cloudiness (-1 to 1) -> Density (0.6 clear to 1.75 overcast)
            float normalizedCloudiness = (cloudiness + 1f) * 0.5f;
            float density = Mathf.Lerp(0.6f, 1.75f, normalizedCloudiness);
            float upperDensity = Mathf.Lerp(0.6f, 1.2f, Mathf.InverseLerp(0.6f, 1.75f, density));

            CustomCloudController.CalculateLightingParameters(timeOfDay, out Color sunColor, out Color moonColor, out Vector3 sunDir, out Vector3 moonDir, out float sunIntensity, out float moonIntensity);

            // Rain darkens clouds and sun
            //sunColor = CustomCloudController.ApplyRainEffect(sunColor, rain);

            Color cloudColor = CustomCloudController.CalculateCloudColor(sunColor, moonColor, timeOfDay, out float upperBrightness);
            CustomCloudController.UpdateCloudMaterial(density, upperDensity, sunColor, moonColor, sunDir, moonDir, sunIntensity, moonIntensity, cloudColor, upperBrightness);
            CustomCloudController.UpdateCloudOffsets();

        }
    }
}
