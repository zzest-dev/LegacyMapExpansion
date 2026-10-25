using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace LegacyMapExpansion
{
    [BepInPlugin("com.bigcitylegacy.map1018", "Legacy Map Expansion", "1.0.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static MapAssets Assets;
        internal static byte[] MapBytes;
        internal static volatile bool Ready;
        private static int mapOverrideAnnounced;
        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            if (!Config.Bind("General", "Enabled", true, "Enable the map expansion on the next game start.").Value) return;
            try
            {
                float timeout = Config.Bind("Network", "HandshakeTimeoutSeconds", 8f,
                    new ConfigDescription("Time allowed to confirm the map before the server disconnects a client.", new AcceptableValueRange<float>(3f, 30f))).Value;
                float heartbeat = Config.Bind("Network", "HeartbeatIntervalSeconds", 30f,
                    new ConfigDescription("Interval between map-plugin presence checks on an established connection.", new AcceptableValueRange<float>(15f, 120f))).Value;
                MapNetworkAdmission.Install(timeout, heartbeat);
                var compatibility = JsonConvert.DeserializeObject<Compatibility>(ReadText("compatibility"));
                if (compatibility == null || string.IsNullOrEmpty(compatibility.baseMapSha256))
                    throw new InvalidDataException("Map compatibility manifest is incomplete");
                var mapPath = Path.Combine(Application.streamingAssetsPath, "Map.bytes");
                string hash;
                using (var file = File.OpenRead(mapPath))
                using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                if (hash != compatibility.baseMapSha256)
                    throw new InvalidDataException("Unsupported base map. Expected MadOut2 PC 9.4 map " + compatibility.baseMapSha256 + ", got " + hash);
                MapBytes = ReadResource("map");
                if (MapBytes.Length < 32) throw new InvalidDataException("Map resource is empty");
                Assets = new MapAssets(ReadResource("bundle"));
                MapNetworkAdmission.Identity = MapNetworkProtocol.Identity(MapBytes, ReadResource("compatibility"), ReadResource("groundmaterials"), ReadResource("networkmanifest"));
                harmony = new Harmony("com.bigcitylegacy.map1018");
                harmony.PatchAll(typeof(Plugin).Assembly);
                Ready = true;
                Logger.LogInfo("Map expansion ready: " + Assets.PrefabCount + " prefab types, " + Assets.GroundCount + " terrain tiles. Native 9.4 actors and decals retained.");
                Logger.LogInfo("Map admission enabled: protocol " + MapNetworkProtocol.Version + ", identity " + MapNetworkAdmission.Identity + ". Clients must confirm this map before Ready/AddPlayer.");
            }
            catch (Exception error)
            {
                Ready = false;
                if (harmony != null) harmony.UnpatchSelf();
                Logger.LogError("Map expansion disabled; original map retained. " + error);
                if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg.Equals("-bend_GameServer", StringComparison.OrdinalIgnoreCase)))
                {
                    Logger.LogError("Server launch aborted because the required map or map admission could not initialize.");
                    Application.Quit(1);
                }
            }
        }

        internal static byte[] ReadResource(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LegacyMapExpansion." + name))
            {
                if (stream == null) throw new FileNotFoundException("Missing map resource: " + name);
                using (var output = new MemoryStream()) { stream.CopyTo(output); return output.ToArray(); }
            }
        }

        private static string ReadText(string name) => System.Text.Encoding.UTF8.GetString(ReadResource(name));
        internal static void Warn(string text) { if (Instance) Instance.Logger.LogWarning(text); else Debug.LogWarning(text); }
        internal static void LogInfo(string text) { if (Instance) Instance.Logger.LogInfo(text); }
        private void Update() { if (MapNetworkAdmission.Installed) MapNetworkAdmission.Update(); }
        private void OnGUI()
        {
            if (!MapNetworkAdmission.ShowFailure) return;
            float width = Mathf.Min(560, Screen.width - 24);
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2, Mathf.Max(12, (Screen.height - 220) / 2), width, 220), GUI.skin.box);
            GUILayout.Label("Legacy Map Expansion");
            GUILayout.Label(MapNetworkAdmission.FailureText);
            if (GUILayout.Button("OK")) MapNetworkAdmission.ShowFailure = false;
            GUILayout.EndArea();
        }
        internal static void ReportMapOverride()
        {
            if (Instance && Interlocked.Exchange(ref mapOverrideAnnounced, 1) == 0)
                Instance.Logger.LogInfo("Map data override active: loading the expansion through native MapPro.");
        }
        [Serializable] private sealed class Compatibility { public string baseMapSha256; }
    }
}
