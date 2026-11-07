using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Build from the same source prefabs as map1018.bundle; only presentation is removed.
public static class MapServerBuild
{
    private const string Generated = "Assets/LegacyMapExpansionServerGenerated";
    [Serializable] public class Prefab { public string guid, asset, name; }
    [Serializable] public class Ground { public int x, z; public string asset; public string[] surfaceNames; }
    [Serializable] public class Manifest { public Prefab[] prefabs; public Ground[] grounds; }

    public static void Build()
    {
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("Assets/map-manifest.json"));
        if (manifest == null || manifest.prefabs == null || manifest.grounds == null)
            throw new InvalidDataException("Missing map manifest.");
        Directory.CreateDirectory(Generated);
        AssetDatabase.Refresh();
        var paths = new List<string>();
        for (int i = 0; i < manifest.prefabs.Length; i++)
        {
            var entry = manifest.prefabs[i];
            var source = LoadPrefab(entry.asset);
            entry.name = source.name;
            entry.asset = Save(source, "object-" + i, false);
            paths.Add(entry.asset);
        }
        foreach (var entry in manifest.grounds)
        {
            var source = LoadPrefab(entry.asset);
            var renderer = source.GetComponent<Renderer>();
            var collider = source.GetComponent<MeshCollider>();
            if (!renderer || !collider || !collider.sharedMesh || !source.GetComponent<MeshFilter>())
                throw new InvalidDataException("Incomplete ground tile: " + entry.asset);
            entry.surfaceNames = renderer.sharedMaterials.Select(m => m ? m.name : null).ToArray();
            if (entry.surfaceNames.Any(string.IsNullOrEmpty) || entry.surfaceNames.Length != collider.sharedMesh.subMeshCount)
                throw new InvalidDataException("Invalid physical surface slots: " + entry.asset);
            entry.asset = Save(source, "ground-" + entry.x + "-" + entry.z, true);
            paths.Add(entry.asset);
        }
        string index = Generated + "/map-manifest.json";
        File.WriteAllText(index, JsonUtility.ToJson(manifest, true));
        AssetDatabase.ImportAsset(index);
        paths.Add(index);
        foreach (string dependency in AssetDatabase.GetDependencies(paths.ToArray(), true))
        {
            var type = AssetDatabase.GetMainAssetTypeAtPath(dependency);
            if (type != null && (typeof(Texture).IsAssignableFrom(type) || type == typeof(Material) ||
                type == typeof(Shader) || type == typeof(AudioClip)))
                throw new InvalidDataException("Server bundle has a visual/audio dependency: " + dependency);
        }
        bool linux = Environment.GetCommandLineArgs().Contains("-mapLinux");
        string directory = linux ? "../Resources/Linux" : "../Resources";
        Directory.CreateDirectory(directory);
        var bundles = BuildPipeline.BuildAssetBundles(directory, new[] {
            new AssetBundleBuild { assetBundleName = "map1018-server.bundle", assetNames = paths.ToArray() }
        }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode |
            BuildAssetBundleOptions.ForceRebuildAssetBundle, linux ? BuildTarget.StandaloneLinux64 : BuildTarget.StandaloneWindows64);
        if (!bundles) throw new InvalidOperationException("Server map bundle build failed.");
        Debug.Log("MAP_SERVER_BUILD_OK prefabs=" + manifest.prefabs.Length + " grounds=" + manifest.grounds.Length);
    }

    private static GameObject LoadPrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!prefab) throw new FileNotFoundException("Missing source prefab: " + path);
        return prefab;
    }

    private static string Save(GameObject source, string name, bool ground)
    {
        var copy = Object.Instantiate(source);
        try
        {
            copy.SetActive(false);
            Strip<LODGroup>(copy);
            Strip<ParticleSystem>(copy);
            Strip<Renderer>(copy);
            Strip<Light>(copy);
            Strip<ReflectionProbe>(copy);
            Strip<AudioSource>(copy);
            Strip<AudioListener>(copy);
            // GroundItem.SetObject uploads the original root MeshFilter mesh.
            // Retain it and the unchanged collider/submesh layout for that native path.
            if (!ground) Strip<MeshFilter>(copy);
            string path = Generated + "/" + name + ".prefab";
            if (!PrefabUtility.SaveAsPrefabAsset(copy, path)) throw new IOException("Cannot save " + path);
            return path;
        }
        finally { Object.DestroyImmediate(copy); }
    }

    private static void Strip<T>(GameObject root) where T : Component
    {
        foreach (var component in root.GetComponentsInChildren<T>(true)) Object.DestroyImmediate(component);
    }
}
