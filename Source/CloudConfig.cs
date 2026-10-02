using BepInEx;
using BepInEx.Configuration;
using CloudSix.Patches;
using UnityEngine;

namespace CloudSix.Source
{
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? IsAdvanced;
    }
    public enum ECloudDebugView { Off, ReprojectionDelta, TemporalDisagreement }

    // Sky diagnostic. SkyViewRaw = show the baked in-scatter LUT amplified (is it zero for a direction?).
    // Coords = paint the sky-view UV as color BEFORE any gate (is the dome even drawn there?).
    public enum EAtmDebug { Off, SkyViewRaw, Coords }

    // Moon position/phase source. GameDefault leaves Tarkov's TOD setting untouched; the other two force
    // TOD_MoonPositionType. OppositeToSun pins the moon 180 deg from the sun (always full); Realistic is
    // date-driven and gives real phases.
    public enum EMoonOrbit { GameDefault, OppositeToSun, Realistic }

    // SkyDome map resolution tiers. The map height (width = 2x) sets angular sharpness independent of
    // screen resolution: ~1536 = 8.5 px/deg ≈ half-res, ~3072 ≈ full-res. Higher = sharper clouds but
    // a bigger per-frame bake (ray count scales with map area — 4x the texels per doubling).
    public enum ESkyDomeQuality { Low, Medium, High, Ultra }

    internal static class CloudConfig
    {
        // Bonus
        public static ConfigEntry<bool> EyeAdaptation;

        // Wind
        public static ConfigEntry<float> WindSpeedMin;
        public static ConfigEntry<float> WindSpeedMax;

        // Weather
        public static ConfigEntry<float> CoverageMinTransitionTime;
        public static ConfigEntry<float> ClearDensity;
        public static ConfigEntry<float> PartlyDensity;
        public static ConfigEntry<float> CloudyDensity;
        public static ConfigEntry<float> HeavyDensity;
        public static ConfigEntry<float> OvercastDensity;

        // Performance
        public static ConfigEntry<int> PrimarySteps;
        public static ConfigEntry<int> LightSteps;
        public static ConfigEntry<VolCloudRenderer.CloudResolution> Resolution;
        public static ConfigEntry<VolCloudRenderer.CloudTemporalMode> TemporalMode;
        public static ConfigEntry<ESkyDomeQuality> SkyDomeQuality;
        public static ConfigEntry<float> TemporalSmoothing;
        public static ConfigEntry<bool> ReprojectionFlip;
        public static ConfigEntry<ECloudDebugView> DebugView;

        public static ConfigEntry<float> WorldExposure;

        // Terrain Shadow
        public static ConfigEntry<float> TerrainShadowDensity;
        public static ConfigEntry<float> ShadowSoftness;
        public static ConfigEntry<float> ShadowDetailReduction;
        public static ConfigEntry<float> ShadowWorldSize;
        public static ConfigEntry<int> ShadowSteps;
        public static ConfigEntry<VolCloudRenderer.EShadowResolution> ShadowResolution;
        public static ConfigEntry<bool> ShadowFlipX;
        public static ConfigEntry<bool> ShadowFlipY;

        // Advanced Performance
        public static ConfigEntry<int> BonusPrimarySteps;
        public static ConfigEntry<int> BonusLightSteps;

        // Shape
        public static ConfigEntry<float> DensityMultiplier;
        public static ConfigEntry<float> NoiseTilingXZ;
        public static ConfigEntry<float> NoiseTilingY;
        public static ConfigEntry<float> DetailTiling;
        public static ConfigEntry<float> DetailErosion;
        public static ConfigEntry<float> DetailOctaveScale;
        public static ConfigEntry<float> DetailOctaveStrength;
        public static ConfigEntry<float> DetailDetile;
        public static ConfigEntry<float> DetailBottomFade;
        public static ConfigEntry<float> DetailBottomExtent;
        public static ConfigEntry<float> BaseInversion;
        public static ConfigEntry<float> CloudOffset;
        public static ConfigEntry<float> CurlTiling;
        public static ConfigEntry<float> CurlStrength;
        public static ConfigEntry<float> BaseWispiness;
        public static ConfigEntry<float> DensitySharpness;
        public static ConfigEntry<float> CloudDensity;
        public static ConfigEntry<float> WorldScale;
        public static ConfigEntry<float> PlanetRadius;

        // Lighting
        public static ConfigEntry<float> Extinction;
        public static ConfigEntry<float> LightDensityScale;
        public static ConfigEntry<float> LongShadowStrength;
        public static ConfigEntry<float> LongShadowReach;
        public static ConfigEntry<float> ScatterForward;
        public static ConfigEntry<float> ScatterBack;
        public static ConfigEntry<float> ScatterMix;
        public static ConfigEntry<float> AmbientStrength;
        public static ConfigEntry<bool> AtmosphereAmbient;
        public static ConfigEntry<float> AtmAmbientHeightPow;
        public static ConfigEntry<float> BaseSkyVeil;
        public static ConfigEntry<bool> SunColorFromSky;
        public static ConfigEntry<float> SunColorStrength;
        public static ConfigEntry<float> SunColorWarmth;
        public static ConfigEntry<float> MultiScatter;
        public static ConfigEntry<bool> MultiOctaveScatter;
        public static ConfigEntry<int> MsOctaves;
        public static ConfigEntry<float> MsOctaveFalloff;
        public static ConfigEntry<float> MsAlbedo;
        public static ConfigEntry<float> MoonSilverLining;
        public static ConfigEntry<float> AlpenStrength;
        public static ConfigEntry<float> PowderStrength;
        public static ConfigEntry<float> PowderBase;
        public static ConfigEntry<float> PowderScale;
        public static ConfigEntry<float> PowderFill;
        public static ConfigEntry<bool> SunDiskOcclusion;
        public static ConfigEntry<float> SunOcclusionRadius;
        public static ConfigEntry<float> SunOcclusionFloor;
        public static ConfigEntry<bool> OwnSunMoonDisc;
        public static ConfigEntry<float> SunDiscSize;
        public static ConfigEntry<float> SunDiscBrightness;
        public static ConfigEntry<float> SunDiscGlow;
        public static ConfigEntry<float> MoonDiscSize;
        public static ConfigEntry<float> MoonDiscBrightness;
        public static ConfigEntry<float> MoonDiscGlow;
        public static ConfigEntry<float> MoonEarthshine;
        public static ConfigEntry<float> MoonTerminator;
        public static ConfigEntry<EMoonOrbit> MoonOrbit;
        public static ConfigEntry<float> DiscOcclusionPow;
        public static ConfigEntry<float> SunIntensity;
        public static ConfigEntry<float> MoonIntensity;
        public static ConfigEntry<float> AtmHazeStrength;
        public static ConfigEntry<float> EdgeSkyBlend;
        public static ConfigEntry<float> EdgeThinExtent;

        // Godrays
        public static ConfigEntry<float> GodrayStrength;
        public static ConfigEntry<float> GodrayDensity;
        public static ConfigEntry<float> GodrayFadeBottom;
        public static ConfigEntry<float> GodrayMaxDist;
        public static ConfigEntry<float> GodrayGlowFloor;
        public static ConfigEntry<int> GodraySteps;

        // Compatibility
        public static ConfigEntry<bool> DisableUltimateBloom;

        // Horizon
        public static ConfigEntry<float> HorizonFade;
        public static ConfigEntry<float> HorizonVanish;

        // High Clouds
        public static ConfigEntry<float> HighCloudHeight;
        public static ConfigEntry<float> HighCloudCoverage;
        public static ConfigEntry<float> HighCloudOpacity;
        public static ConfigEntry<float> HighCloudTiling;
        public static ConfigEntry<float> HighCloudStretch;
        public static ConfigEntry<float> HighCloudHalo;

        // Sky / Atmosphere (physically-based Hillaire sky that replaces TOD's dome)
        public static ConfigEntry<bool> CustomSky;
        public static ConfigEntry<bool> PublishSkyReflections;
        public static ConfigEntry<bool> GroundLightingFromSky;
        public static ConfigEntry<float> GroundLightingStrength;
        public static ConfigEntry<float> AtmExposure;
        public static ConfigEntry<float> AtmRayleigh;
        public static ConfigEntry<float> AtmMie;
        public static ConfigEntry<float> AtmOzone;
        public static ConfigEntry<float> AtmMieG;
        public static ConfigEntry<float> AtmGroundAlbedo;
        public static ConfigEntry<float> AtmWeatherHaze;
        public static ConfigEntry<float> AtmMoonBrightness;
        public static ConfigEntry<bool> AtmDither;
        public static ConfigEntry<bool> Stars;
        public static ConfigEntry<float> StarBrightness;
        public static ConfigEntry<float> StarDensity;
        public static ConfigEntry<float> StarProbability;
        public static ConfigEntry<float> StarTwinkle;
        public static ConfigEntry<float> MilkyWay;
        public static ConfigEntry<EAtmDebug> AtmDebug;

        private static ConfigDescription Adv(string desc, AcceptableValueBase range = null)
        {
            return new ConfigDescription(desc, range,
                new ConfigurationManagerAttributes { IsAdvanced = true });
        }

        // Map height per quality tier (width = 2x). Medium = 1536 = the shipped default (half-res density).
        private static int SkyMapHeightFor(ESkyDomeQuality q)
        {
            switch (q)
            {
                case ESkyDomeQuality.Low:   return 1024;
                case ESkyDomeQuality.High:  return 2048;
                case ESkyDomeQuality.Ultra: return 3072;
                default:                    return 1536;   // Medium
            }
        }

        public static void Bind(ConfigFile config)
        {
            string currentVersion = MetadataHelper.GetMetadata(typeof(Plugin)).Version.ToString();
            var version = config.Bind("Internal", "ConfigVersion", "", "Do not modify");

            if (version == null || version.Value != currentVersion)
            {
                config.Clear();
                System.IO.File.WriteAllText(config.ConfigFilePath, "");
                config.Reload();
                version = config.Bind("Internal", "ConfigVersion", currentVersion, "Do not modify");
                version.Value = currentVersion;
                config.Save();
                Plugin.MyLog.LogInfo($"Config reset for version {currentVersion}");
            }

            DisableUltimateBloom = config.Bind("Bonus", "Disable UltimateBloom", true,
                Adv("Turn off Ultimate Bloom camera effect (can conflict with godrays)"));

            EyeAdaptation = config.Bind("Bonus", "Disable Eye Adaptation", true, "Disables eye adaptation if enabled");           

            WorldExposure = config.Bind("Bonus", "World Exposure", 0.8f,
                new ConfigDescription("Brighten/darken the exposure of your view", new AcceptableValueRange<float>(0, 2)));

            CoverageMinTransitionTime = config.Bind("Weather", "Min Cloud Transition Time (sec)", 200f,
                new ConfigDescription("Slew-rate limit on cloud coverage: the fewest real seconds a full " +
                    "clear<->overcast swing may take. Vanilla weather sometimes swings coverage far faster " +
                    "than this (e.g. when the forecast jumps between weather presets) — those get clamped to " +
                    "this rate. Transitions already slower than this pass through UNCHANGED (it is not a " +
                    "blanket low-pass). 0 = off (no limit, old behavior). Raise if fast pop-ins still slip " +
                    "through; lower if gradual build-ups start to lag.",
                    new AcceptableValueRange<float>(0f, 600f)));

            // Cloud coverage remap: normalized weather cloudiness (0..1) -> shader _CloudDensity through
            // these state anchors, so Tarkov's weather presets render as intended (SUNNY -> clear,
            // CLOUDY -> cloudy/heavy, RAINY -> overcast) instead of the old flat Lerp(0.6,3.3) that crushed
            // the whole CLOUDY band into overcast. Each value is one cloud state's density; slide to taste.
            ClearDensity = config.Bind("Weather", "Density - Clear", 0.85f,
                new ConfigDescription("_CloudDensity for a clear / sunny sky.", new AcceptableValueRange<float>(0.5f, 3.5f)));
            PartlyDensity = config.Bind("Weather", "Density - Partly Cloudy", 1.15f,
                new ConfigDescription("_CloudDensity for partly cloudy.", new AcceptableValueRange<float>(0.5f, 3.5f)));
            CloudyDensity = config.Bind("Weather", "Density - Cloudy", 1.5f,
                new ConfigDescription("_CloudDensity for a cloudy day.", new AcceptableValueRange<float>(0.5f, 3.5f)));
            HeavyDensity = config.Bind("Weather", "Density - Heavy", 1.75f,
                new ConfigDescription("_CloudDensity for heavy cloud coverage.", new AcceptableValueRange<float>(0.5f, 3.5f)));
            OvercastDensity = config.Bind("Weather", "Density - Overcast", 2.1f,
                new ConfigDescription("_CloudDensity for full overcast (rainy). Higher just looks the same.", new AcceptableValueRange<float>(0.5f, 3.5f)));

            WindSpeedMin = config.Bind("Wind", "Min Wind Speed", 0.0008f,
                Adv("Minimum cloud wind speed", new AcceptableValueRange<float>(0.0001f, 0.01f)));
            WindSpeedMax = config.Bind("Wind", "Max Wind Speed", 0.003f,
                Adv("Maximum cloud wind speed", new AcceptableValueRange<float>(0.0001f, 0.01f)));           

            TerrainShadowDensity = config.Bind("Terrain Shadow", "Shadow Density", 0.15f,
                new ConfigDescription("Density of cloud shadows on terrain", new AcceptableValueRange<float>(0f, 1f)));
            ShadowSoftness = config.Bind("Terrain Shadow", "Shadow Softness", 0f,
                new ConfigDescription("Soften the cloud-shadow EDGES on the ground (penumbra), in world metres, " +
                    "so as the sun sweeps past clouds the lit<->shadow transitions ramp gradually instead of " +
                    "snapping — much less harsh/'sensitive' under the fast raid clock. Keeps shadow darkness + " +
                    "coverage (unlike lowering Shadow Density); only the edges soften. 0 = off (sharp). Higher " +
                    "= softer, but costs more (each step re-marches the shadow — keep it moderate).",
                    new AcceptableValueRange<float>(0f, 300f)));
            ShadowDetailReduction = config.Bind("Terrain Shadow", "Shadow Detail Reduction", 5f,
                new ConfigDescription("Coarsen the cloud detail the ground shadow picks up (a mip bias on the " +
                    "cookie's density sampling). The cookie otherwise samples the FINEST cauliflower detail, so " +
                    "the ground shadow reads busy/noisy. Higher = the shadow follows only the big cloud SHAPES " +
                    "(cleaner); 0 = full detail (old, noisy).", new AcceptableValueRange<float>(0f, 5f)));
            ShadowWorldSize = config.Bind("Terrain Shadow", "Shadow World Size (m)", 15000f,
                new ConfigDescription("How much ground the shadow cookie covers, in metres. Larger = the shadow " +
                    "reaches further out AND each texel covers more ground (coarser, softer shadow); smaller = " +
                    "sharper but a smaller shadowed area around you.", new AcceptableValueRange<float>(2000f, 15000f)));
            ShadowSteps = config.Bind("Terrain Shadow", "Shadow Steps", 32,
                new ConfigDescription("Ray-march steps building the shadow cookie. More = smoother/more accurate " +
                    "shadow density, at more GPU; fewer = cheaper but can band.", new AcceptableValueRange<int>(4, 32)));
            ShadowResolution = config.Bind("Terrain Shadow", "Shadow Resolution", VolCloudRenderer.EShadowResolution.Low,
                "Cookie texture resolution (Low=512, Medium=1024, High=2048). Higher = sharper shadow edges; " +
                "lower = blurrier (also hides fine detail). Changing this reallocates the shadow texture — set " +
                "it from the menu, not mid-firefight.");
            ShadowFlipX = config.Bind("Terrain Shadow", "Shadow Flip X", true,
                "Mirror the cloud ground-shadow along one axis. The cookie is laid out in the sun frame, which " +
                "may not match how the game projects it onto terrain — if shadows land on the WRONG side of the " +
                "clouds (mirrored), toggle this (and/or Flip Y) until they line up. Doesn't affect godrays.");
            ShadowFlipY = config.Bind("Terrain Shadow", "Shadow Flip Y", false,
                "Mirror the cloud ground-shadow along the other axis (see Shadow Flip X). Try the four " +
                "combinations of Flip X / Flip Y to find the one where cloud shadows sit correctly vs the sun.");

            // Performance (always visible)
            PrimarySteps = config.Bind("Performance", "Primary Steps", 128,
                new ConfigDescription("Ray march steps for clouds (higher = better quality, worse performance)", new AcceptableValueRange<int>(1, 256)));
            LightSteps = config.Bind("Performance", "Light Steps", 6,
                new ConfigDescription("Steps for light marching through clouds", new AcceptableValueRange<int>(1, 6)));
            Resolution = config.Bind("Performance", "Cloud Resolution", VolCloudRenderer.CloudResolution.Half, "Full = best quality, Half = best performance");
            TemporalMode = config.Bind("Performance", "Temporal Mode", VolCloudRenderer.CloudTemporalMode.SkyDome,
                "Switch between temporal modes or turn off. Skydome looks and performs best.");
            SkyDomeQuality = config.Bind("Performance", "SkyDome Quality", ESkyDomeQuality.Medium,
                "SKYDOME MODE ONLY: sharpness of the direction-anchored sky map (screen resolution / " +
                "Cloud Resolution don't affect SkyDome — this does). Low ≈ 1024px map, Medium ≈ 1536 " +
                "(half-res density, the default), High ≈ 2048, Ultra ≈ 3072 (full-res density). Higher = " +
                "sharper clouds but a bigger per-frame bake; the map re-fills over ~1s when you change it. " +
                "Ignored in Nubis/Blend/Off.");
            TemporalSmoothing = config.Bind("Performance", "Temporal Smoothing", 0f,
                new ConfigDescription("BLEND MODE ONLY: how much of last frame's clouds to keep " +
                    "(reprojected + clamped). Ignored in Nubis mode, which replaces pixels instead of " +
                    "blending them. 0 = off.",
                    new AcceptableValueRange<float>(0f, 0.95f)));
            DebugView = config.Bind("Performance", "Debug View", ECloudDebugView.Off,
                Adv("Temporal diagnostics (history is corrupted while on — set back to Off to recover). " +
                    "ReprojectionDelta: |reprojected uv - uv| x200. FLATSCREEN, standing still: must be " +
                    "pure BLACK. VR: only the RIGHT eye (the history writer) must be black when still — " +
                    "the LEFT eye correctly shows a static pattern (the eye-to-eye remap of the shared " +
                    "history), which is NOT an error. Blue = no history. TemporalDisagreement: " +
                    "|current - history| x8 — where the cloud CONTENT itself changes frame to frame. " +
                    "Also logs fires-per-frame + matrix motion to the BepInEx console while on."));
            ReprojectionFlip = config.Bind("Performance", "Reprojection V-Flip", false,
                Adv("A/B toggle for the temporal reprojection's vertical convention (can't be determined " +
                    "automatically). Test: turn Temporal Smoothing UP (0.9) and PITCH the camera up and " +
                    "down — if the clouds ghost/double vertically, flip this. Whichever setting kills the " +
                    "pitch ghosting is correct; yaw looks fine either way."));

            // Bonus steps (advanced)
            BonusPrimarySteps = config.Bind("Performance", "Bonus Primary Steps", 0,
                Adv("Extra primary steps added on top of base. The shader ceiling is 512 total — grazing " +
                    "views along the deck have very long paths through the layer, and more steps there is " +
                    "what shrinks the visible terracing/banding (cheap in Nubis mode: only 1/16 of pixels " +
                    "march per frame).", new AcceptableValueRange<int>(0, 256)));
            BonusLightSteps = config.Bind("Performance", "Bonus Light Steps", 0,
                Adv("Extra light steps added on top of base", new AcceptableValueRange<int>(0, 16)));

            // Shape (advanced)
            DensityMultiplier = config.Bind("Cloud Shape", "Density Multiplier", 3f,
                Adv("Density multiplier for opacity", new AcceptableValueRange<float>(0.1f, 100f)));
            NoiseTilingXZ = config.Bind("Cloud Shape", "Noise Tiling XZ", 2f,
                Adv("Horizontal noise tiling", new AcceptableValueRange<float>(0.01f, 5f)));
            NoiseTilingY = config.Bind("Cloud Shape", "Noise Tiling Y", 1.5f,
                Adv("Vertical noise tiling", new AcceptableValueRange<float>(0.01f, 2f)));
            DetailTiling = config.Bind("Cloud Shape", "Detail Tiling", 22f,
                Adv("3D detail noise tiling", new AcceptableValueRange<float>(1f, 40f)));
            DetailErosion = config.Bind("Cloud Shape", "Detail Erosion", 0.25f,
                Adv("Fine detail erosion strength", new AcceptableValueRange<float>(0f, 1f)));
            DetailOctaveScale = config.Bind("Cloud Shape", "Detail Octave Scale", 2.64f,
                Adv("Frequency of the SECOND detail octave (fine tendrils at near cloud edges), as a " +
                    "multiplier on Detail Tiling. Higher = finer/smaller tendrils.",
                    new AcceptableValueRange<float>(1f, 8f)));
            DetailOctaveStrength = config.Bind("Cloud Shape", "Detail Octave Strength", 0.86f,
                Adv("How much the second detail octave shows at cloud edges (0 = off). It fades with " +
                    "distance and collapses toward solid cloud cores automatically.",
                    new AcceptableValueRange<float>(0f, 1f)));
            DetailDetile = config.Bind("Cloud Shape", "Detail De-tiling", 6f,
                Adv("Domain-warp strength that hides the detail noise texture's repeat grid (visible as " +
                    "tiling on dense flat undersides). 0 = off (old tiled behavior).",
                    new AcceptableValueRange<float>(0f, 20f)));
            DetailBottomFade = config.Bind("Cloud Shape", "Detail Bottom Fade", 0.9f,
                Adv("Smooth the tiled detail off flat, dense cloud UNDERSIDES (stratus/overcast sheets) where " +
                    "the erosion noise has no shape to hide behind and repeats as a quilt. 0 = detail " +
                    "everywhere (old), 1 = fully smooth flat bottoms. Nubis: detail belongs on edges/tops, " +
                    "not flat faces.", new AcceptableValueRange<float>(0f, 1f)));
            DetailBottomExtent = config.Bind("Cloud Shape", "Detail Bottom Fade Height", 0.08f,
                Adv("How far up from the cloud base the bottom-fade reaches (fraction of cloud thickness). " +
                    "Higher = smooths more of the lower cloud; lower = only the very bottom.",
                    new AcceptableValueRange<float>(0.05f, 1f)));
            BaseInversion = config.Bind("Cloud Shape", "Base Inversion", 0.25f,
                Adv("0 = no inversion, 1 = inverted worley on bottom of cloud", new AcceptableValueRange<float>(0f, 1f)));
            CurlStrength = config.Bind("Cloud Shape", "Curl Strength", 0.105f,
                Adv("Curl noise strength for turbulent edges", new AcceptableValueRange<float>(0f, 1f)));
            CurlTiling = config.Bind("Cloud Shape", "Curl Tiling", 0.1f,
                Adv("Curl noise tiling", new AcceptableValueRange<float>(0.1f, 5f)));
            BaseWispiness = config.Bind("Cloud Shape", "Base Wispiness", 0.5f,
                Adv("Wispiness at cloud base (0 = wispy, 1 = solid)", new AcceptableValueRange<float>(0f, 1f)));
            DensitySharpness = config.Bind("Cloud Shape", "Density Softness", 0.4f,
                Adv("Softness of density falloff (higher = softer edges, lower = sharper clouds)", new AcceptableValueRange<float>(0.1f, 5f)));
            WorldScale = config.Bind("Cloud Shape", "World Scale", 0.0001f,
                Adv("Scale of the clouds", new AcceptableValueRange<float>(0.0001f, 0.005f)));
            PlanetRadius = config.Bind("Cloud Shape", "Planet Curvature Radius", 300000f,
                Adv("Curve the cloud layer over a planet of this radius (meters) so clouds bend down and SET at the horizon instead of fading out. 0 = flat slab (old behavior). Smaller = more curvature; try ~300000. Earth = 6371000 (subtle + less precise).", new AcceptableValueRange<float>(0f, 2000000f)));
            //CloudDensity = config.Bind("Cloud Shape", "Cloud Density", 1f,
            //  Adv("Overall cloud coverage", new AcceptableValueRange<float>(0.1f, 3f)));

            // Lighting (advanced)
            Extinction = config.Bind("Cloud Lighting", "Extinction", 15f,
                Adv("Beer-Lambert extinction", new AcceptableValueRange<float>(0.001f, 100f)));
            LightDensityScale = config.Bind("Cloud Lighting", "Light March Density", 1f,
                Adv("Density scale for light march", new AcceptableValueRange<float>(0.01f, 20f)));
            LongShadowStrength = config.Bind("Cloud Lighting", "Cloud-on-Cloud Shadow", 0.3f,
                Adv("Let clouds cast long shadows on OTHER clouds (distant self-shadowing toward the sun), on " +
                    "top of the local self-shadow. Strongest at low sun (long dawn/dusk shadow bands across the " +
                    "deck); ~nothing at noon. 0 = off (local shadow only). Higher = deeper. Adds a few " +
                    "light-march samples, so it costs some GPU when on.", new AcceptableValueRange<float>(0f, 3f)));
            LongShadowReach = config.Bind("Cloud Lighting", "Cloud Shadow Reach", 8000f,
                Adv("How far (metres) the cloud-on-cloud shadow reaches along the sun direction. Larger = " +
                    "distant clouds shadow nearer ones over a longer range (bigger bands); smaller = only close " +
                    "neighbours.", new AcceptableValueRange<float>(1000f, 30000f)));
            ScatterForward = config.Bind("Cloud Lighting", "Forward Scatter", 0.9f,
                Adv("Silver lining strength", new AcceptableValueRange<float>(0f, 0.99f)));
            ScatterBack = config.Bind("Cloud Lighting", "Back Scatter", 0.3f,
                Adv("Back-lit scatter", new AcceptableValueRange<float>(0f, 0.99f)));
            ScatterMix = config.Bind("Cloud Lighting", "Scatter Blend", 0.75f,
                Adv("Forward vs back blend", new AcceptableValueRange<float>(0f, 1f)));
            AmbientStrength = config.Bind("Cloud Lighting", "Ambient Strength", 1f,
                Adv("Strength of the ambient/skylight fill on the clouds (the shadowed-side fill), as a PHYSICAL " +
                    "FRACTION of the sun. The ambient is auto-coupled to the sun through the atmosphere: the sky " +
                    "and sun come from the same LUT, so 1 = the physically-correct sky/sun ratio (skylight is " +
                    "~15-25% of direct sun on a clear day) and it stays balanced no matter how you tune Sun " +
                    "Intensity. Leave at 1 for correct; raise for a brighter/hazier fill, lower to let the sun's " +
                    "shading dominate. Colour always comes from the physical sky.",
                    new AcceptableValueRange<float>(0f, 2f)));
            AtmosphereAmbient = config.Bind("Cloud Lighting", "Ambient From Sky", true,
                "Drive the clouds' ambient/skylight COLOUR from the physically-based atmosphere sky (the same " +
                "sky you see) instead of Tarkov's flat tint — cloud tops pick up the bright sky, undersides " +
                "the ground bounce, and everything warms correctly at sunset. Tune brightness with 'Ambient " +
                "Strength'. On by default; falls back to the flat tint automatically if Custom Sky is off.");
            AtmAmbientHeightPow = config.Bind("Cloud Lighting", "Sky Ambient Height Falloff", 0.5f,
                Adv("How the sky-driven ambient blends from ground-bounce (cloud base) up to sky (cloud top). " +
                    "1 = linear; >1 keeps the darker ground bounce higher up the cloud; <1 lets sky reach lower.",
                    new AcceptableValueRange<float>(0.25f, 4f)));
            BaseSkyVeil = config.Bind("Cloud Lighting", "Base Sky Veil", 0.1f,
                Adv("How much the cloud UNDERSIDE reads as the cool horizon SKY vs the warm ground bounce. This is " +
                    "physical aerial perspective: looking down through the air below the cloud, the distant ground " +
                    "is veiled toward the horizon-sky colour, so = 1 - transmittance of that air column. 0 = only " +
                    "ground bounce (warm/yellow undersides — the old look); 1 = full cool horizon sky. 0.6 = a cool " +
                    "grey base with a subtle warm underglow (and it auto-warms at sunset, since the horizon reddens). " +
                    "Only when 'Ambient From Sky' is on.",
                    new AcceptableValueRange<float>(0f, 1f)));
            SunColorFromSky = config.Bind("Cloud Lighting", "Sun Color From Sky", true,
                "Colour the sunlight on the clouds from OUR physically-based atmosphere (white sun attenuated " +
                "along the sun ray) instead of Tarkov's TOD colours — neutral-warm at midday, orange/red at " +
                "sunset, and it MATCHES the sky's sun. On by default; off = the old TOD sun colour.");
            SunColorStrength = config.Bind("Cloud Lighting", "Sun Color Strength", 1f,
                Adv("How far to push the sun colour from white (0) toward the full physical atmosphere colour " +
                    "(1). Lower for a more neutral sun, 1 for full sunset reddening. Only when 'Sun Color From " +
                    "Sky' is on.", new AcceptableValueRange<float>(0f, 1f)));
            SunColorWarmth = config.Bind("Cloud Lighting", "Sun Color Warmth", 1f,
                Adv("How red the sun goes at sunrise/sunset (scales the atmospheric path length). 1 = orange " +
                    "(default); lower = more neutral / less reddening; higher = deeper red sunsets. Only when " +
                    "'Sun Color From Sky' is on.", new AcceptableValueRange<float>(0f, 2f)));
            MultiScatter = config.Bind("Cloud Lighting", "Multi-Scatter", 20f,
                Adv("Fake inner bounce light", new AcceptableValueRange<float>(0f, 100f)));
            MultiOctaveScatter = config.Bind("Cloud Lighting", "Multi-Octave Scatter", true,
                "Nubis/Wrenninge multiple-scattering model: instead of one direct term + the separate " +
                "Multi-Scatter fill, sum several 'octaves' of light — each dimmer, deeper-penetrating, and more " +
                "isotropic — which self-fills the shadowed interior more consistently. When ON, the Multi-Scatter " +
                "slider is ignored. Off by default; A/B it against the classic model.");
            MsOctaves = config.Bind("Cloud Lighting", "Multi-Octave Count", 4,
                Adv("How many scattering octaves to sum (only when Multi-Octave Scatter is on). More = brighter, " +
                    "softer, more filled interiors, at a little more GPU.", new AcceptableValueRange<int>(2, 6)));
            MsOctaveFalloff = config.Bind("Cloud Lighting", "Multi-Octave Falloff", 0.5f,
                Adv("How fast each octave's penetration + phase spread out (only when Multi-Octave Scatter is " +
                    "on). 0.5 = classic Wrenninge. Higher = deeper, more forward; lower = shallower, more " +
                    "isotropic.", new AcceptableValueRange<float>(0.1f, 0.9f)));
            MsAlbedo = config.Bind("Cloud Lighting", "Multi-Octave Albedo", 0.6f,
                Adv("Energy kept per scattering bounce in Multi-Octave mode = the cloud's single-scatter albedo. " +
                    "Real cloud droplets barely absorb (~0.99), so the light builds up to bright WHITE tops. The " +
                    "old 0.5 threw that energy away (grey tops). Raise toward 0.99 for whiter, more thickly-lit " +
                    "clouds; lower for darker/thinner.", new AcceptableValueRange<float>(0.1f, 0.99f)));
            MoonSilverLining = config.Bind("Cloud Lighting", "Moon Silver Lining", 0.1f,
                Adv("Fine-tune on the moon's silver-lining rim (thin backlit cloud edges toward the moon). " +
                    "The rim's crispness comes from the corrected moon phase; this just scales its magnitude. " +
                    "1 = as-corrected; lower if too hot, higher for more punch; 0 = off.",
                    new AcceptableValueRange<float>(0f, 8f)));
            AlpenStrength = config.Bind("Cloud Lighting", "Alpenglow Strength", 0f,
                Adv("Warm low-sun glow on cloud undersides at sunrise/sunset (0 = off)", new AcceptableValueRange<float>(0f, 8f)));
            PowderStrength = config.Bind("Cloud Lighting", "Powder Strength", 1f,
                Adv("Dark-edge (powder) effect strength. 0 = no dark outline around cloud edges", new AcceptableValueRange<float>(0f, 10f)));
            PowderBase = config.Bind("Cloud Lighting", "Powder Base", 0f,
                Adv("How much powder shows when NOT looking toward the sun. 0 = backlit edges only (removes the always-on edge outline)", new AcceptableValueRange<float>(0f, 1f)));
            PowderScale = config.Bind("Cloud Lighting", "Powder Detail Scale", 1.5f,
                Adv("Thinness of the dark edge band (higher = thinner/sharper, lower = broader/softer)", new AcceptableValueRange<float>(1f, 16f)));
            PowderFill = config.Bind("Cloud Lighting", "Powder Fill", 0f,
                Adv("Granular 'powdery' texture on the SUNLIT TOPS. Powder Strength only affects the direct " +
                    "sun term, which is nearly off on FRONT-LIT clouds (sun behind you) — so the tops there are " +
                    "carried by the smooth fill light and look creamy. This folds the fine cauliflower detail " +
                    "into that fill so the tops read granular at any sun angle. 0 = smooth (old). If the tops " +
                    "DIM instead of just texturing, raise 'Powder Detail Scale'.", new AcceptableValueRange<float>(0f, 1f)));
            SunDiskOcclusion = config.Bind("Cloud Lighting", "Occlude Sun/Moon Disk", false,
                Adv("Hide the game's bright sun and moon discs where clouds are in front of them, without needing ultra-dense clouds."));
            SunOcclusionRadius = config.Bind("Cloud Lighting", "Disk Occlusion Radius (deg)", 15f,
                Adv("Angular radius around the sun/moon within which clouds hide it. Bigger = a wider halo gets hidden.", new AcceptableValueRange<float>(0f, 15f)));
            SunOcclusionFloor = config.Bind("Cloud Lighting", "Disk Occlusion Cloud Floor", 0.25f,
                Adv("How much cloud must be in front of the sun/moon before it's hidden. Lower = thin wisps hide it; higher = only solid cloud (wisps keep glowing it through).", new AcceptableValueRange<float>(0f, 1f)));

            OwnSunMoonDisc = config.Bind("Cloud Lighting", "Own Sun/Moon Disc", true,
                "Draw the mod's OWN sun & moon disc inside the cloud shader (composited under the clouds) and " +
                "hide the game's built-in sun/moon discs. Because the mod's disc has a controlled brightness " +
                "and is occluded by real cloud transmittance, a dark cloud actually hides it — fixing the " +
                "game's blinding sun/moon leaking through. Overrides 'Occlude Sun/Moon Disk' when on.");
            SunDiscSize = config.Bind("Cloud Lighting", "Sun Disc Size (deg)", 2f,
                Adv("Angular diameter of the mod's sun disc.", new AcceptableValueRange<float>(0.2f, 30f)));
            SunDiscBrightness = config.Bind("Cloud Lighting", "Sun Disc Brightness", 8f,
                Adv("Brightness of the mod's sun disc.", new AcceptableValueRange<float>(0f, 30f)));
            SunDiscGlow = config.Bind("Cloud Lighting", "Sun Disc Glow", 0.66f,
                Adv("Soft halo strength around the mod's sun disc.", new AcceptableValueRange<float>(0f, 8f)));
            MoonDiscSize = config.Bind("Cloud Lighting", "Moon Disc Size (deg)", 2.2f,
                Adv("Angular diameter of the mod's moon disc.", new AcceptableValueRange<float>(0.2f, 30f)));
            MoonDiscBrightness = config.Bind("Cloud Lighting", "Moon Disc Brightness", 1f,
                Adv("Brightness of the mod's moon disc.", new AcceptableValueRange<float>(0f, 10f)));
            MoonDiscGlow = config.Bind("Cloud Lighting", "Moon Disc Glow", 0.02f,
                Adv("Soft halo strength around the mod's moon disc.", new AcceptableValueRange<float>(0f, 8f)));
            MoonEarthshine = config.Bind("Cloud Lighting", "Moon Earthshine", 0.04f,
                Adv("Faint ashen glow on the moon's shadowed side (Earth-reflected light) so the dark part " +
                    "isn't pure black. 0 = off.", new AcceptableValueRange<float>(0f, 0.5f)));
            MoonTerminator = config.Bind("Cloud Lighting", "Moon Terminator Softness", 0.12f,
                Adv("Softness of the moon's day/night line. Lower = crisper terminator, higher = softer.",
                    new AcceptableValueRange<float>(0.01f, 0.5f)));
            MoonOrbit = config.Bind("Cloud Lighting", "Moon Orbit Mode", EMoonOrbit.Realistic,
                "Moon position + PHASE source. GameDefault = leave Tarkov's setting alone. OppositeToSun = " +
                "pin the moon 180 deg from the sun (always FULL, rises where the sun sets). Realistic = " +
                "date-driven orbit with REAL phases (crescent/gibbous/full). Tarkov most likely uses " +
                "OppositeToSun, so set this to Realistic if you want the phase to actually change. The " +
                "detected game mode is logged to the console at raid start.");
            DiscOcclusionPow = config.Bind("Cloud Lighting", "Disc Occlusion Sharpness", 3f,
                Adv("How hard clouds hide the mod's sun/moon disc. 1 = physical (dense cloud still leaks a " +
                    "little). Higher = the darkest/densest cloud fully hides the disc while thin wisps keep " +
                    "glowing it through. Raise this if the sun still shows through dark clouds.",
                    new AcceptableValueRange<float>(1f, 6f)));
            SunIntensity = config.Bind("Cloud Lighting", "Sun Intensity", 8f,
                Adv("The sun's brightness on the clouds at high sun (the physical illuminance in our units — it " +
                    "ramps down toward the horizon). 1.5 suits the classic single-scatter fill; the physical " +
                    "MULTI-OCTAVE model needs the real sun magnitude (higher). Raise this when Multi-Octave " +
                    "Scatter is on until the sunlit tops read WHITE (with Multi-Octave Albedo ~0.9 the fill " +
                    "builds properly, so you shouldn't need it extreme; the near-sun silver lining clipping to " +
                    "white is correct, not a bug).", new AcceptableValueRange<float>(0f, 40f)));
            MoonIntensity = config.Bind("Cloud Lighting", "Moon Intensity", 1f,
                Adv("The moon's brightness on the clouds at night (peak; it fades in as the sun drops). The real " +
                    "moon is far dimmer than the sun, so this is lifted for visibility — set it by taste for how " +
                    "bright moonlit nights read.", new AcceptableValueRange<float>(0f, 5f)));
            AtmHazeStrength = config.Bind("Cloud Horizon", "Haze Strength", 1f,
                Adv("Haze color strength", new AcceptableValueRange<float>(0f, 1f)));
            EdgeSkyBlend = config.Bind("Cloud Horizon", "Edge Sky Blend", 1f,
                Adv("Dissolves the thin cloud EDGE into the sky to kill the dark 'bubble'/sticker outline that " +
                    "hugs clouds (worst at sunset). A thin cloud physically can't be darker than the sky behind " +
                    "it, so the faint edge is floored at the sky colour — dark cores and bright silver-lined " +
                    "edges are untouched. 0 = off (old ring). Requires Ambient From Sky on.",
                    new AcceptableValueRange<float>(0f, 1f)));
            EdgeThinExtent = config.Bind("Cloud Horizon", "Edge Sky Blend Extent", 0.5f,
                Adv("How far in from the cloud edge the sky-blend reaches (by opacity). Higher = more of the " +
                    "cloud's rim dissolves into the sky; lower = only the very thinnest fringe.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            // Godrays (advanced)
            GodrayStrength = config.Bind("Cloud Godrays", "Strength", 0.5f,
                Adv("Crepuscular light shafts below the clouds (0 = off). Now normalized — this scales " +
                    "linearly and predictably (the old build blew out over ~0.01; a value near 1 is normal).",
                    new AcceptableValueRange<float>(0f, 4f)));
            GodrayDensity = config.Bind("Cloud Godrays", "Air Density", 0.5f,
                Adv("How thick the scattering air feels (linear multiplier on the shafts)", new AcceptableValueRange<float>(0f, 2f)));
            GodrayGlowFloor = config.Bind("Cloud Godrays", "Glow Floor", 0.1f,
                Adv("Optional always-on haze glow toward the sun, independent of clouds. 0 = pure shafts " +
                    "(clear sky shows NO godray glow); raise for a soft sun aureole even with no clouds in " +
                    "the way.", new AcceptableValueRange<float>(0f, 1f)));
            GodrayFadeBottom = config.Bind("Cloud Godrays", "Fade Bottom (m)", 347f,
                Adv("Altitude where shafts fade out toward the ground", new AcceptableValueRange<float>(0f, 1000f)));
            GodrayMaxDist = config.Bind("Cloud Godrays", "Max Distance (m)", 2200f,
                Adv("How far the shafts march (keep <= 2500)", new AcceptableValueRange<float>(500f, 3000f)));
            GodraySteps = config.Bind("Cloud Godrays", "Steps", 32,
                Adv("Quality vs cost", new AcceptableValueRange<int>(4, 32)));           

            // Horizon (advanced)
            HorizonFade = config.Bind("Cloud Horizon", "Horizon Color Fade", 0.2f,
                Adv("Horizon color fade height", new AcceptableValueRange<float>(0f, 1f)));
            HorizonVanish = config.Bind("Cloud Horizon", "Horizon Alpha", 0.04f,
                Adv("Horizon alpha height (where clouds disappear into horizon)", new AcceptableValueRange<float>(0f, 1f)));

            // High Clouds (advanced)
            HighCloudHeight = config.Bind("High Clouds", "Height", 8000f,
                Adv("Altitude of high cloud layer (real cirrus sit ~8000 m; higher = lit longer after sunset)", new AcceptableValueRange<float>(0.5f, 12000f)));
            //HighCloudCoverage = config.Bind("High Clouds", "Coverage", 0.8f,
            //    Adv("High cloud coverage amount", new AcceptableValueRange<float>(0f, 1f)));
            HighCloudOpacity = config.Bind("High Clouds", "Opacity", 0.95f,
                Adv("High cloud opacity looking straight up (thicker toward the horizon, like real cirrus). Clearly visible cirrus are ~0.3-0.5; below ~0.1 they're sub-visible except near the sun.", new AcceptableValueRange<float>(0f, 0.95f)));
            HighCloudTiling = config.Bind("High Clouds", "Tiling", 0.4f,
                Adv("High cloud noise scale", new AcceptableValueRange<float>(0.1f, 5f)));
            HighCloudStretch = config.Bind("High Clouds", "Stretch", 0.64f,
                Adv("Wispy streak stretch amount (lower = more streaky)", new AcceptableValueRange<float>(0.05f, 1f)));
            HighCloudHalo = config.Bind("High Clouds", "Ice Halo", 0.5f,
                Adv("22-degree halo around the sun and moon in patches of the high cloud veil (ice crystals can't bend light less than ~22 deg, so it piles up in a ring). 0 = off.", new AcceptableValueRange<float>(0f, 1f)));

            // Sky / Atmosphere
            CustomSky = config.Bind("Sky", "Custom Sky", true,
                "Replace Tarkov's dated, banded TOD atmosphere + PNG stars with a physically-based " +
                "(Hillaire 2020) sky computed from the time of day and weather. Off = vanilla sky.");
            PublishSkyReflections = config.Bind("Sky", "Publish Sky For Reflections", true,
                "Bake the displayed sky (atmosphere + clouds + sun/moon) into a small directional map " +
                "each frame and publish it for other mods to sample. SSRSix uses it so water and puddles " +
                "reflect the LIVE sky — actual clouds, correct time of day — instead of the static baked " +
                "probe. Negligible cost (one 512x256 blit); needs Custom Sky and the SkyDome cloud mode. " +
                "Does nothing visible unless a consumer mod is installed.");
            GroundLightingFromSky = config.Bind("Sky", "Ground Lighting From Sky", true,
                "Drive Tarkov's SCENE lighting (the sun/directional colour on terrain, objects, players + the " +
                "ambient fill) from OUR atmosphere, so the ground finally matches the custom sky and clouds " +
                "instead of TOD's dated colours. Keeps the game's tuned brightness — only the HUE changes — and " +
                "only by day (night keeps the vanilla moon look). Whole-scene change; off by default. " +
                "Conflicts with mods that also override the light colour (e.g. HollywoodGraphics).");
            GroundLightingStrength = config.Bind("Sky", "Ground Lighting Strength", 1f,
                Adv("How far to push the scene lighting toward our atmosphere colour. 0 = vanilla, 1 = full. " +
                    "Only when 'Ground Lighting From Sky' is on.", new AcceptableValueRange<float>(0f, 1f)));
            AtmExposure = config.Bind("Sky", "Sky Brightness", 15f,
                new ConfigDescription("Overall brightness of the sky into the game's tonemapper. This is a " +
                    "CALIBRATION knob — the sky is computed in physical-ish units and scaled by this to sit " +
                    "right in the exposed image. Raise if the sky is too dark, lower if it blows out.",
                    new AcceptableValueRange<float>(0f, 200f)));
            AtmRayleigh = config.Bind("Sky", "Rayleigh (Blue) Strength", 2f,
                Adv("Air-molecule scattering — the daytime blue and the red sunset. Higher = deeper blue / " +
                    "more vivid sunsets.", new AcceptableValueRange<float>(0f, 4f)));
            AtmMie = config.Bind("Sky", "Mie (Haze) Strength", 1f,
                Adv("Aerosol scattering — the white haze near the horizon and the glow around the sun. Higher " +
                    "= hazier/milkier sky.", new AcceptableValueRange<float>(0f, 5f)));
            AtmOzone = config.Bind("Sky", "Ozone Strength", 1f,
                Adv("Ozone absorption — keeps the sky blue (not grey) when the sun is low. Higher = bluer " +
                    "twilight.", new AcceptableValueRange<float>(0f, 4f)));
            AtmMieG = config.Bind("Sky", "Sun Glow Tightness", 0.8f,
                Adv("Mie phase anisotropy: how tightly the haze glow hugs the sun. Higher = a smaller, " +
                    "brighter aureole.", new AcceptableValueRange<float>(0f, 0.95f)));
            AtmGroundAlbedo = config.Bind("Sky", "Ground Albedo", 0.1f,
                Adv("How much light the virtual ground bounces back into the sky.", new AcceptableValueRange<float>(0f, 1f)));
            AtmWeatherHaze = config.Bind("Sky", "Weather Haze Coupling", 1f,
                Adv("How much cloud coverage thickens the sky's haze (adds Mie): overcast reads greyer, clear " +
                    "reads deep blue. 0 = sky ignores the weather.", new AcceptableValueRange<float>(0f, 4f)));
            AtmMoonBrightness = config.Bind("Sky", "Moon Sky Glow", 0.01f,
                new ConfigDescription("Brightness of the moonlit night sky (the moon acts as a second light in " +
                    "the atmosphere, giving a dim blue glow). 0 = pitch-black moonless night.",
                    new AcceptableValueRange<float>(0f, 3f)));
            AtmDither = config.Bind("Sky", "Dither", true,
                Adv("Add ~1 LSB of ordered dither to the sky to break any residual banding from the display " +
                    "buffer precision."));

            Stars = config.Bind("Sky", "Stars", true,
                "Draw procedural stars (replacing Tarkov's PNG star dome): sharp points with realistic " +
                "brightness spread, star colors, twinkle, and a faint Milky Way, wheeling with the night.");
            StarBrightness = config.Bind("Sky", "Star Brightness", 0.5f,
                new ConfigDescription("Overall star brightness.", new AcceptableValueRange<float>(0f, 5f)));
            StarDensity = config.Bind("Sky", "Star Density", 200f,
                Adv("How many stars (grid cells per cube face). Higher = more, smaller stars.",
                    new AcceptableValueRange<float>(20f, 800f)));
            StarProbability = config.Bind("Sky", "Star Fill", 0.02f,
                Adv("Fraction of grid cells that contain a star. Higher = a busier sky.",
                    new AcceptableValueRange<float>(0.02f, 1f)));
            StarTwinkle = config.Bind("Sky", "Star Twinkle", 0f,
                Adv("Atmospheric scintillation strength (0 = steady stars).", new AcceptableValueRange<float>(0f, 1f)));
            MilkyWay = config.Bind("Sky", "Milky Way", 0.15f,
                Adv("Brightness of the faint procedural Milky Way band. 0 = off.", new AcceptableValueRange<float>(0f, 1f)));
            AtmDebug = config.Bind("Sky", "Debug View", EAtmDebug.Off,
                Adv("Diagnostic. SkyViewRaw = show the baked sky brightness amplified (a BLACK region here " +
                    "means the atmosphere produced zero light for that direction). Coords = paint the sky's " +
                    "UV as red/green BEFORE any gate (if a region is BLACK here, the sky dome isn't being " +
                    "drawn there at all = a culling/clipping issue, not the atmosphere)."));
        }

        public static void ApplyToMaterial(Material mat)
        {
            if (mat == null) return;

            mat.SetInt("_PrimarySteps", PrimarySteps.Value + BonusPrimarySteps.Value);
            mat.SetInt("_LightSteps", LightSteps.Value + BonusLightSteps.Value);
            VolCloudRenderer.cloudResolution = Resolution.Value;
            // UpdateSkyMap re-reads this each frame and re-allocates the map when it changes.
            VolCloudRenderer.skyMapHeight = SkyMapHeightFor(SkyDomeQuality.Value);

            mat.SetFloat("_DensityMultiplier", DensityMultiplier.Value);
            mat.SetVector("_NoiseTiling", new Vector4(NoiseTilingXZ.Value, NoiseTilingY.Value, NoiseTilingXZ.Value, 0));
            mat.SetFloat("_DetailTiling", DetailTiling.Value);
            mat.SetFloat("_DetailErosion", DetailErosion.Value);
            mat.SetFloat("_DetailOctaveScale", DetailOctaveScale.Value);
            mat.SetFloat("_DetailOctaveStrength", DetailOctaveStrength.Value);
            mat.SetFloat("_DetailDetile", DetailDetile.Value);
            mat.SetFloat("_DetailBottomFade", DetailBottomFade.Value);
            mat.SetFloat("_DetailBottomExtent", DetailBottomExtent.Value);
            mat.SetFloat("_BaseInversion", BaseInversion.Value);
            mat.SetFloat("_CurlStrength", CurlStrength.Value);
            mat.SetFloat("_CurlTiling", CurlTiling.Value);
            mat.SetFloat("_BaseWispiness", BaseWispiness.Value);
            mat.SetFloat("_DensitySharpness", DensitySharpness.Value);
            mat.SetFloat("_WorldScale", WorldScale.Value);
            mat.SetFloat("_PlanetRadius", PlanetRadius.Value);
            //mat.SetFloat("_CloudDensity", CloudDensity.Value);

            mat.SetFloat("_Extinction", Extinction.Value);
            mat.SetFloat("_LightDensityScale", LightDensityScale.Value);
            mat.SetFloat("_LongShadowStrength", LongShadowStrength.Value);
            mat.SetFloat("_LongShadowReach", LongShadowReach.Value);
            mat.SetFloat("_ScatterForward", ScatterForward.Value);
            mat.SetFloat("_ScatterBack", ScatterBack.Value);
            mat.SetFloat("_ScatterMix", ScatterMix.Value);
            mat.SetFloat("_AmbientStrength", AmbientStrength.Value);
            // Physically couple the sky ambient to the sun: the sky-view LUT is baked with sun illuminance = 1,
            // so LUT_radiance * peakSun is the sky in the SAME units as the sun's direct term, and * PI is the
            // Lambertian hemisphere->irradiance factor. Peak (config) not the ramped _SunIntensity: the LUT
            // already carries the day/night + sunset variation, so at night the sun's DIRECT term ramps to 0
            // while the ambient stays the (dim, moonlit) sky * peak — correct. Ambient now auto-scales with Sun
            // Intensity, so the sky/sun balance holds no matter how the sun is tuned; Ambient Strength = 1 is
            // the physically-correct fraction.
            mat.SetFloat("_AmbientSunScale", SunIntensity.Value * Mathf.PI);
            mat.SetFloat("_SkyAmbientHeightPow", AtmAmbientHeightPow.Value);
            mat.SetFloat("_SkyGroundAlbedo", AtmGroundAlbedo.Value);   // reuse the atmosphere's ground albedo
            mat.SetFloat("_BaseSkyVeil", BaseSkyVeil.Value);
            mat.SetFloat("_MultiScatter", MultiScatter.Value);
            mat.SetFloat("_MultiOctave", MultiOctaveScatter.Value ? 1f : 0f);
            mat.SetFloat("_MsOctaves", MsOctaves.Value);
            mat.SetFloat("_MsOctaveFalloff", MsOctaveFalloff.Value);
            mat.SetFloat("_MsAlbedo", MsAlbedo.Value);
            mat.SetFloat("_MoonSilverLining", MoonSilverLining.Value);
            mat.SetFloat("_AlpenStrength", AlpenStrength.Value);
            mat.SetFloat("_PowderStrength", PowderStrength.Value);
            mat.SetFloat("_PowderBase", PowderBase.Value);
            mat.SetFloat("_PowderScale", PowderScale.Value);
            mat.SetFloat("_PowderFill", PowderFill.Value);
            mat.SetFloat("_SunDiskOcclusion", SunDiskOcclusion.Value ? 1f : 0f);
            mat.SetFloat("_SunOcclusionCos", Mathf.Cos(SunOcclusionRadius.Value * Mathf.Deg2Rad));
            mat.SetFloat("_SunOcclusionFloor", SunOcclusionFloor.Value);

            mat.SetFloat("_CelestialDisc", OwnSunMoonDisc.Value ? 1f : 0f);
            mat.SetFloat("_SunDiscSize", SunDiscSize.Value);
            mat.SetFloat("_SunDiscBrightness", SunDiscBrightness.Value);
            mat.SetFloat("_SunDiscGlow", SunDiscGlow.Value);
            mat.SetFloat("_MoonDiscSize", MoonDiscSize.Value);
            mat.SetFloat("_MoonDiscBrightness", MoonDiscBrightness.Value);
            mat.SetFloat("_MoonDiscGlow", MoonDiscGlow.Value);
            mat.SetFloat("_MoonEarthshine", MoonEarthshine.Value);
            mat.SetFloat("_MoonTerminator", MoonTerminator.Value);
            mat.SetFloat("_DiscOcclusionPow", DiscOcclusionPow.Value);
            //mat.SetFloat("_SunIntensity", SunIntensity.Value);

            mat.SetFloat("_AtmHazeStrength", AtmHazeStrength.Value);
            mat.SetFloat("_AtmSkyExposure", AtmExposure != null ? AtmExposure.Value : 40f);
            mat.SetFloat("_EdgeSkyBlend", EdgeSkyBlend.Value);
            mat.SetFloat("_EdgeThinExtent", EdgeThinExtent.Value);

            mat.SetFloat("_GodrayStrength", GodrayStrength.Value);
            mat.SetFloat("_GodrayDensity", GodrayDensity.Value);
            mat.SetFloat("_GodrayBottomHeight", GodrayFadeBottom.Value);
            mat.SetFloat("_GodrayMaxDist", GodrayMaxDist.Value);
            mat.SetFloat("_GodrayGlowFloor", GodrayGlowFloor.Value);
            mat.SetFloat("_GodraySteps", GodraySteps.Value);
            mat.SetFloat("_HorizonFade", HorizonFade.Value);
            mat.SetFloat("_HorizonVanish", HorizonVanish.Value);

            mat.SetFloat("_HighCloudHeight", HighCloudHeight.Value);
            mat.SetFloat("_HighCloudOpacity", HighCloudOpacity.Value);
            mat.SetFloat("_HighCloudTiling", HighCloudTiling.Value);
            mat.SetFloat("_HighCloudStretch", HighCloudStretch.Value);
            mat.SetFloat("_HighCloudHalo", HighCloudHalo.Value);
        }
    }
}