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
        ZWrite On Cull Back
        
        Pass {
            Name "FORWARDBASE"
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "UnityUI.cginc"
            sampler2D _Diffuse1;
            float4 _Diffuse1_ST;
            sampler2D _DiffuseB;
            float4 _DiffuseB_ST;
            sampler2D _DiffuseG;
            float4 _DiffuseG_ST;
            sampler2D _DiffuseR;
            float4 _DiffuseR_ST;
            float _UVTileB;
            float _UVTileDif;
            float _UVTileG;
            float _UVTileR;
            float _WorldPoseB;
            float _WorldPoseDif;
            float _WorldPoseG;
            float _WorldPoseR;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 normal : TEXCOORD1; float3 world : TEXCOORD2; float4 local : TEXCOORD3; float2 uv2 : TEXCOORD4; fixed4 color : COLOR; UNITY_FOG_COORDS(5) };
            v2f vert(appdata v) {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.uv2 = v.uv2;
                o.normal = UnityObjectToWorldNormal(v.normal); o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.local = v.vertex; o.color = v.color; UNITY_TRANSFER_FOG(o,o.pos); return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                fixed4 c = fixed4(1,1,1,1);
                float2 uvDif = lerp(i.uv, i.world.xz, saturate(_WorldPoseDif)) * _UVTileDif;
                float2 uvR = lerp(i.uv, i.world.xz, saturate(_WorldPoseR)) * _UVTileR;
                float2 uvG = lerp(i.uv, i.world.xz, saturate(_WorldPoseG)) * _UVTileG;
                float2 uvB = lerp(i.uv, i.world.xz, saturate(_WorldPoseB)) * _UVTileB;
                float baseWeight = saturate(1.0 - i.color.r - i.color.g - i.color.b);
                float4 weights = float4(baseWeight, i.color.rgb);
                weights /= max(dot(weights, float4(1,1,1,1)), 0.001);
                c = tex2D(_Diffuse1, uvDif) * weights.x + tex2D(_DiffuseR, uvR) * weights.y + tex2D(_DiffuseG, uvG) * weights.z + tex2D(_DiffuseB, uvB) * weights.w;
                float3 n = normalize(i.normal);
                float3 lightDir = normalize(UnityWorldSpaceLightDir(i.world));
                half3 illumination = max(ShadeSH9(half4(n, 1)), half3(0.2,0.2,0.2)) + _LightColor0.rgb * saturate(dot(n, lightDir));
                c.rgb *= illumination;
                c.a = 1;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}