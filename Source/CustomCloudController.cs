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
        private static Vector2 lastWindDirection = new Vector2(1f, 0f);
        private static float macroWindFactor = 0.05f;
        private const float MAX_MOON_INTENSITY = 0.05f;

        public static void UpdateWind(Vector2 windVector)
        {
            const float WIND_DAMPENING = 2.0f;
            const float MIN_SPEED = 0.0008f;
            const float MAX_SPEED = 0.002f;

            float magnitude = windVector.magnitude;
            float targetSpeed = Mathf.Lerp(MIN_SPEED, MAX_SPEED, Mathf.InverseLerp(0f, 0.5f, magnitude));
            smoothedWindSpeed = Mathf.Lerp(smoothedWindSpeed, targetSpeed, WIND_DAMPENING * Time.deltaTime);

            if (windVector.sqrMagnitude > 0.0001f)
                lastWindDirection = windVector.normalized;
        }

        public static void UpdateMaterial(Material cloudMaterial, float timeOfDay)
        {
            if (cloudMaterial == null)
                return;
            var todSky = MonoBehaviourSingleton<TOD_Sky>.Instance;

            windOffset.x += lastWindDirection.x * smoothedWindSpeed * Time.deltaTime;
            windOffset.z += lastWindDirection.y * smoothedWindSpeed * Time.deltaTime;
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

            // Day/night blend: 0 = full day, 1 = full night
            float nightBlend = Mathf.Clamp01(Mathf.InverseLerp(0.05f, -0.15f, sunHeight));
            nightBlend = nightBlend * nightBlend;

            cloudMaterial.SetColor("_SunColor", todSky.SunSkyColor);
            cloudMaterial.SetColor("_MoonColor", todSky.MoonLightColor);

            Color dayAmbient = todSky.SunSkyColor;
            Color nightAmbient = todSky.MoonSkyColor;
            Color ambient = Color.Lerp(dayAmbient, nightAmbient, nightBlend);
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
            float sunT = Mathf.Clamp01(Mathf.InverseLerp(-0.2f, 0.05f, sunHeight));
            sunT = sunT * sunT;
            sunIntensity = sunT * 3f;

            // Moon: fades in as sun drops
            float moonT = Mathf.Clamp01(Mathf.InverseLerp(0.0f, -0.15f, sunHeight));
            moonT = moonT * moonT;
            moonIntensity = moonT * 2f;
        }
    }
}