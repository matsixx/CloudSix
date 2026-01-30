using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace CloudSix.Source
{
    internal class CloudRenderer
    {
        private static Camera lastRegisteredCamera;

        public static CommandBuffer cloudCommandBuffer;
        public static GameObject cloudInstance;
        public static Renderer lowRenderer;
        public static Material lowMaterial;
        public static GameObject cloudPrefab;

        public static void LoadCloudPrefab()
        {
            if (cloudPrefab != null)
                return;

            try
            {
                string bundlePath = Path.Combine(BepInEx.Paths.PluginPath, "CloudSix", "Assets", "customclouds");
                AssetBundle cloudBundle = AssetBundle.LoadFromFile(bundlePath);
                if (cloudBundle == null)
                {
                    Plugin.MyLog.LogError("Failed to load cloud AssetBundle.");
                    return;
                }

                cloudPrefab = cloudBundle.LoadAsset<GameObject>("Clouds New");
                cloudBundle.Unload(false);
                GameObject.DontDestroyOnLoad(cloudPrefab);
                Plugin.MyLog.LogInfo("Cloud prefab loaded successfully.");
            }
            catch (Exception ex)
            {
                Plugin.MyLog.LogError($"Error loading cloud prefab: {ex.Message}");
            }
        }

        public static void InstantiateCloudPrefab()
        {
            if (cloudCommandBuffer != null)
            {
                if (lastRegisteredCamera != null)
                {
                    lastRegisteredCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, cloudCommandBuffer);
                }
                cloudCommandBuffer.Dispose();
                cloudCommandBuffer = null;
            }

            lastRegisteredCamera = null;

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
        }


        public static void InitializeCloudRenderers()
        {
            if (cloudInstance == null)  // Check instance, not prefab
                return;

            Transform lowCloud = cloudInstance.transform.Find("Low");  // Use instance

            if (lowCloud != null)
            {
                lowCloud.gameObject.layer = 28;

                lowRenderer = lowCloud.GetComponent<Renderer>();
                if (lowRenderer != null)
                {
                    lowRenderer.allowOcclusionWhenDynamic = false;
                    lowRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    lowMaterial = lowRenderer.material;  // Use .material not .sharedMaterial to get instance
                }
            }

            if (lowMaterial != null)
            {
                CustomCloudController.cloudOffset = new Vector4(
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f),
                    UnityEngine.Random.Range(0f, 100f)
                );
            }
        }

        public static void SetupCloudCommandBuffer(Camera cam)
        {
            if (cam == null || lowRenderer == null)
                return;

            if (lastRegisteredCamera != null && lastRegisteredCamera != cam && cloudCommandBuffer != null)
            {
                lastRegisteredCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, cloudCommandBuffer);
                cloudCommandBuffer.Dispose();
                cloudCommandBuffer = null;
            }

            if (cloudCommandBuffer != null)
                return;

            lowRenderer.enabled = false;

            cloudCommandBuffer = new CommandBuffer();
            cloudCommandBuffer.name = "Custom Clouds";

            cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, cloudCommandBuffer);
            lastRegisteredCamera = cam;
        }
    }
}
