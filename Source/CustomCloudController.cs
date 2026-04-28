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
        private static Vector2 lastWindDirection = RandomDirection();
        private static float macroWindFactor = 0.08f;
        private const float MAX_MOON_INTENSITY = 0.05f;
        public static Gradient cachedTopAmbient;

        private static Vector2 RandomDirection()
        {
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        public static void UpdateWind(Vector2 windVector)
        {
            const float WIND_DAMPENING = 2.0f;
            const float MIN_SPEED = 0.0004f;
            const float MAX_SPEED = 0.001f;

            float magnitude = windVector.magnitude;
            float targetSpeed = Mathf.Lerp(MIN_SPEED, MAX_SPEED, Mathf.InverseLerp(0f, 0.5f, magnitude));
            smoothedWindSpeed = Mathf.Lerp(smoothedWindSpeed, targetSpeed, WIND_DAMPENING * Time.deltaTime);

            //if (windVector.sqrMagnitude > 0.0001f)
                //lastWindDirection = windVector.normalized;
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
                float normalizedCloudiness = (weather.WeatherCurve.Cloudiness + 1f) * 0.5f;

                float boostStart = 0.3f;
                float boostEnd = 0.7f;
                float boostT = Mathf.Clamp01(Mathf.InverseLerp(boostStart, boostEnd, normalizedCloudiness));

                // Gate by sun height — no boost at night, full boost at noon
                float dayMask = Mathf.Clamp01(sunHeight);
                boostT *= dayMask;

                Color overcastTint = new Color(0.75f, 0.78f, 0.82f);
                float desaturation = boostT * 0.5f;
                float brightness = 1.0f + boostT * 0.3f;

                ambient = Color.Lerp(ambient, overcastTint, desaturation);
                ambient *= brightness;
                ambient.a = 1f;
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
            moonIntensity = moonT * 1.5f;
        }
    }
}