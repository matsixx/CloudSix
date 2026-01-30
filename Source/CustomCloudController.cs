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
            const float MIN_WIND = 0.00025f;
            const float MAX_WIND = 0.002f;
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
            // zw can be used for detail layer offset if needed

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

        public static Color CalculateCloudColor(Color sunColor, Color moonColor, float timeOfDay, out float upperBrightness)
        {
            Color sourceColor;
            float desaturateAmount;
            float brightnessMultiplier;

            if (timeOfDay >= 4.3f && timeOfDay <= 8f)
            {
                // Dawn
                float t = Mathf.InverseLerp(4.3f, 8f, timeOfDay);
                sourceColor = Color.Lerp(moonColor, sunColor, t);
                desaturateAmount = Mathf.Lerp(0.7f, 0.6f, t);
                brightnessMultiplier = Mathf.Lerp(0.2f, 0.85f, t);
                // Upper clouds get light earlier - brighter at start of dawn
                upperBrightness = Mathf.Lerp(0.4f, 1.1f, t);
            }
            else if (timeOfDay > 8f && timeOfDay <= 19f)
            {
                // Day
                sourceColor = sunColor;
                desaturateAmount = 0.6f;
                brightnessMultiplier = 0.9f;
                upperBrightness = 1.1f;
            }
            else if (timeOfDay > 19f && timeOfDay <= 22.3f)
            {
                // Dusk
                float t = Mathf.InverseLerp(19f, 22.3f, timeOfDay);
                sourceColor = Color.Lerp(sunColor, moonColor, t);
                desaturateAmount = Mathf.Lerp(0.6f, 0.7f, t);
                brightnessMultiplier = Mathf.Lerp(0.85f, 0.2f, t);
                // Upper clouds hold light longer - brighter at the end of dusk
                upperBrightness = Mathf.Lerp(1.1f, 0.5f, t);
            }
            else
            {
                // Night
                sourceColor = moonColor;
                desaturateAmount = 0.7f;
                brightnessMultiplier = 0.25f;
                upperBrightness = 0.35f;
            }

            Color desaturated = Color.Lerp(sourceColor, Color.white, desaturateAmount);

            return desaturated * brightnessMultiplier;
        }

        public static void UpdateCloudMaterial(float density, float upperDensity, Color sunColor, Color moonColor, Vector3 sunDir, Vector3 moonDir, float sunIntensity, float moonIntensity, Color cloudColor, float upperBrightness)
        {
            if (CloudRenderer.lowMaterial == null)
                return;
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
        }

        public static void CalculateLightingParameters(float timeOfDay, out Color sunColor, out Color moonColor, out Vector3 sunDir, out Vector3 moonDir, out float sunIntensity, out float moonIntensity)
        {
            var todSky = MonoBehaviourSingleton<TOD_Sky>.Instance;
            Color rawSunColor = todSky.SunSkyColor;
            Color rawMoonColor = todSky.MoonLightColor;
            sunDir = todSky.LocalSunDirection;
            moonDir = todSky.LocalMoonDirection;

            // Desaturate sun color more at dawn/dusk
            float sunDesaturate;
            if (timeOfDay >= 4.3f && timeOfDay <= 7.0f)
            {
                // Early dawn
                float t = Mathf.InverseLerp(4.3f, 7.0f, timeOfDay);
                sunDesaturate = Mathf.Lerp(0.20f, 0.1f, t);
            }
            else if (timeOfDay > 17.0f && timeOfDay <= 19.8f)
            {
                // Dusk
                float t = Mathf.InverseLerp(17.0f, 19.8f, timeOfDay);
                sunDesaturate = Mathf.Lerp(0.1f, 0.20f, t);
            }
            else
            {
                // Midday
                sunDesaturate = 0.1f;
            }

            sunColor = Color.Lerp(rawSunColor, Color.white, sunDesaturate);

            // Slightly desaturate moon color
            moonColor = Color.Lerp(rawMoonColor, Color.white, 0.10f);

            if (timeOfDay >= 4.3f && timeOfDay <= 6.5f)
            {
                // Dawn
                float t = Mathf.InverseLerp(4.3f, 6.5f, timeOfDay);
                sunIntensity = t;
                moonIntensity = 1.0f - t;
            }
            else if (timeOfDay > 6.5f && timeOfDay <= 18.0f)
            {
                // Day
                sunIntensity = 1.0f;
                moonIntensity = 0.0f;
            }
            else if (timeOfDay > 18.0f && timeOfDay <= 19.8f)
            {
                // Dusk
                float t = Mathf.InverseLerp(18.0f, 19.8f, timeOfDay);
                sunIntensity = 1.0f - t;
                moonIntensity = t;
            }
            else
            {
                // Night
                sunIntensity = 0.0f;
                moonIntensity = 1.0f;
            }
        }
    }
}
