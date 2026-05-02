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
    internal static class CloudConfig
    {
        // Bonus
        public static ConfigEntry<bool> EyeAdaptation;

        // Wind
        public static ConfigEntry<float> WindSpeedMin;
        public static ConfigEntry<float> WindSpeedMax;

        // Performance
        public static ConfigEntry<int> PrimarySteps;
        public static ConfigEntry<int> LightSteps;
        public static ConfigEntry<CloudRenderer.CloudResolution> Resolution;

        // Advanced Performance
        public static ConfigEntry<int> BonusPrimarySteps;
        public static ConfigEntry<int> BonusLightSteps;

        // Shape
        public static ConfigEntry<float> DensityMultiplier;
        public static ConfigEntry<float> NoiseTilingXZ;
        public static ConfigEntry<float> NoiseTilingY;
        public static ConfigEntry<float> DetailTiling;
        public static ConfigEntry<float> DetailErosion;
        public static ConfigEntry<float> BaseInversion;
        public static ConfigEntry<float> CloudOffset;
        public static ConfigEntry<float> CurlTiling;
        public static ConfigEntry<float> CurlStrength;
        public static ConfigEntry<float> BaseWispiness;
        public static ConfigEntry<float> DensitySharpness;
        public static ConfigEntry<float> CloudDensity;
        public static ConfigEntry<float> WorldScale;

        // Lighting
        public static ConfigEntry<float> Extinction;
        public static ConfigEntry<float> LightDensityScale;
        public static ConfigEntry<float> ScatterForward;
        public static ConfigEntry<float> ScatterBack;
        public static ConfigEntry<float> ScatterMix;
        public static ConfigEntry<float> AmbientStrength;
        public static ConfigEntry<float> MultiScatter;
        public static ConfigEntry<float> SunIntensity;

        // Horizon
        public static ConfigEntry<float> HorizonFade;
        public static ConfigEntry<float> HorizonVanish;

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

            EyeAdaptation = config.Bind("Bonus", "Disable Eye Adaptation", true, "Disables eye adaptation if set to true (requires reset of raid or client)");
            DisableEyeAdaptation.IsEnabled = EyeAdaptation.Value;

            WindSpeedMin = config.Bind("Wind", "Min Wind Speed", 0.001f,
                Adv("Minimum cloud wind speed", new AcceptableValueRange<float>(0.0001f, 0.01f)));
            WindSpeedMax = config.Bind("Wind", "Max Wind Speed", 0.002f,
                Adv("Maximum cloud wind speed", new AcceptableValueRange<float>(0.0001f, 0.01f)));

            // Performance (always visible)
            PrimarySteps = config.Bind("Performance", "Primary Steps", 64,
                new ConfigDescription("Ray march steps for clouds (higher = better quality, worse performance)", new AcceptableValueRange<int>(1, 64)));
            LightSteps = config.Bind("Performance", "Light Steps", 6,
                new ConfigDescription("Steps for light marching through clouds", new AcceptableValueRange<int>(1, 6)));
            Resolution = config.Bind("Performance", "Cloud Resolution", CloudRenderer.CloudResolution.Half, "Full = best quality, Half = best performance");

            // Bonus steps (advanced)
            BonusPrimarySteps = config.Bind("Performance", "Bonus Primary Steps", 0,
                Adv("Extra primary steps added on top of base", new AcceptableValueRange<int>(0, 192)));
            BonusLightSteps = config.Bind("Performance", "Bonus Light Steps", 0,
                Adv("Extra light steps added on top of base", new AcceptableValueRange<int>(0, 16)));

            // Shape (advanced)
            DensityMultiplier = config.Bind("Cloud Shape", "Density Multiplier", 1f,
                Adv("Density multiplier for opacity", new AcceptableValueRange<float>(0.1f, 100f)));
            NoiseTilingXZ = config.Bind("Cloud Shape", "Noise Tiling XZ", 1.8f,
                Adv("Horizontal noise tiling", new AcceptableValueRange<float>(0.01f, 5f)));
            NoiseTilingY = config.Bind("Cloud Shape", "Noise Tiling Y", 1.8f,
                Adv("Vertical noise tiling", new AcceptableValueRange<float>(0.01f, 2f)));
            DetailTiling = config.Bind("Cloud Shape", "Detail Tiling", 28f,
                Adv("3D detail noise tiling", new AcceptableValueRange<float>(1f, 40f)));
            DetailErosion = config.Bind("Cloud Shape", "Detail Erosion", 0.17f,
                Adv("Fine detail erosion strength", new AcceptableValueRange<float>(0f, 1f)));
            BaseInversion = config.Bind("Cloud Shape", "Base Inversion", 0.3f,
                Adv("0 = no inversion, 1 = inverted worley on bottom of cloud", new AcceptableValueRange<float>(0f, 1f)));
            CurlStrength = config.Bind("Cloud Shape", "Curl Strength", 0.20f,
                Adv("Curl noise strength for turbulent edges", new AcceptableValueRange<float>(0f, 1f)));
            CurlTiling = config.Bind("Cloud Shape", "Curl Tiling", 0.5f,
                Adv("Curl noise tiling", new AcceptableValueRange<float>(0.1f, 5f)));
            BaseWispiness = config.Bind("Cloud Shape", "Base Wispiness", 0.35f,
                Adv("Wispiness at cloud base (0 = wispy, 1 = solid)", new AcceptableValueRange<float>(0f, 1f)));
            DensitySharpness = config.Bind("Cloud Shape", "Density Softness", 0.4f,
                Adv("Softness of density falloff (higher = softer edges, lower = sharper clouds)", new AcceptableValueRange<float>(0.1f, 5f)));
            WorldScale = config.Bind("Cloud Shape", "World Scale", 0.0001f,
                Adv("Scale of the clouds", new AcceptableValueRange<float>(0.0001f, 0.005f)));
            //CloudDensity = config.Bind("Cloud Shape", "Cloud Density", 1f,
            //  Adv("Overall cloud coverage", new AcceptableValueRange<float>(0.1f, 3f)));

            // Lighting (advanced)
            Extinction = config.Bind("Cloud Lighting", "Extinction", 50f,
                Adv("Beer-Lambert extinction", new AcceptableValueRange<float>(0.001f, 100f)));
            LightDensityScale = config.Bind("Cloud Lighting", "Light March Density", 1f,
                Adv("Density scale for light march", new AcceptableValueRange<float>(0.01f, 20f)));
            ScatterForward = config.Bind("Cloud Lighting", "Forward Scatter", 0.90f,
                Adv("Silver lining strength", new AcceptableValueRange<float>(0f, 0.99f)));
            ScatterBack = config.Bind("Cloud Lighting", "Back Scatter", 0.3f,
                Adv("Back-lit scatter", new AcceptableValueRange<float>(0f, 0.99f)));
            ScatterMix = config.Bind("Cloud Lighting", "Scatter Blend", 0.75f,
                Adv("Forward vs back blend", new AcceptableValueRange<float>(0f, 1f)));
            AmbientStrength = config.Bind("Cloud Lighting", "Ambient Strength", 0.50f,
                Adv("Fill light in shadows", new AcceptableValueRange<float>(0f, 1f)));
            MultiScatter = config.Bind("Cloud Lighting", "Multi-Scatter", 30f,
                Adv("Fake inner bounce light", new AcceptableValueRange<float>(0f, 100f)));
            //SunIntensity = config.Bind("Cloud Lighting", "Sun Intensity", 1f,
             //   Adv("Intensity of sun when viewed through clouds", new AcceptableValueRange<float>(0f, 5f)));

            // Horizon (advanced)
            HorizonFade = config.Bind("Cloud Horizon", "Horizon Color Fade", 0.25f,
                Adv("Horizon color fade height", new AcceptableValueRange<float>(0f, 1f)));
            HorizonVanish = config.Bind("Cloud Horizon", "Horizon Alpha", 0.10f,
                Adv("Horizon alpha height (where clouds disappear into horizon)", new AcceptableValueRange<float>(0f, 1f)));

            // High Clouds (advanced)
            HighCloudHeight = config.Bind("High Clouds", "Height", 5000f,
                Adv("Altitude of high cloud layer", new AcceptableValueRange<float>(0.5f, 5000f)));
            //HighCloudCoverage = config.Bind("High Clouds", "Coverage", 0.8f,
            //    Adv("High cloud coverage amount", new AcceptableValueRange<float>(0f, 1f)));
            HighCloudOpacity = config.Bind("High Clouds", "Opacity", 1f,
                Adv("High cloud opacity", new AcceptableValueRange<float>(0f, 1f)));
            HighCloudTiling = config.Bind("High Clouds", "Tiling", 0.35f,
                Adv("High cloud noise scale", new AcceptableValueRange<float>(0.1f, 5f)));
            HighCloudStretch = config.Bind("High Clouds", "Stretch", 0.52f,
                Adv("Wispy streak stretch amount (lower = more streaky)", new AcceptableValueRange<float>(0.05f, 1f)));
        }

        public static void ApplyToMaterial(Material mat)
        {
            if (mat == null) return;

            mat.SetInt("_PrimarySteps", PrimarySteps.Value + BonusPrimarySteps.Value);
            mat.SetInt("_LightSteps", LightSteps.Value + BonusLightSteps.Value);
            CloudRenderer.cloudResolution = Resolution.Value;

            mat.SetFloat("_DensityMultiplier", DensityMultiplier.Value);
            mat.SetVector("_NoiseTiling", new Vector4(NoiseTilingXZ.Value, NoiseTilingY.Value, NoiseTilingXZ.Value, 0));
            mat.SetFloat("_DetailTiling", DetailTiling.Value);
            mat.SetFloat("_DetailErosion", DetailErosion.Value);
            mat.SetFloat("_BaseInversion", BaseInversion.Value);
            mat.SetFloat("_CurlStrength", CurlStrength.Value);
            mat.SetFloat("_CurlTiling", CurlTiling.Value);
            mat.SetFloat("_BaseWispiness", BaseWispiness.Value);
            mat.SetFloat("_DensitySharpness", DensitySharpness.Value);
            mat.SetFloat("_WorldScale", WorldScale.Value);
            //mat.SetFloat("_CloudDensity", CloudDensity.Value);

            mat.SetFloat("_Extinction", Extinction.Value);
            mat.SetFloat("_LightDensityScale", LightDensityScale.Value);
            mat.SetFloat("_ScatterForward", ScatterForward.Value);
            mat.SetFloat("_ScatterBack", ScatterBack.Value);
            mat.SetFloat("_ScatterMix", ScatterMix.Value);
            mat.SetFloat("_AmbientStrength", AmbientStrength.Value);
            mat.SetFloat("_MultiScatter", MultiScatter.Value);
            //mat.SetFloat("_SunIntensity", SunIntensity.Value);

            mat.SetFloat("_HorizonFade", HorizonFade.Value);
            mat.SetFloat("_HorizonVanish", HorizonVanish.Value);

            mat.SetFloat("_HighCloudHeight", HighCloudHeight.Value);
            mat.SetFloat("_HighCloudOpacity", HighCloudOpacity.Value);
            mat.SetFloat("_HighCloudTiling", HighCloudTiling.Value);
            mat.SetFloat("_HighCloudStretch", HighCloudStretch.Value);
        }
    }
}