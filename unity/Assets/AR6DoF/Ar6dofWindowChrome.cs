using System;
using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>Petites icônes dessinées par code (fermer, réduire, redimensionner) ; la roue crantée vient d'Ar6dofVrUi.</summary>
    public static class Ar6dofIcons
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string name)
        {
            Texture2D t;
            if (cache.TryGetValue(name, out t) && t != null) return t;
            if (name == "gear")
            {
                var sp = Ar6dofVrUi.GearSprite();
                t = sp != null ? sp.texture : null;
            }
            else t = Draw(name);
            if (t != null) cache[name] = t;
            return t;
        }

        static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
            float dx = px - (ax + vx * t), dy = py - (ay + vy * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Distance signée à un rectangle arrondi (négative dedans).</summary>
        static float RRect(float px, float py, float cx, float cy, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(px - cx) - (hw - r), qy = Mathf.Abs(py - cy) - (hh - r);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }


        static float Fill(float signedDist) { return Mathf.Clamp01(0.5f - signedDist); }

        /// <summary>Triangle plein (couverture 0..1, bords adoucis).</summary>
        static float TriFill(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
            bool inside = !(neg && pos);
            float md = Mathf.Min(SegDist(px, py, ax, ay, bx, by), Mathf.Min(SegDist(px, py, bx, by, cx, cy), SegDist(px, py, cx, cy, ax, ay)));
            return Fill(inside ? -md : md);
        }

        static Texture2D Draw(string name)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f, a = 0f;
                    switch (name)
                    {
                        case "close":
                            a = Mathf.Clamp01(3.5f - Mathf.Min(SegDist(fx, fy, 19f, 19f, 45f, 45f), SegDist(fx, fy, 19f, 45f, 45f, 19f)));
                            break;
                        case "minimize":
                            a = Mathf.Clamp01(3.5f - SegDist(fx, fy, 17f, 32f, 47f, 32f));
                            break;
                        case "grip":
                        {
                            // une simple ligne arrondie : la poignée « prends-moi ici pour déplacer »
                            a = Mathf.Clamp01(5f - SegDist(fx, fy, 10f, 32f, 54f, 32f));
                            break;
                        }
                        case "monitor":
                        {
                            // écran (contour) sur son pied : le chiffre de l'écran se pose à l'intérieur
                            a = Mathf.Clamp01(2.6f - Mathf.Abs(RRect(fx, fy, 32f, 38f, 27f, 19f, 7f)));
                            if (fx > 28f && fx < 36f && fy > 12f && fy < 20f) a = 1f;
                            if (fx > 18f && fx < 46f && fy > 8f && fy < 12.5f) a = 1f;
                            break;
                        }
                        case "window":
                        {
                            // fenêtre d'application : contour avec sa barre de titre
                            a = Mathf.Max(Mathf.Clamp01(2.6f - Mathf.Abs(RRect(fx, fy, 32f, 32f, 27f, 24f, 7f))), Mathf.Clamp01(2.6f - SegDist(fx, fy, 6f, 45f, 58f, 45f)));
                            break;
                        }
                        case "keyboard":
                        {
                            // clavier : contour, deux rangees de touches et la barre espace
                            a = Mathf.Clamp01(2.6f - Mathf.Abs(RRect(fx, fy, 32f, 32f, 28f, 19f, 7f)));
                            for (int k = 0; k < 5; k++)
                                if (Mathf.Abs(fx - (13f + k * 9.5f)) < 2.6f && Mathf.Abs(fy - 39f) < 2.6f) a = 1f;
                            for (int k = 0; k < 4; k++)
                                if (Mathf.Abs(fx - (17.75f + k * 9.5f)) < 2.6f && Mathf.Abs(fy - 31f) < 2.6f) a = 1f;
                            if (fx > 19f && fx < 45f && fy > 21.5f && fy < 25.5f) a = 1f;
                            break;
                        }
                        case "apps":
                        {
                            // grille de neuf carres : toutes les applications
                            for (int gy = 0; gy < 3; gy++)
                                for (int gx = 0; gx < 3; gx++)
                                    a = Mathf.Max(a, Mathf.Clamp01(2.0f - RRect(fx, fy, 14f + gx * 18f, 14f + gy * 18f, 7f, 7f, 2.5f)));
                            break;
                        }
                        case "mic":
                        {
                            // micro : capsule, arceau et pied
                            a = Mathf.Clamp01(2.6f - Mathf.Abs(SegDist(fx, fy, 32f, 38f, 32f, 44f) - 8f));
                            float dc = Mathf.Sqrt((fx - 32f) * (fx - 32f) + (fy - 38f) * (fy - 38f));
                            if (fy < 38f) a = Mathf.Max(a, Mathf.Clamp01(2.6f - Mathf.Abs(dc - 14f)));
                            a = Mathf.Max(a, Mathf.Clamp01(2.8f - SegDist(fx, fy, 32f, 24f, 32f, 12f)));
                            a = Mathf.Max(a, Mathf.Clamp01(2.8f - SegDist(fx, fy, 22f, 11f, 42f, 11f)));
                            break;
                        }
                        case "pads":
                        {
                            // quatre boutons de soundboard
                            float[] cxs = { 19f, 45f, 19f, 45f }, cys = { 19f, 19f, 45f, 45f };
                            for (int k = 0; k < 4; k++) a = Mathf.Max(a, Mathf.Clamp01(2.6f - Mathf.Abs(RRect(fx, fy, cxs[k], cys[k], 11f, 11f, 4f))));
                            break;
                        }
                        case "mixer":
                        {
                            // table de mixage : trois glissieres et leurs curseurs
                            float[] xs = { 16f, 32f, 48f }, ys = { 40f, 24f, 34f };
                            for (int k = 0; k < 3; k++)
                            {
                                a = Mathf.Max(a, Mathf.Clamp01(2.6f - SegDist(fx, fy, xs[k], 10f, xs[k], 54f)));
                                a = Mathf.Max(a, Mathf.Clamp01(2.0f - RRect(fx, fy, xs[k], ys[k], 7f, 4f, 2f)));
                            }
                            break;
                        }
                        case "camera":
                        {
                            // appareil photo : boitier, objectif et petite bosse
                            a = Mathf.Clamp01(2.6f - Mathf.Abs(RRect(fx, fy, 32f, 29f, 27f, 18f, 6f)));
                            float dl = Mathf.Sqrt((fx - 32f) * (fx - 32f) + (fy - 29f) * (fy - 29f));
                            a = Mathf.Max(a, Mathf.Clamp01(2.6f - Mathf.Abs(dl - 9f)));
                            a = Mathf.Max(a, Mathf.Clamp01(2.8f - SegDist(fx, fy, 20f, 51f, 32f, 51f)));
                            break;
                        }
                        case "mirror":
                        {
                            // miroir : cadre ovale, deux reflets en biais et un petit pied
                            a = Mathf.Clamp01(2.8f - Mathf.Abs(RRect(fx, fy, 32f, 36f, 20f, 25f, 19f)));
                            a = Mathf.Max(a, 0.9f * Mathf.Clamp01(2.4f - SegDist(fx, fy, 24f, 31f, 36f, 47f)));
                            a = Mathf.Max(a, 0.9f * Mathf.Clamp01(2.4f - SegDist(fx, fy, 23f, 42f, 29f, 50f)));
                            a = Mathf.Max(a, Mathf.Clamp01(2.8f - SegDist(fx, fy, 22f, 6f, 42f, 6f)));
                            a = Mathf.Max(a, Mathf.Clamp01(2.8f - SegDist(fx, fy, 32f, 6f, 32f, 11f)));
                            break;
                        }
                        case "vision":
                        {
                            // oeil avec un petit rayonnement : vision nocturne / thermique
                            float de = Mathf.Abs(Mathf.Sqrt((fx - 32f) * (fx - 32f) + 2.2f * (fy - 30f) * (fy - 30f)) - 25f);
                            a = Mathf.Clamp01(2.8f - de);
                            float dp = Mathf.Sqrt((fx - 32f) * (fx - 32f) + (fy - 30f) * (fy - 30f));
                            a = Mathf.Max(a, Mathf.Clamp01(2.6f - Mathf.Abs(dp - 9f)));
                            a = Mathf.Max(a, Mathf.Clamp01(dp < 4f ? 1f : 0f));
                            break;
                        }
                        case "calib":
                        {
                            // damier 4 x 3 (la mire de calibration) dans un cadre arrondi
                            a = Mathf.Clamp01(2.6f - Mathf.Abs(RRect(fx, fy, 32f, 32f, 28f, 22f, 5f)));
                            for (int ci = 0; ci < 4; ci++)
                                for (int cj = 0; cj < 3; cj++)
                                {
                                    if (((ci + cj) & 1) != 0) continue;
                                    a = Mathf.Max(a, Fill(RRect(fx, fy, 14f + ci * 12f, 20f + cj * 12f, 5.5f, 5.5f, 0.5f)));
                                }
                            break;
                        }
                        case "cube":
                        {
                            // cube vu en perspective
                            float[] px6 = { 32f, 54f, 54f, 32f, 10f, 10f }, py6 = { 57f, 44f, 20f, 7f, 20f, 44f };
                            float md = 99f;
                            for (int k = 0; k < 6; k++) md = Mathf.Min(md, SegDist(fx, fy, px6[k], py6[k], px6[(k + 1) % 6], py6[(k + 1) % 6]));
                            md = Mathf.Min(md, SegDist(fx, fy, 32f, 32f, 54f, 44f));
                            md = Mathf.Min(md, SegDist(fx, fy, 32f, 32f, 10f, 44f));
                            md = Mathf.Min(md, SegDist(fx, fy, 32f, 32f, 32f, 7f));
                            a = Mathf.Clamp01(2.6f - md);
                            break;
                        }
                        case "reset":
                        {
                            // « restaurer » : deux carres qui se chevauchent (comme le bouton de Windows), net a toute taille
                            float fd = RRect(fx, fy, 26f, 38f, 16f, 16f, 3f);     // carre de devant (negatif a l'interieur)
                            float bd2 = RRect(fx, fy, 38f, 26f, 16f, 16f, 3f);    // carre de derriere
                            a = Mathf.Clamp01(2.8f - Mathf.Abs(fd));
                            float back = Mathf.Clamp01(2.8f - Mathf.Abs(bd2));
                            if (fd > 3.5f) a = Mathf.Max(a, back);                // le carre de derriere disparait derriere celui de devant
                            break;
                        }
                        case "speaker":
                        {
                            // enceinte : boitier, cone et deux ondes
                            float[] sx = { 12f, 23f, 36f, 36f, 23f, 12f, 12f }, sy = { 25f, 25f, 15f, 49f, 39f, 39f, 25f };
                            float sd = 99f;
                            for (int k = 0; k < 6; k++) sd = Mathf.Min(sd, SegDist(fx, fy, sx[k], sy[k], sx[k + 1], sy[k + 1]));
                            a = Mathf.Clamp01(2.6f - sd);
                            float ang = Mathf.Atan2(fy - 32f, fx - 37f), dr = Mathf.Sqrt((fx - 37f) * (fx - 37f) + (fy - 32f) * (fy - 32f));
                            if (Mathf.Abs(ang) < 0.75f)
                            {
                                a = Mathf.Max(a, Mathf.Clamp01(2.6f - Mathf.Abs(dr - 10f)));
                                a = Mathf.Max(a, Mathf.Clamp01(2.6f - Mathf.Abs(dr - 19f)));
                            }
                            break;
                        }
                        case "weather":
                        {
                            // nuage devant un soleil a rayons
                            float c1 = Mathf.Sqrt((fx - 28f) * (fx - 28f) + (fy - 28f) * (fy - 28f)) - 11f;
                            float c2 = Mathf.Sqrt((fx - 43f) * (fx - 43f) + (fy - 30f) * (fy - 30f)) - 9f;
                            float cloud = Mathf.Min(Mathf.Min(c1, c2), RRect(fx, fy, 38f, 22f, 21f, 8f, 8f));
                            a = Mathf.Clamp01(2.6f - Mathf.Abs(cloud));
                            if (cloud > 3.5f)
                            {
                                float ds = Mathf.Sqrt((fx - 22f) * (fx - 22f) + (fy - 44f) * (fy - 44f));
                                float sun = Mathf.Clamp01(2.6f - Mathf.Abs(ds - 8f));
                                for (int k = 0; k < 8; k++)
                                {
                                    float ak = k * Mathf.PI / 4f;
                                    sun = Mathf.Max(sun, Mathf.Clamp01(2.4f - SegDist(fx, fy, 22f + Mathf.Cos(ak) * 12f, 44f + Mathf.Sin(ak) * 12f, 22f + Mathf.Cos(ak) * 16f, 44f + Mathf.Sin(ak) * 16f)));
                                }
                                a = Mathf.Max(a, sun);
                            }
                            break;
                        }
                        case "clock":
                        {
                            // reveil : cadran, deux aiguilles et deux petites cloches
                            float dcl = Mathf.Sqrt((fx - 32f) * (fx - 32f) + (fy - 30f) * (fy - 30f));
                            a = Mathf.Clamp01(2.8f - Mathf.Abs(dcl - 21f));
                            a = Mathf.Max(a, Mathf.Clamp01(2.8f - SegDist(fx, fy, 32f, 30f, 32f, 44f)));
                            a = Mathf.Max(a, Mathf.Clamp01(2.8f - SegDist(fx, fy, 32f, 30f, 42f, 24f)));
                            float db1 = Mathf.Sqrt((fx - 13f) * (fx - 13f) + (fy - 51f) * (fy - 51f));
                            float db2 = Mathf.Sqrt((fx - 51f) * (fx - 51f) + (fy - 51f) * (fy - 51f));
                            a = Mathf.Max(a, Mathf.Clamp01(2.8f - Mathf.Abs(db1 - 7f)));
                            a = Mathf.Max(a, Mathf.Clamp01(2.8f - Mathf.Abs(db2 - 7f)));
                            break;
                        }
                        case "play":
                            a = TriFill(fx, fy, 21f, 11f, 21f, 53f, 54f, 32f);
                            break;
                        case "pause":
                            a = Mathf.Max(Fill(RRect(fx, fy, 21f, 32f, 7f, 20f, 2.5f)), Fill(RRect(fx, fy, 43f, 32f, 7f, 20f, 2.5f)));
                            break;
                        case "stop":
                            a = Fill(RRect(fx, fy, 32f, 32f, 19f, 19f, 3.5f));
                            break;
                        case "prev":
                            a = Mathf.Max(Fill(RRect(fx, fy, 14f, 32f, 3.5f, 19f, 1.5f)), TriFill(fx, fy, 52f, 11f, 52f, 53f, 22f, 32f));
                            break;
                        case "next":
                            a = Mathf.Max(Fill(RRect(fx, fy, 50f, 32f, 3.5f, 19f, 1.5f)), TriFill(fx, fy, 12f, 11f, 12f, 53f, 42f, 32f));
                            break;
                        case "bell":
                        {
                            // cloche pleine : dome, evasement, bord, battant et petit anneau
                            float dome = fy >= 34f ? Mathf.Sqrt((fx - 32f) * (fx - 32f) + (fy - 34f) * (fy - 34f)) - 15f : 99f;
                            a = Fill(dome);
                            a = Mathf.Max(a, TriFill(fx, fy, 17f, 34f, 47f, 34f, 54f, 17f));
                            a = Mathf.Max(a, TriFill(fx, fy, 17f, 34f, 54f, 17f, 10f, 17f));
                            a = Mathf.Max(a, Fill(RRect(fx, fy, 32f, 17f, 23f, 2.5f, 2.5f)));
                            a = Mathf.Max(a, Fill(Mathf.Sqrt((fx - 32f) * (fx - 32f) + (fy - 9f) * (fy - 9f)) - 5f));
                            a = Mathf.Max(a, Fill(Mathf.Sqrt((fx - 32f) * (fx - 32f) + (fy - 52f) * (fy - 52f)) - 3.5f));
                            break;
                        }
                        case "calendar":
                        {
                            // calendrier : cadre, bandeau, deux anneaux et une grille de jours
                            a = Mathf.Clamp01(2.6f - Mathf.Abs(RRect(fx, fy, 32f, 29f, 24f, 22f, 4f)));
                            a = Mathf.Max(a, Mathf.Clamp01(2.6f - SegDist(fx, fy, 9f, 40f, 55f, 40f)));
                            a = Mathf.Max(a, Mathf.Clamp01(3.0f - SegDist(fx, fy, 21f, 47f, 21f, 57f)));
                            a = Mathf.Max(a, Mathf.Clamp01(3.0f - SegDist(fx, fy, 43f, 47f, 43f, 57f)));
                            for (int gy = 0; gy < 2; gy++)
                                for (int gx = 0; gx < 3; gx++)
                                    a = Mathf.Max(a, Fill(RRect(fx, fy, 20f + gx * 12f, 28f - gy * 12f, 3.2f, 3.2f, 1f)));
                            break;
                        }
                        case "scan":
                        {
                            // viseur : quatre coins et un trait au milieu
                            float[] cxs = { 10f, 54f }, cys = { 10f, 54f };
                            float md = 99f;
                            for (int ix = 0; ix < 2; ix++)
                                for (int iy = 0; iy < 2; iy++)
                                {
                                    float sx = ix == 0 ? 1f : -1f, sy = iy == 0 ? 1f : -1f;
                                    md = Mathf.Min(md, SegDist(fx, fy, cxs[ix], cys[iy], cxs[ix] + sx * 16f, cys[iy]));
                                    md = Mathf.Min(md, SegDist(fx, fy, cxs[ix], cys[iy], cxs[ix], cys[iy] + sy * 16f));
                                }
                            md = Mathf.Min(md, SegDist(fx, fy, 17f, 32f, 47f, 32f));
                            a = Mathf.Clamp01(3.0f - md);
                            break;
                        }
                        case "alert":
                        {
                            // triangle d'avertissement avec un point d'exclamation
                            float[] tx = { 32f, 57f, 7f, 32f }, ty = { 56f, 10f, 10f, 56f };
                            float md = 99f;
                            for (int k = 0; k < 3; k++) md = Mathf.Min(md, SegDist(fx, fy, tx[k], ty[k], tx[k + 1], ty[k + 1]));
                            a = Mathf.Clamp01(2.8f - md);
                            a = Mathf.Max(a, Mathf.Clamp01(3.0f - SegDist(fx, fy, 32f, 26f, 32f, 40f)));
                            a = Mathf.Max(a, Fill(Mathf.Sqrt((fx - 32f) * (fx - 32f) + (fy - 18f) * (fy - 18f)) - 3.2f));
                            break;
                        }
                        case "note":
                        {
                            // note de musique : tete, hampe et crochet
                            a = Fill(Mathf.Sqrt((fx - 24f) * (fx - 24f) + 1.4f * (fy - 15f) * (fy - 15f)) - 10f);
                            a = Mathf.Max(a, Mathf.Clamp01(3.0f - SegDist(fx, fy, 33f, 17f, 33f, 52f)));
                            a = Mathf.Max(a, Mathf.Clamp01(3.0f - SegDist(fx, fy, 33f, 52f, 49f, 44f)));
                            a = Mathf.Max(a, Mathf.Clamp01(3.0f - SegDist(fx, fy, 49f, 44f, 49f, 36f)));
                            break;
                        }
                        case "plus":
                        {
                            a = Mathf.Max(Mathf.Clamp01(3.2f - SegDist(fx, fy, 32f, 14f, 32f, 50f)), Mathf.Clamp01(3.2f - SegDist(fx, fy, 14f, 32f, 50f, 32f)));
                            break;
                        }
                        case "bluetooth":
                        {
                            // rune Bluetooth
                            float[] bx = { 20f, 44f, 32f, 32f, 44f, 20f }, by = { 41f, 23f, 10f, 54f, 41f, 23f };
                            float bd = 99f;
                            for (int k = 0; k < 5; k++) bd = Mathf.Min(bd, SegDist(fx, fy, bx[k], by[k], bx[k + 1], by[k + 1]));
                            a = Mathf.Clamp01(2.8f - bd);
                            break;
                        }
                        case "resize":
                        {
                            // 3 traits en diagonale dans le coin bas droit (le plus long est le plus loin du coin)
                            float[] off = { 10f, 24f, 38f };
                            for (int k = 0; k < off.Length; k++)
                            {
                                float o = off[k];
                                float d = SegDist(fx, fy, o + 6f, 6f, 56f, 56f - o);
                                a = Mathf.Max(a, Mathf.Clamp01(3.2f - d));
                            }
                            break;
                        }
                    }
                    px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }
    }

    /// <summary>Un bouton de fenêtre (réduire, fermer, redimensionner) : petit carré ou rond 3D que le laser peut viser.</summary>
    public class Ar6dofChromeButton : MonoBehaviour, IAr6dofPointerTarget, IAr6dofPointerDrag
    {
        public enum Kind { Minimize, Close, Resize, Move, Settings, ResetSize }
        public Kind kind;
        public Ar6dofWindowChrome chrome;
        public Renderer bg, icon;
        public bool armed;      // fermeture en attente de confirmation
        public bool toggled;    // réglages : le panneau de cette fenêtre est ouvert
        bool hover, pressed;

        public void Refresh()
        {
            if (bg == null || chrome == null) return;
            var d = Ar6dofSettings.Current;
            var st = Ar6dofSettings.StyleFor(chrome.elem);
            bool mac = d.chromeStyle == 1;
            bool active = hover || pressed;
            Color c, ic = st.text;
            float iconAlpha = 1f;
            if (armed) { c = new Color(0.9f, 0.2f, 0.2f); ic = Color.white; }
            else if (mac && kind == Kind.Close) { c = new Color(1f, 0.37f, 0.34f); ic = new Color(0.3f, 0.05f, 0.05f); iconAlpha = active ? 1f : 0f; }
            else if (mac && kind == Kind.Minimize) { c = new Color(1f, 0.74f, 0.18f); ic = new Color(0.35f, 0.22f, 0f); iconAlpha = active ? 1f : 0f; }
            else if (kind == Kind.Close && active) { c = new Color(0.88f, 0.2f, 0.2f); ic = Color.white; }
            else
            {
                c = (active || (kind == Kind.Settings && toggled)) ? st.accent : st.button;
                c.a = Mathf.Lerp(1f, st.alpha, 0.5f);
            }
            if (kind == Kind.Settings && toggled && !active) ic = Ar6dofTheme.OnColor(c);   // roue allumée : contraste avec l'accent
            ic.a = iconAlpha;
            bg.sharedMaterial.color = c;
            if (icon != null) icon.sharedMaterial.color = ic;
        }

        public void OnPointerEnter() { hover = true; Refresh(); }
        public void OnPointerExit() { hover = false; Refresh(); }
        public void OnPointerHover(Vector3 world) { }

        public void OnPointerDown(Vector3 world, int button)
        {
            pressed = true;
            Refresh();
            if (kind == Kind.Resize) chrome.ResizeBegin(world);
            else if (kind == Kind.Move) chrome.MoveBegin(world, button);
        }

        public void OnPointerUp(Vector3 world, int button)
        {
            bool was = pressed;
            pressed = false;
            Refresh();
            if (kind == Kind.Resize) { if (was) chrome.ResizeEnd(); return; }
            if (kind == Kind.Move) { if (was) chrome.MoveEnd(); return; }
            if (was && hover) chrome.Click(kind);   // clic = appuyé ET relâché sur le bouton
        }

        public void OnDrag(Ray ray)
        {
            if (!pressed) return;
            if (kind == Kind.Resize) chrome.ResizeDrag(ray);
            else if (kind == Kind.Move) chrome.MoveDrag(ray);
        }
    }

    /// <summary>
    /// Boutons d'une fenêtre (écran virtuel ou menu) : Réduire et Fermer dans un coin au choix, style Windows ou Mac (paramètres globaux),
    /// et une seule poignée de redimensionnement dans le coin bas droit. Placés juste à l'extérieur de la fenêtre, donc jamais devant son contenu.
    /// </summary>
    public class Ar6dofWindowChrome : MonoBehaviour
    {
        public Transform target;
        public Ar6dofElem elem = Ar6dofElem.Menus;
        public Func<Vector2> halfSize;                 // demi-taille de la fenêtre en mètres (dans les axes de 'target')
        public Action onMinimize, onClose;
        public Func<bool> confirmClose;                // vrai : la fermeture demande un second clic
        public Action<Vector3> resizeBegin;            // vides : redimensionnement par défaut = échelle de 'target' (menus)
        public Action<Ray> resizeDrag;
        public Action resizeEnd;
        public Action onSettings;                      // roue à droite de la barre de déplacement (même place pour toutes les fenêtres) ; vide = pas de roue
        public Func<bool> settingsOn;                  // vrai : le panneau de réglages de cette fenêtre est ouvert (la roue s'allume)
        public Action onResetSize;                     // remet la fenetre a sa taille d'origine ; vide = l'echelle d'origine de 'target' (menus) ; les fenetres au redimensionnement personnalise n'ont pas ce bouton
        public Action moved;                           // appelé quand on commence à déplacer la fenêtre à la main (un panneau collé à son écran se détache)
        public Action<Vector3, int> moveBegin;         // vides : déplacement par défaut = le menu suit le laser
        public Action<Ray> moveDrag;
        public Action moveEnd;

        /// <summary>Vrai pour les menus (Canvas) : une barre de déplacement apparaît sous la fenêtre. Les écrans virtuels ont déjà la leur.</summary>
        public bool canMove;

        const float Gap = 0.008f;
        const float MoveLen = 4.2f, MoveThick = 0.7f;   // barre de déplacement : en multiples de S
        /// <summary>Taille d'un bouton en mètres : 4,5 cm x le réglage global "Taille des boutons de fenêtre".</summary>
        static float S { get { return 0.045f * Mathf.Clamp(Ar6dofSettings.Current.chromeScale, 0.5f, 3f); } }
        /// <summary>Taille d'un bouton de fenêtre en mètres, et dimensions de la barre de déplacement (pour aligner d'autres poignées).</summary>
        public static float ButtonSize { get { return S; } }
        public static float MoveBarLength { get { return S * MoveLen; } }
        public static float MoveBarThickness { get { return S * MoveThick; } }
        public static float EdgeGap { get { return Gap; } }
        float lastS = -1f;
        Ar6dofChromeButton bMin, bClose, bResize, bMove, bSet, bReset;
        bool lastSetOn;
        Ar6dofPassthrough pt;
        float grabDist;
        Vector3 grabOffset;
        GameObject group;
        float baseScale = 1f, startRadius = 0.1f, startScale = 1f, armedUntil;
        bool dirty = true;
        int lastStyle = -1;

        /// <summary>Demi-taille en mètres d'un menu Canvas.</summary>
        public static Func<Vector2> CanvasHalf(Canvas c)
        {
            return () =>
            {
                var rt = (RectTransform)c.transform;
                return rt.rect.size * 0.5f * c.transform.lossyScale.x;
            };
        }

        public static Ar6dofWindowChrome Create(string name, Transform target, Ar6dofElem elem, Func<Vector2> halfSize, Action onMinimize, Action onClose)
        {
            var go = new GameObject("Fenetre " + name);
            var c = go.AddComponent<Ar6dofWindowChrome>();
            c.target = target;
            c.elem = elem;
            c.halfSize = halfSize;
            c.onMinimize = onMinimize;
            c.onClose = onClose;
            c.baseScale = target != null ? target.localScale.x : 1f;
            c.Build();
            return c;
        }

        void OnEnable() { Ar6dofSettings.Changed += MarkDirty; }
        void OnDisable() { Ar6dofSettings.Changed -= MarkDirty; }
        void MarkDirty() { dirty = true; }

        void Build()
        {
            group = new GameObject("Boutons");
            group.transform.SetParent(transform, false);
            bMin = MakeBtn(Ar6dofChromeButton.Kind.Minimize, "minimize");
            bClose = MakeBtn(Ar6dofChromeButton.Kind.Close, "close");
            bResize = MakeBtn(Ar6dofChromeButton.Kind.Resize, "resize");
            bMove = MakeBtn(Ar6dofChromeButton.Kind.Move, "grip");
            bSet = MakeBtn(Ar6dofChromeButton.Kind.Settings, "gear");
            bReset = MakeBtn(Ar6dofChromeButton.Kind.ResetSize, "reset");
            group.SetActive(false);
        }

        Ar6dofChromeButton MakeBtn(Ar6dofChromeButton.Kind kind, string iconName)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = kind.ToString();
            var mc = go.GetComponent<MeshCollider>();
            if (mc != null) Destroy(mc);
            go.transform.SetParent(group.transform, false);
            go.transform.localScale = new Vector3(S, S, 1f);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = kind == Ar6dofChromeButton.Kind.Move ? new Vector3(1.05f, 2.0f, 0.05f) : new Vector3(1.25f, 1.25f, 0.05f);   // zone de visée un peu plus grande que le dessin (la barre fine est élargie en hauteur)
            var r = go.GetComponent<Renderer>();
            Shader sh = Ar6dofTheme.RoundedShader;
            if (sh == null) sh = Shader.Find("Sprites/Default");
            r.sharedMaterial = new Material(sh);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var ig = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ig.name = "Icone";
            var imc = ig.GetComponent<MeshCollider>();
            if (imc != null) Destroy(imc);
            ig.transform.SetParent(go.transform, false);
            ig.transform.localPosition = new Vector3(0f, 0f, -0.02f);
            ig.transform.localScale = new Vector3(0.62f, 0.62f, 1f);
            var ir = ig.GetComponent<Renderer>();
            var ish = Shader.Find("Sprites/Default");
            if (ish == null) ish = sh;
            var im = new Material(ish);
            im.mainTexture = Ar6dofIcons.Get(iconName);
            ir.sharedMaterial = im;
            ir.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ir.receiveShadows = false;

            var b = go.AddComponent<Ar6dofChromeButton>();
            b.kind = kind;
            b.chrome = this;
            b.bg = r;
            b.icon = ir;
            return b;
        }

        void ApplyShape()
        {
            var d = Ar6dofSettings.Current;
            bool mac = d.chromeStyle == 1;
            int corner = mac ? 999 : Mathf.Min(Ar6dofSettings.StyleFor(elem).corner, 10);
            foreach (var b in new[] { bMin, bClose, bResize, bSet, bReset })
            {
                Ar6dofTheme.SetRoundedSize(b.bg.sharedMaterial, S, S, corner);
                b.Refresh();
            }
            Ar6dofTheme.SetRoundedSize(bMove.bg.sharedMaterial, S * MoveLen, S * MoveThick, 999);   // la barre de déplacement est toujours une pastille
            bMove.Refresh();
            lastStyle = d.chromeStyle;
        }

        void LateUpdate()
        {
            if (target == null) { Destroy(gameObject); return; }
            bool vis = target.gameObject.activeInHierarchy;
            if (group.activeSelf != vis) group.SetActive(vis);
            if (!vis) return;
            float sc = S;
            if (Mathf.Abs(sc - lastS) > 1e-4f)
            {
                lastS = sc;
                foreach (var b in new[] { bMin, bClose, bResize, bSet, bReset }) b.transform.localScale = new Vector3(sc, sc, 1f);
                bMove.transform.localScale = new Vector3(sc * MoveLen, sc * MoveThick, 1f);
                var ic = bMove.icon != null ? bMove.icon.transform : null;
                if (ic != null) ic.localScale = new Vector3(0.8f, 0.62f, 1f);   // une simple ligne qui occupe presque toute la barre
                dirty = true;
            }
            if (dirty) { dirty = false; ApplyShape(); }
            if (bClose.armed && Time.unscaledTime > armedUntil) { bClose.armed = false; bClose.Refresh(); }

            var d = Ar6dofSettings.Current;
            bool mac = d.chromeStyle == 1;
            Vector2 hs = halfSize();
            // Un menu dont le pivot n'est pas au centre (pivot en haut : le haut reste en place quand la hauteur change) : les boutons se placent
            // autour du CENTRE de la fenetre, pas autour de son pivot. Sans ce decalage ils flottent loin de la fenetre.
            Vector3 centre = Vector3.zero;
            var trt = target as RectTransform;
            if (trt != null && trt.GetComponent<Canvas>() != null)
            {
                Vector2 full = trt.rect.size * target.lossyScale.x;
                centre = new Vector3((0.5f - trt.pivot.x) * full.x, (0.5f - trt.pivot.y) * full.y, 0f);
            }
            transform.SetPositionAndRotation(target.position + target.rotation * centre, target.rotation);
            int corner = Mathf.Clamp(d.chromeCorner, 0, 3);
            bool right = corner == 1 || corner == 3;
            bool top = corner == 0 || corner == 1;
            float yy = top ? hs.y + Gap + S * 0.5f : -hs.y - Gap - S * 0.5f;
            float sign = right ? 1f : -1f;
            float step = (S + Gap) * (mac ? 0.9f : 1f);
            float x0 = sign * (hs.x - S * 0.5f);
            if (corner == 3) x0 -= step;                 // en bas à droite : la poignée de taille occupe le coin, les boutons se décalent d'un cran
            bClose.gameObject.SetActive(onClose != null);
            bMin.gameObject.SetActive(onMinimize != null);
            bClose.transform.localPosition = new Vector3(x0, yy, 0f);                     // fermer = le plus près du coin
            bMin.transform.localPosition = new Vector3(x0 - sign * step, yy, 0f);
            // poignée de taille : SOUS la fenêtre, dans son aplomb (jamais à droite : elle tomberait sur l'écran voisin)
            bResize.transform.localPosition = new Vector3(hs.x - S * 0.5f, -hs.y - Gap - S * 0.5f, 0f);
            // remise a la taille d'origine : juste a gauche de la poignee de taille
            bool canReset = onResetSize != null || resizeBegin == null;
            bReset.gameObject.SetActive(canReset);
            bReset.transform.localPosition = new Vector3(hs.x - S * 0.5f - (S + Gap), -hs.y - Gap - S * 0.5f, 0f);
            // barre de déplacement : sous la fenêtre, au milieu
            bMove.gameObject.SetActive(canMove);
            float barY = -hs.y - Gap - S * MoveThick * 0.5f;
            bMove.transform.localPosition = new Vector3(0f, barY, 0f);
            // roue des réglages : juste à droite de la barre de déplacement, à sa hauteur (identique pour toutes les fenêtres)
            bSet.gameObject.SetActive(onSettings != null);
            bSet.transform.localPosition = new Vector3(S * MoveLen * 0.5f + Gap + S * 0.5f, barY, 0f);
            if (onSettings != null)
            {
                bool on = settingsOn != null && settingsOn();
                if (on != lastSetOn) { lastSetOn = on; bSet.toggled = on; bSet.Refresh(); }
            }
        }

        // ---- actions
        public void Click(Ar6dofChromeButton.Kind kind)
        {
            if (kind == Ar6dofChromeButton.Kind.Minimize) { if (onMinimize != null) onMinimize(); return; }
            if (kind == Ar6dofChromeButton.Kind.Settings) { if (onSettings != null) onSettings(); return; }
            if (kind == Ar6dofChromeButton.Kind.ResetSize) { ResetSize(); return; }
            if (kind != Ar6dofChromeButton.Kind.Close) return;
            if (confirmClose != null && confirmClose() && !bClose.armed)
            {
                bClose.armed = true;                      // 1er clic : le bouton devient rouge ; 2e clic dans les 2,5 s : fermeture
                armedUntil = Time.unscaledTime + 2.5f;
                bClose.Refresh();
                return;
            }
            bClose.armed = false;
            if (onClose != null) onClose();
        }

        public void ResizeBegin(Vector3 hit)
        {
            if (resizeBegin != null) { resizeBegin(hit); return; }
            startRadius = Mathf.Max(0.05f, (hit - target.position).magnitude);
            startScale = target.localScale.x;
        }

        public void ResizeDrag(Ray ray)
        {
            if (resizeDrag != null) { resizeDrag(ray); return; }
            var plane = new Plane(target.forward, target.position);
            float t;
            if (!plane.Raycast(ray, out t)) return;
            float radius = (ray.GetPoint(t) - target.position).magnitude;
            float sc = Mathf.Clamp(startScale * radius / startRadius, baseScale * 0.5f, baseScale * 3f);
            target.localScale = Vector3.one * sc;
        }

        public void ResizeEnd() { if (resizeEnd != null) resizeEnd(); }

        public void ResetSize()
        {
            if (onResetSize != null) { onResetSize(); return; }
            if (target != null) target.localScale = Vector3.one * baseScale;
        }

        // ---- déplacement du menu : le menu suit le laser en gardant la distance et en restant de face, à la verticale
        public void MoveEnd() { if (moveEnd != null) moveEnd(); }

        public void MoveBegin(Vector3 hit, int button = 0)
        {
            if (moved != null) moved();
            if (moveBegin != null) { moveBegin(hit, button); return; }
            grabDist = Vector3.Distance(Ar6dofPointer.LastRay.origin, hit);
            grabOffset = target.position - hit;
        }

        public void MoveDrag(Ray ray)
        {
            if (moveDrag != null) { moveDrag(ray); return; }
            if (target == null) return;
            if (pt == null) pt = FindFirstObjectByType<Ar6dofPassthrough>();
            Transform head = pt != null ? pt.HeadView : null;
            Vector3 p = ray.origin + ray.direction * grabDist + grabOffset;
            Quaternion r = target.rotation;
            if (head != null)
            {
                Vector3 d = p - head.position;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) r = Quaternion.LookRotation(d.normalized);   // l'axe Z du menu s'éloigne de toi : le texte est lisible de ton côté
            }
            target.SetPositionAndRotation(p, r);
        }
    }
}
