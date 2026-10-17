// Composition finale du casque : image caméra corrigée + scène 3D, pour les deux yeux, en une seule passe.
// L'écran est une seule fenêtre (ex. 2880x1440) : moitié gauche = oeil gauche, moitié droite = oeil droit.
// Tout le calcul géométrique est ici (même algorithme que la référence validée tests/passthrough_ref.py) :
//   écran -> pré-déformation des lentilles -> décalage (IPD) -> rayon -> modèle de la caméra (rectification,
//   distorsion OpenCV "rational") -> rotation / miroir du flux -> pixel de l'image partagée.
Shader "AR6DoF/Composite"
{
    Properties
    {
        _Cam ("Image camera (donnees BGR lues comme RGB24)", 2D) = "black" {}
        _SceneL ("Scene oeil gauche", 2D) = "black" {}
        _SceneR ("Scene oeil droit", 2D) = "black" {}
        _Flip ("Inversion verticale (1 ou -1)", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Opaque" "IgnoreProjector"="True" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            sampler2D _Cam, _SceneL, _SceneR;
            float _Flip;
            float4 _TexSize;   // largeur, hauteur de l'image partagee
            float4 _Misc;      // x : marge de la scene (overscan), y : 1 = etirer les bords de l'image
            float4 _U[20];     // 10 vecteurs par oeil (voir Ar6dofPassthrough.cs)

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                float2 p = v.uv * 2.0 - 1.0;
                p.y *= _ProjectionParams.x * _Flip;   // suit la convention de retournement de Unity
                o.pos = float4(p, 0.0, 1.0);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                bool right = i.uv.x >= 0.5;
                float2 e = float2(i.uv.x * 2.0 - (right ? 1.0 : 0.0), i.uv.y);   // 0..1 dans l'oeil, origine en bas a gauche
                int b = right ? 10 : 0;

                // 1) pre-deformation des lentilles de l'afficheur, puis decalage de l'image (IPD logiciel)
                float4 A = _U[b + 0];        // panX, panY, lensCx, lensCy
                float4 Bq = _U[b + 1];       // k1, k2, k3, tan(fov/2)
                float2 p = e * 2.0 - 1.0;
                float2 d = p - A.zw;
                float r2 = dot(d, d);
                float s = 1.0 + Bq.x * r2 + Bq.y * r2 * r2 + Bq.z * r2 * r2 * r2;
                float2 q = A.zw + d * s - A.xy;
                float2 suv = q / _Misc.x * 0.5 + 0.5;  // la scene est rendue plus large que l'affichage (marge) : le decalage IPD ne la rogne pas

                // 2) image de la camera : rayon -> repere brut de la camera -> pixel
                float3 col = float3(0.0, 0.0, 0.0);
                float3 rv = float3(q.x * Bq.w, -q.y * Bq.w, 1.0);
                float3 rr = float3(dot(_U[b + 5].xyz, rv), dot(_U[b + 6].xyz, rv), dot(_U[b + 7].xyz, rv));
                if (rr.z > 1e-4)
                {
                    float x = rr.x / rr.z;
                    float y = rr.y / rr.z;
                    float rc2 = x * x + y * y;
                    float rc4 = rc2 * rc2;
                    float rc6 = rc4 * rc2;
                    float4 D0 = _U[b + 3];   // k1, k2, p1, p2
                    float4 D1 = _U[b + 4];   // k3, k4, k5, k6
                    float rad = (1.0 + D0.x * rc2 + D0.y * rc4 + D1.x * rc6) / (1.0 + D1.y * rc2 + D1.z * rc4 + D1.w * rc6);
                    float xd = x * rad + 2.0 * D0.z * x * y + D0.w * (rc2 + 2.0 * x * x);
                    float yd = y * rad + D0.z * (rc2 + 2.0 * y * y) + 2.0 * D0.w * x * y;
                    float4 Kp = _U[b + 2];   // fx, fy, cx, cy (image corrigee)
                    float u = Kp.x * xd + Kp.z;
                    float v = Kp.y * yd + Kp.w;

                    // 3) image corrigee -> image source (annule rotation puis miroir)
                    float4 S8 = _U[b + 8];   // Wc, Hc, miroirX, miroirY
                    float4 S9 = _U[b + 9];   // quarts de tour horaires, x0, y0
                    float Wc = S8.x;
                    float Hc = S8.y;
                    int rot = (int)(S9.x + 0.5);
                    bool odd = (rot == 1 || rot == 3);
                    float Ws = odd ? Hc : Wc;
                    float Hs = odd ? Wc : Hc;
                    float um = u;
                    float vm = v;
                    if (rot == 1) { um = v; vm = Wc - 1.0 - u; }
                    else if (rot == 2) { um = Ws - 1.0 - u; vm = Hs - 1.0 - v; }
                    else if (rot == 3) { um = Hc - 1.0 - v; vm = u; }
                    float us = (S8.z > 0.5) ? (Ws - 1.0 - um) : um;
                    float vs = (S8.w > 0.5) ? (Hs - 1.0 - vm) : vm;

                    bool inside = (us >= -0.5 && us <= Ws - 0.5 && vs >= -0.5 && vs <= Hs - 0.5);
                    if (inside || _Misc.y > 0.5)
                    {
                        us = clamp(us, 0.0, Ws - 1.0);
                        vs = clamp(vs, 0.0, Hs - 1.0);
                        // Unity : l'origine d'une texture est en BAS a gauche, nos lignes sont comptees depuis le HAUT
                        float2 uvT = float2((S9.y + us + 0.5) / _TexSize.x, 1.0 - (S9.z + vs + 0.5) / _TexSize.y);
                        col = tex2Dlod(_Cam, float4(uvT, 0.0, 0.0)).bgr;
                    }
                }

                // 4) scene virtuelle (menu...) par-dessus, avec la meme deformation
                float4 sc = float4(0.0, 0.0, 0.0, 0.0);
                if (suv.x >= 0.0 && suv.x <= 1.0 && suv.y >= 0.0 && suv.y <= 1.0)
                    sc = right ? tex2Dlod(_SceneR, float4(suv, 0.0, 0.0)) : tex2Dlod(_SceneL, float4(suv, 0.0, 0.0));
                col = lerp(col, sc.rgb, sc.a);
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
}
