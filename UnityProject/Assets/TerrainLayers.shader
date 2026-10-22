Shader "LegacyMapExpansion/TerrainLayers" {
Properties {
		_Diffuse1 ("Diffuse1", 2D) = "white" {}
		_UVTileDif ("UVTileDif", Float) = 0.1
		[Toggle] _WorldPoseDif ("WorldPoseDif", Float) = 1
		_Normal1 ("Normal1", 2D) = "bump" {}
		_UVTileDifN ("UVTileDifN", Float) = 0.1
		[Toggle] _WorldPoseDifN ("WorldPoseDifN", Float) = 1
		_DiffuseR ("DiffuseR", 2D) = "white" {}
		_UVTileR ("UVTileR", Float) = 0.1
		[Toggle] _WorldPoseR ("WorldPoseR", Float) = 1
		_DiffuseG ("DiffuseG", 2D) = "white" {}
		_UVTileG ("UVTileG", Float) = 0.1
		[Toggle] _WorldPoseG ("WorldPoseG", Float) = 0
		_NormalG ("NormalG", 2D) = "bump" {}
		_UVTileGN ("UVTileGN", Float) = 0.1
		[Toggle] _WorldPoseGN ("WorldPoseGN", Float) = 0
		_DiffuseB ("DiffuseB", 2D) = "white" {}
		_UVTileB ("UVTileB", Float) = 0.1
		[Toggle] _WorldPoseB ("WorldPoseB", Float) = 1
		_NormalB ("NormalB", 2D) = "bump" {}
		_UVTileBN ("UVTileBN", Float) = 0.1
		[Toggle] _WorldPoseBN ("WorldPoseBN", Float) = 0
		_SpecularTexture ("Specular Texture", 2D) = "white" {}
		_Specular ("Specular", Range(0, 1)) = 0
		_Glossines ("Glossines", Range(0, 1)) = 0
		_UVTileSpec ("UVTileSpec", Float) = 1
		[Toggle] _UVWWorldPos_Spec ("UVWWorldPos_Spec", Float) = 1
		_Enable_Relief ("Enable_Relief", Range(0, 1)) = 0
		_Relief_DIffuse ("Relief_DIffuse", 2D) = "white" {}
		_Relief_UV ("Relief_UV", Float) = 0.1
		_Mask_Relief_Diffuse ("Mask_Relief_Diffuse", 2D) = "white" {}
		_Mask_Relief_UV ("Mask_Relief_UV", Float) = 0.1
		[HideInInspector] _texcoord ("", 2D) = "white" {}
		[HideInInspector] __dirty ("", Float) = 1
	}
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Blend Off
        ZWrite On
        Cull Back
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows addshadow vertex:vert
        #pragma target 3.0
        sampler2D _Diffuse1, _DiffuseR, _DiffuseG, _DiffuseB;
        sampler2D _Normal1, _NormalG, _NormalB;
        float _UVTileDif, _UVTileR, _UVTileG, _UVTileB;
        float _WorldPoseDif, _WorldPoseR, _WorldPoseG, _WorldPoseB;
        float _UVTileDifN, _UVTileGN, _UVTileBN;
        float _WorldPoseDifN, _WorldPoseGN, _WorldPoseBN;
        struct Input {
            float3 worldPos;
            float2 meshUV;
            fixed4 color : COLOR;
        };
        void vert(inout appdata_full vertex, out Input data) {
            UNITY_INITIALIZE_OUTPUT(Input, data);
            data.meshUV = vertex.texcoord.xy;
            data.color = vertex.color;
        }
        float2 LayerUV(Input data, float world, float tile) {
            return lerp(data.meshUV, data.worldPos.xz, saturate(world)) * tile;
        }
        void surf(Input data, inout SurfaceOutput output) {
            float4 weights = float4(saturate(1.0-data.color.r-data.color.g-data.color.b),data.color.rgb);
            weights /= max(dot(weights,float4(1,1,1,1)),0.001);
            output.Albedo = tex2D(_Diffuse1,LayerUV(data,_WorldPoseDif,_UVTileDif)).rgb*weights.x
                + tex2D(_DiffuseR,LayerUV(data,_WorldPoseR,_UVTileR)).rgb*weights.y
                + tex2D(_DiffuseG,LayerUV(data,_WorldPoseG,_UVTileG)).rgb*weights.z
                + tex2D(_DiffuseB,LayerUV(data,_WorldPoseB,_UVTileB)).rgb*weights.w;
            float3 normal = UnpackNormal(tex2D(_Normal1,LayerUV(data,_WorldPoseDifN,_UVTileDifN)))*weights.x
                + float3(0,0,1)*weights.y
                + UnpackNormal(tex2D(_NormalG,LayerUV(data,_WorldPoseGN,_UVTileGN)))*weights.z
                + UnpackNormal(tex2D(_NormalB,LayerUV(data,_WorldPoseBN,_UVTileBN)))*weights.w;
            output.Normal = normalize(normal+float3(0,0,0.00001));
            output.Specular = 0;
            output.Gloss = 0;
            output.Emission = 0;
            output.Alpha = 1;
        }
        ENDCG
    }
    Fallback Off
}
