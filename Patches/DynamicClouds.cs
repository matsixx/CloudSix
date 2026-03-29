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
        public static Camera fpsCam;
        public static Camera opticCam;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WeatherController), nameof(WeatherController.LateUpdate));
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
            if (fpsCam == null || opticCam == null)
            {
                InitializeCameras();
            }
            if (fpsCam == null)
                return;
            CloudRenderer.SetupCloudCommandBuffer(fpsCam, opticCam);
            CloudRenderer.cloudInstance.transform.position = fpsCam.transform.position;

            float cloudiness = __instance.WeatherCurve.Cloudiness;
            float timeOfDay = GClass4.Instance.Cycle.Hour;
            Vector2 windVector = __instance.WeatherCurve.Wind;

            // Wind
            CustomCloudController.UpdateWind(windVector);

            // Coverage: cloudiness (-1 to 1) -> coverage (0.2 clear to 1.0 overcast)
            float normalizedCloudiness = (cloudiness + 1f) * 0.5f;
            float coverage = Mathf.Lerp(0.2f, 1.9f, normalizedCloudiness);
            CloudRenderer.lowMaterial.SetFloat("_CloudDensity", coverage);

            // Cloud base lowers as coverage increases
            float heightT = Mathf.Clamp01((coverage - 0.2f) / (0.85f - 0.2f));
            heightT = heightT * heightT;
            float bottomHeight = Mathf.Lerp(1500f, 1200f, heightT);
            CloudRenderer.lowMaterial.SetFloat("_CloudBottomHeight", bottomHeight);
            CloudRenderer.lowMaterial.SetFloat("_CloudTopHeight", 4000f);

            CustomCloudController.UpdateMaterial(CloudRenderer.lowMaterial, timeOfDay);
            CloudConfig.ApplyToMaterial(CloudRenderer.lowMaterial);
            CloudRenderer.PopulateCommandBuffer(CloudRenderer.mainCloudCommandBuffer, fpsCam);
            CloudRenderer.PopulateCommandBuffer(CloudRenderer.opticCloudCommandBuffer, opticCam);
        }

        private static void InitializeCameras()
        {
            foreach (var cam in Camera.allCameras)
            {
                if (cam.name == "FPS Camera")
                    fpsCam = cam;
                else if (cam.name == "BaseOpticCamera(Clone)")
                    opticCam = cam;
            }
        }
    }
}
