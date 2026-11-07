using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace LegacyMapExpansion
{
    internal sealed class NativeGroundMaterials
    {
        private readonly Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>(StringComparer.Ordinal);
        private readonly Dictionary<Material, Material> fallbackMaterials = new Dictionary<Material, Material>();
        private readonly bool visuals;
        private readonly Func<string, GameObject> load;
        private readonly Dictionary<string, Surface> surfaces = new Dictionary<string, Surface>(StringComparer.Ordinal);
        private readonly Dictionary<string, PhysicMaterial> physics = new Dictionary<string, PhysicMaterial>(StringComparer.Ordinal);
        private readonly HashSet<int> cached = new HashSet<int>();
        private Shader terrainShader;

        internal NativeGroundMaterials(byte[] json, bool visuals, Func<string, GameObject> load = null)
        {
            this.visuals = visuals;
            this.load = load ?? (path => Resources.Load<GameObject>(path));
            var manifest = JsonConvert.DeserializeObject<Manifest>(System.Text.Encoding.UTF8.GetString(json));
            if (manifest == null || manifest.bindings == null) throw new InvalidDataException("Native ground material index is missing");
            foreach (var item in manifest.bindings) sources.Add(item.name, item.resource);
            if (manifest.surfaces == null) throw new InvalidDataException("Native ground surface index is missing");
            foreach (var item in manifest.surfaces) surfaces.Add(item.name, item);
        }

        internal bool Bind(GameObject original, GameObject replacement, string[] names)
        {
            Cache(original);
            Renderer renderer = null;
            Material[] result = null;
            if (visuals)
            {
                renderer = replacement.GetComponent<Renderer>();
                if (!renderer) return false;
                var bundled = renderer.sharedMaterials;
                names = new string[bundled.Length];
                result = new Material[bundled.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    if (!bundled[i]) return false;
                    string name = names[i] = bundled[i].name;
                    Material material;
                    if (!materials.TryGetValue(name, out material) || !material)
                    {
                        string resource;
                        if (sources.TryGetValue(name, out resource)) Cache(load(resource));
                        if (!materials.TryGetValue(name, out material) || !material)
                            material = PrepareFallback(bundled[i]);
                    }
                    result[i] = material;
                }
            }
            if (names == null) return false;
            var collider = replacement.GetComponent<MeshCollider>();
            if (!collider || !collider.sharedMesh || collider.sharedMesh.subMeshCount != names.Length) return false;
            var runs = new List<PhMatByTriangleIndex.MatToPhMat>();
            int triangles = 0;
            for (int i = 0; i < names.Length; i++)
            {
                Surface surface;
                if (!surfaces.TryGetValue(names[i], out surface)) return false;
                PhysicMaterial physical;
                if (!physics.TryGetValue(surface.physics, out physical) || !physical)
                {
                    Cache(load(surface.resource));
                    if (!physics.TryGetValue(surface.physics, out physical) || !physical) return false;
                }
                int count = checked((int)collider.sharedMesh.GetIndexCount(i)) / 3;
                if (count == 0) continue;
                triangles += count;
                if (runs.Count > 0 && runs[runs.Count - 1].phMat == physical) runs.RemoveAt(runs.Count - 1);
                runs.Add(new PhMatByTriangleIndex.MatToPhMat { offset = triangles - 1, phMat = physical });
            }
            if (runs.Count == 0) return false;
            var mapping = replacement.GetComponent<PhMatByTriangleIndex>() ?? replacement.AddComponent<PhMatByTriangleIndex>();
            mapping.matToPhMat = runs.ToArray();
            collider.sharedMaterial = null;
            if (visuals) renderer.sharedMaterials = result;
            return true;
        }

        private void Cache(GameObject prefab)
        {
            if (!prefab || !cached.Add(prefab.GetInstanceID())) return;
            if (visuals)
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material)
                    {
                        materials[Name(material.name)] = material;
                        if (material.shader && material.shader.name == "Custom/VertexColorMix4" && material.shader.isSupported)
                            terrainShader = material.shader;
                    }
            foreach (var mapping in prefab.GetComponentsInChildren<PhMatByTriangleIndex>(true))
                if (mapping.matToPhMat != null)
                    foreach (var run in mapping.matToPhMat)
                        if (run.phMat) physics[Name(run.phMat.name)] = run.phMat;
        }

        private Material PrepareFallback(Material source)
        {
            if (!source.shader || source.shader.name != "LegacyMapExpansion/TerrainLayers") return source;
            var shader = terrainShader ? terrainShader : Shader.Find("Custom/VertexColorMix4");
            if (!shader || !shader.isSupported) return source;
            Material material;
            if (fallbackMaterials.TryGetValue(source, out material) && material) return material;
            material = new Material(source) { name = source.name, shader = shader, renderQueue = -1 };
            material.shaderKeywords = source.shaderKeywords;
            fallbackMaterials[source] = material;
            return material;
        }

        private static string Name(string name)
        {
            const string suffix = " (Instance)";
            return name.EndsWith(suffix, StringComparison.Ordinal) ? name.Substring(0, name.Length - suffix.Length) : name;
        }

        [Serializable] private sealed class Manifest { public Binding[] bindings; public Surface[] surfaces; }
        [Serializable] private sealed class Binding { public string name, resource; }
        [Serializable] private sealed class Surface { public string name, physics, resource; }
    }
}
