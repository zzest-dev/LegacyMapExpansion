Shader "LegacyMapExpansion/Glass"
{
    Properties
    {
        _MainTex("Texture",2D)="white"{}
        _Color("Tint and opacity",Color)=(.75,.8,.85,.25)
        _Glossiness("Smoothness",Range(0,1))=.78
        _Metallic("Metallic",Range(0,1))=0
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        Cull Off ZWrite Off
        CGPROGRAM
        #pragma surface surf Standard alpha:fade fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness,_Metallic;
        struct Input {float2 uv_MainTex;};
        void surf(Input i,inout SurfaceOutputStandard o)
        {
            fixed4 tex=tex2D(_MainTex,i.uv_MainTex)*_Color;
            o.Albedo=tex.rgb;o.Alpha=tex.a;o.Smoothness=_Glossiness;o.Metallic=_Metallic;
        }
        ENDCG
    }
    FallBack "Transparent/Diffuse"
}
