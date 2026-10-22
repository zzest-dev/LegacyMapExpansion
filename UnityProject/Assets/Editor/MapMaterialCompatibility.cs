using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class MapMaterialCompatibility
{
    private static void Alias(Material material,string target,params string[] names)
    {
        var properties=new SerializedObject(material).FindProperty("m_SavedProperties.m_TexEnvs");
        foreach(var name in names)
            for(int i=0;i<properties.arraySize;i++)
            {
                var entry=properties.GetArrayElementAtIndex(i);
                if(entry.FindPropertyRelative("first").stringValue!=name)continue;
                var value=entry.FindPropertyRelative("second");
                var texture=value.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
                if(!texture)continue;
                material.SetTexture(target,texture);
                material.SetTextureScale(target,value.FindPropertyRelative("m_Scale").vector2Value);
                material.SetTextureOffset(target,value.FindPropertyRelative("m_Offset").vector2Value);
                return;
            }
    }
    public static void Configure(Material material,string original)
    {
        if(original=="Custom/VertexColorMix4")return;
        bool plant=original=="Custom/Transparent from plant";
        bool cutout=plant || original=="Custom/Transparent_Diffuse" || original=="Nature/OurTreeShader";
        bool glass=original=="Custom/Car/Car_Light_Glass";
        if(plant || cutout)
        {
            material.shader=Shader.Find("LegacyMapExpansion/Cutout");
            Alias(material,"_MainTex","_Diffuse","_Texture","_MainTex");
            if(plant)material.SetFloat("_Cutoff",Mathf.Clamp(material.GetFloat("_ClipPower"),.03f,.8f));
            material.renderQueue=2450;
        }
        else if(glass)
        {
            var serialized=new SerializedObject(material).FindProperty("m_SavedProperties.m_Colors");
            Color tint=new Color(.75f,.8f,.85f,.24f);
            for(int i=0;i<serialized.arraySize;i++)
            {
                var entry=serialized.GetArrayElementAtIndex(i);
                if(entry.FindPropertyRelative("first").stringValue=="_Glass_Color")tint=entry.FindPropertyRelative("second").colorValue;
            }
            material.shader=Shader.Find("LegacyMapExpansion/Glass");
            Alias(material,"_MainTex","_TextureGlass","_CarWindows","_MainTex");
            tint.a=Mathf.Clamp(tint.a*.35f,.12f,.4f);material.SetColor("_Color",tint);material.SetFloat("_Glossiness",.78f);
            material.SetFloat("_Metallic",0);
            material.renderQueue=3000;
        }
        else if(material.shader.name=="Standard")
        {
            Alias(material,"_MainTex","_MainTex","_Texture","_Diffuse");
            Alias(material,"_BumpMap","_BumpMap","_Normal");
            if(material.GetTexture("_BumpMap"))material.EnableKeyword("_NORMALMAP");
            int mode=(int)material.GetFloat("_Mode");
            if(original.IndexOf("Transparent",StringComparison.OrdinalIgnoreCase)>=0 && !cutout)mode=2;
            material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_ALPHABLEND_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType",mode==1?"TransparentCutout":mode>=2?"Transparent":"");
            material.SetFloat("_Mode",mode);material.SetInt("_ZWrite",mode<2?1:0);
            material.SetInt("_SrcBlend",mode==2?5:1);material.SetInt("_DstBlend",mode>=2?10:0);
            if(mode==1)material.EnableKeyword("_ALPHATEST_ON");
            if(mode==2)material.EnableKeyword("_ALPHABLEND_ON");
            if(mode==3)material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue=mode>=2?3000:mode==1?2450:2000;
            if(mode>=2 && material.name.IndexOf("Glass",StringComparison.OrdinalIgnoreCase)>=0)
            {
                material.shader=Shader.Find("LegacyMapExpansion/Glass");
                material.SetFloat("_Glossiness",.78f);
                material.SetFloat("_Metallic",0);
            }
        }
        if(!material.shader || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))throw new Exception("Material shader unavailable "+material.name);
        EditorUtility.SetDirty(material);
    }
}
