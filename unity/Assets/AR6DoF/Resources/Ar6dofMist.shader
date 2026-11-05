// Brouillard de la météo 3D : bancs translucides aux bords très doux (opaques au centre, transparents sur le contour), sans liseré.
// Pas d'écriture de profondeur : les bancs se mélangent entre eux et avec l'île sans arêtes dures. Pas d'éclairage Unity.
Shader "AR6DoF/Mist"
{
    Properties
    {
        _Color ("Couleur", Color) = (0.82,0.86,0.90,1)
        _Alpha ("Opacite", Range(0,1)) = 0.4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Cull Back
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _Color;
            float4x4 _ClipM; float4 _ClipHalf;   // coupe au cadre : x, y dans le repere du cadre (z = 1 : actif) ; la profondeur reste libre
            float _Alpha;
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
                if (_ClipHalf.z > 0.5)
                {
                    float3 q = mul(_ClipM, float4(i.wpos, 1.0)).xyz;
                    float2 dd = max(abs(q.xy) - (_ClipHalf.xy - _ClipHalf.w), 0.0);
                    clip(_ClipHalf.w - length(dd));
                }
                float3 n = normalize(i.n);
                float3 v = normalize(_WorldSpaceCameraPos - i.wpos);
                float f = saturate(dot(n, v));
                float a = smoothstep(0.0, 0.75, f);   // bord transparent, centre plein
                return float4(_Color.rgb, a * _Alpha);
            }
            ENDCG
        }
    }
}
