using BepInEx;
using BepInEx.Logging;

namespace CloudSixFikaSync
{
    [BepInPlugin("com.matsix.cloudsix.fikasync", "CloudSix FIKA Sync", "1.0.0")]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.matsix.cloudsix", BepInDependency.DependencyFlags.HardDependency)]
    public class CloudFikaSyncPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            CloudFikaSync.Init();
            Log.LogInfo("CloudSix FIKA Sync loaded.");
        }

        private bool _tickErr;
        private void Update()
        {
            try { CloudFikaSync.Tick(); }
            catch (System.Exception e)
            {
                if (!_tickErr) { _tickErr = true; Log.LogError($"[CloudSix-FikaSync] tick error: {e}"); }
            }
        }
    }
}
