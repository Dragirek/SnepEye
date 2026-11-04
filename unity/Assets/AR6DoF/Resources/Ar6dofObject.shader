// Objets 3D et stickers poses dans le monde (Ar6dofObjectsApp). Texture x teinte, éclairage simple facultatif, double face.
// Le mode de fusion est réglé par le code : opaque (modèles) ou transparent (stickers).
Shader "AR6DoF/Object"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Teinte", Color) = (1,1,1,1)
        _Shade ("Eclairage (0 = aucun, 1 = simple)", Float) = 1
        _Glow ("Surbrillance", Float) = 0
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
        _Cutoff ("Seuil de transparence", Range(0,1)) = 0.02
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Shade, _Glow, _Cutoff;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.n = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            float4 frag (v2f i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv) * _Color;
                clip(c.a - _Cutoff);
                float3 n = normalize(i.n);
                float l = abs(dot(n, normalize(float3(0.35, 0.8, 0.45)))) * 0.55 + 0.45;   // symétrique : le dos d'une forme est éclairé comme le devant
                c.rgb *= lerp(1.0, l, _Shade);
                c.rgb += _Glow;
                return c;
            }
            ENDCG
        }
    }
}
