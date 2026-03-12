using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CloudSix.Source
{
    internal class CustomCloudController
    {

        public static float lastWind = 0.0f;
        public static Vector2 cloudOffset = Vector4.zero;
        public static Vector2 lastWindDirection = new Vector2(1f, 0f);

        public static void UpdateWindSystem(Vector2 windVector)
        {
            const float WIND_DAMPENING = 0.02f;
            const float MIN_WIND = 0.001f;
            const float MAX_WIND = 0.006f;
            const float MIN_WIND_MAGNITUDE = 0.0f;
            const float MAX_WIND_MAGNITUDE = 0.5f;

            float windMagnitude = windVector.magnitude;
            float mappedSpeed = Mathf.Lerp(MIN_WIND, MAX_WIND, Mathf.InverseLerp(MIN_WIND_MAGNITUDE, MAX_WIND_MAGNITUDE, windMagnitude));
            lastWind = Mathf.Lerp(lastWind, mappedSpeed, WIND_DAMPENING * Time.deltaTime);

            if (windVector.sqrMagnitude > 0.0001f)
                lastWindDirection = windVector.normalized;
        }

        public static void UpdateCloudOffsets()
        {
            cloudOffset.x += lastWindDirection.x * lastWind * Time.deltaTime;
            cloudOffset.y += lastWindDirection.y * lastWind * Time.deltaTime;

            if (CloudRenderer.lowMaterial != null)
                CloudRenderer.lowMaterial.SetVector("_Offset", cloudOffset);
        }

        public static Color ApplyRainEffect(Color sunColor, float rain)
        {
            if (rain <= 0.2f)
                return sunColor;

            float rainT = Mathf.InverseLerp(0.2f, 1.0f, rain);

            // Dims the sun color
            sunColor *= Mathf.Lerp(1.0f, 0.7f, rainT);

            // Desaturate toward gray
            float gray = (sunColor.r + sunColor.g + sunColor.b) / 3f;
            return Color.Lerp(sunColor, new Color(gray, gray, gray, sunColor.a), rainT * 0.5f);
        }

        private static readonly Color nightCloudColor = new Color(0.22f, 0.24f, 0.28f).linear;

        public static Color CalculateCloudColor(Color sunColor, Color moonColor, float timeOfDay, out float upperBrightness)
        {
            Color sourceColor;
            float desaturateAmount;
            float brightnessMultiplier;
            upperBrightness = 0.96f;
            if (timeOfDay >= 4.3f && timeOfDay <= 8f)
            {
                float t = Mathf.InverseLerp(4.3f, 8f, timeOfDay);
                t = t * t;  // Ease-in: stays on nightCloudColor longer, accelerates toward sun at end
                sourceColor = Color.Lerp(nightCloudColor, sunColor, t);
                desaturateAmount = Mathf.Lerp(0.7f, 0.6f, t);
                brightnessMultiplier = Mathf.Lerp(0.1f, 0.7f, t);
            }
            else if (timeOfDay > 8f && timeOfDay <= 19f)
            {
                sourceColor = sunColor;
                desaturateAmount = 0.6f;
                brightnessMultiplier = 0.7f;
            }
            else if (timeOfDay > 19f && timeOfDay <= 22f)
            {
                float t = Mathf.InverseLerp(19f, 22f, timeOfDay);
                t = t * t;  // Ease-in: stays on sunColor longer, darkens faster at end
                sourceColor = Color.Lerp(sunColor, nightCloudColor, t);
                desaturateAmount = Mathf.Lerp(0.6f, 0.7f, t);
                brightnessMultiplier = Mathf.Lerp(0.7f, 0.1f, t);
            }
            else
            {
                return nightCloudColor;
            }

            // Desaturate: lerp toward white
            Color desaturated = Color.Lerp(sourceColor, Color.white, desaturateAmount);
            // Then darken
            return desaturated * brightnessMultiplier;
        }

        public static void UpdateCloudMaterial(float density, float upperDensity, Color sunColor, Color moonColor, Vector3 sunDir, Vector3 moonDir, float sunIntensity, float moonIntensity, Color cloudColor, float upperBrightness)
        {
            if (CloudRenderer.lowMaterial == null)
                return;
            cloudColor.a = 1.0f;
            CloudRenderer.lowMaterial.SetFloat("_Density", density);
            CloudRenderer.lowMaterial.SetFloat("_UpperDensity", upperDensity);
            CloudRenderer.lowMaterial.SetColor("_SunColor", sunColor);
            CloudRenderer.lowMaterial.SetColor("_MoonColor", moonColor);
            CloudRenderer.lowMaterial.SetVector("_SunDirection", sunDir);
            CloudRenderer.lowMaterial.SetVector("_MoonDirection", moonDir);
            CloudRenderer.lowMaterial.SetFloat("_SunIntensity", sunIntensity);
            CloudRenderer.lowMaterial.SetFloat("_MoonIntensity", moonIntensity);
            CloudRenderer.lowMaterial.SetColor("_CloudColor", cloudColor);
            CloudRenderer.lowMaterial.SetFloat("_UpperBrightness", upperBrightness);
            CloudRenderer.lowMaterial.SetFloat("_EdgeSoftness", 0.31f);
            CloudRenderer.lowMaterial.SetFloat("_ShadowStrength", 0.3f);
            CloudRenderer.lowMaterial.SetFloat("_SubsurfaceIntensity", 0.7f);
            CloudRenderer.lowMaterial.SetFloat("_UpperScale", 1.8f);
            CloudRenderer.lowMaterial.SetFloat("_UpperAlpha", 0.6f);
            CloudRenderer.lowMaterial.SetFloat("_UpperSpeedMult", 0.02f);
            CloudRenderer.lowMaterial.SetFloat("_RimIntensity", 0.1f);
            CloudRenderer.lowMaterial.SetFloat("_RimPower", 2f);
            CloudRenderer.lowMaterial.SetFloat("_SunFalloff", 1f);
            CloudRenderer.lowMaterial.SetFloat("_DomeScale", 5f);
        }

        private static readonly Color baseMoonColor = new Color(0.722f, 0.753f, 0.812f).linear;

        private const float DESATURATION_MULTIPLIER = 0.3f;
        private const float MAX_MOON_INTENSITY = 0.4f;

        public static void CalculateLightingParameters(float timeOfDay, out Color sunColor, out Color moonColor, out Vector3 sunDir, out Vector3 moonDir, out float sunIntensity, out float moonIntensity)
        {
            var todSky = MonoBehaviourSingleton<TOD_Sky>.Instance;
            Color rawSunColor = todSky.SunSkyColor;
            moonColor = baseMoonColor;
            sunDir = todSky.LocalSunDirection;
            moonDir = todSky.LocalMoonDirection;
            float sunDesaturate;
            if (timeOfDay >= 4.3f && timeOfDay <= 7.0f)
            {
                float t = Mathf.InverseLerp(4.3f, 7.0f, timeOfDay);
                sunDesaturate = Mathf.Lerp(0.20f, 0.1f, t);
            }
            else if (timeOfDay > 20.0f && timeOfDay <= 23.0f)
            {
                float t = Mathf.InverseLerp(20.0f, 23.0f, timeOfDay);
                sunDesaturate = Mathf.Lerp(0.1f, 0.20f, t);
            }
            else
            {
                sunDesaturate = 0.1f;
            }

            sunDesaturate *= DESATURATION_MULTIPLIER;
            sunColor = Color.Lerp(rawSunColor, Color.white, Mathf.Clamp01(sunDesaturate));
            if (timeOfDay >= 4.3f && timeOfDay <= 6.5f)
            {
                // Dawn: sun stays low longer, ramps up at end
                float t = Mathf.InverseLerp(4.3f, 6.5f, timeOfDay);
                t = t * t;
                sunIntensity = t;
                moonIntensity = 1.0f - t;
            }
            else if (timeOfDay > 6.5f && timeOfDay <= 20f)
            {
                sunIntensity = 1.0f;
                moonIntensity = 0.0f;
            }
            else if (timeOfDay > 20f && timeOfDay <= 23.5f)
            {
                float t = Mathf.InverseLerp(20f, 23.5f, timeOfDay);
                sunIntensity = 1.0f - t;
                moonIntensity = 0.0f;
            }
            else if (timeOfDay > 23.5f)
            {
                // Late night: moon slowly ramps up, stays low longer
                float t = Mathf.InverseLerp(23.5f, 24f, timeOfDay);
                t = t * t;
                sunIntensity = 0.0f;
                moonIntensity = t;
            }
            else
            {
                // Early morning before dawn (0-4.3)
                sunIntensity = 0.0f;
                moonIntensity = 1.0f;
            }

            // Cap moon intensity
            moonIntensity = Mathf.Min(moonIntensity, MAX_MOON_INTENSITY);
        }
    }
}
