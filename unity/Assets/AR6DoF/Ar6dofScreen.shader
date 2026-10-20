// Écran virtuel : affiche la texture du bureau (mipmaps + filtrage trilinéaire pour un texte net), ou un aplat de couleur
// (poignées). Sans éclairage, double face. _Hover (0..1) éclaire le contour de l'écran visé.
Shader "AR6DoF/Screen"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Color ("Color", Color) = (1,1,1,1)
        _UseTex ("Use texture (1) or flat colour (0)", Float) = 1
        _FlipY ("Flip vertical", Float) = 1
        _Hover ("Hover", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Color;
            float _UseTex, _FlipY, _Hover;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            float4 frag (v2f i) : SV_Target
            {
                float4 c = _Color;
                if (_UseTex > 0.5)
                {
                    float2 uv = float2(i.uv.x, _FlipY > 0.5 ? 1.0 - i.uv.y : i.uv.y);
                    c = float4(tex2D(_MainTex, uv).rgb, 1.0);
                    float2 e = min(i.uv, 1.0 - i.uv);
                    float edge = step(min(e.x, e.y), 0.004);
                    c.rgb = lerp(c.rgb, float3(0.3, 0.85, 1.0), edge * _Hover);
                }
                return float4(c.rgb, 1.0);
            }
            ENDCG
        }
    }
}
