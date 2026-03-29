using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace CloudSix.Source
{
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? IsAdvanced;
    }
    internal static class CloudConfig
    {
        // Performance
        public static ConfigEntry<int> PrimarySteps;
        public static ConfigEntry<int> LightSteps;
        public static ConfigEntry<CloudRenderer.CloudResolution> Resolution;

        // Advanced Performance
        public static ConfigEntry<int> BonusPrimarySteps;
        public static ConfigEntry<int> BonusLightSteps;

        // Shape
        public static ConfigEntry<float> DensityMultiplier;
        public static ConfigEntry<float> BottomHeight;
        public static ConfigEntry<float> TopHeight;
        public static ConfigEntry<float> HeightVariation;
        public static ConfigEntry<float> NoiseTilingXZ;
        public static ConfigEntry<float> NoiseTilingY;
        public static ConfigEntry<float> DetailTiling;
        public static ConfigEntry<float> DetailErosion;
        public static ConfigEntry<float> DetailStrength;
        public static ConfigEntry<float> BottomDetailErosion;
        public static ConfigEntry<float> DomeScale;
        public static ConfigEntry<float> DomePositionY;
        public static ConfigEntry<float> CurlTiling;
        public static ConfigEntry<float> CurlStrength;

        // Lighting
        public static ConfigEntry<float> Extinction;
        public static ConfigEntry<float> LightDensityScale;
        public static ConfigEntry<float> ScatterForward;
        public static ConfigEntry<float> ScatterBack;
        public static ConfigEntry<float> ScatterMix;
        public static ConfigEntry<float> PowderStrength;
        public static ConfigEntry<float> AmbientStrength;
        public static ConfigEntry<float> MultiScatter;

        // Horizon
        public static ConfigEntry<float> HorizonFade;
        public static ConfigEntry<float> _FadePower;
        public static ConfigEntry<float> _HazeStrength;

        // High Clouds
        public static ConfigEntry<float> HighCloudHeight;
        public static ConfigEntry<float> HighCloudCoverage;
        public static ConfigEntry<float> HighCloudOpacity;
        public static ConfigEntry<float> HighCloudTiling;
        public static ConfigEntry<float> HighCloudStretch;

        private static ConfigDescription Adv(string desc, AcceptableValueBase range = null)
        {
            return new ConfigDescription(desc, range,
                new ConfigurationManagerAttributes { IsAdvanced = true });
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

            // Performance (always visible)
            PrimarySteps = config.Bind("Performance", "Primary Steps", 64,
                new ConfigDescription("Ray march steps for clouds (higher = better quality, worse performance)", new AcceptableValueRange<int>(1, 64)));
            LightSteps = config.Bind("Performance", "Light Steps", 16,
                new ConfigDescription("Steps for light marching through clouds", new AcceptableValueRange<int>(1, 16)));
            Resolution = config.Bind("Performance", "Cloud Resolution", CloudRenderer.CloudResolution.Half, "Full = best quality, Half = best performance");

            // Bonus steps (advanced)
            BonusPrimarySteps = config.Bind("Performance", "Bonus Primary Steps", 0,
                Adv("Extra primary steps added on top of base (up to 256 total)", new AcceptableValueRange<int>(0, 192)));
            BonusLightSteps = config.Bind("Performance", "Bonus Light Steps", 0,
                Adv("Extra light steps added on top of base (up to 32 total)", new AcceptableValueRange<int>(0, 16)));

            // Shape (advanced)
            DensityMultiplier = config.Bind("Cloud Shape", "Density Multiplier", 15f,
                Adv("Density multiplier for opacity", new AcceptableValueRange<float>(1f, 100f)));
            HeightVariation = config.Bind("Cloud Shape", "Height Variation", 0f,
                Adv("Per-cloud top height variation", new AcceptableValueRange<float>(0f, 5f)));
            NoiseTilingXZ = config.Bind("Cloud Shape", "Noise Tiling XZ", 0.1f,
                Adv("Horizontal noise tiling", new AcceptableValueRange<float>(0.1f, 5f)));
            NoiseTilingY = config.Bind("Cloud Shape", "Noise Tiling Y", 0.33f,
                Adv("Vertical noise tiling", new AcceptableValueRange<float>(0.05f, 2f)));
            DetailTiling = config.Bind("Cloud Shape", "Detail Tiling", 2.5f,
                Adv("3D detail noise tiling", new AcceptableValueRange<float>(1f, 20f)));
            DetailErosion = config.Bind("Cloud Shape", "Detail Erosion Top", 0.75f,
                Adv("Detail erosion on cloud tops", new AcceptableValueRange<float>(0f, 1f)));
            DetailStrength = config.Bind("Cloud Shape", "Worley Erosion", 0f,
                Adv("Worley FBM erosion strength", new AcceptableValueRange<float>(0f, 1f)));
            BottomDetailErosion = config.Bind("Cloud Shape", "Detail Erosion Bottom", 0.35f,
                Adv("Detail erosion on cloud bases", new AcceptableValueRange<float>(0f, 1f)));
            DomeScale = config.Bind("Cloud Shape", "Dome Scale", 50f,
                Adv("Dome mesh scale", new AcceptableValueRange<float>(1f, 1000f)));
            DomePositionY = config.Bind("Cloud Shape", "Dome Position Y", -150f,
                Adv("Dome Y Position", new AcceptableValueRange<float>(-1000f, 1000f)));
            CurlStrength = config.Bind("Cloud Shape", "Curl Strength", 0.03f,
                Adv("Curl noise strength for extra detail", new AcceptableValueRange<float>(0f, 1f)));
            CurlTiling = config.Bind("Cloud Shape", "Curl Tiling", 0.6f,
                Adv("Curl noise tiling", new AcceptableValueRange<float>(0.1f, 5f)));

            // Lighting (advanced)
            Extinction = config.Bind("Cloud Lighting", "Extinction", 10f,
                Adv("Beer-Lambert extinction", new AcceptableValueRange<float>(0.1f, 20f)));
            LightDensityScale = config.Bind("Cloud Lighting", "Light March Density", 0.05f,
                Adv("Density scale for light march", new AcceptableValueRange<float>(0.01f, 1f)));
            ScatterForward = config.Bind("Cloud Lighting", "Forward Scatter", 0.6f,
                Adv("Silver lining strength", new AcceptableValueRange<float>(0f, 0.99f)));
            ScatterBack = config.Bind("Cloud Lighting", "Back Scatter", 0.3f,
                Adv("Back-lit scatter", new AcceptableValueRange<float>(0f, 0.99f)));
            ScatterMix = config.Bind("Cloud Lighting", "Scatter Blend", 0.5f,
                Adv("Forward vs back blend", new AcceptableValueRange<float>(0f, 1f)));
            PowderStrength = config.Bind("Cloud Lighting", "Powder Effect", 0.7f,
                Adv("Dark edge powder effect", new AcceptableValueRange<float>(0f, 3f)));
            AmbientStrength = config.Bind("Cloud Lighting", "Ambient Strength", 0.3f,
                Adv("Fill light in shadows", new AcceptableValueRange<float>(0f, 1f)));
            MultiScatter = config.Bind("Cloud Lighting", "Multi-Scatter", 15f,
                Adv("Fake bounce light", new AcceptableValueRange<float>(0f, 15f)));

            // Horizon (advanced)
            HorizonFade = config.Bind("Cloud Horizon", "Horizon Fade", 0.4f,
                Adv("Horizon fade height", new AcceptableValueRange<float>(0f, 1f)));

            // High Clouds (advanced)
            HighCloudHeight = config.Bind("High Clouds", "Height", 5000f,
                Adv("Altitude of high cloud layer", new AcceptableValueRange<float>(0.5f, 5000f)));
            HighCloudCoverage = config.Bind("High Clouds", "Coverage", 1f,
                Adv("High cloud coverage amount", new AcceptableValueRange<float>(0f, 1f)));
            HighCloudOpacity = config.Bind("High Clouds", "Opacity", 0.08f,
                Adv("High cloud opacity", new AcceptableValueRange<float>(0f, 1f)));
            HighCloudTiling = config.Bind("High Clouds", "Tiling", 0.27f,
                Adv("High cloud noise scale", new AcceptableValueRange<float>(0.1f, 5f)));
            HighCloudStretch = config.Bind("High Clouds", "Stretch", 0.15f,
                Adv("Wispy streak stretch amount (lower = more streaky)", new AcceptableValueRange<float>(0.05f, 1f)));
        }

        public static void ApplyToMaterial(Material mat)
        {
            if (mat == null) return;

            mat.SetInt("_PrimarySteps", PrimarySteps.Value + BonusPrimarySteps.Value);
            mat.SetInt("_LightSteps", LightSteps.Value + BonusLightSteps.Value);
            CloudRenderer.cloudResolution = Resolution.Value;

            mat.SetFloat("_DensityMultiplier", DensityMultiplier.Value);
            mat.SetFloat("_HeightVariation", HeightVariation.Value);
            mat.SetVector("_NoiseTiling", new Vector4(NoiseTilingXZ.Value, NoiseTilingY.Value, NoiseTilingXZ.Value, 0));
            mat.SetFloat("_DetailTiling", DetailTiling.Value);
            mat.SetFloat("_DetailErosion", DetailErosion.Value);
            mat.SetFloat("_DetailStrength", DetailStrength.Value);
            mat.SetFloat("_BottomDetailErosion", BottomDetailErosion.Value);
            mat.SetFloat("_DomeScale", DomeScale.Value);
            mat.SetVector("_DomePosition", new Vector4(0, DomePositionY.Value, 0, 0));
            mat.SetFloat("_CurlStrength", CurlStrength.Value);
            mat.SetFloat("_CurlTiling", CurlTiling.Value);

            mat.SetFloat("_Extinction", Extinction.Value);
            mat.SetFloat("_LightDensityScale", LightDensityScale.Value);
            mat.SetFloat("_ScatterForward", ScatterForward.Value);
            mat.SetFloat("_ScatterBack", ScatterBack.Value);
            mat.SetFloat("_ScatterMix", ScatterMix.Value);
            mat.SetFloat("_PowderStrength", PowderStrength.Value);
            mat.SetFloat("_AmbientStrength", AmbientStrength.Value);
            mat.SetFloat("_MultiScatter", MultiScatter.Value);

            mat.SetFloat("_HorizonFade", HorizonFade.Value);

            mat.SetFloat("_HighCloudHeight", HighCloudHeight.Value);
            mat.SetFloat("_HighCloudCoverage", HighCloudCoverage.Value);
            mat.SetFloat("_HighCloudOpacity", HighCloudOpacity.Value);
            mat.SetFloat("_HighCloudTiling", HighCloudTiling.Value);
            mat.SetFloat("_HighCloudStretch", HighCloudStretch.Value);
        }
    }
}