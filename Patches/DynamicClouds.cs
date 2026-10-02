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
        public static bool EyeAdaptationIsEnabled = true;
        private static Behaviour _ultimateBloom;
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(WeatherController), nameof(WeatherController.LateUpdate));
        } 

        [PatchPostfix]
        static void Postfix(WeatherController __instance)
        {
            if (VolCloudRenderer.cloudPrefab == null)
                return;

            if (VolCloudRenderer.lowRenderer == null)
            {
                VolCloudRenderer.InitializeCloudRenderers();
                if (VolCloudRenderer.lowRenderer == null)
                    return;
            }
            if (fpsCam == null || opticCam == null)
            {
                InitializeCameras();
            }
            if (fpsCam == null)
                return;

            HandleUltimateBloom(fpsCam);

            if (CloudConfig.EyeAdaptation.Value)
            {
                fpsCam.GetComponent<PrismEffects>().exposureLowerLimit = CloudConfig.WorldExposure.Value;
                fpsCam.GetComponent<PrismEffects>().exposureUpperLimit = CloudConfig.WorldExposure.Value;
            }
            else
            {
                fpsCam.GetComponent<PrismEffects>().exposureLowerLimit = -6;
                fpsCam.GetComponent<PrismEffects>().exposureUpperLimit = 6;
            }
            

            var todSky = MonoBehaviourSingleton<TOD_Sky>.Instance;

            // When the cloud shader draws its own sun/moon disc, hide the game's built-in billboards so the
            // two don't double up and the game's HDR sun can't leak through dark cloud. They're child
            // GameObjects of the TOD_Sky dome; TOD only toggles their Renderer.enabled per frame, so
            // deactivating the GameObject sticks. The directional light is a separate object, untouched.
            SetCelestialBillboards(todSky, !CloudConfig.OwnSunMoonDisc.Value);
            ApplyMoonOrbitMode(todSky);

            VolCloudRenderer.SetupCloudCommandBuffer(fpsCam, opticCam);
            VolCloudRenderer.cloudInstance.transform.position = fpsCam.transform.position;

            float cloudiness = __instance.WeatherCurve.Cloudiness;
            float timeOfDay = TODSkyProvider.Instance.Cycle.Hour;
            Vector2 windVector = __instance.WeatherCurve.Wind;

            // Wind
            CustomCloudController.UpdateWind(windVector);

            // Cloud low and high coverage. UpdateCoverage slew-rate-limits the weather's coverage so the
            // occasional too-fast clear->overcast snap is capped, while normal gradual changes pass through.
            float normalizedCloudiness = CustomCloudController.UpdateCoverage(cloudiness);
            float density = CustomCloudController.CoverageToDensity(normalizedCloudiness);
            VolCloudRenderer.lowMaterial.SetFloat("_CloudDensity", density);

            // High/cirrus coverage ramps 0.5 -> 1.0 as the main density goes clear (0.85) -> cloudy (1.35),
            // then holds full — tied to the same density anchors so it tracks the coverage remap.
            float highCloudDensity = CustomCloudController.HighCoverageFromDensity(density);
            VolCloudRenderer.lowMaterial.SetFloat("_HighCloudCoverage", highCloudDensity);

            // Cloud type based on coverage
            float cloudType = normalizedCloudiness;
            VolCloudRenderer.lowMaterial.SetFloat("_CloudType", cloudType);
            VolCloudRenderer.lowMaterial.SetFloat("_CloudBottomHeight", CustomCloudController.cloudBottomHeight);
            VolCloudRenderer.lowMaterial.SetFloat("_CloudTopHeight", CustomCloudController.cloudTopHeight);

            // Dither policy per temporal mode. Nubis and SkyDome: step the phase once per 16-frame
            // refresh cycle — NOT static (a frozen dither pins every pixel/texel to a different band
            // phase = permanent grain; in SkyDome it bakes into the map and magnifies on screen) and NOT
            // per-frame (all 16 phases of a cycle would decorrelate at full amplitude). The per-refresh
            // own-history blend (recon's phase-pixel lerp / the bake's _CloudMapBlend EMA) then averages
            // the banding across refreshes. Blend/Off keep the per-frame animated dither.
            var tMode = CloudConfig.TemporalMode.Value;
            CustomCloudController.ditherPhaseStride =
                (tMode == VolCloudRenderer.CloudTemporalMode.Nubis ||
                 tMode == VolCloudRenderer.CloudTemporalMode.SkyDome) ? 16 : 1;

            CustomCloudController.UpdateMaterial(VolCloudRenderer.lowMaterial, timeOfDay);
            CloudConfig.ApplyToMaterial(VolCloudRenderer.lowMaterial);
            // Drive Tarkov's scene light + ambient hue from our atmosphere so the ground matches the sky (toggle).
            CustomCloudController.UpdateGroundLighting(todSky);

            // Update the cloud shadow cookie. The shadow pipeline is created HERE, eagerly — not from the
            // game's Class1819.Allocate postfix. Allocate only fires once EFT's CloudController wins its
            // startup race (init + settings + a MainCamera-tagged camera pre-rendering), which can lag raid
            // start by seconds — clouds drew with no ground shadows until then. We also assign the sun
            // cookie ourselves to cover that gap; once the game path wakes it assigns the SAME RT (the
            // Allocate postfix hands it ours), so the two writers agree.
            var mainLight = todSky.Components.LightSource;
            if (mainLight != null)
            {
                VolCloudRenderer.LoadShadowMaterial();
                if (VolCloudRenderer.ShadowPipelineReady)
                {
                    Vector3 camPos = fpsCam.transform.position;
                    VolCloudRenderer.UpdateCloudShadowMap(VolCloudRenderer.lowMaterial, mainLight, camPos);
                    CloudShadowsAllocate.SyncShadowRT();
                    EnsureSunCookie(__instance, mainLight);
                }
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
            // SkyDome: bake this frame's map slice HERE (once per frame, no camera/CB involved) so the
            // per-eye display draws below only sample the finished map. Runs after UpdateMaterial and
            // the shadow map so the baked texels carry this frame's wind/sun/weather state.
            if (tMode == VolCloudRenderer.CloudTemporalMode.SkyDome)
                VolCloudRenderer.UpdateSkyMap(fpsCam);

            // Bake the physically-based sky LUTs (once per frame, view-independent). The command buffers
            // below draw the sky dome, which samples the freshly-baked sky-view LUT.
            SkyAtmosphere.Bake(fpsCam, todSky);

            // Publish the complete displayed sky (atmosphere + clouds + discs) as a small equirect global
            // for other mods — SSRSix samples it on reflection-ray misses so water mirrors the LIVE sky.
            // Needs SkyDome mode (the cloud latlong map) AND the custom sky (the sky-view LUT it
            // composites); placed after Bake so the LUT is this frame's.
            if (tMode == VolCloudRenderer.CloudTemporalMode.SkyDome && SkyAtmosphere.Enabled &&
                CloudConfig.PublishSkyReflections.Value)
                VolCloudRenderer.PublishSkyReflMap();

            // Temporal only on the main camera — the optic's frustum is wildly different and would trash
            // the shared history; its clouds stay raw (small picture, brief looks — fine).
            VolCloudRenderer.PopulateCommandBuffer(VolCloudRenderer.mainCloudCommandBuffer, fpsCam, true);
            VolCloudRenderer.PopulateCommandBuffer(VolCloudRenderer.opticCloudCommandBuffer, opticCam, false);
        }

        // The game projects the shadow map onto terrain as the sun light's cookie (CloudController.method_0),
        // but only once its cloud path is awake. Assign the same cookie directly so ground shadows start with
        // the clouds. _sun is the serialized Light the game's own assignment targets — write that same one
        // (TOD's light as fallback) so both writers touch the same Light with the same values.
        private static readonly FieldInfo _cloudSunField = AccessTools.Field(typeof(CloudController), "_sun");
        private static void EnsureSunCookie(WeatherController weather, Light fallback)
        {
            Light sun = null;
            if (weather.CloudController != null && _cloudSunField != null)
                sun = _cloudSunField.GetValue(weather.CloudController) as Light;
            if (sun == null) sun = fallback;

            if (sun.cookie != VolCloudRenderer.cloudShadowMap)
                sun.cookie = VolCloudRenderer.cloudShadowMap;
            if (!Mathf.Approximately(sun.cookieSize, VolCloudRenderer.SunCookieWorldSize))
                sun.cookieSize = VolCloudRenderer.SunCookieWorldSize;
        }

        private static void HandleUltimateBloom(Camera cam)
        {
            if (!CloudConfig.DisableUltimateBloom.Value)
            {
                if (_ultimateBloom != null) _ultimateBloom.enabled = true;
                return;
            }
            if (_ultimateBloom == null)
                _ultimateBloom = cam.GetComponent("UltimateBloom") as Behaviour;
            if (_ultimateBloom != null)
                _ultimateBloom.enabled = false;
        }

        // Show/hide TOD's own sun & moon disc GameObjects (children of the sky dome). No-ops when already
        // in the requested state so we don't thrash SetActive every frame.
        private static void SetCelestialBillboards(TOD_Sky todSky, bool visible)
        {
            var comp = todSky?.Components;
            if (comp == null)
                return;

            if (comp.SunTransform != null && comp.SunTransform.gameObject.activeSelf != visible)
                comp.SunTransform.gameObject.SetActive(visible);
            if (comp.MoonTransform != null && comp.MoonTransform.gameObject.activeSelf != visible)
                comp.MoonTransform.gameObject.SetActive(visible);
        }

        // Optionally force TOD's moon-position mode. GameDefault leaves it alone. The current mode is logged
        // once so we can see what Tarkov actually uses (OppositeToSun => always-full moon; Realistic => real
        // date-driven phases). The moon's PHASE only varies in Realistic mode.
        private static bool _moonModeLogged;
        private static void ApplyMoonOrbitMode(TOD_Sky todSky)
        {
            if (todSky == null)
                return;

            if (!_moonModeLogged)
            {
                Plugin.MyLog.LogInfo($"[CloudSix] TOD moon position mode = {todSky.Moon.Position}. " +
                    "OppositeToSun = moon pinned 180 deg from the sun (rises where the sun sets, always FULL " +
                    "phase). Realistic = date-driven orbit with real phases. Set 'Moon Orbit Mode' in config " +
                    "to Realistic for changing phases.");
                // The cloud shadow cookie is now built in the true-sun frame (godrays line up with the visible
                // sun). The GAME still projects that cookie onto terrain along the directional light, which TOD
                // clamps to Light.MinimumHeight. If this is > 0, terrain cloud shadows can drift slightly from
                // the clouds only within a thin near-horizon (golden-hour) band; 0 = no divergence ever.
                Plugin.MyLog.LogInfo($"[CloudSix] TOD Light.MinimumHeight = {todSky.Light.MinimumHeight:0.###} " +
                    "(0 = terrain cloud shadows never diverge from clouds; >0 = a faint dawn/dusk band only).");
                _moonModeLogged = true;
            }

            switch (CloudConfig.MoonOrbit.Value)
            {
                case EMoonOrbit.OppositeToSun:
                    if (todSky.Moon.Position != TOD_MoonPositionType.OppositeToSun)
                        todSky.Moon.Position = TOD_MoonPositionType.OppositeToSun;
                    break;
                case EMoonOrbit.Realistic:
                    if (todSky.Moon.Position != TOD_MoonPositionType.Realistic)
                        todSky.Moon.Position = TOD_MoonPositionType.Realistic;
                    break;
                // GameDefault: leave whatever the game set.
            }
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
