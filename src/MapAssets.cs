using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegacyMapExpansion
{
    internal sealed class MapAssets : IDisposable
    {
        private readonly Stream bundleStream;
        private AssetBundle bundle;
        private readonly Dictionary<string, PrefabEntry> prefabs = new Dictionary<string, PrefabEntry>();
        private readonly Dictionary<long, GroundEntry> grounds = new Dictionary<long, GroundEntry>();
        private readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
        private readonly Dictionary<long, GameObject> groundTemplates = new Dictionary<long, GameObject>();
        private readonly Dictionary<string, MaterialEntry[]> materialShaders = new Dictionary<string, MaterialEntry[]>(StringComparer.Ordinal);
        private readonly HashSet<int> restoredMaterials = new HashSet<int>();
        private Dictionary<string, Shader> shaders;
        private readonly NativeGroundMaterials groundMaterials;
        internal bool Headless { get; }
        internal int PrefabCount => prefabs.Count;
        internal int GroundCount => grounds.Count;

        internal MapAssets(Stream stream, bool headless)
        {
            bundleStream = stream ?? throw new ArgumentNullException(nameof(stream));
            Headless = headless;
            try
            {
                // LZ4 chunks are read from the embedded resource on demand. Unity owns
                // the stream position; keep it open until the AssetBundle is unloaded.
                bundle = AssetBundle.LoadFromStream(bundleStream);
                if (!bundle) throw new InvalidDataException("Cannot load map bundle on " + Application.platform + ". Install the matching platform build.");
                var data = bundle.LoadAsset<TextAsset>(headless ? "Assets/LegacyMapExpansionServerGenerated/map-manifest.json" : "Assets/map-manifest.json");
                if (!data) throw new InvalidDataException("Map bundle manifest is missing");
                var manifest = JsonConvert.DeserializeObject<Manifest>(data.text);
                if (manifest == null || manifest.prefabs == null || manifest.grounds == null || !headless && manifest.materials == null)
                    throw new InvalidDataException("Map bundle manifest is incomplete");
                var paths = new HashSet<string>(bundle.GetAllAssetNames(), StringComparer.OrdinalIgnoreCase);
                foreach (var item in manifest.prefabs)
                {
                    if (!paths.Contains(item.asset)) throw new InvalidDataException("Missing map prefab " + item.asset);
                    prefabs.Add(item.guid, item);
                }
                foreach (var item in manifest.grounds)
                {
                    if (!paths.Contains(item.asset) || headless && (item.surfaceNames == null || item.surfaceNames.Length == 0))
                        throw new InvalidDataException("Invalid terrain index " + item.asset);
                    grounds.Add(Key(item.x, item.z), item);
                }
                if (!headless)
                    foreach (var group in manifest.materials.GroupBy(item => Path.GetFileNameWithoutExtension(item.asset), StringComparer.Ordinal))
                        materialShaders.Add(group.Key, group.Select(item => item.shader).Distinct().Count() == 1 ? new[] { group.First() } : group.ToArray());
                groundMaterials = new NativeGroundMaterials(Plugin.ReadResource("groundmaterials"), !headless);
            }
            catch { Dispose(); throw; }
        }

        internal bool TryPrefab(MapProObjInfo info, out GameObject prefab)
        {
            prefab = null;
            PrefabEntry item;
            if (!prefabs.TryGetValue(info.objGUID, out item)) return false;
            if (!Headless && templates.TryGetValue(info.objGUID, out prefab) && prefab) return true;
            var source = bundle.LoadAsset<GameObject>(item.asset);
            if (!source) return false;
            source.SetActive(false);
            prefab = Object.Instantiate(source);
            prefab.name = item.name ?? source.name;
            prefab.hideFlags = HideFlags.HideAndDontSave;
            NativeMapObjects.Prepare(prefab, !Headless);
            if (Headless) return true; // MapPro owns and releases this one server template.

            if (Application.isPlaying) Object.DontDestroyOnLoad(prefab);
            ApplyMaterials(prefab);
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh && filter.sharedMesh.vertexCount != 0) continue;
                var renderer = filter.GetComponent<Renderer>();
                if (renderer) renderer.enabled = false;
            }
            if (!NetManager.isServer)
            {
                var distance = prefab.AddComponent<Distance>();
                distance.distances = new float[6];
                for (int i = 0; i < 6; i++) distance.distances[i] = info.dists[i];
                distance.lod_0 = prefab.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.enabled).ToArray();
                distance.imposterType = Distance.ImposterType.NotUse;
            }
            templates[info.objGUID] = prefab;
            return true;
        }

        internal bool TryGround(int x, int z, GameObject original, out GameObject prefab)
        {
            var key = Key(x, z);
            if (groundTemplates.TryGetValue(key, out prefab) && prefab) return true;
            GroundEntry item;
            if (!grounds.TryGetValue(key, out item)) return false;
            var source = bundle.LoadAsset<GameObject>(item.asset);
            if (!source || !source.GetComponent<MeshFilter>() || !source.GetComponent<MeshFilter>().sharedMesh) return false;
            source.SetActive(false);
            var prepared = Object.Instantiate(source);
            prepared.name = source.name;
            if (!Headless) ApplyMaterials(prepared);
            if (!groundMaterials.Bind(original, prepared, item.surfaceNames))
            {
                DestroyTemplate(prepared);
                return false;
            }
            prepared.hideFlags = HideFlags.HideAndDontSave;
            if (Application.isPlaying) Object.DontDestroyOnLoad(prepared);
            groundTemplates[key] = prefab = prepared;
            return true;
        }

        internal bool IsTemplate(MapProObjInfo info, GameObject prefab)
        {
            GameObject stored;
            return prefab && templates.TryGetValue(info.objGUID, out stored) && stored == prefab;
        }

        internal bool OwnsGround(GroundItem item)
        {
            GameObject prefab;
            if (!item.Instanciated || !groundTemplates.TryGetValue(Key(item.x, item.z), out prefab) || !prefab) return false;
            var filter = item.Instanciated.GetComponent<MeshFilter>();
            return filter && filter.sharedMesh == prefab.GetComponent<MeshFilter>().sharedMesh;
        }

        private void ApplyMaterials(GameObject prefab)
        {
            if (shaders == null)
            {
                var needed = new HashSet<string>(materialShaders.Values.SelectMany(entries => entries).Select(item => item.shader), StringComparer.Ordinal);
                shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
                foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
                    if (shader && shader.isSupported && needed.Contains(shader.name) && !shaders.ContainsKey(shader.name)) shaders.Add(shader.name, shader);
            }
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!material || !restoredMaterials.Add(material.GetInstanceID()) ||
                        material.shader && material.shader.name.StartsWith("LegacyMapExpansion/", StringComparison.Ordinal)) continue;
                    MaterialEntry[] entries;
                    Shader shader;
                    if (!materialShaders.TryGetValue(material.name, out entries)) continue;
                    // Different imported locations can use the same material name.
                    // Compare bundle asset identity only for names with conflicting shaders.
                    var entry = entries.Length == 1 ? entries[0] : entries.FirstOrDefault(item => bundle.LoadAsset<Material>(item.asset) == material);
                    if (entry == null) continue;
                    string original = entry.shader;
                    if (shaders.TryGetValue(original, out shader)) material.shader = shader;
                    else if (original != "Standard") Plugin.Warn("Native shader unavailable: " + original + "; retaining the bundled material " + material.name + ".");
                }
        }

        private static void DestroyTemplate(GameObject template)
        {
            if (!template) return;
            if (Application.isPlaying) Object.Destroy(template);
            else Object.DestroyImmediate(template);
        }

        public void Dispose()
        {
            foreach (var template in templates.Values) DestroyTemplate(template);
            foreach (var template in groundTemplates.Values) DestroyTemplate(template);
            templates.Clear(); groundTemplates.Clear();
            try { if (bundle) bundle.Unload(true); }
            finally { bundle = null; bundleStream.Dispose(); }
        }

        private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
        [Serializable] private sealed class Manifest { public PrefabEntry[] prefabs; public GroundEntry[] grounds; public MaterialEntry[] materials; }
        [Serializable] private sealed class PrefabEntry { public string guid, asset, name; }
        [Serializable] private sealed class GroundEntry { public int x, z; public string asset; public string[] surfaceNames; }
        [Serializable] private sealed class MaterialEntry { public string asset, shader; }
    }
}
