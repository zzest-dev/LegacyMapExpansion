using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class MapPortBuild
{
    [Serializable] public class GroundStats { public string asset; public int vertices, oldVertices; public float maximumVertexDelta; public bool indicesEqual; }
    [Serializable] public class GroundStatsReport { public GroundStats[] tiles; }
    [Serializable] public class Prefab { public string guid, asset; }
    [Serializable] public class Ground { public int x, z; public string asset, old; }
    [Serializable] public class MaterialInfo { public string asset, shader; }
    [Serializable] public class MeshAssignment { public string prefab, component, raw; }
    [Serializable] public class Input { public Prefab[] prefabs; public Ground[] grounds; public MaterialInfo[] materials; public MeshAssignment[] meshes; }
    [Serializable] public class Output { public Prefab[] prefabs; public Ground[] grounds; public MaterialInfo[] materials; }
    private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

    public static void Repack()
    {
        var manifest = JsonUtility.FromJson<Output>(File.ReadAllText("Assets/map-manifest.json"));
        var assets = new HashSet<string>(manifest.prefabs.Select(p => p.asset));
        foreach (var ground in manifest.grounds) assets.Add(ground.asset);
        foreach (var info in manifest.materials)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(info.asset);
            if (info.shader.IndexOf("cut", StringComparison.OrdinalIgnoreCase) >= 0 || info.shader.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 || info.shader.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                material.SetFloat("_Mode", 1); material.SetInt("_SrcBlend", 1); material.SetInt("_DstBlend", 0);
                material.SetInt("_ZWrite", 1); material.EnableKeyword("_ALPHATEST_ON"); material.renderQueue = 2450;
            }
            else if (info.shader.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0 || info.shader.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                material.SetFloat("_Mode", 3); material.SetInt("_SrcBlend", 5); material.SetInt("_DstBlend", 10);
                material.SetInt("_ZWrite", 0); material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); material.renderQueue = 3000;
            }
            MapMaterialCompatibility.Configure(material,info.shader);
            EditorUtility.SetDirty(material);
            assets.Add(info.asset);
        }
        assets.Add("Assets/map-manifest.json");
        AssetDatabase.SaveAssets();
        var result = BuildPipeline.BuildAssetBundles("../Resources", new[] {
            new AssetBundleBuild { assetBundleName = "map1018.bundle", assetNames = assets.ToArray() }
        }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        if (!result) throw new InvalidDataException("Map repack failed");
        ValidateBundle();
        Debug.Log("REGIONAL_MAP_REPACK_OK");
    }

    public static void ValidateBundle()
    {
        var bundle = AssetBundle.LoadFromFile(Path.GetFullPath("../Resources/map1018.bundle"));
        if (!bundle) throw new InvalidDataException("Cannot load the built map bundle");
        var manifest = JsonUtility.FromJson<Output>(bundle.LoadAsset<TextAsset>("Assets/map-manifest.json").text);
        int renderers = 0, colliders = 0, terrainHits = 0, recoveredMeshes = 0;
        foreach (var item in manifest.prefabs)
        {
            var prefab = bundle.LoadAsset<GameObject>(item.asset);
            if (!prefab || prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                throw new InvalidDataException("Missing or incompatible scripts in " + item.asset);
            var meshes = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (!meshes.Any(filter => filter.sharedMesh && filter.sharedMesh.vertexCount > 0))
                throw new InvalidDataException("Empty imported geometry " + item.asset);
            foreach (var filter in meshes)
            {
                if (!filter.sharedMesh) continue;
                if (filter.sharedMesh.name.Contains("_")) recoveredMeshes++;
                if (filter.sharedMesh.isReadable)
                {
                    var vertices = filter.sharedMesh.vertices;
                    foreach (var vertex in vertices)
                        if (float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsNaN(vertex.z))
                            throw new InvalidDataException("Invalid vertex in " + item.asset);
                }
            }
            renderers += prefab.GetComponentsInChildren<Renderer>(true).Length;
            colliders += prefab.GetComponentsInChildren<Collider>(true).Count(collider => !(collider is MeshCollider) || ((MeshCollider)collider).sharedMesh);
            if (item.asset.Contains("Building_Nightclub") || item.asset.Contains("Fire_Station_2") || item.asset.Contains("Building_Police_2") || item.asset.Contains("Building_FitnesCentr_1"))
            {
                if (!prefab.GetComponent<MeshFilter>().sharedMesh)
                    throw new InvalidDataException("Landmark root mesh is missing " + item.asset);
                if (!prefab.GetComponentsInChildren<MeshCollider>(true).Any(collider => collider.sharedMesh))
                    throw new InvalidDataException("Landmark collision is missing " + item.asset);
            }
        }
        foreach (var item in manifest.grounds)
        {
            var prefab = bundle.LoadAsset<GameObject>(item.asset);
            foreach (var material in prefab.GetComponent<Renderer>().sharedMaterials)
                if (material && material.shader.name == "LegacyMapExpansion/TerrainLayers" &&
                    (material.FindPass("DEFERRED") < 0 || material.FindPass("ShadowCaster") < 0 || material.renderQueue > 2500))
                    throw new InvalidDataException("Terrain requires opaque deferred and depth passes: " + item.asset);
            var instance = UnityEngine.Object.Instantiate(prefab);
            var collider = instance.GetComponent<MeshCollider>();
            if (!collider || !collider.sharedMesh) throw new InvalidDataException("Missing ground collider " + item.asset);
            Physics.SyncTransforms();
            var bounds = collider.bounds;
            bool hit = false;
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    RaycastHit result;
                    var point = bounds.center + new Vector3(bounds.extents.x * x * .5f, bounds.extents.y + 1000, bounds.extents.z * z * .5f);
                    if (collider.Raycast(new Ray(point, Vector3.down), out result, bounds.size.y + 2000)) hit = true;
                }
            UnityEngine.Object.DestroyImmediate(instance);
            if (!hit) throw new InvalidDataException("Ground collision raycasts failed " + item.asset);
            terrainHits++;
        }
        File.WriteAllText("../bundle-verification.json", "{\"prefabs\":" + manifest.prefabs.Length + ",\"groundTiles\":" + manifest.grounds.Length + ",\"renderers\":" + renderers + ",\"colliders\":" + colliders + ",\"groundRaycastsPassed\":" + terrainHits + ",\"packedMeshInstances\":" + recoveredMeshes + "}");
        bundle.Unload(true);
        Debug.Log("MAP_PORT_VERIFY_OK");
    }

    public static void CompareGround()
    {
        var input = JsonUtility.FromJson<Input>(File.ReadAllText("Assets/build-input.json"));
        var stats = new List<GroundStats>();
        foreach (var tile in input.grounds)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<GameObject>(tile.asset).GetComponent<MeshFilter>().sharedMesh;
            var old = string.IsNullOrEmpty(tile.old) ? null : AssetDatabase.LoadAssetAtPath<Mesh>(tile.old);
            var item = new GroundStats { asset = tile.asset, vertices = mesh.vertexCount, oldVertices = old ? old.vertexCount : -1 };
            if (old && old.vertexCount == mesh.vertexCount)
            {
                var a = mesh.vertices; var b = old.vertices;
                for (int i = 0; i < a.Length; i++) item.maximumVertexDelta = Mathf.Max(item.maximumVertexDelta, Vector3.Distance(a[i], b[i]));
                item.indicesEqual = mesh.subMeshCount == old.subMeshCount && Enumerable.Range(0, mesh.subMeshCount).All(i => mesh.GetIndices(i).SequenceEqual(old.GetIndices(i)));
            }
            stats.Add(item);
        }
        File.WriteAllText("../ground-comparison.json", JsonUtility.ToJson(new GroundStatsReport { tiles = stats.ToArray() }, true));
    }

    public static void Build()
    {
        var input = JsonUtility.FromJson<Input>(File.ReadAllText("Assets/build-input.json"));
        Directory.CreateDirectory("Assets/RebuiltMeshes");
        foreach (var group in input.meshes.GroupBy(item => item.prefab))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(group.Key);
            foreach (var item in group)
            {
                Mesh mesh;
                if (!Meshes.TryGetValue(item.raw, out mesh))
                {
                    mesh = ReadMesh(item.raw);
                    var meshPath = "Assets/RebuiltMeshes/" + Path.GetFileNameWithoutExtension(item.raw) + ".asset";
                    var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (existingMesh)
                    {
                        EditorUtility.CopySerialized(mesh, existingMesh);
                        UnityEngine.Object.DestroyImmediate(mesh);
                        mesh = existingMesh;
                    }
                    else AssetDatabase.CreateAsset(mesh, meshPath);
                    Meshes.Add(item.raw, mesh);
                }
                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (!(component is MeshFilter) && !(component is MeshCollider)) continue;
                    string guid; long id;
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out guid, out id);
                    if (id.ToString() != item.component) continue;
                    var filter = component as MeshFilter;
                    if (filter) filter.sharedMesh = mesh;
                    var collider = component as MeshCollider;
                    if (collider) collider.sharedMesh = mesh;
                    EditorUtility.SetDirty(component);
                }
            }
            PrefabUtility.SavePrefabAsset(prefab);
        }
        AssetDatabase.SaveAssets();
        var valid = new List<Prefab>();
        var rejected = new List<string>();
        foreach (var item in input.prefabs)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(item.asset);
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (!filters.Any(filter => filter.sharedMesh && filter.sharedMesh.vertexCount > 0))
            { rejected.Add(item.asset); continue; }
            valid.Add(item);
        }
        var changed = new List<Ground>();
        foreach (var item in input.grounds)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(item.asset);
            var mesh = prefab.GetComponent<MeshFilter>().sharedMesh;
            if (!mesh || mesh.vertexCount == 0) throw new InvalidDataException("Empty ground " + item.asset);
            var old = string.IsNullOrEmpty(item.old) ? null : AssetDatabase.LoadAssetAtPath<Mesh>(item.old);
            if (!SameGeometry(mesh, old)) changed.Add(item);
        }
        var assets = new HashSet<string>(valid.Select(item => item.asset));
        foreach (var item in changed) assets.Add(item.asset);
        var dependencies = new HashSet<string>(AssetDatabase.GetDependencies(assets.ToArray(), true));
        var materials = input.materials.Where(item => dependencies.Contains(item.asset)).ToArray();
        foreach (var item in materials)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(item.asset);
            if (item.shader.IndexOf("cut", StringComparison.OrdinalIgnoreCase) >= 0 || item.shader.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 || item.shader.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                material.SetFloat("_Mode", 1); material.SetInt("_SrcBlend", 1); material.SetInt("_DstBlend", 0);
                material.SetInt("_ZWrite", 1); material.EnableKeyword("_ALPHATEST_ON"); material.renderQueue = 2450;
            }
            else if (item.shader.IndexOf("transparent", StringComparison.OrdinalIgnoreCase) >= 0 || item.shader.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                material.SetFloat("_Mode", 3); material.SetInt("_SrcBlend", 5); material.SetInt("_DstBlend", 10);
                material.SetInt("_ZWrite", 0); material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); material.renderQueue = 3000;
            }
            material.SetFloat("_Glossiness", .15f);
            MapMaterialCompatibility.Configure(material,item.shader);
            EditorUtility.SetDirty(material);
            assets.Add(item.asset);
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText("Assets/map-manifest.json", JsonUtility.ToJson(new Output { prefabs = valid.ToArray(), grounds = changed.ToArray(), materials = materials }, true));
        AssetDatabase.ImportAsset("Assets/map-manifest.json");
        assets.Add("Assets/map-manifest.json");
        Directory.CreateDirectory("../Resources");
        var result = BuildPipeline.BuildAssetBundles("../Resources", new[] {
            new AssetBundleBuild { assetBundleName = "map1018.bundle", assetNames = assets.ToArray() }
        }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        if (!result) throw new Exception("Map AssetBundle build failed");
        File.WriteAllText("../bundle-report.json", JsonUtility.ToJson(new Output { prefabs = valid.ToArray(), grounds = changed.ToArray(), materials = materials }, true));
        File.WriteAllLines("../rejected-prefabs.txt", rejected.ToArray());
        Debug.Log("MAP_PORT_BUILD_OK prefabs=" + valid.Count + " ground=" + changed.Count + " meshes=" + Meshes.Count);
    }

    private static bool SameGeometry(Mesh a, Mesh b)
    {
        return b && GeometryKeys(a).SequenceEqual(GeometryKeys(b));
    }

    private static ulong[] GeometryKeys(Mesh mesh)
    {
        var vertices = mesh.vertices;
        var keys = new ulong[vertices.Length];
        unchecked
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                ulong hash = 14695981039346656037UL;
                hash = (hash ^ (ulong)(long)Math.Round(vertices[i].x * 1000)) * 1099511628211UL;
                hash = (hash ^ (ulong)(long)Math.Round(vertices[i].y * 1000)) * 1099511628211UL;
                keys[i] = (hash ^ (ulong)(long)Math.Round(vertices[i].z * 1000)) * 1099511628211UL;
            }
            var triangles = new List<ulong>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var indices = mesh.GetIndices(sub);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var a = keys[indices[i]]; var b = keys[indices[i+1]]; var c = keys[indices[i+2]];
                    if (a > b) { var temp = a; a = b; b = temp; }
                    if (b > c) { var temp = b; b = c; c = temp; }
                    if (a > b) { var temp = a; a = b; b = temp; }
                    triangles.Add((((14695981039346656037UL ^ a) * 1099511628211UL ^ b) * 1099511628211UL ^ c) * 1099511628211UL);
                }
            }
            triangles.Sort();
            return triangles.ToArray();
        }
    }

    private static Mesh ReadMesh(string path)
    {
        using (var reader = new BinaryReader(File.OpenRead(path)))
        {
            var version = reader.ReadByte();
            if (version != 3) throw new InvalidDataException("Unsupported packed mesh version " + version + ": " + path);
            reader.ReadInt64();
            var vertices = Vectors3(reader);
            int colorCount = Count(reader);
            var colors = new Color32[colorCount];
            for (int i = 0; i < colorCount; i++) colors[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            var normals = Vectors3(reader);
            int tangentCount = Count(reader);
            var tangents = new Vector4[tangentCount];
            for (int i = 0; i < tangentCount; i++) tangents[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            int uvCount = Count(reader);
            var uv = new Vector2[uvCount];
            for (int i = 0; i < uvCount; i++) uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            int submeshes = Count(reader, 256);
            int totalIndices = Count(reader, 30000000);
            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path), indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices;
            if (colors.Length == vertices.Length) mesh.colors32 = colors;
            if (normals.Length == vertices.Length) mesh.normals = normals;
            if (tangents.Length == vertices.Length) mesh.tangents = tangents;
            if (uv.Length == vertices.Length) mesh.uv = uv;
            mesh.subMeshCount = submeshes;
            int readIndices = 0;
            for (int sub = 0; sub < submeshes; sub++)
            {
                int size = Count(reader, 30000000);
                if (size % 3 != 0) throw new InvalidDataException("Non-triangular packed mesh " + path);
                var indices = new int[size];
                for (int i = 0; i < size; i++)
                {
                    indices[i] = reader.ReadUInt16();
                    if (indices[i] >= vertices.Length) throw new InvalidDataException("Index outside vertices: " + path);
                }
                mesh.SetTriangles(indices, sub, false);
                readIndices += size;
            }
            if (readIndices != totalIndices || reader.BaseStream.Position != reader.BaseStream.Length)
                throw new InvalidDataException("Unexpected packed mesh layout " + path + " at " + reader.BaseStream.Position + "/" + reader.BaseStream.Length);
            if (normals.Length != vertices.Length) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    private static int Count(BinaryReader reader, int maximum = 3000000)
    {
        int value = reader.ReadInt32();
        if (value < 0 || value > maximum) throw new InvalidDataException("Invalid array length " + value);
        return value;
    }

    private static Vector3[] Vectors3(BinaryReader reader)
    {
        var values = new Vector3[Count(reader)];
        for (int i = 0; i < values.Length; i++) values[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return values;
    }
}
