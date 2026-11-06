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
            float4x4 _ClipM; float4 _ClipHalf;   // coupe au cadre : x, y dans le repere du cadre (z = 1 : actif) ; la profondeur reste libre
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
                if (_ClipHalf.z > 0.5)
                {
                    // fenetre : on coupe selon ce que l'oeil VOIT du cadre. Le rayon oeil -> point est prolonge jusqu'au plan du cadre et c'est ce point-la
                    // qui est teste (un test droit sur x, y laissait le relief 3D depasser visuellement du cadre des qu'on regarde en biais)
                    float3 q = mul(_ClipM, float4(i.wpos, 1.0)).xyz;
                    float3 qc = mul(_ClipM, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                    float dz = qc.z - q.z;
                    float2 pq = q.xy;
                    if (abs(dz) > 1e-3)
                    {
                        float tt = qc.z / dz;
                        if (tt > 0.0) pq = qc.xy + tt * (q.xy - qc.xy);
                    }
                    float2 dd = max(abs(pq) - (_ClipHalf.xy - _ClipHalf.w), 0.0);
                    clip(_ClipHalf.w - length(dd));
                }
                float3 n = normalize(i.n);
                float3 v = normalize(_WorldSpaceCameraPos - i.wpos);
                float3 l = normalize(_Light.xyz);
                float d = dot(n, l);
                // les trois tons ont des transitions de ~1 pixel (derivees) : plus de marches d'escalier sur les bords des aplats
                float w = max(fwidth(d) * 1.2, 0.015);
                float tHi = smoothstep(0.35 - w, 0.35 + w, d);
                float tMid = smoothstep(-0.25 - w, -0.25 + w, d);
                float3 mid = lerp(_Shade.rgb, _Color.rgb, 0.55);
                float3 col = lerp(_Shade.rgb, lerp(mid, _Color.rgb, tHi), tMid);
                // petit reflet franc
                float sp = dot(normalize(l + v), n);
                float ws = max(fwidth(sp) * 1.2, 0.004);
                col += smoothstep(0.975 - ws, 0.975 + ws, sp) * 0.22;
                // émission : aplat lumineux (soleil, étoiles, gouttes)
                col = lerp(col, _Color.rgb * 1.08, _Emit);
                // liseré : fin, lisse (anticrenelé) et de la couleur de l'objet en plus fonce, pas noir pur
                float nv = saturate(dot(n, v));
                float th = _Outline * 0.38;
                float wv = max(fwidth(nv) * 1.4, 0.012);
                float edge = 1.0 - smoothstep(th - wv, th + wv, nv);
                float3 oc = lerp(_OutlineColor.rgb, _Color.rgb * 0.42, 0.65);
                oc = lerp(oc, _Color.rgb * 0.62, _Emit);
                col = lerp(col, oc, edge);
                return float4(saturate(col), 1.0);
            }
            ENDCG
        }
    }
}
