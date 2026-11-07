using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BigCityLegacy.Api;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace LegacyMapExpansion
{
    [BepInPlugin("com.bigcitylegacy.map1018", "Legacy Map Expansion", "1.0.1")]
    [BepInDependency("com.bigcitylegacy.baseplugin", "1.4.2")]
    [LegacyPluginActivation(RequiresRestart = true)]
    public sealed class Plugin : BaseUnityPlugin, ILegacyPluginActivation
    {
        internal static Plugin Instance;
        internal static MapAssets Assets;
        internal static volatile bool Ready;
        internal static ConfigEntry<bool> Enabled;
        private static int mapOverrideAnnounced;
        private Harmony harmony;

        // Report the map actually used by this process, not the next-start setting.
        public bool IsEnabled => Ready;
        public void Enable() { SetEnabled(true); }
        public void Disable() { SetEnabled(false); }

        internal static void SetEnabled(bool value)
        {
            bool previous = Enabled.Value;
            try { Enabled.Value = value; Enabled.ConfigFile.Save(); }
            catch { Enabled.Value = previous; throw; }
        }

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "Enable the map expansion on the next game start.");
            MapSettings.Install();
            if (!Enabled.Value) return;
            try
            {
                var compatibility = JsonConvert.DeserializeObject<Compatibility>(ReadText("compatibility"));
                if (compatibility == null || string.IsNullOrEmpty(compatibility.baseMapSha256))
                    throw new InvalidDataException("Map compatibility manifest is incomplete");
                var mapPath = Path.Combine(Application.streamingAssetsPath, "Map.bytes");
                string hash;
                using (var file = File.OpenRead(mapPath))
                using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                if (hash != compatibility.baseMapSha256 && hash != compatibility.baseMapSha256Linux)
                    throw new InvalidDataException("Unsupported base map. Expected MadOut2 PC 9.4 map " + compatibility.baseMapSha256 + " (Windows) or " + compatibility.baseMapSha256Linux + " (Linux), got " + hash);
                using (var map = OpenResource("map"))
                    if (map.Length < 32) throw new InvalidDataException("Map resource is empty");
                bool headless = LegacyServerHeadlessBootstrap.IsOptimizationEnabled;
                Assets = new MapAssets(OpenResource(headless ? "serverbundle" : "bundle"), headless);
                harmony = new Harmony("com.bigcitylegacy.map1018");
                harmony.PatchAll(typeof(Plugin).Assembly);
                Ready = true;
                Logger.LogInfo("Map expansion ready (" + (Assets.Headless ? "headless" : "client") + "): " + Assets.PrefabCount + " prefab types, " + Assets.GroundCount + " terrain tiles. Native 9.4 actors and decals retained.");
            }
            catch (Exception error)
            {
                Ready = false;
                if (harmony != null) harmony.UnpatchSelf();
                Assets?.Dispose(); Assets = null;
                Logger.LogError("Map expansion disabled; original map retained. " + error);
            }
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Ready = false;
            harmony?.UnpatchSelf();
            Assets?.Dispose(); Assets = null;
            Instance = null;
        }

        internal static Stream OpenResource(string name)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LegacyMapExpansion." + name);
            if (stream == null) throw new FileNotFoundException("Missing map resource: " + name);
            return stream;
        }

        internal static byte[] ReadResource(string name)
        {
            using (var stream = OpenResource(name))
            using (var reader = new BinaryReader(stream))
            {
                int length = checked((int)stream.Length);
                byte[] bytes = reader.ReadBytes(length);
                if (bytes.Length != length) throw new EndOfStreamException("Incomplete map resource: " + name);
                return bytes;
            }
        }

        private static string ReadText(string name) => System.Text.Encoding.UTF8.GetString(ReadResource(name));
        internal static void Warn(string text) { if (Instance) Instance.Logger.LogWarning(text); else Debug.LogWarning(text); }
        internal static void ReportMapOverride()
        {
            if (Instance && Interlocked.Exchange(ref mapOverrideAnnounced, 1) == 0)
                Instance.Logger.LogInfo("Map data override active: loading the expansion through native MapPro.");
        }
        [Serializable] private sealed class Compatibility { public string baseMapSha256, baseMapSha256Linux; }
    }
}
