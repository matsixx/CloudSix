using CloudSix.Patches;
using EFT.Weather;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CloudSix.Source
{
    internal class CustomCloudController
    {
        public static Vector3 windOffset = Vector3.zero;
        public static Vector3 macroOffset = Vector3.zero;

        // Per-raid randomized slab height (synced over FIKA). Rolled from a sky archetype at raid start.
        public static float cloudBottomHeight = 1000f;
        public static float cloudTopHeight = 3000f;
        // { bottomMin, bottomMax, thicknessMin, thicknessMax }: low overcast / fair cumulus / built-up cumulus
        public static float[][] heightArchetypes =
        {
            new[] { 500f, 800f, 600f, 1200f },
            new[] { 900f, 1300f, 1200f, 2000f },
            new[] { 800f, 1100f, 2200f, 3200f },
        };

        // rng(min,max) so the headless can pass its own System.Random and stay deterministic-per-instance.
        // The archetype is WEIGHTED by the current cloudiness so the vertical scale matches the weather:
        // clear/partly -> fair cumulus, cloudy -> built-up cumulus, heavy/overcast -> low flat overcast deck.
        // (Was a uniform random pick, which sometimes rolled the tall built-up slab under heavy coverage —
        // towering clouds on an overcast sky looks wrong.) Cloudiness comes from the server-synced
        // WeatherCurve and the rng draw count is unchanged, so FIKA peers still roll consistently.
        public static void RandomizeHeights(System.Func<float, float, float> rng)
        {
            float n = 0.5f;   // fallback = mid ("cloudy") if the weather isn't available yet
            var wc = WeatherController.Instance;
            if (wc != null)
                n = Mathf.Clamp01((wc.WeatherCurve.Cloudiness + 1f) * 0.5f);

            // Per-archetype weight vs normalized cloudiness (bands overlap so there's still variety):
            //   [0] low overcast    -> heavy / overcast
            //   [1] fair cumulus     -> clear / partly
            //   [2] built-up cumulus -> the mid "cloudy" band only; drops to zero by heavy coverage
            float w0 = SmoothStep01(0.50f, 0.85f, n);
            float w1 = SmoothStep01(0.40f, 0.10f, n);
            float w2 = SmoothStep01(0.20f, 0.45f, n) * SmoothStep01(0.75f, 0.50f, n);
            float sum = w0 + w1 + w2;

            int i;
            if (sum < 1e-4f)
            {
                i = 1;   // never happens across 0..1, but keep a safe default (fair cumulus)
            }
            else
            {
                float r = rng(0f, sum);
                i = (r < w0) ? 0 : (r < w0 + w1) ? 1 : 2;
            }

            float[] a = heightArchetypes[i];
            cloudBottomHeight = rng(a[0], a[1]);
            cloudTopHeight = cloudBottomHeight + rng(a[2], a[3]);
        }

        // smoothstep(edge0, edge1, x) with HLSL semantics — supports descending edges (edge0 > edge1).
        private static float SmoothStep01(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        // Physical sun COLOUR reaching the cloud layer: white sunlight attenuated by the atmosphere along the
        // sun ray (Rayleigh + Mie + ozone, the SAME coefficients as the Hillaire sky), normalized to a pure
        // HUE so only the colour shifts (brightness stays on _SunIntensity). Neutral-warm at noon, deep
        // orange/red at sunset — and consistent with the sky because it shares the atmosphere + Rayleigh/Mie/
        // Ozone config. sunElevSin = sin(sun elevation) = the sun dir's world-up component. Analytic (no LUT
        // readback): remaining Rayleigh/Mie column above the cloud x a slant airmass, + the ozone slab.
        private static readonly Vector3 SunBetaR = new Vector3(5.802e-3f, 13.558e-3f, 33.1e-3f);
        private const float SunBetaMExt = 8.396e-3f;   // Mie scattering 3.996e-3 + absorption 4.40e-3
        private static readonly Vector3 SunBetaO = new Vector3(0.650e-3f, 1.881e-3f, 0.085e-3f);
        private static Color SunTransmittanceColor(float sunElevSin, float cloudAltM)
        {
            float ray = CloudConfig.AtmRayleigh != null ? CloudConfig.AtmRayleigh.Value : 1f;
            float mie = CloudConfig.AtmMie != null ? CloudConfig.AtmMie.Value : 1f;
            float ozo = CloudConfig.AtmOzone != null ? CloudConfig.AtmOzone.Value : 1f;
            float warmth = CloudConfig.SunColorWarmth != null ? CloudConfig.SunColorWarmth.Value : 1f;
            float hKm = Mathf.Clamp(cloudAltM * 0.001f, 0f, 20f);
            float mu = Mathf.Max(sunElevSin, 0.001f);
            // Slant airmass. The floor caps how extreme the horizon path gets (higher floor = less deep-red at
            // sunrise/sunset, more ORANGE); warmth scales the whole path (higher = redder sunsets). Tuned so
            // low sun lands on orange, not pure red.
            float airmass = warmth / (mu + 0.05f);
            float colR = 8.0f * Mathf.Exp(-hKm / 8.0f) * airmass;     // Rayleigh column above the cloud (H_R=8km)
            float colM = 1.2f * Mathf.Exp(-hKm / 1.2f) * airmass;     // Mie column (H_M=1.2km)
            float colO = 8.0f * airmass;                              // ozone slab — absorbs GREEN, so keep it
                                                                      // modest or the sun goes red/magenta not orange
            Vector3 od = SunBetaR * (ray * colR)
                       + new Vector3(SunBetaMExt, SunBetaMExt, SunBetaMExt) * (mie * colM)
                       + SunBetaO * (ozo * colO);
            Color t = new Color(Mathf.Exp(-od.x), Mathf.Exp(-od.y), Mathf.Exp(-od.z), 1f);
            float m = Mathf.Max(t.r, Mathf.Max(t.g, t.b));
            return m > 1e-4f ? new Color(t.r / m, t.g / m, t.b / m, 1f) : Color.white;
        }

        // ===== Ground lighting override (scene light + ambient from OUR atmosphere) =====
        // Tarkov's scene lighting comes from WeatherController.TimeOfDayController's gradients: LightColor (the
        // sun/directional colour on EVERYTHING) and AddTopAmbient (the ambient fill). We swap the HUE of those
        // to our atmosphere's sun/sky colour while KEEPING the game's tuned brightness (so exposure stays
        // right), so the ground finally matches the custom sky + clouds. Gated to daytime (night keeps the
        // vanilla moon look) and behind a toggle; defaults are cached and restored on toggle-off / raid end.
        // Mechanism confirmed from the HollywoodGraphics mod, which overrides LightColor the same way.
        private static bool _groundLightActive = false;
        private static Gradient _defaultLightColor;
        private static Gradient _defaultTopAmbient;
        private static Gradient _overrideLightColor;
        private static Gradient _overrideTopAmbient;
        private static readonly GradientAlphaKey[] _flatAlpha = { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) };
        private static readonly GradientColorKey[] _lightKeys = new GradientColorKey[2];
        private static readonly GradientColorKey[] _ambKeys = new GradientColorKey[2];

        public static void UpdateGroundLighting(TOD_Sky todSky)
        {
            var todc = WeatherController.Instance != null ? WeatherController.Instance.TimeOfDayController : null;
            if (todc == null || todSky == null) return;

            bool on = CloudConfig.GroundLightingFromSky != null && CloudConfig.GroundLightingFromSky.Value;
            if (!on) { RestoreGroundLighting(); return; }

            if (!_groundLightActive)
            {
                _defaultLightColor = todc.LightColor;     // cache the vanilla gradients (static presets)
                _defaultTopAmbient = todc.AddTopAmbient;
                _overrideLightColor = new Gradient();      // reused flat gradients (mutated per frame, no GC)
                _overrideTopAmbient = new Gradient();
                _groundLightActive = true;
            }

            float sunElev = todSky.SunDirection.y;
            float param = Mathf.Clamp01(sunElev * 0.5f + 0.5f);   // sun elevation -> 0..1 (how the ambient gradient is keyed)
            float dayBlend = Mathf.Clamp01(sunElev / 0.1f);       // 0 at/below horizon, 1 once the sun is up (night = vanilla)
            float strength = CloudConfig.GroundLightingStrength != null ? CloudConfig.GroundLightingStrength.Value : 1f;
            float t = dayBlend * Mathf.Clamp01(strength);

            // LightColor: keep the default's brightness, swap the hue to our physical sun colour.
            if (_defaultLightColor != null)
            {
                Color def = _defaultLightColor.Evaluate(param);
                Color hue = SunTransmittanceColor(sunElev, 0f);            // ground-level sun (full atmospheric path)
                Color outc = Color.Lerp(def, hue * def.maxColorComponent, t);
                _lightKeys[0] = new GradientColorKey(outc, 0f);
                _lightKeys[1] = new GradientColorKey(outc, 1f);
                _overrideLightColor.SetKeys(_lightKeys, _flatAlpha);
                todc.LightColor = _overrideLightColor;
            }

            // AddTopAmbient: same, with our sky-ambient hue (Rayleigh blue by day, warming toward the sun low).
            if (_defaultTopAmbient != null)
            {
                Color def = _defaultTopAmbient.Evaluate(param);
                Color hue = SkyAmbientHue(sunElev);
                Color outc = Color.Lerp(def, hue * def.maxColorComponent, t);
                _ambKeys[0] = new GradientColorKey(outc, 0f);
                _ambKeys[1] = new GradientColorKey(outc, 1f);
                _overrideTopAmbient.SetKeys(_ambKeys, _flatAlpha);
                todc.AddTopAmbient = _overrideTopAmbient;
            }
        }

        // Put the vanilla gradients back (toggle-off / raid end). Safe to call when inactive.
        public static void RestoreGroundLighting()
        {
            if (!_groundLightActive) return;
            var todc = WeatherController.Instance != null ? WeatherController.Instance.TimeOfDayController : null;
            if (todc != null)
            {
                if (_defaultLightColor != null) todc.LightColor = _defaultLightColor;
                if (_defaultTopAmbient != null) todc.AddTopAmbient = _defaultTopAmbient;
            }
            _groundLightActive = false;
        }

        // Analytic sky-ambient HUE: lightened Rayleigh blue by day, warming toward the sun colour near the
        // horizon. Normalized to a pure hue (brightness comes from the game's default ambient gradient).
        private static Color SkyAmbientHue(float sunElevSin)
        {
            Color skyBlue = Color.Lerp(Color.white, new Color(SunBetaR.x / SunBetaR.z, SunBetaR.y / SunBetaR.z, 1f), 0.6f);
            Color sun = SunTransmittanceColor(sunElevSin, 0f);
            float lowSun = 1f - Mathf.Clamp01(sunElevSin / 0.2f);
            Color hue = Color.Lerp(skyBlue, sun, lowSun * 0.6f);
            float mm = Mathf.Max(hue.r, Mathf.Max(hue.g, hue.b));
            return mm > 1e-4f ? new Color(hue.r / mm, hue.g / mm, hue.b / mm) : Color.white;
        }

        public static float macroEvolution = 0.0f;       // accumulated morph offset through the macro noise volume
        public static float macroEvolveRate = 0.0003f;    // morph speed: how fast cloud shapes form/dissipate (decoupled from wind)
        private static float smoothedWindSpeed = 0.0f;

        // Slew-rate-limited cloud coverage (see UpdateCoverage). Persists across frames; reset per raid via
        // ResetCoverage() so a new raid snaps to its own weather instead of slewing in from the last one.
        public static float smoothedNormalizedCloudiness = 0f;
        private static bool coverageInitialized = false;

        public static Vector2 lastWindDirection = Vector2.zero;
        private static float macroWindFactor = 0.2f;
        private const float MAX_MOON_INTENSITY = 0.05f;
        public static Gradient cachedTopAmbient;
        public static float DesaturationStrength = 1.3f;
        public static float NightAmbientMultiplier = 0.2f;
        public Gradient ambientGradient;
        public static float AmbientDaytimeDesat = 0.2f;   // baseline daytime desat — takes the edge off clear-sky blue
        public static float AmbientCloudDesat = 0.6f;     // extra desat that ramps with cloud coverage (overcast -> grey)

        public static float lowSunWarmHeight = -0.3f;  // sun height where warming starts ramping in (0 = horizon)
        public static float lowSunWarmAmount = 0.9f;   // how far to blend _SunColor toward the sky color at the horizon
        public static float lowSunIntensity = 1f;    // sun-intensity floor at the horizon (vs 1.5 high) so the warmth isn't clipped to white

        public static float moonIntensityScale = 1f;

        // Frames per dither-phase step. 1 = advance every frame (Blend averages it; Off = old behaviour).
        // Nubis uses 16 (one step per full refresh cycle): each pixel's successive retraces then sample
        // DIFFERENT band phases and the recon's own-history blend averages them out. A STATIC dither
        // instead pins every pixel to a different band phase forever = permanent stipple across the
        // clouds, worst at night where banding contrast peaks. 0 = frozen dither.
        public static int ditherPhaseStride = 1;

        public static void UpdateWind(Vector2 windVector)
        {
            const float WIND_DAMPENING = 2.0f;
            float minSpeed = CloudConfig.WindSpeedMin.Value;
            float maxSpeed = CloudConfig.WindSpeedMax.Value;
            float magnitude = windVector.magnitude;
            float targetSpeed = Mathf.Lerp(minSpeed, maxSpeed, Mathf.InverseLerp(0f, 0.5f, magnitude));
            smoothedWindSpeed = Mathf.Lerp(smoothedWindSpeed, targetSpeed, WIND_DAMPENING * Time.deltaTime);
        }

        // Slew-rate limit on cloud coverage. Returns the (0..1) coverage to render this frame. The weather
        // target moves at a wildly variable rate — the SPT forecast can jump between weather presets between
        // adjacent nodes and the ~7x raid clock sweeps that fast, so coverage sometimes snaps clear->overcast
        // in seconds. We cap the change to one FULL swing per CoverageMinTransitionTime real seconds: any
        // transition already slower than that reaches the target exactly (MoveTowards leaves it untouched);
        // only the too-fast ones get clamped. NOT a low-pass — gradual build-ups are unaffected.
        public static float UpdateCoverage(float rawCloudiness)
        {
            float target = (rawCloudiness + 1f) * 0.5f;   // -1..1 -> 0..1, matches vanilla normalization
            float minTime = CloudConfig.CoverageMinTransitionTime != null ? CloudConfig.CoverageMinTransitionTime.Value : 0f;

            if (!coverageInitialized || minTime <= 0f)
            {
                // Raid start, or limiter disabled: snap to current weather (no slew-in from clear / last raid).
                smoothedNormalizedCloudiness = target;
                coverageInitialized = true;
            }
            else
            {
                float maxDelta = (1f / minTime) * Time.deltaTime;   // max coverage change allowed this frame
                smoothedNormalizedCloudiness = Mathf.MoveTowards(smoothedNormalizedCloudiness, target, maxDelta);
            }
            return smoothedNormalizedCloudiness;
        }

        // Snap the coverage limiter to the next frame's weather. Called at raid start (RandomizeHeights site)
        // so a new raid doesn't slew in from the previous raid's final sky.
        public static void ResetCoverage()
        {
            coverageInitialized = false;
        }

        // Remap normalized coverage (0..1) to the shader's _CloudDensity through the calibrated cloud-state
        // anchors, so the game's weather presets render as intended: SUNNY (n~0-0.1) -> clear,
        // CLOUDY (n~0.5-0.7) -> cloudy/heavy, RAINY (n~0.85+) -> overcast. Piecewise-linear between anchors;
        // the input breakpoints are placed to match the server's per-preset cloudiness distribution.
        // Replaces the old flat Lerp(0.6,3.3) that mapped the whole CLOUDY band into overcast.
        public static float CoverageToDensity(float n)
        {
            n = Mathf.Clamp01(n);
            if (CloudConfig.ClearDensity == null)
                return Mathf.Lerp(0.85f, 2.1f, n);   // config not bound yet: sane linear fallback

            // Input position of each state, and the density it maps to (same length, ascending inputs).
            float[] bp = { 0.00f, 0.22f, 0.45f, 0.72f, 1.00f };
            float[] d =
            {
                CloudConfig.ClearDensity.Value,
                CloudConfig.PartlyDensity.Value,
                CloudConfig.CloudyDensity.Value,
                CloudConfig.HeavyDensity.Value,
                CloudConfig.OvercastDensity.Value,
            };
            for (int i = 1; i < bp.Length; i++)
            {
                if (n <= bp[i])
                    return Mathf.Lerp(d[i - 1], d[i], Mathf.InverseLerp(bp[i - 1], bp[i], n));
            }
            return d[d.Length - 1];
        }

        // High/cirrus layer coverage: ramp 0.5 -> 1.0 as the main density goes clear -> cloudy, then hold
        // full. Tied to the Clear/Cloudy density anchors so it tracks the same calibration as CoverageToDensity.
        public static float HighCoverageFromDensity(float density)
        {
            float clear = CloudConfig.ClearDensity != null ? CloudConfig.ClearDensity.Value : 0.85f;
            float cloudy = CloudConfig.CloudyDensity != null ? CloudConfig.CloudyDensity.Value : 1.35f;
            return Mathf.Lerp(0.5f, 1.0f, Mathf.InverseLerp(clear, cloudy, density));
        }

        public static void UpdateMaterial(Material cloudMaterial, float timeOfDay)
        {
            if (cloudMaterial == null)
                return;
            var todSky = MonoBehaviourSingleton<TOD_Sky>.Instance;

            // === WIND ===
            float windSpeedMult = 1.2f;
            windOffset.x += lastWindDirection.x * smoothedWindSpeed * windSpeedMult * Time.deltaTime;
            windOffset.z += lastWindDirection.y * smoothedWindSpeed * windSpeedMult * Time.deltaTime;
            windOffset.y += smoothedWindSpeed * Time.deltaTime * 0.00005f;
            cloudMaterial.SetVector("_WindOffset", windOffset);

            macroOffset += new Vector3(
                lastWindDirection.x * smoothedWindSpeed,
                0,
                lastWindDirection.y * smoothedWindSpeed
            ) * macroWindFactor * Time.deltaTime;
            cloudMaterial.SetVector("_MacroOffset", macroOffset);

            // Evolve the field's SHAPE over time (not just translate it): the shader slides the macro
            // sample along the noise volume's vertical axis, so coverage changes read as clouds forming
            // and dissipating in place instead of a uniform in-place grow/shrink. Decoupled from wind.
            macroEvolution += macroEvolveRate * Time.deltaTime;
            cloudMaterial.SetFloat("_MacroEvolution", macroEvolution);

            // === SUN / MOON DIRECTIONS ===
            cloudMaterial.SetVector("_SunDirection", todSky.LocalSunDirection);
            cloudMaterial.SetVector("_MoonDirection", todSky.LocalMoonDirection);

            // === INTENSITIES ===
            float sunHeight = todSky.LocalSunDirection.y;
            CalculateIntensities(todSky, out float sunIntensity, out float moonIntensity);
            cloudMaterial.SetFloat("_SunIntensity", sunIntensity);
            cloudMaterial.SetFloat("_MoonIntensity", moonIntensity);

            // Golden-ratio dither phase from a real frame counter (mod keeps the float precise) — TAA-friendly,
            // and frame-indexed so it can't beat into a pulse the way _Time-based did. Stepped once per
            // ditherPhaseStride frames (see the field comment for the Nubis reasoning).
            float ditherPhase = ditherPhaseStride > 0
                ? (((Time.frameCount / ditherPhaseStride) % 8192) * 0.61803398875f) % 1f
                : 0f;
            cloudMaterial.SetFloat("_DitherPhase", ditherPhase);
            // === SUN / MOON COLOR ===
            // Physical sun colour from OUR atmosphere (see SunTransmittanceColor): neutral-warm at noon,
            // orange/red at sunset, and it MATCHES the sky. Falls back to the TOD colours when the toggle's off.
            Color sunColor;
            if (CloudConfig.SunColorFromSky == null || CloudConfig.SunColorFromSky.Value)
            {
                float strength = CloudConfig.SunColorStrength != null ? CloudConfig.SunColorStrength.Value : 1f;
                Color physical = SunTransmittanceColor(todSky.SunDirection.y, cloudTopHeight);
                sunColor = Color.Lerp(Color.white, physical, Mathf.Clamp01(strength));
            }
            else
            {
                float lowSun = 1f - Mathf.Clamp01(sunHeight / lowSunWarmHeight);  // 0 sun-up, 1 at horizon
                sunColor = Color.Lerp(todSky.SunLightColor, todSky.SunSkyColor, lowSun * lowSunWarmAmount);
            }
            cloudMaterial.SetColor("_SunColor", sunColor);
            cloudMaterial.SetColor("_MoonColor", todSky.MoonLightColor);

            // Sun/moon DISC colors = TOD's own mesh colors (what it would have tinted its sun/moon billboards,
            // which we hide), so the disc tracks the vanilla sun/moon look across the day/night cycle.
            cloudMaterial.SetColor("_SunDiscColor", todSky.SunMeshColor);
            cloudMaterial.SetColor("_MoonDiscColor", todSky.MoonMeshColor);

            // Realistic moon: hand the shader TOD's actual lunar surface texture so our sphere-shaded disc
            // shows the real moon face. Reading the material is fine even though we hide TOD's moon renderer.
            var moonMat = todSky.Components != null ? todSky.Components.MoonMaterial : null;
            if (moonMat != null)
            {
                var moonTex = moonMat.mainTexture != null ? moonMat.mainTexture : moonMat.GetTexture("_MainTex");
                if (moonTex != null)
                    cloudMaterial.SetTexture("_MoonTex", moonTex);
            }

            // === AMBIENT (base Tarkov gradient + daytime/coverage desaturation) ===
            if (cachedTopAmbient == null && WeatherController.Instance?.TimeOfDayController != null)
                cachedTopAmbient = WeatherController.Instance.TimeOfDayController.AddTopAmbient;

            if (cachedTopAmbient != null)
            {
                float time = Mathf.Clamp01(todSky.SunDirection.y * 0.5f + 0.5f);  // full gradient, world sun height
                Color ambient = cachedTopAmbient.Evaluate(time);

                // The gradient's daytime end is sky-blue. Pull it toward neutral so it doesn't read
                // as a cold cast — only during the day (dawn/dusk/night colors stay untouched), and
                // harder as cloud cover rises, since that's when ambient dominates the look.
                float dayAmount = Mathf.Clamp01(todSky.SunDirection.y / 0.2f);  // 0 at horizon, 1 once sun is up
                float cloudCover = 0f;
                var weather = WeatherController.Instance;
                if (weather != null)
                    cloudCover = Mathf.Clamp01(smoothedNormalizedCloudiness);  // match the slewed cloud coverage

                float desat = Mathf.Clamp01(dayAmount * (AmbientDaytimeDesat + cloudCover * AmbientCloudDesat));
                float lum = ambient.r * 0.2126f + ambient.g * 0.7152f + ambient.b * 0.0722f;
                ambient.r = Mathf.Lerp(ambient.r, lum, desat);
                ambient.g = Mathf.Lerp(ambient.g, lum, desat);
                ambient.b = Mathf.Lerp(ambient.b, lum, desat);

                ambient.a = 1f;
                cloudMaterial.SetColor("_AmbientColor", ambient);
            }

            // === ATMOSPHERE-DRIVEN AMBIENT ===
            // Feed the cloud shader our own sky-view LUT so the ambient/skylight on the clouds comes from the
            // custom Hillaire atmosphere (colour + sunset warmth), directionally split top/bottom in-shader,
            // instead of the flat TOD tint set just above. Gated on the sky actually being baked; when it
            // isn't ready we leave the toggle off so the clouds fall back to _AmbientColor above.
            bool atmAmbient = CloudConfig.AtmosphereAmbient != null && CloudConfig.AtmosphereAmbient.Value
                              && SkyAtmosphere.Ready && SkyAtmosphere.SkyViewLut != null;
            cloudMaterial.SetFloat("_UseAtmosphereAmbient", atmAmbient ? 1f : 0f);
            if (atmAmbient)
                cloudMaterial.SetTexture("_SkyViewLut", SkyAtmosphere.SkyViewLut);

            // Transmittance LUT: the high clouds are lit by the sun as seen at THEIR altitude (still lit after
            // the ground's sunset). Independent of the ambient toggle; falls back to the ground sun when off.
            bool atmSun = SkyAtmosphere.Ready && SkyAtmosphere.TransLut != null;
            cloudMaterial.SetFloat("_UseAtmosphereSun", atmSun ? 1f : 0f);
            if (atmSun)
                cloudMaterial.SetTexture("_SkyTransLut", SkyAtmosphere.TransLut);

            // === HAZE ===
            float dayTransition = Mathf.InverseLerp(-0.1f, 0.1f, sunHeight);
            Color dayHaze = todSky.SunSkyColor;
            Color nightHaze = todSky.MoonSkyColor;
            Color finalHaze = Color.Lerp(nightHaze, dayHaze, dayTransition);
            cloudMaterial.SetColor("_HazeColor", finalHaze);
        }

        private static void CalculateIntensities(TOD_Sky todSky, out float sunIntensity, out float moonIntensity)
        {
            float sunHeight = todSky.LocalSunDirection.y;

            // Sun: starts lighting clouds earlier in sunrise/later in sunset
            float sunT = Mathf.Clamp01(Mathf.InverseLerp(-0.3f, 0.0f, sunHeight));
            // Drop intensity toward the horizon (instead of pinning 1.5 for any sun above 0) so the warm
            // low-sun color reads instead of clipping to white: lowSunIntensity at the horizon -> 1.5 high.
            float highSun = Mathf.Clamp01(sunHeight / 0.3f);
            float peak = CloudConfig.SunIntensity != null ? CloudConfig.SunIntensity.Value : 1.5f;   // high-sun sun brightness (config)
            sunIntensity = sunT * Mathf.Lerp(lowSunIntensity, peak, highSun);

            // Moon: fades in as sun drops
            float moonT = Mathf.Clamp01(Mathf.InverseLerp(0.0f, -0.15f, sunHeight));
            moonT = moonT * moonT;
            moonIntensity = moonT * (CloudConfig.MoonIntensity != null ? CloudConfig.MoonIntensity.Value : moonIntensityScale);
        }
    }
}