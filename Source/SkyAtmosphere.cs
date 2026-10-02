using UnityEngine;
using UnityEngine.Rendering;

namespace CloudSix.Source
{
    // Physically-based sky (Hillaire 2020) that replaces Tarkov's banded TOD atmosphere dome. Bakes three
    // float LUTs each frame via Graphics.Blit (no camera/CB) and draws the sky on the cloud dome, BEHIND the
    // clouds, per-eye correct
    internal static class SkyAtmosphere
    {
        public static Shader shader;
        public static Material material;

        private static RenderTexture transLut;   // 256x64  transmittance (rebaked only on composition change)
        private static RenderTexture msLut;       // 32x32   multiple scattering (composition change)
        private static RenderTexture skyViewLut;  // 200x100 latlong sky (every frame)

        private const int PassTransmittance = 0;
        private const int PassMultiScatter = 1;
        private const int PassSkyView = 2;
        public const int PassDisplay = 3;

        private static float _lutSig = float.NaN;   // composition signature; rebake T+MS when it changes

        public static bool Enabled => CloudConfig.CustomSky != null && CloudConfig.CustomSky.Value;
        public static bool Ready => material != null && skyViewLut != null;

        // The per-frame sky-view LUT (raw linear in-scatter radiance, world-azimuth latlong). Exposed so the
        // cloud shader can sample it directly for atmosphere-driven cloud ambient (see CustomCloudController).
        public static Texture SkyViewLut => skyViewLut;
        public static Texture TransLut => transLut;

        public static void SetShader(Shader s)
        {
            if (s == null) return;
            shader = s;
        }

        private static bool EnsureMaterial()
        {
            if (material != null) return true;
            if (shader == null) return false;
            material = new Material(shader);
            return true;
        }

        private static void EnsureLuts()
        {
            if (transLut == null)
            {
                transLut = MakeLut(256, 64, TextureWrapMode.Clamp);
                _lutSig = float.NaN;   // force a rebake against the fresh (undefined) RT
            }
            if (msLut == null)
            {
                msLut = MakeLut(32, 32, TextureWrapMode.Clamp);
                _lutSig = float.NaN;
            }
            if (skyViewLut == null)
            {
                skyViewLut = new RenderTexture(200, 100, 0, RenderTextureFormat.ARGBHalf)
                { filterMode = FilterMode.Bilinear, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
                skyViewLut.Create();
            }
        }

        private static RenderTexture MakeLut(int w, int h, TextureWrapMode wrap)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf)
            { filterMode = FilterMode.Bilinear, wrapMode = wrap };
            rt.Create();
            return rt;
        }

        // Runs once per frame from DynamicClouds.Postfix (LateUpdate), before the command buffers are
        // populated. cam is the FPS camera; the LUTs are view-direction independent so one bake serves both
        // eyes and the optic scope.
        public static void Bake(Camera cam, TOD_Sky todSky)
        {
            if (!Enabled || cam == null || todSky == null) return;
            if (!EnsureMaterial()) return;
            EnsureLuts();

            ApplyConfig();

            // Sun/moon in WORLD space (the sky is world-oriented, up = +Y). Moon fades in as the sun sets.
            Vector3 sunDir = todSky.SunDirection;
            Vector3 moonDir = todSky.MoonDirection;
            material.SetVector("_AtmSunDir", sunDir);
            material.SetVector("_AtmMoonDir", moonDir);

            float night = Mathf.Clamp01((-sunDir.y + 0.02f) / 0.14f);
            float moonIll = (CloudConfig.AtmMoonBrightness != null ? CloudConfig.AtmMoonBrightness.Value : 0.3f) * night;
            material.SetFloat("_AtmMoonIll", moonIll);

            // 1 m floor: at exactly 0 the bake's ground test misses and downward rays integrate through the
            // planet (NaN rows in the LUT's lower half).
            material.SetFloat("_AtmCamAltKm", Mathf.Clamp(cam.transform.position.y * 0.001f, 0.001f, 60f));

            // Dome vert needs to match the cloud dome exactly so directions line up (both centered on the cam).
            if (VolCloudRenderer.lowMaterial != null)
            {
                material.SetFloat("_DomeScale", VolCloudRenderer.lowMaterial.GetFloat("_DomeScale"));
                material.SetVector("_DomePosition", VolCloudRenderer.lowMaterial.GetVector("_DomePosition"));
            }

            // Stars ride TOD's sidereal star rotation so they wheel with the night.
            Matrix4x4 starRot = Matrix4x4.identity;
            if (todSky.Components != null && todSky.Components.SpaceTransform != null)
                starRot = todSky.Components.SpaceTransform.worldToLocalMatrix;
            material.SetMatrix("_StarRotation", starRot);

            material.SetFloat("_AtmOn", 1f);

            // Transmittance + multiple-scattering depend only on atmosphere COMPOSITION (not the sun angle —
            // the MS LUT covers all sun angles), so rebake them only when a composition knob / weather haze
            // actually moves. Sky-view depends on the sun/moon direction and is rebaked every frame.
            float sig = CompositionSignature();
            if (sig != _lutSig)
            {
                _lutSig = sig;
                material.SetTexture("_AtmTransLut", null);
                Graphics.Blit(null, transLut, material, PassTransmittance);
                material.SetTexture("_AtmTransLut", transLut);
                Graphics.Blit(null, msLut, material, PassMultiScatter);
                material.SetTexture("_AtmMsLut", msLut);
            }
            else
            {
                material.SetTexture("_AtmTransLut", transLut);
                material.SetTexture("_AtmMsLut", msLut);
            }

            Graphics.Blit(null, skyViewLut, material, PassSkyView);
            material.SetTexture("_AtmSkyView", skyViewLut);
        }

