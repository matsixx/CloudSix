using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace CloudSix.Source
{
    internal class CloudRenderer
    {
        private static Camera lastMainCamera;
        private static Camera lastOpticCamera;

        public static CommandBuffer mainCloudCommandBuffer;
        public static CommandBuffer opticCloudCommandBuffer;
        public static GameObject cloudInstance;
        public static Renderer lowRenderer;
        public static Material lowMaterial;
        public static GameObject cloudPrefab;

        // Half-res rendering
        private static Material compositeMaterial;
        private static Shader compositeShader;
        private static int cloudRT = Shader.PropertyToID("_CloudRT");
        public static bool useHalfRes = true;
        public static CloudResolution cloudResolution = CloudResolution.Half;
        public enum CloudResolution
        {
            Full,
            ThreeQuarter,
            Half
        }

        public static void LoadCloudPrefab()
        {
            if (cloudPrefab != null)
                return;

            try
            {
                string bundlePath = Path.Combine(BepInEx.Paths.PluginPath, "CloudSix", "Assets", "volumetricclouds");
                AssetBundle cloudBundle = AssetBundle.LoadFromFile(bundlePath);
                if (cloudBundle == null)
                {
                    Plugin.MyLog.LogError("Failed to load cloud AssetBundle.");
                    return;
                }

                cloudPrefab = cloudBundle.LoadAsset<GameObject>("Clouds Vol");
                compositeShader = cloudBundle.LoadAsset<Shader>("CloudComposite");
                cloudBundle.Unload(false);
                GameObject.DontDestroyOnLoad(cloudPrefab);
                Plugin.MyLog.LogInfo("Cloud prefab loaded successfully.");
            }
            catch (Exception ex)
            {
                Plugin.MyLog.LogError($"Error loading cloud prefab: {ex.Message}");
            }
        }

        public static void LoadCompositeMaterial()
        {
            if (compositeMaterial != null)
                return;

            if (compositeShader != null)
            {
                compositeMaterial = new Material(compositeShader);
                Plugin.MyLog.LogInfo("Cloud composite material loaded.");
            }
            else
            {
                Plugin.MyLog.LogError("Composite shader not loaded from bundle. Half-res rendering disabled.");
                useHalfRes = false;
            }
        }

        public static void InstantiateCloudPrefab()
        {
            // Clean up main camera buffer
            if (mainCloudCommandBuffer != null)
            {
                if (lastMainCamera != null)
                    lastMainCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, mainCloudCommandBuffer);
                mainCloudCommandBuffer.Dispose();
                mainCloudCommandBuffer = null;
            }
            lastMainCamera = null;

            // Clean up optic camera buffer
            if (opticCloudCommandBuffer != null)
            {
                if (lastOpticCamera != null)
                    lastOpticCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, opticCloudCommandBuffer);
                opticCloudCommandBuffer.Dispose();
                opticCloudCommandBuffer = null;
            }
            lastOpticCamera = null;

            if (cloudInstance != null)
            {
                GameObject.Destroy(cloudInstance);
                cloudInstance = null;
            }

            lowRenderer = null;
            lowMaterial = null;

            if (cloudPrefab != null && cloudInstance == null)
            {
                cloudInstance = GameObject.Instantiate(cloudPrefab);
                cloudInstance.transform.position = new Vector3(0, -70, 0);
                cloudInstance.transform.localScale = new Vector3(10f, 10f, 10f);
                Plugin.MyLog.LogInfo("Cloud prefab instantiated.");
            }

            LoadCompositeMaterial();
        }

        public static void InitializeCloudRenderers()
        {
            if (cloudInstance == null)
                return;

            Transform lowCloud = cloudInstance.transform.Find("Low");

            if (lowCloud != null)
            {
                lowCloud.gameObject.layer = 28;

                lowRenderer = lowCloud.GetComponent<Renderer>();
                if (lowRenderer != null)
                {
                    lowRenderer.allowOcclusionWhenDynamic = false;
                    lowRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                    lowMaterial = lowRenderer.material;
                }
            }

            if (lowMaterial != null)
            {
                CustomCloudController.windOffset = new Vector4(
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f),
                    0f
                );

                CustomCloudController.macroOffset = new Vector3(
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f)
                );
            }
        }

        public static void SetupCloudCommandBuffer(Camera mainCamera, Camera opticCamera)
        {
            if (mainCamera == null || lowRenderer == null)
                return;

            if (lastMainCamera != null && lastMainCamera != mainCamera && mainCloudCommandBuffer != null)
            {
                lastMainCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, mainCloudCommandBuffer);
                mainCloudCommandBuffer.Dispose();
                mainCloudCommandBuffer = null;
            }

            if (mainCloudCommandBuffer == null)
            {
                lowRenderer.enabled = false;
                mainCloudCommandBuffer = new CommandBuffer();
                mainCloudCommandBuffer.name = "Custom Clouds Main";
                mainCamera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, mainCloudCommandBuffer);
                lastMainCamera = mainCamera;
            }

            if (opticCamera != null)
            {
                if (lastOpticCamera != null && lastOpticCamera != opticCamera && opticCloudCommandBuffer != null)
                {
                    lastOpticCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, opticCloudCommandBuffer);
                    opticCloudCommandBuffer.Dispose();
                    opticCloudCommandBuffer = null;
                }

                if (opticCloudCommandBuffer == null)
                {
                    opticCloudCommandBuffer = new CommandBuffer();
                    opticCloudCommandBuffer.name = "Custom Clouds Optic";
                    opticCamera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, opticCloudCommandBuffer);
                    lastOpticCamera = opticCamera;
                }
            }
            else if (opticCloudCommandBuffer != null)
            {
                if (lastOpticCamera != null)
                    lastOpticCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, opticCloudCommandBuffer);
                opticCloudCommandBuffer.Dispose();
                opticCloudCommandBuffer = null;
                lastOpticCamera = null;
            }
        }

        // Populates a command buffer with cloud rendering commands.
        // If useHalfRes is true, renders to a half-res RT then composites back.

        public static void PopulateCommandBuffer(CommandBuffer cmd, Camera cam)
        {
            if (cmd == null || cam == null || lowRenderer == null)
                return;

            cmd.Clear();

            if (cloudResolution != CloudResolution.Full && compositeMaterial != null)
            {
                int divisor;
                switch (cloudResolution)
                {
                    case CloudResolution.ThreeQuarter:
                        divisor = 4;
                        break;
                    case CloudResolution.Half:
                    default:
                        divisor = 2;
                        break;
                }

                int width, height;
                if (cloudResolution == CloudResolution.ThreeQuarter)
                {
                    width = cam.pixelWidth * 3 / 4;
                    height = cam.pixelHeight * 3 / 4;
                }
                else
                {
                    width = cam.pixelWidth / 2;
                    height = cam.pixelHeight / 2;
                }

                cmd.GetTemporaryRT(cloudRT, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
                cmd.SetRenderTarget(cloudRT);
                cmd.ClearRenderTarget(false, true, new Color(0, 0, 0, 0));
                cmd.DrawRenderer(lowRenderer, lowMaterial);

                cmd.Blit(cloudRT, BuiltinRenderTextureType.CameraTarget, compositeMaterial);
                cmd.ReleaseTemporaryRT(cloudRT);
            }
            else
            {
                cmd.DrawRenderer(lowRenderer, lowMaterial);
            }
        }

        public static void CleanupClouds()
        {
            if (mainCloudCommandBuffer != null)
            {
                if (lastMainCamera)
                    lastMainCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, mainCloudCommandBuffer);
                mainCloudCommandBuffer.Dispose();
                mainCloudCommandBuffer = null;
            }
            lastMainCamera = null;

            if (opticCloudCommandBuffer != null)
            {
                if (lastOpticCamera)
                    lastOpticCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, opticCloudCommandBuffer);
                opticCloudCommandBuffer.Dispose();
                opticCloudCommandBuffer = null;
            }
            lastOpticCamera = null;

            if (cloudInstance)
                GameObject.Destroy(cloudInstance);

            cloudInstance = null;
            lowRenderer = null;
            lowMaterial = null;

            if (compositeMaterial != null)
            {
                UnityEngine.Object.Destroy(compositeMaterial);
                compositeMaterial = null;
            }

            CustomCloudController.windOffset = Vector4.zero;
        }
    }
}
