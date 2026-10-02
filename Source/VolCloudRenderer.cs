using CloudSix.Patches;
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace CloudSix.Source
{
    internal class VolCloudRenderer
    {
        private static Camera lastMainCamera;
        private static Camera lastOpticCamera;

        public static CommandBuffer mainCloudCommandBuffer;
        public static CommandBuffer opticCloudCommandBuffer;
        public static GameObject cloudInstance;
        public static Renderer lowRenderer;
        public static Material lowMaterial;
        public static GameObject cloudPrefab;

        // Half-res rendering
        private static Material compositeMaterial;
        // The temporal resolve gets its OWN material: uniforms are read at CB EXECUTION, not record time,
        // so a shared material is a race — the optic buffer's recording used to disarm _CloudTemporal on
        // the shared material AFTER main recorded, freezing the main resolve whenever an optic CB existed
        // (the "jitter on and off sometimes" bug). compositeMaterial never sets _CloudTemporal at all, so
        // the pass-1 discard gate stays safe against stray all-pass blits.
        private static Material temporalMaterial;
        private static Shader compositeShader;
        private static int cloudRT = Shader.PropertyToID("_CloudRT");
        private static int traceRT = Shader.PropertyToID("_CloudTraceRT");
        public static bool useHalfRes = true;
        public static CloudResolution cloudResolution = CloudResolution.Half;

        public enum CloudTemporalMode
        {
            Off,
            Blend,   // TAA-style history blend (Temporal Smoothing)
            Nubis,   // HZD amortized reconstruction: trace 1/16 of pixels/frame, reproject the rest
            SkyDome  // direction-anchored latlong accumulation: the march bakes into a persistent sky
                     // map (1/16 of texels/frame, view-cone prioritized); display = one texture sample
                     // per pixel. No reprojection, no history resampling -> head motion CANNOT blur it
                     // (built for VR, where the camera never rests and screen-space history smears).
        }

        // ---- SkyDome mode state ----
        private static RenderTexture skyMap;      // persistent latlong accumulation (ARGBHalf, U wraps)
        private static RenderTexture skyTraceRT;  // compact 1/16 bake target (one texel per 4x4 map block)
        // Latlong map height in texels (width = 2x). ~1536 = 8.5 px/deg ≈ half-res screen density; raise
        // for sharper clouds at linear VRAM/ray cost (2048 = 64MB + ~1.8x rays). Live-tunable; the map
        // reallocates + refills on change.
        public static int skyMapHeight = 1536;
        // Refresh-cone margin beyond the camera's half-FOV: texels inside refresh every 16-frame cycle,
        // outside only via the slow wheel (1/8 of blocks per cycle). Bigger = fresher periphery when
        // turning fast, at more rays/frame.
        public static float skyConeMarginDeg = 25f;
        // Per-refresh EMA toward each texel's previous map value. With the dither phase stepping once
        // per refresh cycle, this averages the march's banding noise out of the map (the static grain a
        // frozen dither bakes in). Higher = smoother clouds but slower response to weather/sun changes
        // (~1/(1-w) refreshes ≈ 0.4s in view at 0.6). 0 = plain replace. Live-tunable.
        public static float skyMapBlend = 0.6f;
        private static int skyFillCycles;         // full-sphere refresh cycles remaining (startup/reset)

        // Shader pass indices
        private const int PassDomeMarch = 0;         // CloudSixVolClouds: classic per-pixel march
        private const int PassSkyTrace = 1;          // CloudSixVolClouds: SkyDome compact latlong bake
        private const int PassSkyDisplay = 2;        // CloudSixVolClouds: SkyDome map-sample display
        private const int PassSkyRefl = 3;           // CloudSixVolClouds: full-sky equirect publish for reflections
        private const int PassCompositeScatter = 3;  // CloudComposite: bake -> persistent map scatter

        // Nubis phase order: a 4x4 Bayer sequence spreads the refreshed pixel maximally within each block,
        // so the 16-frame refill never reads as a sweep.
        private static readonly int[] bayerOrder = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        // The optic CB draws the same dome but must NOT see the main path's per-frame _CloudTraceJitter
        // (material uniforms resolve at CB EXECUTION — the shared-material race again). It gets a clone,
        // property-synced once per populate, with the jitter zeroed.
        private static Material opticDomeMaterial;

        private static Material shadowMaterial;
        private static Shader shadowShader;
        public static RenderTexture cloudShadowMap;
        private static int shadowMapSize = 1024;
        private static bool shadowShaderErrorLogged;
        // World meters the sun cookie projects over. Shared by the game-side cookie-size patch and our
        // direct assignment so the two writers can never flip-flop. (Deliberately NOT ShadowWorldSize —
        // the shipped look was tuned with the cookie at 5000 while the map spans ShadowWorldSize.)
        public const float SunCookieWorldSize = 5000f;
        public static bool ShadowPipelineReady => shadowMaterial != null;

        public enum EShadowResolution { Low, Medium, High }
        public static int ShadowResPixels(EShadowResolution r)
        {
            return r == EShadowResolution.Low ? 512 : (r == EShadowResolution.High ? 2048 : 1024);
        }

        // Temporal accumulation (main camera only): each frame's cloud render is resolved against last
        // frame's result (CloudComposite pass 1: direction reprojection + neighborhood clamp), so the
        // march's animated golden-ratio dither (_DitherPhase) averages into clean clouds at low step
        // counts — same recipe as FogSix/SSRSix. Ping-pong pair; deliberately SHARED across VR eyes:
        // clouds at dome distance have zero stereo disparity, so one history serves both.
        private static RenderTexture histRead, histWrite;
        private static bool histValid;
        public enum CloudResolution
        {
            Full,
            ThreeQuarter,
            Half
        }

        public static void LoadCloudPrefab()
        {
            if (cloudPrefab != null)
                return;

            try
            {
                string bundlePath = Path.Combine(BepInEx.Paths.PluginPath, "CloudSix", "Assets", "volumetricclouds");
                AssetBundle cloudBundle = AssetBundle.LoadFromFile(bundlePath);
                if (cloudBundle == null)
                {
                    Plugin.MyLog.LogError("Failed to load cloud AssetBundle.");
                    return;
                }

                cloudPrefab = cloudBundle.LoadAsset<GameObject>("Cloudsix");
                compositeShader = cloudBundle.LoadAsset<Shader>("CloudComposite");
                shadowShader = cloudBundle.LoadAsset<Shader>("CloudShadowMap");
                SkyAtmosphere.SetShader(cloudBundle.LoadAsset<Shader>("CloudSixAtmosphere"));
                cloudBundle.Unload(false);
                GameObject.DontDestroyOnLoad(cloudPrefab);
                Plugin.MyLog.LogInfo("Cloud prefab loaded successfully.");
            }
            catch (Exception ex)
            {
                Plugin.MyLog.LogError($"Error loading cloud prefab: {ex.Message}");
            }
        }

        public static void LoadShadowMaterial()
        {
            if (shadowMaterial != null) return;
            if (shadowShader != null)
            {
                Plugin.MyLog.LogInfo($"shadowShader loaded: name='{shadowShader.name}', isSupported={shadowShader.isSupported}");
                shadowMaterial = new Material(shadowShader);
                Plugin.MyLog.LogInfo($"Shadow material created. Material's shader: '{shadowMaterial.shader.name}', passes={shadowMaterial.passCount}");
            }
            else if (!shadowShaderErrorLogged)   // one-shot: this is now called every frame
            {
                Plugin.MyLog.LogError("Shadow shader is null!");
                shadowShaderErrorLogged = true;
            }

            if (cloudShadowMap == null)
            {
                cloudShadowMap = new RenderTexture(shadowMapSize, shadowMapSize, 0, RenderTextureFormat.ARGB32);
                cloudShadowMap.name = "CloudSix Shadow Map";
                cloudShadowMap.wrapMode = TextureWrapMode.Repeat;
                cloudShadowMap.filterMode = FilterMode.Bilinear;
                cloudShadowMap.Create();
                Plugin.MyLog.LogInfo($"Shadow RT created: {cloudShadowMap.IsCreated()}");
            }
        }

        public static void LoadCompositeMaterial()
        {
            if (compositeMaterial != null)
                return;

            if (compositeShader != null)
            {
                compositeMaterial = new Material(compositeShader);
                temporalMaterial = new Material(compositeShader);
                Plugin.MyLog.LogInfo("Cloud composite material loaded.");
            }
            else
            {
                Plugin.MyLog.LogError("Composite shader not loaded from bundle. Half-res rendering disabled.");
                useHalfRes = false;
            }
        }

        public static void InstantiateCloudPrefab()
        {
            // Clean up main camera buffer
            if (mainCloudCommandBuffer != null)
            {
                if (lastMainCamera != null)
                    lastMainCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, mainCloudCommandBuffer);
                mainCloudCommandBuffer.Dispose();
                mainCloudCommandBuffer = null;
            }
            lastMainCamera = null;

            // Clean up optic camera buffer
            if (opticCloudCommandBuffer != null)
            {
                if (lastOpticCamera != null)
                    lastOpticCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, opticCloudCommandBuffer);
                opticCloudCommandBuffer.Dispose();
                opticCloudCommandBuffer = null;
            }
            lastOpticCamera = null;

            if (cloudInstance != null)
            {
                GameObject.Destroy(cloudInstance);
                cloudInstance = null;
            }

            lowRenderer = null;
            lowMaterial = null;

            if (cloudPrefab != null && cloudInstance == null)
            {
                cloudInstance = GameObject.Instantiate(cloudPrefab);
                //cloudInstance.transform.position = new Vector3(0, -70, 0);
                //cloudInstance.transform.localScale = new Vector3(10f, 10f, 10f);
                Plugin.MyLog.LogInfo("Cloud prefab instantiated.");
            }
            
            LoadCompositeMaterial();
        }

        private static Vector2 RandomDirection()
        {
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        public static void InitializeCloudRenderers()
        {
            if (cloudInstance == null)
                return;

            Transform lowCloud = cloudInstance.transform.Find("Low");

            if (lowCloud != null)
            {
                lowCloud.gameObject.layer = 28;

                lowRenderer = lowCloud.GetComponent<Renderer>();
                if (lowRenderer != null)
                {
                    lowRenderer.allowOcclusionWhenDynamic = false;
                    lowRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                    lowMaterial = lowRenderer.material;
                }
            }

            if (lowMaterial != null)
            {
                
                CustomCloudController.windOffset = new Vector4(
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f),
                    0f
                );
                
                CustomCloudController.macroOffset = new Vector3(
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f)
                );

                CustomCloudController.lastWindDirection = RandomDirection();
                CustomCloudController.RandomizeHeights(UnityEngine.Random.Range);
                CustomCloudController.ResetCoverage();   // snap coverage limiter to this raid's weather
            }
        }

        public static void SetupCloudCommandBuffer(Camera mainCamera, Camera opticCamera)
        {
            if (mainCamera == null || lowRenderer == null)
                return;

            if (lastMainCamera != null && lastMainCamera != mainCamera && mainCloudCommandBuffer != null)
            {
                lastMainCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, mainCloudCommandBuffer);
                mainCloudCommandBuffer.Dispose();
                mainCloudCommandBuffer = null;
            }

            if (mainCloudCommandBuffer == null)
            {
                lowRenderer.enabled = false;
                mainCloudCommandBuffer = new CommandBuffer();
                mainCloudCommandBuffer.name = "Custom Clouds Main";
                mainCamera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, mainCloudCommandBuffer);
                lastMainCamera = mainCamera;
            }

            if (opticCamera != null)
            {
                if (lastOpticCamera != null && lastOpticCamera != opticCamera && opticCloudCommandBuffer != null)
                {
                    lastOpticCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, opticCloudCommandBuffer);
                    opticCloudCommandBuffer.Dispose();
                    opticCloudCommandBuffer = null;
                }

                if (opticCloudCommandBuffer == null)
                {
                    opticCloudCommandBuffer = new CommandBuffer();
                    opticCloudCommandBuffer.name = "Custom Clouds Optic";
                    opticCamera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, opticCloudCommandBuffer);
                    lastOpticCamera = opticCamera;
                }
            }
            else if (opticCloudCommandBuffer != null)
            {
                if (lastOpticCamera != null)
                    lastOpticCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, opticCloudCommandBuffer);
                opticCloudCommandBuffer.Dispose();
                opticCloudCommandBuffer = null;
                lastOpticCamera = null;
            }
        }

        // ---- SkyDome map update: runs ONCE per frame from WeatherController.LateUpdate (no camera, no
        // command buffer, no per-eye anything — the map is view-independent). Bakes this frame's 1/16 of
        // texels at full march quality into the compact RT, then scatters them into the persistent map.
        // Translation parallax needs no handling: at cloud distance the refresh itself (0.25s full cycle
        // in-view) tracks camera movement far faster than it could ever be seen.
        public static void UpdateSkyMap(Camera cam)
        {
            if (lowMaterial == null || compositeMaterial == null || lowRenderer == null || cam == null)
                return;

            int h = Mathf.Clamp(skyMapHeight, 256, 4096) & ~3;
            int w = h * 2;
            if (skyMap == null || skyMap.height != h)
            {
                ReleaseSkyMap();
                skyMap = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapModeU = TextureWrapMode.Repeat,   // bilinear crosses the azimuth seam
                    wrapModeV = TextureWrapMode.Clamp
                };
                skyTraceRT = new RenderTexture(w / 4, h / 4, 0, RenderTextureFormat.ARGBHalf)
                { filterMode = FilterMode.Point };
                RenderTexture prevActive = RenderTexture.active;
                RenderTexture.active = skyMap; GL.Clear(false, true, Color.clear);
                RenderTexture.active = prevActive;
                skyFillCycles = 3;    // fill the whole sphere once: 16 phases = 1 cycle, +margin for
                                      // starting mid-cycle (~0.8s of full-sphere tracing, raid-load noise)
            }

            int n = bayerOrder[Time.frameCount & 15];
            int px = n & 3, py = n >> 2;
            if ((Time.frameCount & 15) == 0 && skyFillCycles > 0) skyFillCycles--;

            // Refresh cone = the camera frustum's half-diagonal + margin, facing where the player looks.
            float tv = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float th = tv * cam.aspect;
            float halfDiag = Mathf.Atan(Mathf.Sqrt(tv * tv + th * th));
            float coneCos = Mathf.Cos(halfDiag + skyConeMarginDeg * Mathf.Deg2Rad);
            Vector3 fwd = cam.transform.forward;
            Vector4 cone = new Vector4(fwd.x, fwd.y, fwd.z, (skyFillCycles > 0) ? -2f : coneCos);

            lowMaterial.SetVector("_CloudMapPhase", new Vector4(px, py, 0f, 0f));
            lowMaterial.SetVector("_CloudMapSize", new Vector4(w, h, 0f, 0f));
            lowMaterial.SetVector("_CloudMapCone", cone);
            lowMaterial.SetFloat("_CloudMapCycle", (Time.frameCount >> 4) & 7);
            lowMaterial.SetFloat("_CloudMapBlend", Mathf.Clamp01(skyMapBlend));
            lowMaterial.SetVector("_CloudTraceOrigin", cam.transform.position);
            Graphics.Blit(null, skyTraceRT, lowMaterial, PassSkyTrace);

            compositeMaterial.SetFloat("_CloudScatterOn", 1f);
            compositeMaterial.SetVector("_CloudMapPhase", new Vector4(px, py, 0f, 0f));
            Graphics.Blit(skyTraceRT, skyMap, compositeMaterial, PassCompositeScatter);

            lowMaterial.SetTexture("_CloudSkyMap", skyMap);
        }

        private static void ReleaseSkyMap()
        {
            if (skyMap != null) { skyMap.Release(); UnityEngine.Object.Destroy(skyMap); skyMap = null; }
            if (skyTraceRT != null) { skyTraceRT.Release(); UnityEngine.Object.Destroy(skyTraceRT); skyTraceRT = null; }
            if (skyReflMap != null)
            {
                Shader.SetGlobalTexture("_SsrSkyReflMap", null);   // unbind so consumers gate off cleanly
                skyReflMap.Release(); UnityEngine.Object.Destroy(skyReflMap); skyReflMap = null;
            }
        }

        // ---- Reflection sky publish (consumed by SSRSix via the global _SsrSkyReflMap) ----
        // Bakes the complete DISPLAYED sky — atmosphere + accumulated clouds + sun/moon discs, in scene
        // radiance units — into a small upper-hemisphere equirect map and publishes it as a shader GLOBAL,
        // so other mods can sample the live sky by direction with zero assembly dependency (an interested
        // consumer checks GetGlobalTexture != null; nobody listening = one cheap 512x256 blit). SSRSix
        // reflects it on ray miss: puddles mirror the actual clouds instead of a stale baked probe.
        // SkyDome-mode only (the latlong cloud map this composites is only maintained there).
        private static RenderTexture skyReflMap;

        public static void PublishSkyReflMap()
        {
            if (lowMaterial == null || skyMap == null) return;
            if (skyReflMap == null)
            {
                skyReflMap = new RenderTexture(512, 256, 0, RenderTextureFormat.ARGBHalf)
                {
                    filterMode = FilterMode.Trilinear,    // consumers fetch lobe-matched mips (SSRSix specular AA)
                    useMipMap = true,
                    autoGenerateMips = false,             // baked once per publish, below
                    wrapModeU = TextureWrapMode.Repeat,   // bilinear crosses the azimuth seam
                    wrapModeV = TextureWrapMode.Clamp
                };
            }
            Graphics.Blit(null, skyReflMap, lowMaterial, PassSkyRefl);
            // Mip chain = the sky pre-integrated over widening cones: a rough/varied reflector must fetch
            // an AVERAGE of the sky over its lobe, and a mip-0 fetch through a detail normal is a white-
            // speck generator (SSRSix picks the mip from its effective roughness).
            skyReflMap.GenerateMips();
            Shader.SetGlobalTexture("_SsrSkyReflMap", skyReflMap);
        }

        // Populates a command buffer with cloud rendering commands.
        // If useHalfRes is true, renders to a half-res RT then composites back.

        public static void PopulateCommandBuffer(CommandBuffer cmd, Camera cam, bool allowTemporal)
        {
            if (cmd == null || cam == null || lowRenderer == null)
                return;

            cmd.Clear();

            // Disarm the render-time matrix capture by default; only the Nubis/Blend resolve paths
            // re-arm it below. Keeps OnCamPreRender a no-op (and the reprojection uniforms untouched)
            // in SkyDome/Off/Full, where nothing reads them. Main populate only — the optic populate
            // runs AFTER main and must not clear what main just armed.
            if (allowTemporal)
                temporalCam = null;

            // Our physically-based sky, drawn FIRST (behind the clouds) on the same dome — replaces
            // Tarkov's disabled atmosphere dome. View-independent LUT, so main + optic share one bake.
            SkyAtmosphere.DrawSky(cmd, lowRenderer);

            // SkyDome: the map was already baked this frame in LateUpdate; the camera just draws the
            // display dome — ONE texture sample per pixel, per-eye correct for free (the draw uses the
            // rendering eye's matrices), occluded natively by scene depth like the Full path. The
            // resolution setting is irrelevant here (display cost is trivial at any res).
            if (allowTemporal && CloudConfig.TemporalMode.Value == CloudTemporalMode.SkyDome && skyMap != null)
            {
                ReleaseHistory();   // the screen-space modes' ping-pong RTs are dead weight here
                lowMaterial.SetVector("_CloudTraceJitter", Vector4.zero);
                lowMaterial.SetVector("_CloudDitherMap", new Vector4(1f, 0f, 0f, 0f));
                cmd.DrawRenderer(lowRenderer, lowMaterial, 0, PassSkyDisplay);
                return;
            }

            if (cloudResolution != CloudResolution.Full && compositeMaterial != null)
            {
                int divisor;
                switch (cloudResolution)
                {
                    case CloudResolution.ThreeQuarter:
                        divisor = 4;
                        break;
                    case CloudResolution.Half:
                    default:
                        divisor = 2;
                        break;
                }

                int width, height;
                if (cloudResolution == CloudResolution.ThreeQuarter)
                {
                    width = cam.pixelWidth * 3 / 4;
                    height = cam.pixelHeight * 3 / 4;
                }
                else
                {
                    width = cam.pixelWidth / 2;
                    height = cam.pixelHeight / 2;
                }

                CloudTemporalMode mode = allowTemporal ? CloudConfig.TemporalMode.Value : CloudTemporalMode.Off;
                if (mode == CloudTemporalMode.SkyDome)
                    mode = CloudTemporalMode.Off;   // map not ready (early return above didn't fire) -> raw
                float smoothing = Mathf.Clamp(CloudConfig.TemporalSmoothing.Value, 0f, 0.95f);
                if (temporalMaterial == null || (mode == CloudTemporalMode.Blend && smoothing <= 0.001f))
                    mode = CloudTemporalMode.Off;
                bool flip = CloudConfig.ReprojectionFlip.Value;

                if (allowTemporal && mode == CloudTemporalMode.Nubis)
                {
                    // ---- Nubis/HZD amortized reconstruction: trace ONE half-res pixel per 4x4 block per
                    // frame at full quality into a compact 1/16-size RT (the ray nudged off the block
                    // centre via _CloudTraceJitter), then shader pass 2 rebuilds the full image: the phase
                    // pixel takes the fresh trace outright, everything else reprojects last frame's
                    // reconstruction. No blend, no clamp -> no ghosting, no shimmer, ~16x cheaper march.
                    // Snap the recon target to multiples of 4: the trace RT rasterizes the full frustum
                    // over ceil(w/4) pixels, so unless w == 4*traceW exactly, trace texel centres drift
                    // off their 4x4 blocks across the screen (up to ~3 half-res px at the far edge) and
                    // every fresh sample lands skewed from where the recon places it. The composite
                    // upscale absorbs the (<=3px) size difference invisibly.
                    width &= ~3; height &= ~3;
                    int traceW = width / 4, traceH = height / 4;
                    int n = bayerOrder[Time.frameCount & 15];
                    int px = n & 3, py = n >> 2;
                    lowMaterial.SetVector("_CloudTraceJitter", new Vector4(px - 1.5f, py - 1.5f, 0f, 0f));
                    // The march dither must follow the HALF-RES pixel each trace sample represents — on
                    // trace-RT raster coords all 16 pixels of a block share one dither value and the grain
                    // quantizes into a visible 4x4 checkerboard.
                    lowMaterial.SetVector("_CloudDitherMap", new Vector4(4f, px, py, 0f));

                    cmd.GetTemporaryRT(traceRT, traceW, traceH, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
                    cmd.SetRenderTarget(traceRT);
                    cmd.ClearRenderTarget(false, true, new Color(0, 0, 0, 0));
                    // Explicit pass 0: the dome shader is multi-pass now (SkyDome bake/display passes) and
                    // a pass-less DrawRenderer renders ALL passes — the Blit variant of saga rule 1.
                    cmd.DrawRenderer(lowRenderer, lowMaterial, 0, PassDomeMarch);

                    EnsureHistory(width, height);
                    temporalMaterial.SetFloat("_CloudReconOn", 1f);
                    temporalMaterial.SetFloat("_CloudHistValid", histValid ? 1f : 0f);
                    temporalMaterial.SetVector("_CloudPhase", new Vector4(px, py, 0f, 0f));
                    temporalMaterial.SetTexture("_CloudHistTex", histRead);
                    temporalMaterial.SetFloat("_CloudReproFlip", flip ? 1f : 0f);
                    temporalMaterial.SetFloat("_CloudLayerY",
                        (CustomCloudController.cloudBottomHeight + CustomCloudController.cloudTopHeight) * 0.5f);
                    temporalMaterial.SetFloat("_CloudPlanetRadius", PlanetRadius());
                    temporalMaterial.SetFloat("_CloudDebugView", (float)(int)CloudConfig.DebugView.Value);
                    // _CloudInvVP/_CloudPrevVP are NOT set here: this runs in WeatherController.LateUpdate,
                    // and Tarkov's procedural camera (ADS zoom, aim inertia, breathing) keeps moving the
                    // camera/FOV until render. They're captured in OnCamPreRender — the true render state.
                    ArmPreRenderCapture(cam);

                    cmd.Blit(traceRT, histWrite, temporalMaterial, 2);                                // reconstruct
                    cmd.Blit(histWrite, BuiltinRenderTextureType.CameraTarget, compositeMaterial, 0); // composite
                    cmd.ReleaseTemporaryRT(traceRT);

                    histValid = true;
                    RenderTexture t = histRead; histRead = histWrite; histWrite = t;
                }
                else
                {
                    // Only the MAIN populate may touch lowMaterial (execution-time material reads: the
                    // optic recording must never stomp the main path's per-frame state).
                    if (allowTemporal)
                    {
                        lowMaterial.SetVector("_CloudTraceJitter", Vector4.zero);
                        lowMaterial.SetVector("_CloudDitherMap", new Vector4(1f, 0f, 0f, 0f));
                    }
                    Material domeMat = allowTemporal ? lowMaterial : OpticDomeMaterial();

                    cmd.GetTemporaryRT(cloudRT, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
                    cmd.SetRenderTarget(cloudRT);
                    cmd.ClearRenderTarget(false, true, new Color(0, 0, 0, 0));
                    cmd.DrawRenderer(lowRenderer, domeMat, 0, PassDomeMarch);

                    if (allowTemporal && mode == CloudTemporalMode.Blend)
                    {
                        EnsureHistory(width, height);
                        if (!histValid)
                        {
                            // Seed frame: plain copy into history (a fresh RT is undefined garbage).
                            cmd.Blit(cloudRT, histWrite);
                            cmd.Blit(cloudRT, BuiltinRenderTextureType.CameraTarget, compositeMaterial, 0);
                        }
                        else
                        {
                            temporalMaterial.SetTexture("_CloudHistTex", histRead);
                            temporalMaterial.SetFloat("_CloudTemporal", smoothing);
                            temporalMaterial.SetFloat("_CloudReproFlip", flip ? 1f : 0f);
                            temporalMaterial.SetFloat("_CloudLayerY",
                                (CustomCloudController.cloudBottomHeight + CustomCloudController.cloudTopHeight) * 0.5f);
                            temporalMaterial.SetFloat("_CloudPlanetRadius", PlanetRadius());
                            temporalMaterial.SetFloat("_CloudDebugView", (float)(int)CloudConfig.DebugView.Value);
                            ArmPreRenderCapture(cam);   // matrices at render time, not record time

                            cmd.Blit(cloudRT, histWrite, temporalMaterial, 1);                              // resolve
                            cmd.Blit(histWrite, BuiltinRenderTextureType.CameraTarget, compositeMaterial, 0); // composite
                        }

                        histValid = true;
                        RenderTexture t = histRead; histRead = histWrite; histWrite = t;
                    }
                    else
                    {
                        cmd.Blit(cloudRT, BuiltinRenderTextureType.CameraTarget, compositeMaterial, 0);
                    }
                    cmd.ReleaseTemporaryRT(cloudRT);
                }
            }
            else
            {
                // Full-resolution path: direct dome draw, no composite chain (and no amortization).
                if (allowTemporal)
                {
                    lowMaterial.SetVector("_CloudTraceJitter", Vector4.zero);
                    lowMaterial.SetVector("_CloudDitherMap", new Vector4(1f, 0f, 0f, 0f));
                }
                cmd.DrawRenderer(lowRenderer, allowTemporal ? lowMaterial : OpticDomeMaterial(), 0, PassDomeMarch);
            }
        }

        // ---- render-time camera capture for the temporal reprojection ----
        // The CB is populated in WeatherController.LateUpdate, but Tarkov's procedural camera (ADS zoom,
        // aim inertia, breathing, recoil) keeps changing the camera pose AND its FOV until the moment it
        // renders — and the dome draws with that true render state. Matrices captured at record time are
        // stale by exactly the inertia delta, so the reprojection mislands hardest during ADS and fast
        // looks (jitter/ghosting that intensifies with camera motion). Camera.onPreRender fires after ALL
        // camera manipulation, right before the CB executes: material uniforms resolve at execution, so
        // values set here are what the resolve actually reads.
        private static Camera temporalCam;
        private static Matrix4x4 prevRenderVP;      // matrices of the render that WROTE the current history
        private static Matrix4x4 lastCapturedVP;    // most recent capture; promoted to prevRenderVP on the next frame
        private static bool prevRenderVPValid;
        private static bool lastCapturedVPValid;
        private static int captureFrame = -1;
        private static int firesThisFrame, firesLastFrame;   // diagnostics (logged while Debug View is on)
        private static bool preRenderHooked;

        private static void ArmPreRenderCapture(Camera cam)
        {
            temporalCam = cam;
            if (!preRenderHooked)
            {
                Camera.onPreRender += OnCamPreRender;
                preRenderHooked = true;
            }
        }

        private static void OnCamPreRender(Camera cam)
        {
            if (cam == null || cam != temporalCam || temporalMaterial == null)
                return;

            // VR MULTIPASS: this fires once PER EYE (the camera renders twice per frame), and each eye
            // rasterizes with its own view matrix and an OFF-AXIS ASYMMETRIC projection that the mono
            // cam.worldToCameraMatrix/projectionMatrix do NOT describe. The game-wide established pattern
            // (AmplifyOcclusion, SSRSix, the upscaler patches) is stereoActiveEye + the GetStereo*
            // getters. Uniforms resolve at CB execution and each fire immediately precedes its own eye's
            // render, so the values set here are per-eye correct.
            // NON-jittered projection, critically: TAA/FSR/DLSS tilt the raw projection by a sub-pixel
            // jitter every frame, so reprojection built from it mislands by the jitter delta (~1px swim).
            Matrix4x4 view, proj;
            if (cam.stereoEnabled)
            {
                Camera.StereoscopicEye eye = (cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right)
                    ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left;
                view = cam.GetStereoViewMatrix(eye);
                proj = cam.GetStereoNonJitteredProjectionMatrix(eye);
            }
            else
            {
                view = cam.worldToCameraMatrix;
                proj = cam.nonJitteredProjectionMatrix;
            }
            Matrix4x4 vp = proj * view;

            // _CloudPrevVP must describe the render that actually WROTE the history being read — the LAST
            // render of the PREVIOUS frame (in VR both eyes blit into the same histWrite, so the right
            // eye's output is what survives the ping-pong). Promote once per frame and NEVER between
            // same-frame fires: the old per-fire update handed the right eye THIS frame's left-eye
            // matrices for LAST frame's right-eye content — a constant eye-to-eye mis-registration that
            // the Nubis pass re-resampled into the history every frame (the directional stretch/wobble;
            // Blend's 3x3 clamp merely bounded the same error, which is why lower smoothing hurt less).
            // This also stays correct under ANY extra same-camera render per frame, not just stereo.
            if (Time.frameCount != captureFrame)
            {
                captureFrame = Time.frameCount;
                prevRenderVP = lastCapturedVP;
                prevRenderVPValid = lastCapturedVPValid;
                firesLastFrame = firesThisFrame;
                firesThisFrame = 0;
            }
            firesThisFrame++;

            temporalMaterial.SetMatrix("_CloudInvVP", InverseD(vp));
            temporalMaterial.SetMatrix("_CloudPrevVP", prevRenderVPValid ? prevRenderVP : vp);
            // The current VP too: the shader reprojects as ndc + (prev - cur) so the float roundtrip
            // error cancels — bitwise-equal matrices (still camera) give an EXACTLY zero delta.
            temporalMaterial.SetMatrix("_CloudCurVP", vp);
            // Eye position derived FROM THE VIEW MATRIX, never transform.position: EFT's camera rig keeps
            // the two DIFFERENT (the TOD-fog "weird eyePos" finding — FogSix/SSRSix already derive it this
            // way). The anchor math bends every ray through this point.
            temporalMaterial.SetVector("_CloudCamPos", view.inverse.MultiplyPoint3x4(Vector3.zero));

            lastCapturedVP = vp;
            lastCapturedVPValid = true;

            // Diagnostics, only while a Debug View is up: fires/frame (VR multipass = 2, flatscreen = 1;
            // anything else = an extra render is stepping the temporal state) + matrix motion since the
            // last fire (~0 standing still). Console writes HITCH — never leave this unconditional.
            if (CloudConfig.DebugView.Value != ECloudDebugView.Off && firesThisFrame == 1 && (Time.frameCount % 120) == 0)
            {
                float d = 0f;
                for (int i = 0; i < 16; i++) d += Mathf.Abs(vp[i] - prevRenderVP[i]);
                Vector3 eyePos = view.inverse.MultiplyPoint3x4(Vector3.zero);
                Plugin.MyLog.LogInfo($"[CloudTemporal] frame={Time.frameCount} firesLastFrame={firesLastFrame} " +
                    $"stereo={cam.stereoEnabled} eye={cam.stereoActiveEye} |vp-prevVP|={d:F6} " +
                    $"eyePos=({eyePos.x:F3}, {eyePos.y:F3}, {eyePos.z:F3})");
            }
        }

        // Double-precision 4x4 inverse (glMatrix cofactor scheme). Matrix4x4.inverse is FLOAT32, and for
        // a view-projection matrix that mixes ~1-scale rotation/projection terms with a hundreds-of-
        // meters translation column, its cancellation error leaves invVP*VP off identity by a small
        // CONSTANT amount. The shader's uv->world->prev-uv roundtrip then misses by a fixed sub-pixel
        // offset even with bit-identical matrices — a drift pump for the recon's copy loop: cloud pixels
        // smear off in one direction while standing perfectly still, edges go blocky from repeated
        // off-registered resampling, and the ReprojectionDelta debug view shows the faint static
        // direction-dependent colours that haunted this feature from the start (the x200 scale made the
        // numerical error visible). Double precision buys ~3 orders of magnitude; the residual is at
        // float-cast level (~0.001 px/frame), invisible.
        private static Matrix4x4 InverseD(Matrix4x4 m)
        {
            double a00 = m.m00, a01 = m.m01, a02 = m.m02, a03 = m.m03;
            double a10 = m.m10, a11 = m.m11, a12 = m.m12, a13 = m.m13;
            double a20 = m.m20, a21 = m.m21, a22 = m.m22, a23 = m.m23;
            double a30 = m.m30, a31 = m.m31, a32 = m.m32, a33 = m.m33;

            double b00 = a00 * a11 - a01 * a10;
            double b01 = a00 * a12 - a02 * a10;
            double b02 = a00 * a13 - a03 * a10;
            double b03 = a01 * a12 - a02 * a11;
            double b04 = a01 * a13 - a03 * a11;
            double b05 = a02 * a13 - a03 * a12;
            double b06 = a20 * a31 - a21 * a30;
            double b07 = a20 * a32 - a22 * a30;
            double b08 = a20 * a33 - a23 * a30;
            double b09 = a21 * a32 - a22 * a31;
            double b10 = a21 * a33 - a23 * a31;
            double b11 = a22 * a33 - a23 * a32;

            double det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
            if (System.Math.Abs(det) < 1e-30)
                return m.inverse;   // degenerate — let Unity produce whatever it produces
            double id = 1.0 / det;

            Matrix4x4 r = default;
            r.m00 = (float)((a11 * b11 - a12 * b10 + a13 * b09) * id);
            r.m01 = (float)((a02 * b10 - a01 * b11 - a03 * b09) * id);
            r.m02 = (float)((a31 * b05 - a32 * b04 + a33 * b03) * id);
            r.m03 = (float)((a22 * b04 - a21 * b05 - a23 * b03) * id);
            r.m10 = (float)((a12 * b08 - a10 * b11 - a13 * b07) * id);
            r.m11 = (float)((a00 * b11 - a02 * b08 + a03 * b07) * id);
            r.m12 = (float)((a32 * b02 - a30 * b05 - a33 * b01) * id);
            r.m13 = (float)((a20 * b05 - a22 * b02 + a23 * b01) * id);
            r.m20 = (float)((a10 * b10 - a11 * b08 + a13 * b06) * id);
            r.m21 = (float)((a01 * b08 - a00 * b10 - a03 * b06) * id);
            r.m22 = (float)((a30 * b04 - a31 * b02 + a33 * b00) * id);
            r.m23 = (float)((a21 * b02 - a20 * b04 - a23 * b00) * id);
            r.m30 = (float)((a11 * b07 - a10 * b09 - a12 * b06) * id);
            r.m31 = (float)((a00 * b09 - a01 * b07 + a02 * b06) * id);
            r.m32 = (float)((a31 * b01 - a30 * b03 - a32 * b00) * id);
            r.m33 = (float)((a20 * b03 - a21 * b01 + a22 * b00) * id);
            return r;
        }

        // The march's curvature radius, for the reprojection anchor (must match the shell the clouds
        // actually render on). Missing/zero property (flat-mode builds) degrades to a near-flat huge
        // sphere, which is exactly the flat-plane behaviour.
        private static float PlanetRadius()
        {
            float r = (lowMaterial != null && lowMaterial.HasProperty("_PlanetRadius"))
                ? lowMaterial.GetFloat("_PlanetRadius") : 0f;
            return (r > 1000f) ? r : 1e7f;
        }

        // Per-populate clone of the dome material for the optic CB: full property sync (one native copy),
        // then zero the per-frame jitter the main path may have set. Never writes lowMaterial.
        private static Material OpticDomeMaterial()
        {
            if (lowMaterial == null) return null;
            if (opticDomeMaterial == null)
                opticDomeMaterial = new Material(lowMaterial);
            opticDomeMaterial.CopyPropertiesFromMaterial(lowMaterial);
            opticDomeMaterial.SetVector("_CloudTraceJitter", Vector4.zero);
            opticDomeMaterial.SetVector("_CloudDitherMap", new Vector4(1f, 0f, 0f, 0f));
            return opticDomeMaterial;
        }

        private static void EnsureHistory(int w, int h)
        {
            if (histRead != null && histRead.width == w && histRead.height == h)
                return;
            ReleaseHistory();
            histRead = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            histWrite = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            // A fresh RenderTexture is UNDEFINED VRAM, and histValid is set at populate time even on
            // frames the camera never renders — if those ever de-sync, the recon Catmull-Roms raw
            // garbage around the screen (coloured confetti patches). Clear both so the worst case is
            // a black (empty) history that refills over one 16-frame cycle.
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = histRead;  GL.Clear(false, true, Color.clear);
            RenderTexture.active = histWrite; GL.Clear(false, true, Color.clear);
            RenderTexture.active = prevActive;
            histValid = false;   // resolution changed -> stale history
        }

        private static void ReleaseHistory()
        {
            if (histRead != null) { histRead.Release(); UnityEngine.Object.Destroy(histRead); histRead = null; }
            if (histWrite != null) { histWrite.Release(); UnityEngine.Object.Destroy(histWrite); histWrite = null; }
            histValid = false;
        }

        public static void CleanupClouds()
        {
            // Put Tarkov's scene light/ambient gradients back if we overrode them (raid end).
            CustomCloudController.RestoreGroundLighting();

            if (mainCloudCommandBuffer != null)
            {
                if (lastMainCamera)
                    lastMainCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, mainCloudCommandBuffer);
                mainCloudCommandBuffer.Dispose();
                mainCloudCommandBuffer = null;
            }
            lastMainCamera = null;

            if (opticCloudCommandBuffer != null)
            {
                if (lastOpticCamera)
                    lastOpticCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, opticCloudCommandBuffer);
                opticCloudCommandBuffer.Dispose();
                opticCloudCommandBuffer = null;
            }
            lastOpticCamera = null;

            if (cloudInstance)
                GameObject.Destroy(cloudInstance);

            cloudInstance = null;
            lowRenderer = null;
            lowMaterial = null;

            ReleaseHistory();
            ReleaseSkyMap();
            SkyAtmosphere.Cleanup();

            if (compositeMaterial != null)
            {
                UnityEngine.Object.Destroy(compositeMaterial);
                compositeMaterial = null;
            }

            if (temporalMaterial != null)
            {
                UnityEngine.Object.Destroy(temporalMaterial);
                temporalMaterial = null;
            }

            if (opticDomeMaterial != null)
            {
                UnityEngine.Object.Destroy(opticDomeMaterial);
                opticDomeMaterial = null;
            }

            if (preRenderHooked)
            {
                Camera.onPreRender -= OnCamPreRender;
                preRenderHooked = false;
            }
            temporalCam = null;
            prevRenderVPValid = false;
            lastCapturedVPValid = false;
            captureFrame = -1;
            firesThisFrame = firesLastFrame = 0;

            if (cloudShadowMap != null)
            {
                cloudShadowMap.Release();
                UnityEngine.Object.Destroy(cloudShadowMap);
                cloudShadowMap = null;
            }

            if (shadowMaterial != null)
            {
                UnityEngine.Object.Destroy(shadowMaterial);
                shadowMaterial = null;
            }
            CustomCloudController.cachedTopAmbient = null;
            CustomCloudController.windOffset = Vector4.zero;
        }

        public static void UpdateCloudShadowMap(Material cloudMat, Light mainLight, Vector3 camPos)
        {
            //Plugin.MyLog.LogInfo($"UpdateShadow: lightDir={lightDir}, sunY={lightDir.y}");
            if (shadowMaterial == null || cloudMat == null)
            {
                Plugin.MyLog.LogWarning($"UpdateCloudShadowMap skipped: shadowMat={shadowMaterial != null}, cloudMat={cloudMat != null}");
                return;
            }

            // Reallocate the cookie if the configured resolution changed. Deferred free (Destroy, not Release)
            // so the old surface isn't yanked out from under an in-flight cookie sample — AMD faults on that.
            // Shadow Resolution is a discrete enum (menu click, not a drag) so this is a single realloc.
            int wantRes = ShadowResPixels(CloudConfig.ShadowResolution != null ? CloudConfig.ShadowResolution.Value : EShadowResolution.Medium);
            if (cloudShadowMap != null && cloudShadowMap.width != wantRes)
            {
                UnityEngine.Object.Destroy(cloudShadowMap);
                cloudShadowMap = null;
            }
            if (cloudShadowMap == null)
            {
                cloudShadowMap = new RenderTexture(wantRes, wantRes, 0, RenderTextureFormat.ARGB32)
                { name = "CloudSix Shadow Map", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
                cloudShadowMap.Create();
            }

            shadowMaterial.SetTexture("_CloudNoise3D", cloudMat.GetTexture("_CloudNoise3D"));
            shadowMaterial.SetVector("_NoiseTiling", cloudMat.GetVector("_NoiseTiling"));
            shadowMaterial.SetVector("_WindOffset", cloudMat.GetVector("_WindOffset"));
            shadowMaterial.SetFloat("_WorldScale", cloudMat.GetFloat("_WorldScale"));
            shadowMaterial.SetFloat("_CloudBottomHeight", cloudMat.GetFloat("_CloudBottomHeight"));
            shadowMaterial.SetFloat("_CloudTopHeight", cloudMat.GetFloat("_CloudTopHeight"));
            shadowMaterial.SetFloat("_CloudDensity", cloudMat.GetFloat("_CloudDensity"));
            shadowMaterial.SetFloat("_DensityMultiplier", cloudMat.GetFloat("_DensityMultiplier"));
            shadowMaterial.SetFloat("_DensitySharpness", cloudMat.GetFloat("_DensitySharpness"));
            shadowMaterial.SetVector("_MacroOffset", cloudMat.GetVector("_MacroOffset"));
            shadowMaterial.SetFloat("_MacroEvolution", cloudMat.GetFloat("_MacroEvolution"));
            shadowMaterial.SetFloat("_MacroScale", cloudMat.GetFloat("_MacroScale"));
            shadowMaterial.SetFloat("_MacroContrast", cloudMat.GetFloat("_MacroContrast"));
            shadowMaterial.SetFloat("_MacroVariety", cloudMat.GetFloat("_MacroVariety"));
            shadowMaterial.SetFloat("_MacroEdgeNoise", cloudMat.GetFloat("_MacroEdgeNoise"));
            shadowMaterial.SetFloat("_BaseWispiness", cloudMat.GetFloat("_BaseWispiness"));
            shadowMaterial.SetFloat("_BaseInversion", cloudMat.GetFloat("_BaseInversion"));
            shadowMaterial.SetFloat("_CloudType", cloudMat.GetFloat("_CloudType"));
            shadowMaterial.SetFloat("_WorleyBottom", cloudMat.GetFloat("_WorleyBottom"));
            shadowMaterial.SetFloat("_WorleyMid", cloudMat.GetFloat("_WorleyMid"));
            shadowMaterial.SetFloat("_WorleyTop", cloudMat.GetFloat("_WorleyTop"));
            shadowMaterial.SetFloat("_DetailStrength", cloudMat.GetFloat("_DetailStrength"));
            shadowMaterial.SetTexture("_CloudDetail3D", cloudMat.GetTexture("_CloudDetail3D"));
            shadowMaterial.SetTexture("_CurlNoise", cloudMat.GetTexture("_CurlNoise"));
            shadowMaterial.SetFloat("_DetailTiling", cloudMat.GetFloat("_DetailTiling"));
            shadowMaterial.SetFloat("_DetailErosion", cloudMat.GetFloat("_DetailErosion"));
            shadowMaterial.SetFloat("_DetailDetile", cloudMat.GetFloat("_DetailDetile"));
            shadowMaterial.SetFloat("_CurlTiling", cloudMat.GetFloat("_CurlTiling"));
            shadowMaterial.SetFloat("_CurlStrength", cloudMat.GetFloat("_CurlStrength"));
            shadowMaterial.SetFloat("_Extinction", cloudMat.GetFloat("_Extinction"));

            // Build the cookie in the SAME sun frame the clouds + sun disc use (cloudMat._SunDirection =
            // todSky.LocalSunDirection), NOT from mainLight.transform. The TOD directional light is clamped to
            // Light.MinimumHeight, throttled by Light.UpdateInterval, and becomes the MOON at night, so it
            // drifts from the real sun (worst at low sun) — which made the godray shafts converge slightly off
            // the visible sun/clouds. Deriving the shadow+godray basis from _SunDirection guarantees the
            // shadows line up with what the clouds are actually lit by.
            Vector3 sunDir = cloudMat.GetVector("_SunDirection");
            if (sunDir.sqrMagnitude < 1e-6f)
                sunDir = -mainLight.transform.forward;   // fallback if the material hasn't been fed yet
            sunDir.Normalize();
            Vector3 refUp = Mathf.Abs(sunDir.y) < 0.99f ? Vector3.up : Vector3.forward;
            Vector3 lightRight = Vector3.Normalize(Vector3.Cross(refUp, sunDir));
            Vector3 lightUp = Vector3.Cross(sunDir, lightRight);

            // Cookie orientation flips. The cookie is laid out in the SUN frame, but the GAME projects it onto
            // terrain with its own light-cookie convention — a handedness mismatch MIRRORS the shadows onto the
            // wrong side of the cloud. These correct it empirically (can't be known from C#, like the
            // reprojection V-flip). Flipping the BASIS keeps the godray read consistent (it uses the same
            // vectors, so build<->read stay matched), so godrays are unaffected — only the terrain mirrors.
            if (CloudConfig.ShadowFlipX != null && CloudConfig.ShadowFlipX.Value) lightRight = -lightRight;
            if (CloudConfig.ShadowFlipY != null && CloudConfig.ShadowFlipY.Value) lightUp = -lightUp;

            float shadowWorldSize = CloudConfig.ShadowWorldSize != null ? CloudConfig.ShadowWorldSize.Value : 5000f;
            shadowMaterial.SetVector("_ShadowSunDir", sunDir);
            shadowMaterial.SetVector("_ShadowCamPos", camPos);
            shadowMaterial.SetFloat("_ShadowMapSize", shadowWorldSize);
            shadowMaterial.SetFloat("_ShadowSteps", CloudConfig.ShadowSteps != null ? CloudConfig.ShadowSteps.Value : 16f);
            shadowMaterial.SetFloat("_ShadowDensityScale", CloudConfig.TerrainShadowDensity.Value);
            shadowMaterial.SetFloat("_ShadowSoftness", CloudConfig.ShadowSoftness != null ? CloudConfig.ShadowSoftness.Value : 0f);
            shadowMaterial.SetFloat("_ShadowMip", CloudConfig.ShadowDetailReduction != null ? CloudConfig.ShadowDetailReduction.Value : 2f);

            shadowMaterial.SetVector("_ShadowLightRight", lightRight);
            shadowMaterial.SetVector("_ShadowLightUp", lightUp);

            Graphics.Blit(null, cloudShadowMap, shadowMaterial);

            // Feed the cookie + its light-space basis to the cloud material for the godray march (SAME sun
            // frame used to build it above, so the lookup lines up with the visible clouds/sun).
            cloudMat.SetTexture("_CloudShadowMap", cloudShadowMap);
            cloudMat.SetVector("_ShadowLightRight", lightRight);
            cloudMat.SetVector("_ShadowLightUp", lightUp);
            cloudMat.SetVector("_ShadowCamPos", camPos);
            cloudMat.SetFloat("_ShadowMapSize", shadowWorldSize);
        }
    }
}