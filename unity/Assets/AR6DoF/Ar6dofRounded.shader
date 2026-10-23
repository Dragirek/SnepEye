// Aplat de couleur à coins arrondis, sans éclairage, double face, avec transparence (touches du clavier, poignées des écrans).
// _Size = taille réelle de la face en mètres (x, y) ; _Radius = rayon des coins en mètres. Dessiné AVANT les textes (file d'attente Transparent-10).
Shader "AR6DoF/Rounded"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Size ("Size in meters (xy)", Vector) = (0.045, 0.045, 0, 0)
        _Radius ("Corner radius in meters", Float) = 0.008
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _Color;
            float4 _Size;
            float _Radius;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            float4 frag (v2f i) : SV_Target
            {
                float2 hs = _Size.xy * 0.5;
                float r = min(_Radius, min(hs.x, hs.y));
                float2 p = (i.uv - 0.5) * _Size.xy;
                float2 q = abs(p) - (hs - r);
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
                float aa = max(fwidth(d), 0.00001);
                float a = saturate(0.5 - d / aa);
                return float4(_Color.rgb, _Color.a * a);
            }
            ENDCG
        }
    }
}
