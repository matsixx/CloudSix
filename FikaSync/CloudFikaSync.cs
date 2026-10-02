using System;
using CloudSix.Source;
using Comfort.Common;
using EFT.Weather;
using Fika.Core.Main.Utils;
using Fika.Core.Modding;
using Fika.Core.Modding.Events;
using Fika.Core.Networking;
using Fika.Core.Networking.LiteNetLib;
using UnityEngine;

namespace CloudSixFikaSync
{
    public static class CloudFikaSync
    {
        public static bool enableCloudSync = true;

        public static bool verboseLogging = false;
        // How often the authority re-broadcasts its state (seconds). This is NOT drift correction (clients
        // ignore it once locked) — it's just a beacon so a late joiner or a client whose request reply was
        // lost still gets a state to snap to. Low rate, ~32-byte payload.
        public static float broadcastInterval = 5f;

        private static float _lastBroadcast;
        private static bool _requested;            // client: have we asked the host for state this raid?
        private static bool _hasTarget;            // client: have we snapped to a host state since clouds came up?
        private static bool _headlessSeeded;       // headless: have we seeded the random offsets this raid?
        private static float _headlessWindSpeed;   // headless copy of CustomCloudController.smoothedWindSpeed
        private static bool _firstBroadcastLogged;  // authority: logged the first broadcast this raid?
        private static IFikaNetworkManager _manager;
        // UnityEngine.Random is deterministic on a headless instance (identical sequence -> identical clouds
        // every raid), so seed the cloud field from an independent clock-seeded RNG that advances across
        // raids. The headless owns the seed and broadcasts it, so it only needs to VARY, not match anything.
        private static readonly System.Random _rng = new System.Random();

        public static void Init()
        {
            FikaEventDispatcher.SubscribeEvent<FikaNetworkManagerCreatedEvent>(OnNetworkManagerCreated);
        }

        // Fires once per raid (when the FIKA net manager is built). Register the receive side on each end
        // — failing to register a received packet throws endlessly in FIKA. The server receives requests
        // (and answers); clients receive state (and apply). The other direction is a defensive no-op.
        private static void OnNetworkManagerCreated(FikaNetworkManagerCreatedEvent ev)
        {
            _manager = ev.Manager;
            _requested = false;
            _hasTarget = false;
            _lastBroadcast = 0f;
            _headlessSeeded = false;
            _headlessWindSpeed = 0f;
            _firstBroadcastLogged = false;
            CloudFikaSyncPlugin.Log.LogInfo(
                $"[CloudSix-FikaSync] raid net manager up — role: {(ev.Manager is FikaServer ? (FikaBackendUtils.IsHeadless ? "HEADLESS host (authority, weather-driven)" : "host (authority)") : "client")}");

            if (ev.Manager is FikaServer server)
            {
                server.RegisterPacket<CloudStateRequestPacket, NetPeer>(OnRequestServer);
                server.RegisterPacket<CloudStatePacket, NetPeer>(OnStateServerIgnore);
            }
            else if (ev.Manager is FikaClient client)
            {
                client.RegisterPacket<CloudStatePacket>(OnStateReceived);
                client.RegisterPacket<CloudStateRequestPacket>(OnRequestClientIgnore);
            }
        }

        public static void Tick()
        {
            if (!enableCloudSync || _manager == null)
                return;

            if (FikaBackendUtils.IsServer)
            {
                if (FikaBackendUtils.IsHeadless)
                {
                    if (!AdvanceHeadless())
                        return; // weather not up yet
                }
                else if (VolCloudRenderer.lowRenderer == null)
                {
                    return;
                }

                if (Time.time - _lastBroadcast >= broadcastInterval)
                {
                    _lastBroadcast = Time.time;
                    CloudStatePacket p = CurrentState();
                    Singleton<FikaServer>.Instance?.SendData(ref p, DeliveryMethod.Unreliable, broadcast: true);
                    if (!_firstBroadcastLogged || verboseLogging)
                    {
                        _firstBroadcastLogged = true;
                        CloudFikaSyncPlugin.Log.LogInfo(
                            $"[CloudSix-FikaSync] broadcasting cloud state to clients — windDir={p.WindDir} windOffset={p.WindOffset} macroOffset={p.MacroOffset} macroEvolution={p.MacroEvolution} height={p.BottomHeight}-{p.TopHeight}");
                    }
                }
            }
            else
            {
                if (VolCloudRenderer.lowRenderer == null)
                    return;
                if (!_requested)
                {
                    _requested = true;
                    _hasTarget = false;
                    CloudStateRequestPacket req = default;
                    Singleton<FikaClient>.Instance?.SendData(ref req, DeliveryMethod.ReliableOrdered, broadcast: true);
                    CloudFikaSyncPlugin.Log.LogInfo("[CloudSix-FikaSync] clouds up — requested current state from host");
                }
            }
        }

