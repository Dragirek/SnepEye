// Rendu « toon » (dessin animé) de la météo 3D : trois tons nets au lieu d'un dégradé, un petit reflet, un liseré sombre sur les bords.
// Une seule passe, sans éclairage Unity : la direction de la lumière est donnée par le code (_Light, en coordonnées du monde),
// donc le résultat est le même en pipeline intégré et en URP, et il ne dépend d'aucune lumière de la scène.
Shader "AR6DoF/Toon"
{
    Properties
    {
        _Color ("Couleur", Color) = (1,1,1,1)
        _Shade ("Couleur de l'ombre", Color) = (0.6,0.65,0.85,1)
        _OutlineColor ("Couleur du liseré", Color) = (0.08,0.10,0.18,1)
        _Outline ("Epaisseur du liseré", Range(0,0.6)) = 0.26
        _Emit ("Emission (0 = éclairé, 1 = plat lumineux)", Range(0,1)) = 0
        _Light ("Direction de la lumière (monde)", Vector) = (-0.35,0.8,-0.45,0)
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _Color, _Shade, _OutlineColor, _Light;
            float _Outline, _Emit;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wpos : TEXCOORD1; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.n);
                float3 v = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 l = normalize(_Light.xyz);
                float d = dot(n, l);
                float3 mid = lerp(_Shade.rgb, _Color.rgb, 0.55);
                float3 col = d > 0.35 ? _Color.rgb : (d > -0.25 ? mid : _Shade.rgb);
                // petit reflet franc
                col += step(0.975, saturate(dot(normalize(l + v), n))) * 0.22;
                // émission : aplat lumineux (soleil, étoiles, gouttes)
                col = lerp(col, _Color.rgb * 1.08, _Emit);
                // liseré sombre sur les bords
                float edge = step(saturate(dot(n, v)), _Outline * 0.5);
                col = lerp(col, lerp(_OutlineColor.rgb, _Color.rgb * 0.62, _Emit), edge);
                return float4(saturate(col), 1.0);
            }
            ENDCG
        }
    }
}
