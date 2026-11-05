Shader "Hidden/StillWrong/FlatId"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _IdColor ("Id", Vector) = (0, 0, 0, 1)
        _Cutoff ("Cutoff", Float) = 0.5
        _VertexAlpha ("Vertex Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _IdColor;
            float _Cutoff;
            float _VertexAlpha;
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float alpha : TEXCOORD1;
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.alpha = lerp(1.0, v.color.a, _VertexAlpha);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                clip(tex2D(_MainTex, i.uv).a * i.alpha - _Cutoff);
                return float4(_IdColor.rgb, 1.0);
            }
            ENDCG
        }
    }
}
