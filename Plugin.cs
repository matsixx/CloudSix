using BepInEx;
using BepInEx.Logging;
using CloudSix.Patches;
using CloudSix.Source;

namespace CloudSix
{
    [BepInPlugin("com.matsix.cloudsix", "CloudSix", "3.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource MyLog;

        private void Awake()
        {
            MyLog = Logger;
            MyLog.LogInfo("CloudSix loaded!");

            CloudConfig.Bind(Config);
            new DisableClouds().Enable();
            new DynamicClouds().Enable();
            new SpawnClouds().Enable();
            new DisableEyeAdaptation().Enable();
            new CloudShadowsDisable().Enable();
            new CloudShadowsAllocate().Enable();
            new CloudShadowsCookieSize().Enable();
            new CloudShadowsRelease().Enable();
        }
    }
}