        // Draw the sky on the cloud dome renderer (per-eye correct via the eye's matrices). Recorded FIRST in
        // the command buffer so the clouds composite on top of it.
        public static void DrawSky(CommandBuffer cmd, Renderer domeRenderer)
        {
            if (!Enabled || cmd == null || domeRenderer == null || material == null) return;
            cmd.DrawRenderer(domeRenderer, material, 0, PassDisplay);
        }

        private static void ApplyConfig()
        {
            // Weather couples into Mie (haze): overcast skies read hazier/greyer, clear reads deep blue.
            float haze = 1f;
            if (CloudConfig.AtmWeatherHaze != null)
                haze = 1f + CloudConfig.AtmWeatherHaze.Value * Mathf.Clamp01(CustomCloudController.smoothedNormalizedCloudiness);

            material.SetFloat("_AtmRayleigh", Cfg(CloudConfig.AtmRayleigh, 1f));
            material.SetFloat("_AtmMie", Cfg(CloudConfig.AtmMie, 1f) * haze);
            material.SetFloat("_AtmOzone", Cfg(CloudConfig.AtmOzone, 1f));
            material.SetFloat("_AtmMieG", Cfg(CloudConfig.AtmMieG, 0.8f));
            material.SetFloat("_GroundAlbedo", Cfg(CloudConfig.AtmGroundAlbedo, 0.3f));
            material.SetFloat("_AtmExposure", Cfg(CloudConfig.AtmExposure, 40f));
            material.SetFloat("_AtmDither", (CloudConfig.AtmDither == null || CloudConfig.AtmDither.Value) ? 1f : 0f);

            material.SetFloat("_StarsOn", (CloudConfig.Stars == null || CloudConfig.Stars.Value) ? 1f : 0f);
            material.SetFloat("_StarBrightness", Cfg(CloudConfig.StarBrightness, 1f));
            material.SetFloat("_StarDensity", Cfg(CloudConfig.StarDensity, 200f));
            material.SetFloat("_StarProb", Cfg(CloudConfig.StarProbability, 0.35f));
            material.SetFloat("_StarTwinkle", Cfg(CloudConfig.StarTwinkle, 0.5f));
            material.SetFloat("_MilkyWay", Cfg(CloudConfig.MilkyWay, 0.15f));
            material.SetFloat("_AtmDebug", CloudConfig.AtmDebug != null ? (float)(int)CloudConfig.AtmDebug.Value : 0f);
        }

        private static float Cfg(BepInEx.Configuration.ConfigEntry<float> e, float fallback)
        {
            return e != null ? e.Value : fallback;
        }

        // Only the composition knobs (not the sun) feed this; rounded so tiny weather drift doesn't rebake.
        private static float CompositionSignature()
        {
            float haze = 1f;
            if (CloudConfig.AtmWeatherHaze != null)
                haze = 1f + CloudConfig.AtmWeatherHaze.Value * Mathf.Clamp01(CustomCloudController.smoothedNormalizedCloudiness);
            float s = 0f;
            s += Mathf.Round(Cfg(CloudConfig.AtmRayleigh, 1f) * 100f);
            s += Mathf.Round(Cfg(CloudConfig.AtmMie, 1f) * haze * 100f) * 7f;
            s += Mathf.Round(Cfg(CloudConfig.AtmOzone, 1f) * 100f) * 31f;
            s += Mathf.Round(Cfg(CloudConfig.AtmGroundAlbedo, 0.3f) * 100f) * 131f;
            return s;
        }

        public static void Cleanup()
        {
            Release(ref transLut);
            Release(ref msLut);
            Release(ref skyViewLut);
            if (material != null) { Object.Destroy(material); material = null; }
            _lutSig = float.NaN;
        }

        private static void Release(ref RenderTexture rt)
        {
            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
        }
    }
}
