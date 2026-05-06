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
        private static float smoothedWindSpeed = 0.0f;
        public static Vector2 lastWindDirection = Vector2.zero;
        private static float macroWindFactor = 0.2f;
        private const float MAX_MOON_INTENSITY = 0.05f;
        public static Gradient cachedTopAmbient;
        public static float DesaturationStrength = 1.3f;
        public static float NightAmbientMultiplier = 0.4f;

        public static void UpdateWind(Vector2 windVector)
        {
            const float WIND_DAMPENING = 2.0f;
            float minSpeed = CloudConfig.WindSpeedMin.Value;
            float maxSpeed = CloudConfig.WindSpeedMax.Value;
            float magnitude = windVector.magnitude;
            float targetSpeed = Mathf.Lerp(minSpeed, maxSpeed, Mathf.InverseLerp(0f, 0.5f, magnitude));
            smoothedWindSpeed = Mathf.Lerp(smoothedWindSpeed, targetSpeed, WIND_DAMPENING * Time.deltaTime);
        }

        public static void UpdateMaterial(Material cloudMaterial, float timeOfDay)
        {
            if (cloudMaterial == null)
                return;
            var todSky = MonoBehaviourSingleton<TOD_Sky>.Instance;

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

            cloudMaterial.SetVector("_SunDirection", todSky.LocalSunDirection);
            cloudMaterial.SetVector("_MoonDirection", todSky.LocalMoonDirection);

            float sunHeight = todSky.LocalSunDirection.y;
            CalculateIntensities(todSky, out float sunIntensity, out float moonIntensity);
            cloudMaterial.SetFloat("_SunIntensity", sunIntensity);
            cloudMaterial.SetFloat("_MoonIntensity", moonIntensity);

            cloudMaterial.SetColor("_SunColor", todSky.SunSkyColor);
            cloudMaterial.SetColor("_MoonColor", todSky.MoonLightColor);

            if (cachedTopAmbient == null && WeatherController.Instance?.TimeOfDayController != null)
            {
                cachedTopAmbient = WeatherController.Instance.TimeOfDayController.AddTopAmbient;
            }

            float t01 = sunHeight * 0.5f + 0.5f;
            Color ambient = cachedTopAmbient.Evaluate(t01);
            var weather = WeatherController.Instance;
            if (weather != null)
            {
                float rawCloud = weather.WeatherCurve.Cloudiness;
                float normCloud = (rawCloud + 1f) * 0.5f;
                float desat = Mathf.Clamp01(normCloud * DesaturationStrength);

                float lum = ambient.r * 0.2126f + ambient.g * 0.7152f + ambient.b * 0.0722f;
                ambient.r = Mathf.Lerp(ambient.r, lum, desat);
                ambient.g = Mathf.Lerp(ambient.g, lum, desat);
                ambient.b = Mathf.Lerp(ambient.b, lum, desat);
                ambient.a = 1f;

                // Dim ambient as sun drops below horizon. 1.0 at/above horizon, NightAmbientMultiplier at -0.3 and below.
                float nightT = Mathf.InverseLerp(-0.3f, 0.0f, sunHeight);
                float nightFactor = Mathf.Lerp(NightAmbientMultiplier, 1.0f, nightT);
                ambient.r *= nightFactor;
                ambient.g *= nightFactor;
                ambient.b *= nightFactor;
            }

            cloudMaterial.SetColor("_AmbientColor", ambient);

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
            sunIntensity = sunT * 1.5f;

            // Moon: fades in as sun drops
            float moonT = Mathf.Clamp01(Mathf.InverseLerp(0.0f, -0.15f, sunHeight));
            moonT = moonT * moonT;
            moonIntensity = moonT * 1f;
        }
    }
}