        private static bool AdvanceHeadless()
        {
            WeatherController wc = WeatherController.Instance;
            if (wc == null)
                return false;
            Vector2 wind = wc.WeatherCurve.Wind;

            if (!_headlessSeeded)
            {
                float R() => (float)(_rng.NextDouble() * 100.0);   // clock-seeded -> varies per raid
                CustomCloudController.windOffset = new Vector3(R(), R(), R());
                CustomCloudController.macroOffset = new Vector3(R(), R(), R());
                double a = _rng.NextDouble() * Math.PI * 2.0;
                CustomCloudController.lastWindDirection = new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
                CustomCloudController.RandomizeHeights((lo, hi) => (float)(_rng.NextDouble() * (hi - lo) + lo));
                _headlessSeeded = true;
            }

            float dt = Time.deltaTime;

            // mirror UpdateWind
            const float WIND_DAMPENING = 2.0f;
            float targetSpeed = Mathf.Lerp(CloudConfig.WindSpeedMin.Value, CloudConfig.WindSpeedMax.Value,
                Mathf.InverseLerp(0f, 0.5f, wind.magnitude));
            _headlessWindSpeed = Mathf.Lerp(_headlessWindSpeed, targetSpeed, WIND_DAMPENING * dt);

            // mirror the offset accumulation in UpdateMaterial
            const float windSpeedMult = 1.2f;
            const float macroWindFactor = 0.2f;
            Vector2 dir = CustomCloudController.lastWindDirection;
            Vector3 wo = CustomCloudController.windOffset;
            wo.x += dir.x * _headlessWindSpeed * windSpeedMult * dt;
            wo.z += dir.y * _headlessWindSpeed * windSpeedMult * dt;
            wo.y += _headlessWindSpeed * dt * 0.00005f;
            CustomCloudController.windOffset = wo;
            CustomCloudController.macroOffset += new Vector3(
                dir.x * _headlessWindSpeed, 0f, dir.y * _headlessWindSpeed) * macroWindFactor * dt;

            // mirror the macro-field evolution (CustomCloudController.UpdateMaterial). Rate read straight
            // from the source field so the two can't drift out of sync.
            CustomCloudController.macroEvolution += CustomCloudController.macroEvolveRate * dt;
            return true;
        }

        private static bool HasAuthoritativeState()
        {
            return FikaBackendUtils.IsHeadless ? _headlessSeeded : VolCloudRenderer.lowRenderer != null;
        }

        private static CloudStatePacket CurrentState()
        {
            CloudStatePacket p = default;
            p.WindOffset = CustomCloudController.windOffset;
            p.MacroOffset = CustomCloudController.macroOffset;
            p.WindDir = CustomCloudController.lastWindDirection;
            p.MacroEvolution = CustomCloudController.macroEvolution;
            p.BottomHeight = CustomCloudController.cloudBottomHeight;
            p.TopHeight = CustomCloudController.cloudTopHeight;
            return p;
        }

        // ---- host side ----
        private static void OnRequestServer(CloudStateRequestPacket _, NetPeer peer)
        {
            try
            {
                if (!HasAuthoritativeState())
                    return; // not ready yet; the heartbeat will catch this peer
                CloudStatePacket p = CurrentState();
                // Reply straight to the asker (server-only API) instead of broadcasting a reply to everyone.
                Singleton<FikaServer>.Instance?.SendDataToPeer(ref p, DeliveryMethod.ReliableOrdered, peer);
            }
            catch (Exception e) { LogOnce(ref _err, "state request", e); }
        }

        private static void OnStateServerIgnore(CloudStatePacket _, NetPeer __) { } // server is the authority; clients never send state

        // ---- client side ----
        private static void OnStateReceived(CloudStatePacket p)
        {
            try
            {
                // Apply exactly ONCE per cloud-init, then free-run. Snapping every heartbeat is what made
                // the clouds shift; ignoring subsequent ones keeps them steady.
                if (_hasTarget)
                {
                    if (verboseLogging)
                        CloudFikaSyncPlugin.Log.LogInfo(
                            $"[CloudSix-FikaSync] ignoring heartbeat (already synced) — drift windOffset={p.WindOffset - CustomCloudController.windOffset}");
                    return;
                }

                CustomCloudController.windOffset = p.WindOffset;
                CustomCloudController.macroOffset = p.MacroOffset;
                CustomCloudController.lastWindDirection = p.WindDir;
                CustomCloudController.macroEvolution = p.MacroEvolution;
                CustomCloudController.cloudBottomHeight = p.BottomHeight;
                CustomCloudController.cloudTopHeight = p.TopHeight;
                _hasTarget = true;
                CloudFikaSyncPlugin.Log.LogInfo(
                    $"[CloudSix-FikaSync] locked to host cloud state — windDir={p.WindDir} windOffset={p.WindOffset}");
            }
            catch (Exception e) { LogOnce(ref _err, "state recv", e); }
        }

        private static void OnRequestClientIgnore(CloudStateRequestPacket _) { } // the host always answers; clients never receive requests

        private static bool _err;
        private static void LogOnce(ref bool latch, string what, Exception e)
        {
            if (latch)
                return;
            latch = true;
            CloudFikaSyncPlugin.Log.LogError($"[CloudSix-FikaSync] {what} error: {e}");
        }
    }
}
