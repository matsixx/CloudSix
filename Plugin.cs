using BepInEx;
using BepInEx.Logging;
using CloudSix.Patches;
using CloudSix.Source;

namespace CloudSix
{
    [BepInPlugin("com.matsix.cloudsix", "CloudSix", "4.0.4")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource MyLog;

        // Load confirmations and one-time diagnostics (UnityExplorer, live); errors and what the player asks for go out always.
        public static bool debugLog = false;
        internal static void LogDetail(string message)
        {
            if (debugLog) MyLog.LogInfo(message);
        }

        private void Awake()
        {
            MyLog = Logger;
            MyLog.LogInfo("CloudSix loaded!");

            CloudConfig.Bind(Config);
            new DisableClouds().Enable();
            new DisableTodSky().Enable();
            new DynamicClouds().Enable();
            new SpawnClouds().Enable();
            new DisableEyeAdaptation().Enable();
            new EyeAdaptationMeter().Enable();
            new CloudShadowsDisable().Enable();
            new CloudShadowsAllocate().Enable();
            new CloudShadowsCookieSize().Enable();
            new CloudShadowsRelease().Enable();
        }
    }
}
