using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace LegacyMapExpansion
{
    [BepInPlugin("com.bigcitylegacy.map1018", "Legacy Map Expansion", "0.2.1")]
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
                harmony = new Harmony("com.bigcitylegacy.map1018");
                harmony.PatchAll(typeof(Plugin).Assembly);
                Ready = true;
                Logger.LogInfo("Map expansion ready: " + Assets.PrefabCount + " prefab types, " + Assets.GroundCount + " terrain tiles. Native 9.4 actors and decals retained.");
            }
            catch (Exception error)
            {
                Ready = false;
                if (harmony != null) harmony.UnpatchSelf();
                Logger.LogError("Map expansion disabled; original map retained. " + error);
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
        internal static void ReportMapOverride()
        {
            if (Instance && Interlocked.Exchange(ref mapOverrideAnnounced, 1) == 0)
                Instance.Logger.LogInfo("Map data override active: loading the expansion through native MapPro.");
        }
        [Serializable] private sealed class Compatibility { public string baseMapSha256; }
    }
}
