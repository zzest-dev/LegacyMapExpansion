Shader "LegacyMapExpansion/Cutout"
{
    Properties
    {
        _MainTex("Diffuse",2D)="white"{}
        _Color("Tint",Color)=(1,1,1,1)
        _Cutoff("Alpha cutoff",Range(0,1))=.5
        _ClipPower("Plant cutoff",Range(0,1))=.2
    }
    SubShader
    {
        Tags {"Queue"="AlphaTest" "RenderType"="TransparentCutout"}
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard alphatest:_Cutoff addshadow fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        struct Input {float2 uv_MainTex; fixed4 color:COLOR;};
        void surf(Input i,inout SurfaceOutputStandard o)
        {
            fixed4 tex=tex2D(_MainTex,i.uv_MainTex)*_Color;
            o.Albedo=tex.rgb;o.Alpha=tex.a;o.Smoothness=.08;o.Metallic=0;
        }
        ENDCG
    }
    FallBack "Transparent/Cutout/Diffuse"
}
