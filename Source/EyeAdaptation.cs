using EFT.EnvironmentEffect;
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CloudSix.Source
{
    // CloudSix's own eye adaptation, replacing Prism's (the game's). Prism meters a plain average of the whole
    // frame, sky included, into an 8-bit texture that clamps at 1.0 and applies exposure = clamp(middleGrey /
    // average, lower, upper) (read from the compiled shaders). Our HDR sky pegs that average, so the exposure
    // collapsed whenever sky was in view. Here we meter the same frame ourselves and hand Prism the result
    // through its limits (lower = upper = our exposure), the mechanism World Exposure already uses.
    //
    // Meter: Unreal's histogram mode - drop the darkest/brightest 10%, average the LOG luminance of the rest -
    // with sky pixels (far depth) weighted down (HDRP/Unreal metering-mask idea).
    // Response: stops against a FIXED daylight reference (Daylight Brightness: an ordinary daytime outdoor view,
    // a setting - measured on demand, never by itself), so the same view always gets the same exposure: interiors, dawn and dusk brighten, bright views
    // dim, by Adaptation Strength within the Max limits. (Until 2026-10-14 the reference was learned - a 90 s
    // average of what was seen while the game flagged "outdoor" - and that made it depend on what you had just
    // looked at, on interiors the game does not flag, and kept dawn at World Exposure.)
    internal static class EyeAdaptation
    {
        private const int MeterW = 512, MeterH = 256, MeterMip = 4;   // read back 32x16 block averages
        private const int GridW = 32, GridH = 16, GridN = GridW * GridH;
        private const float DarkenRate = 3f, BrightenRate = 1f;   // 1/s (Unreal: 3 toward bright, 1 toward dark)
        private const float MeasureSeconds = 10f;                  // readings averaged into the daylight reference
        private const float StaleAfter = 0.5f;                     // s without a new reading: back to World Exposure
        private const float LostAfter = 1f;                        // s a readback may take before it is given up

        // VR hidden-area mask: SteamVR blacks out the corners the lenses never show (~25 % of each eye on an
        // Index), depth AT the near plane. More than the 10 % dark trim, so those black blocks read as a dark scene
        // and brightened everything. Depth is read 4x4 per block to know each block's visible share.
        private const int MaskSub = 4;
        private const float MaskRawCut = 0.5f;   // reversed-Z raw depth at 2x the near plane
        private static RenderTexture maskRT;
        private static readonly float[] maskDepth = new float[GridN * MaskSub * MaskSub];
        private static readonly float[] visible = new float[GridN];
        private static float maskedFrac;
        private static bool maskRequested, maskLogged;
        // diagnostic (EyeLog line + one size line): what the mask's depth actually holds
        private static float maskMin, maskMax;
        private static int maskNear, maskNear99, maskFar;
        private static string meterSizes = "";

        private static RenderTexture meterRT, depthRT;
        private static PrismEffects prism;
        private static bool pending, requestFailed, depthRequested;
        private static int pendingParts, generation;
        private static float pendingSince;
        private static int lastMeterFrame = -1;
        private static float skyDepthThreshold;

        private static readonly float[] lum = new float[GridN];
        private static readonly float[] depth = new float[GridN];
        private static readonly float[] sortLum = new float[GridN];
        private static readonly float[] sortW = new float[GridN];

        private static bool hasReading;
        private static int readings, readingsUsed;
        private static float lastReadingTime = -1f;
        private static float meteredLog2, currentStops, lastTarget;
        private static float skyFrac, skyRowsLow, skyRowsHigh;
        private static bool lastHadDepth;
        private static float lastLogTime, firstUpdateTime = -1f;
        private static bool warnedNoMeter, warnedStale;

        // the daylight measurement in progress
        private static float measureSum, measureTime, measureLastAt;
        private static bool measuringManual;

        public static bool Active => CloudConfig.EyeAdaptation != null && !CloudConfig.EyeAdaptation.Value;

        // The camera's Prism, cached (was two GetComponent calls a frame). Logs its shipped exposure values once.
        public static PrismEffects PrismFor(Camera cam)
        {
            if (prism == null && cam != null)
            {
                prism = cam.GetComponent<PrismEffects>();
                if (prism != null)
                    Plugin.MyLog.LogInfo($"[CloudSix] Prism exposure as found: useExposure={prism.useExposure} middleGrey={prism.exposureMiddleGrey:0.###} " +
                                         $"limits=[{prism.exposureLowerLimit:0.###}, {prism.exposureUpperLimit:0.###}] speed={prism.exposureSpeed:0.###}");
            }
            return prism;
        }

        // Raid end: drop the scene reference and forget this raid's adaptation state.
        public static void Reset()
        {
            prism = null;
            pending = false;
            pendingParts = 0;
            generation++;                 // answers to requests made before this are ignored
            hasReading = false;
            lastReadingTime = -1f;
            currentStops = 0f;
            lastMeterFrame = -1;
            firstUpdateTime = -1f;
            warnedNoMeter = false;
            warnedStale = false;
            measureSum = measureTime = 0f;
            measuringManual = false;
        }

        // Called from Prism's own metering step with the HDR frame it is about to measure. Once per frame (the
        // first eye in VR), and only while no readback is outstanding.
        public static void Meter(PrismEffects instance, RenderTexture source)
        {
            if (!Active || instance == null || instance != prism || source == null)
                return;
            if (pending)
            {
                if (Time.unscaledTime - pendingSince < LostAfter)
                    return;
                pending = false;          // never answered: give it up (a late answer has an old generation)
            }
            if (Time.frameCount == lastMeterFrame)
                return;
            lastMeterFrame = Time.frameCount;

            if (meterRT == null)
            {
                meterRT = new RenderTexture(MeterW, MeterH, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "CloudSix Exposure Meter",
                    useMipMap = true,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                depthRT = new RenderTexture(GridW, GridH, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
                {
                    name = "CloudSix Exposure Meter Depth",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var prevActive = RenderTexture.active;
            Graphics.Blit(source, meterRT);
            meterRT.GenerateMips();

            Texture depthTex = Shader.GetGlobalTexture("_CameraDepthTexture");
            depthRequested = depthTex != null;
            maskRequested = false;
            if (depthRequested)
            {
                Graphics.Blit(depthTex, depthRT);
                // Sky = beyond 98% of the far plane. Raw depth there is ~0 with reversed Z (D3D11), ~1 otherwise.
                Camera cam = instance.GetComponent<Camera>();
                float nearOverFar = cam != null ? cam.nearClipPlane / Mathf.Max(cam.farClipPlane, 1f) : 1e-5f;
                skyDepthThreshold = 0.02f * nearOverFar;
                // VR only (flatscreen has no mask, so nothing changes there)
                maskRequested = cam != null && cam.stereoEnabled;
                string sizes = $"source {source.width}x{source.height}, depth {depthTex.width}x{depthTex.height}, " +
                               $"camera {(cam != null ? cam.pixelWidth + "x" + cam.pixelHeight + " rect " + cam.rect : "none")}, stereo {(cam != null && cam.stereoEnabled)}";
                if (sizes != meterSizes)
                {
                    meterSizes = sizes;
                    if (CloudConfig.EyeLog.Value) Plugin.MyLog.LogInfo("[CloudSix] Eye adaptation meter: " + sizes);
                }
                if (maskRequested)
                {
                    if (maskRT == null)
                        maskRT = new RenderTexture(GridW * MaskSub, GridH * MaskSub, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
                        {
                            name = "CloudSix Exposure Meter Mask",
                            filterMode = FilterMode.Point,
                            wrapMode = TextureWrapMode.Clamp
                        };
                    Graphics.Blit(depthTex, maskRT);
                }
            }
            RenderTexture.active = prevActive;

            pending = true;
            pendingSince = Time.unscaledTime;
            requestFailed = false;
            pendingParts = 1 + (depthRequested ? 1 : 0) + (maskRequested ? 1 : 0);
            int g = ++generation;
            AsyncGPUReadback.Request(meterRT, MeterMip, TextureFormat.RGBAFloat, r => OnColor(r, g));
            if (depthRequested)
                AsyncGPUReadback.Request(depthRT, 0, TextureFormat.RFloat, r => OnDepth(r, g));
            if (maskRequested)
                AsyncGPUReadback.Request(maskRT, 0, TextureFormat.RFloat, r => OnMask(r, g));
        }

        private static void OnMask(AsyncGPUReadbackRequest r, int g)
        {
            if (!pending || g != generation)
                return;
            if (r.hasError)
            {
                requestFailed = true;
            }
            else
            {
                var d = r.GetData<float>();
                if (d.Length < maskDepth.Length)
                    requestFailed = true;
                else
                    d.CopyTo(maskDepth);
            }
            PartDone();
        }

        // Share of each block the mask leaves visible (1 everywhere without the mask). The mask is drawn ~1.01x the
        // near plane out (raw 0.991 with reversed Z, measured 2026-10-19 on an Index); the nearest real surface read
        // 0.014 (~72 cm). Anything closer than 2x the near plane (raw 0.5, 2 cm) is the mask.
        private static void ComputeVisible(bool haveMask, bool reversedZ)
        {
            int masked = 0;
            for (int by = 0; by < GridH; by++)
                for (int bx = 0; bx < GridW; bx++)
                {
                    int n = 0;
                    if (haveMask)
                        for (int sy = 0; sy < MaskSub; sy++)
                            for (int sx = 0; sx < MaskSub; sx++)
                            {
                                float d = maskDepth[(by * MaskSub + sy) * GridW * MaskSub + bx * MaskSub + sx];
                                if (reversedZ ? d >= MaskRawCut : d <= 1f - MaskRawCut) n++;
                            }
                    masked += n;
                    visible[by * GridW + bx] = 1f - n / (float)(MaskSub * MaskSub);
                }
            maskedFrac = masked / (float)maskDepth.Length;
            if (haveMask)
            {
                maskMin = float.MaxValue; maskMax = float.MinValue; maskNear = maskNear99 = maskFar = 0;
                foreach (float d in maskDepth)
                {
                    if (d < maskMin) maskMin = d;
                    if (d > maskMax) maskMax = d;
                    float near = reversedZ ? d : 1f - d;   // 1 = at the near plane
                    if (near >= 0.9999f) maskNear++;
                    if (near >= 0.99f) maskNear99++;
                    if (near <= 0.0001f) maskFar++;
                }
            }
            if (haveMask && !maskLogged && maskedFrac > 0.05f)
            {
                maskLogged = true;
                Plugin.MyLog.LogInfo($"[CloudSix] Eye adaptation: VR hidden-area mask detected - {maskedFrac:P0} of the image is masked and left out of the meter");
            }
        }

        private static void OnColor(AsyncGPUReadbackRequest r, int g)
        {
            if (!pending || g != generation)
                return;
            if (r.hasError)
            {
                requestFailed = true;
            }
            else
            {
                var d = r.GetData<float>();
                if (d.Length < GridN * 4)
                    requestFailed = true;
                else
                    for (int i = 0; i < GridN; i++)   // Prism's own luminance weights
                        lum[i] = 0.22f * d[i * 4] + 0.707f * d[i * 4 + 1] + 0.071f * d[i * 4 + 2];
            }
            PartDone();
        }

        private static void OnDepth(AsyncGPUReadbackRequest r, int g)
        {
            if (!pending || g != generation)
                return;
            if (r.hasError)
            {
                requestFailed = true;
            }
            else
            {
                var d = r.GetData<float>();
                if (d.Length < GridN)
                    requestFailed = true;
                else
                    for (int i = 0; i < GridN; i++)
                        depth[i] = d[i];
            }
            PartDone();
        }

        private static void PartDone()
        {
            if (--pendingParts > 0)
                return;
            pending = false;
            if (!requestFailed)
                Analyze(depthRequested);
        }

        private static void Analyze(bool haveDepth)
        {
            float skyInfluence = Mathf.Clamp01(CloudConfig.EyeSkyInfluence.Value);
            bool reversedZ = SystemInfo.usesReversedZBuffer;
            ComputeVisible(haveDepth && maskRequested, reversedZ);
            int n = 0, valid = 0, sky = 0, skyLow = 0, skyHigh = 0;
            float wTotal = 0f;
            for (int i = 0; i < GridN; i++)
            {
                float l = lum[i];
                if (!(l >= 0f) || float.IsInfinity(l))   // NaN fails the comparison
                    continue;
                // Mostly mask = no scene to meter. Otherwise the block mean over its visible part (the masked
                // pixels are black), counted by how much of it is visible.
                float vis = visible[i];
                if (vis < 0.25f)
                    continue;
                l /= vis;
                valid++;
                bool isSky = haveDepth && (reversedZ ? depth[i] < skyDepthThreshold : depth[i] > 1f - skyDepthThreshold);
                if (isSky)
                {
                    sky++;
                    if (i < GridN / 2) skyLow++; else skyHigh++;
                }
                float w = (isSky ? skyInfluence : 1f) * vis;
                if (w <= 0f)
                    continue;
                sortLum[n] = Mathf.Max(l, 1e-5f);
                sortW[n] = w;
                wTotal += w;
                n++;
            }
            if (valid == 0)
                return;
            skyFrac = (float)sky / valid;
            skyRowsLow = skyLow / (GridN * 0.5f);
            skyRowsHigh = skyHigh / (GridN * 0.5f);
            lastHadDepth = haveDepth;
            lastReadingTime = Time.unscaledTime;
            if (n < 8 || wTotal <= 0f)
                return;   // nothing but sky and the sky doesn't count: the last reading still stands

            // Histogram window: discard the darkest and brightest 10% (by weight), log-average what is left.
            Array.Sort(sortLum, sortW, 0, n);
            float lo = wTotal * 0.10f, hi = wTotal * 0.90f, acc = 0f, sum = 0f, wsum = 0f;
            for (int k = 0; k < n; k++)
            {
                float w0 = acc;
                acc += sortW[k];
                float wIn = Mathf.Min(acc, hi) - Mathf.Max(w0, lo);
                if (wIn <= 0f)
                    continue;
                sum += wIn * Mathf.Log(sortLum[k]);
                wsum += wIn;
            }
            if (wsum <= 0f)
                return;
            meteredLog2 = sum / wsum * 1.442695f;   // ln -> log2
            hasReading = true;
            readings++;
        }

        // Measure Daylight Brightness Now: the log average of MeasureSeconds of readings becomes the reference.
        // Only on demand - an automatic measurement (sun up + some sky in view) also fired in a dark building
        // looking out of a door, and set -5.16 (his log 2026-10-14: every outdoor view at half brightness).
        private static void MeasureDaylight(float now)
        {
            if (readings == readingsUsed)
                return;                   // no new reading since the last frame
            readingsUsed = readings;

            if (!CloudConfig.EyeMeasureNow.Value)
            {
                measuringManual = false;
                measureSum = measureTime = 0f;
                return;
            }
            if (!measuringManual)
            {
                measuringManual = true;
                measureSum = measureTime = 0f;
                measureLastAt = now;
                Plugin.MyLog.LogInfo($"[CloudSix] Eye adaptation: measuring Daylight Brightness for {MeasureSeconds:0} s - keep looking around outdoors.");
            }

            float dt = Mathf.Min(now - measureLastAt, 0.1f);
            measureLastAt = now;
            measureSum += meteredLog2 * dt;
            measureTime += dt;
            if (measureTime < MeasureSeconds)
                return;

            float reference = measureSum / measureTime;
            CloudConfig.EyeDaylight.Value = reference;
            var tod = MonoBehaviourSingleton<TOD_Sky>.Instance;
            Plugin.MyLog.LogInfo($"[CloudSix] Eye adaptation: Daylight Brightness measured = {reference:0.00} (log2 luminance), " +
                                 $"sun {(tod != null ? Mathf.Asin(Mathf.Clamp(tod.SunDirection.y, -1f, 1f)) * Mathf.Rad2Deg : 0f):0} deg up. Views this bright get exactly World Exposure.");
            CloudConfig.EyeMeasureNow.Value = false;
            measuringManual = false;
            measureSum = measureTime = 0f;
        }

        // Once per frame, before rendering: the exposure multiplier to hand Prism (both eyes get the same one).
        public static float Update()
        {
            float worldExposure = CloudConfig.WorldExposure.Value;
            float now = Time.unscaledTime;
            if (firstUpdateTime < 0f)
                firstUpdateTime = now;
            var env = EnvironmentManager.Instance;
            bool thermal = env != null && env.IsMainThermalVisionOn;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.25f);

            if (!hasReading && !warnedNoMeter && now - firstUpdateTime > 10f)
            {
                Plugin.MyLog.LogWarning("[CloudSix] Eye adaptation has no meter reading after 10 s (Prism's exposure step is not running on this camera) - staying at World Exposure.");
                warnedNoMeter = true;
            }
            // A meter that stops answering must not hold its last verdict (a dark room's brightening, kept outdoors).
            bool fresh = hasReading && now - lastReadingTime <= StaleAfter;
            if (hasReading && !fresh && !warnedStale)
            {
                Plugin.MyLog.LogWarning($"[CloudSix] Eye adaptation: no new meter reading for {StaleAfter} s - easing back to World Exposure until they resume.");
                warnedStale = true;
            }
            else if (fresh)
                warnedStale = false;

            if (fresh)
                MeasureDaylight(now);

            float reference = CloudConfig.EyeDaylight.Value;
            float target = 0f;
            if (fresh && !thermal)   // the thermal image is not the scene we metered
            {
                float strength = Mathf.Clamp01(CloudConfig.EyeStrength.Value);
                target = Mathf.Clamp((reference - meteredLog2) * strength,
                                     -Mathf.Max(CloudConfig.EyeMaxDarken.Value, 0f),
                                      Mathf.Max(CloudConfig.EyeMaxBrighten.Value, 0f));
            }
            lastTarget = target;

            float rate = (target < currentStops ? DarkenRate : BrightenRate) * Mathf.Max(CloudConfig.EyeSpeed.Value, 0.01f);
            currentStops += (target - currentStops) * (1f - Mathf.Exp(-dt * rate));
            float exposure = worldExposure * Mathf.Pow(2f, currentStops);

            if (CloudConfig.EyeLog.Value && now - lastLogTime >= 1f)
            {
                lastLogTime = now;
                bool outdoor = env == null || env.Environment == EnvironmentType.Outdoor;
                string refText = $"daylight {reference:0.00}";
                string measuring = measureTime > 0f ? $" | measuring daylight {measureTime:0.0}/{MeasureSeconds:0} s" : "";
                Plugin.MyLog.LogInfo($"[CloudSix] EyeAdapt: scene {meteredLog2:0.00} {refText} (log2 luminance) | target {target:+0.00;-0.00} now {currentStops:+0.00;-0.00} stops -> exposure {exposure:0.00} | " +
                                     $"reading {(hasReading ? (now - lastReadingTime).ToString("0.00") + " s old" : "none")} | sky {skyFrac:P0} (rows 0-7 {skyRowsLow:P0}, rows 8-15 {skyRowsHigh:P0}) depth={(lastHadDepth ? "yes" : "NO")} | VR mask {maskedFrac:P0} (asked {maskRequested}, raw {maskMin:0.#####}..{maskMax:0.#####}, at near {maskNear} / >=0.99 {maskNear99} / at far {maskFar} of {maskDepth.Length}) | game says {(outdoor ? "outdoor" : "indoor")}{measuring}");
            }
            return exposure;
        }
    }
}
