using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LegacyMapExpansion
{
    internal sealed class MapAssets
    {
        private readonly AssetBundle bundle;
        private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        private readonly Dictionary<long, GameObject> grounds = new Dictionary<long, GameObject>();
        private readonly Dictionary<long, string[]> groundMaterialNames = new Dictionary<long, string[]>();
        private readonly NativeGroundMaterials groundMaterials;
        private readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
        private readonly HashSet<int> templateIds = new HashSet<int>();
        private readonly HashSet<long> readyGrounds = new HashSet<long>();
        private readonly MaterialEntry[] materials;
        private bool materialsReady;
        internal int PrefabCount => prefabs.Count;
        internal int GroundCount => grounds.Count;

        internal MapAssets(byte[] bytes)
        {
            bundle = AssetBundle.LoadFromMemory(bytes);
            if (!bundle) throw new InvalidDataException("Cannot load map bundle on " + Application.platform + ". Install the matching platform build.");
            var data = bundle.LoadAsset<TextAsset>("Assets/map-manifest.json");
            if (!data) throw new InvalidDataException("Map bundle manifest is missing");
            var manifest = JsonConvert.DeserializeObject<Manifest>(data.text);
            if (manifest == null || manifest.prefabs == null || manifest.grounds == null || manifest.materials == null)
                throw new InvalidDataException("Map bundle manifest is incomplete: prefabs, grounds and materials are required");
            materials = manifest.materials;
            var excluded = new HashSet<int>();
            var fallback = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var item in materials)
            {
                var material = bundle.LoadAsset<Material>(item.asset);
                if (material) { excluded.Add(material.GetInstanceID()); fallback[material.name] = material; }
            }
            groundMaterials = new NativeGroundMaterials(Plugin.ReadResource("groundmaterials"), excluded, fallback);
            foreach (var item in manifest.prefabs)
            {
                var prefab = bundle.LoadAsset<GameObject>(item.asset);
                if (!prefab || !prefab.GetComponentInChildren<MeshFilter>(true))
                    throw new InvalidDataException("Invalid map prefab " + item.asset);
                prefabs.Add(item.guid, prefab);
            }
            foreach (var item in manifest.grounds)
            {
                var prefab = bundle.LoadAsset<GameObject>(item.asset);
                var filter = prefab ? prefab.GetComponent<MeshFilter>() : null;
                var collider = prefab ? prefab.GetComponent<MeshCollider>() : null;
                if (!prefab || !filter || !filter.sharedMesh || !collider || !collider.sharedMesh)
                    throw new InvalidDataException("Invalid terrain tile " + item.asset);
                grounds.Add(Key(item.x, item.z), prefab);
                var renderer = prefab.GetComponent<Renderer>();
                if (!renderer) throw new InvalidDataException("Terrain renderer is missing: " + item.asset);
                groundMaterialNames.Add(Key(item.x, item.z), renderer.sharedMaterials.Select(material => material ? material.name : null).ToArray());
            }
        }

        internal bool TryPrefab(MapProObjInfo info, out GameObject prefab)
        {
            prefab = null;
            GameObject source;
            if (!prefabs.TryGetValue(info.objGUID, out source)) return false;
            ApplyMaterials();
            if (!templates.TryGetValue(info.objGUID, out prefab) || !prefab)
            {
                source.SetActive(false);
                prefab = Object.Instantiate(source);
                prefab.name = source.name;
                prefab.hideFlags = HideFlags.HideAndDontSave;
                if (Application.isPlaying) Object.DontDestroyOnLoad(prefab);
                NativeMapObjects.Prepare(prefab);
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.sharedMesh || filter.sharedMesh.vertexCount == 0)
                    {
                        var renderer = filter.GetComponent<Renderer>();
                        if (renderer) renderer.enabled = false;
                    }
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
                templateIds.Add(prefab.GetInstanceID());
            }
            return true;
        }

        internal bool TryGround(int x, int z, GameObject original, out GameObject prefab)
        {
            var key = Key(x, z);
            if (!grounds.TryGetValue(key, out prefab)) return false;
            if (readyGrounds.Contains(key) && prefab) return true;
            ApplyMaterials();
            prefab.SetActive(false);
            var prepared = Object.Instantiate(prefab);
            prepared.name = prefab.name;
            if (!groundMaterials.Bind(original, prepared, groundMaterialNames[key]))
            {
                if (Application.isPlaying) Object.Destroy(prepared);
                else Object.DestroyImmediate(prepared);
                prefab = null;
                return false;
            }
            prepared.hideFlags = HideFlags.HideAndDontSave;
            if (Application.isPlaying) Object.DontDestroyOnLoad(prepared);
            grounds[key] = prefab = prepared;
            readyGrounds.Add(key);
            return true;
        }

        internal bool IsTemplate(GameObject prefab) => prefab && templateIds.Contains(prefab.GetInstanceID());

        internal bool IsGround(int x, int z) => grounds.ContainsKey(Key(x, z));

        private void ApplyMaterials()
        {
            if (materialsReady) return;
            var shaders = new Dictionary<string, Shader>();
            foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
                if (shader && shader.isSupported && !shaders.ContainsKey(shader.name)) shaders.Add(shader.name, shader);
            int missing = 0;
            foreach (var item in materials)
            {
                var material = bundle.LoadAsset<Material>(item.asset);
                if (material && material.shader && material.shader.name.StartsWith("LegacyMapExpansion/", StringComparison.Ordinal)) continue;
                Shader shader;
                if (material && shaders.TryGetValue(item.shader, out shader)) material.shader = shader;
                else if (item.shader != "Standard") missing++;
            }
            materialsReady = true;
            if (missing > 0) Plugin.Warn(missing + " materials use the bundled Standard fallback because their original shader is unavailable.");
        }

        private static long Key(int x, int z) => ((long)x << 32) | (uint)z;
        [Serializable] private sealed class Manifest { public PrefabEntry[] prefabs; public GroundEntry[] grounds; public MaterialEntry[] materials; }
        [Serializable] private sealed class PrefabEntry { public string guid, asset; }
        [Serializable] private sealed class GroundEntry { public int x, z; public string asset; }
        [Serializable] private sealed class MaterialEntry { public string asset, shader; }
    }
}
