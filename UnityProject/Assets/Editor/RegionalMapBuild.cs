using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class RegionalMapBuild
{
    [Serializable] private sealed class OriginalGround { public string asset, mesh; public string[] materials; }
    [Serializable] private sealed class Originals { public OriginalGround[] grounds; }
    [Serializable] private sealed class Region { public float x,z,radius; }
    [Serializable] private sealed class RegionList { public Region[] regions; }
    private struct Area
    {
        public float x0, x1, z0, z1;
        public Area(float x, float z, float r) { x0=x-r; x1=x+r; z0=z-r; z1=z+r; }
        public bool Touches(Bounds b) { return b.max.x >= x0 && b.min.x <= x1 && b.max.z >= z0 && b.min.z <= z1; }
    }
    private struct Vertex
    {
        public Vector3 p, n;
        public Vector4 tangent;
        public Vector2 uv, uv2;
        public Color color;
        public static Vertex Mix(Vertex a, Vertex b, float t)
        {
            return new Vertex { p=Vector3.Lerp(a.p,b.p,t), n=Vector3.Lerp(a.n,b.n,t).normalized,
                tangent=Vector4.Lerp(a.tangent,b.tangent,t), uv=Vector2.Lerp(a.uv,b.uv,t),
                uv2=Vector2.Lerp(a.uv2,b.uv2,t), color=Color.Lerp(a.color,b.color,t) };
        }
    }
    private sealed class Surface
    {
        public Vertex[] vertices;
        public int[][] indices;
        public Material[] materials;
        public Vector3 offset;
        public Surface(GameObject prefab)
        {
            var mesh=prefab.GetComponent<MeshFilter>().sharedMesh;
            offset=prefab.transform.position;
            var p=mesh.vertices; var n=mesh.normals; var t=mesh.tangents; var uv=mesh.uv; var uv2=mesh.uv2; var c=mesh.colors;
            vertices=new Vertex[p.Length];
            for (int i=0;i<p.Length;i++) vertices[i]=new Vertex { p=p[i]+offset, n=n.Length==p.Length?n[i]:Vector3.up,
                tangent=t.Length==p.Length?t[i]:new Vector4(1,0,0,1), uv=uv.Length==p.Length?uv[i]:Vector2.zero,
                uv2=uv2.Length==p.Length?uv2[i]:Vector2.zero, color=c.Length==p.Length?c[i]:Color.black };
            indices=Enumerable.Range(0,mesh.subMeshCount).Select(mesh.GetTriangles).ToArray();
            materials=prefab.GetComponent<Renderer>().sharedMaterials;
            if (indices.Length!=materials.Length || materials.Any(m=>!m)) throw new InvalidDataException("Ground material mismatch "+prefab.name);
        }
        public bool Height(Vector3 p, out float y)
        {
            foreach(var sub in indices)
                for(int i=0;i<sub.Length;i+=3)
                {
                    var a=vertices[sub[i]].p; var b=vertices[sub[i+1]].p; var c=vertices[sub[i+2]].p;
                    float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
                    if(Mathf.Abs(d)<.000001f) continue;
                    float u=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.z-c.z))/d;
                    float v=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.z-c.z))/d;
                    if(u>=-.0001f && v>=-.0001f && u+v<=1.0001f) { y=u*a.y+v*b.y+(1-u-v)*c.y; return true; }
                }
            y=0; return false;
        }
    }

    public static void Build()
    {
        var input=JsonUtility.FromJson<MapPortBuild.Input>(File.ReadAllText("Assets/build-input.json"));
        var originals=JsonUtility.FromJson<Originals>(File.ReadAllText("Assets/regional-originals.json"));
        var areas=JsonUtility.FromJson<RegionList>(File.ReadAllText("Assets/regions.json")).regions.Select(r=>new Area(r.x,r.z,r.radius)).ToArray();
        Directory.CreateDirectory("Assets/RegionalGround");
        int composed=0;
        foreach(var ground in input.grounds.Where(g=>g.asset.Contains("Imported1024")))
        {
            var target=AssetDatabase.LoadAssetAtPath<GameObject>(ground.asset);
            var original=originals.grounds.Single(g=>g.asset==ground.asset);
            var originalMesh=AssetDatabase.LoadAssetAtPath<Mesh>(original.mesh);
            target.GetComponent<MeshFilter>().sharedMesh=originalMesh;
            target.GetComponent<MeshCollider>().sharedMesh=originalMesh;
            target.GetComponent<Renderer>().sharedMaterials=original.materials.Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
            string path="Assets/Imported/GameObject/x "+ground.x+" z "+ground.z+".prefab";
            var baseline=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!target || !baseline) throw new InvalidDataException("Regional base ground missing "+path);
            var area=areas.Single(a=>a.Touches(new Bounds(target.transform.position,new Vector3(200,1000,200))));
            Compose(target,baseline,area,ground.x,ground.z);
            composed++;
        }
        AssetDatabase.SaveAssets();
        File.WriteAllText("../regional-ground-verification.json","{\"tiles\":"+composed+",\"clippedToRegions\":true,\"boundaryHeightBlended\":true}");
        MapPortBuild.Build();
        MapPortBuild.ValidateBundle();
        Debug.Log("REGIONAL_MAP_BUILD_OK");
    }

    private static void Compose(GameObject target, GameObject baseline, Area area, int x, int z)
    {
        var old=new Surface(baseline); var newer=new Surface(target);
        var vertices=new List<Vertex>(); var slots=new List<List<int>>(); var mats=new List<Material>();
        Action<List<Vertex>,Material,bool> emit=(polygon,material,blend)=>
        {
            if(polygon.Count<3) return;
            int slot=mats.FindIndex(m=>m.name==material.name);
            if(slot<0) { slot=mats.Count; mats.Add(material); slots.Add(new List<int>()); }
            int start=vertices.Count;
            foreach(var item in polygon)
            {
                var v=item;
                if(blend)
                {
                    float distance=Mathf.Min(v.p.x-area.x0,area.x1-v.p.x,v.p.z-area.z0,area.z1-v.p.z);
                    if(distance<20)
                    {
                        float height;
                        if(old.Height(v.p,out height)) v.p.y=Mathf.Lerp(height,v.p.y,Mathf.SmoothStep(0,1,Mathf.Clamp01(distance/20)));
                    }
                }
                v.p-=target.transform.position;
                vertices.Add(v);
            }
            for(int i=1;i+1<polygon.Count;i++)
            {
                if(Vector3.Cross(vertices[start+i].p-vertices[start].p,vertices[start+i+1].p-vertices[start].p).sqrMagnitude<.00000001f) continue;
                slots[slot].Add(start); slots[slot].Add(start+i); slots[slot].Add(start+i+1);
            }
        };
        for(int source=0;source<2;source++)
        {
            var surface=source==0?old:newer;
            for(int sub=0;sub<surface.indices.Length;sub++)
            {
                var indices=surface.indices[sub];
                for(int i=0;i<indices.Length;i+=3)
                {
                    var polygon=new List<Vertex>{surface.vertices[indices[i]],surface.vertices[indices[i+1]],surface.vertices[indices[i+2]]};
                    if(source==1)
                    {
                        for(int plane=0;plane<4;plane++) polygon=Clip(polygon,area,plane,true);
                        emit(polygon,surface.materials[sub],true);
                    }
                    else
                    {
                        for(int plane=0;plane<4 && polygon.Count>0;plane++)
                        {
                            emit(Clip(polygon,area,plane,false),surface.materials[sub],false);
                            polygon=Clip(polygon,area,plane,true);
                        }
                    }
                }
            }
        }
        var mesh=new Mesh { name="RegionalGround_"+x+"_"+z,indexFormat=IndexFormat.UInt32 };
        mesh.vertices=vertices.Select(v=>v.p).ToArray(); mesh.normals=vertices.Select(v=>v.n).ToArray();
        mesh.tangents=vertices.Select(v=>v.tangent).ToArray(); mesh.uv=vertices.Select(v=>v.uv).ToArray();
        mesh.uv2=vertices.Select(v=>v.uv2).ToArray(); mesh.colors=vertices.Select(v=>v.color).ToArray();
        mesh.subMeshCount=slots.Count;
        for(int i=0;i<slots.Count;i++) mesh.SetTriangles(slots[i],i,false);
        mesh.RecalculateBounds();
        string asset="Assets/RegionalGround/"+mesh.name+".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(asset);
        if(existing) { EditorUtility.CopySerialized(mesh,existing); UnityEngine.Object.DestroyImmediate(mesh); mesh=existing; }
        else AssetDatabase.CreateAsset(mesh,asset);
        target.GetComponent<MeshFilter>().sharedMesh=mesh;
        target.GetComponent<MeshCollider>().sharedMesh=mesh;
        target.GetComponent<Renderer>().sharedMaterials=mats.ToArray();
        EditorUtility.SetDirty(target); PrefabUtility.SavePrefabAsset(target);
    }

    private static List<Vertex> Clip(List<Vertex> input, Area a, int plane, bool inside)
    {
        var output=new List<Vertex>(); if(input.Count==0) return output;
        Func<Vertex,float> distance=v=>plane==0?v.p.x-a.x0:plane==1?a.x1-v.p.x:plane==2?v.p.z-a.z0:a.z1-v.p.z;
        var previous=input[input.Count-1]; float before=distance(previous)*(inside?1:-1);
        foreach(var current in input)
        {
            float now=distance(current)*(inside?1:-1);
            if((before>=0)!=(now>=0)) output.Add(Vertex.Mix(previous,current,before/(before-now)));
            if(now>=0) output.Add(current);
            previous=current; before=now;
        }
        return output;
    }
}
