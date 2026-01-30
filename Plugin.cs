using BepInEx;
using BepInEx.Logging;
using CloudSix.Patches;

namespace CloudSix
{
    [BepInPlugin("com.matsix.cloudsix", "CloudSix", "1.0.4")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource MyLog;

        private void Awake()
        {
            MyLog = Logger;
            MyLog.LogInfo("CloudSix loaded!");
          
            new DisableClouds().Enable();
            new DisableClouds().Enable();
            new DynamicClouds().Enable();
            new SpawnClouds().Enable();
        }
    }
}
