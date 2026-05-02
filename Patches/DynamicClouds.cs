using CloudSix.Source;
using EFT;
using EFT.Rendering.Clouds;
using EFT.Weather;
using HarmonyLib;
using Newtonsoft.Json.Linq;
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
        private static bool frontInitialized = false;
        private static float frontDirection = 0f;
        private static float frontTargetDistance = 0f;
        private static float frontCurrentDistance = 0f;

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

            var todSky = MonoBehaviourSingleton<TOD_Sky>.Instance;
            CloudRenderer.SetupCloudCommandBuffer(fpsCam, opticCam);
            CloudRenderer.cloudInstance.transform.position = fpsCam.transform.position;

            float cloudiness = __instance.WeatherCurve.Cloudiness;
            float timeOfDay = GClass4.Instance.Cycle.Hour;
            Vector2 windVector = __instance.WeatherCurve.Wind;

            // Wind
            CustomCloudController.UpdateWind(windVector);

            // Cloud low and high coverage
            float normalizedCloudiness = (cloudiness + 1f) * 0.5f;
            float density = Mathf.Lerp(0.3f, 3f, normalizedCloudiness);
            CloudRenderer.lowMaterial.SetFloat("_CloudDensity", density);

            float highCloudDensity = Mathf.Lerp(0.5f, 1.0f, normalizedCloudiness);
            CloudRenderer.lowMaterial.SetFloat("_HighCloudCoverage", highCloudDensity);

            // Cloud type based on coverage
            float cloudType = normalizedCloudiness;
            CloudRenderer.lowMaterial.SetFloat("_CloudType", cloudType);

            CloudRenderer.lowMaterial.SetFloat("_CloudBottomHeight", 1200f);
            CloudRenderer.lowMaterial.SetFloat("_CloudTopHeight", 4000);

            CustomCloudController.UpdateMaterial(CloudRenderer.lowMaterial, timeOfDay);
            CloudConfig.ApplyToMaterial(CloudRenderer.lowMaterial);

            // Update the cloud shadow cookie          
            var mainLight = todSky.Components.LightSource;
            if (mainLight != null && CloudRenderer.cloudShadowMap != null)
            {
                Vector3 lightDir = -mainLight.transform.forward;
                Vector3 camPos = fpsCam.transform.position;

                CloudRenderer.UpdateCloudShadowMap(CloudRenderer.lowMaterial, mainLight, camPos);
            }
            /*
            // After UpdateCloudShadowMap has been called
            if (Time.frameCount % 300 == 0 && CloudRenderer.cloudShadowMap != null)
            {
                var prev = RenderTexture.active;
                RenderTexture.active = CloudRenderer.cloudShadowMap;
                var debug = new Texture2D(CloudRenderer.cloudShadowMap.width,
                                          CloudRenderer.cloudShadowMap.height,
                                          TextureFormat.RGBA32, false);
                debug.ReadPixels(new Rect(0, 0, debug.width, debug.height), 0, 0);
                debug.Apply();
                RenderTexture.active = prev;

                var path = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "CloudSix", "shadow_debug.tga");
                System.IO.File.WriteAllBytes(path, debug.EncodeToTGA());
                UnityEngine.Object.Destroy(debug);
                Plugin.MyLog.LogInfo($"Shadow dumped to {path}");
            }
            */
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
