using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Panorama : plusieurs photos prises en tournant la tete sont posees sur un cylindre grace a l'orientation de la tete a chaque prise (aucune recherche de points communs :
    /// c'est le suivi de la tete qui place les images), puis fondues entre elles (poids plus fort au centre de chaque image). Bon pour une scene eloignee ; sur un sujet tres
    /// proche, le decalage de la tete entre deux prises se voit (parallaxe). Premiere ligne des images = BAS (comme Texture2D.GetPixels32). Calcul a faire hors du fil de Unity.
    /// </summary>
    public static class Ar6dofPano
    {
        /// <param name="frames">images (w x h)</param>
        /// <param name="rots">orientation de la tete a chaque prise</param>
        /// <param name="tan">tangente de la moitie du champ vertical d'une image (Ar6dofPassthrough.DisplayTan) ; horizontalement : tan * w / h</param>
        /// <param name="maxW">largeur maximale du resultat</param>
        public static Color32[] Build(List<Color32[]> frames, List<Quaternion> rots, int w, int h, float tan, int maxW, out int ow, out int oh)
        {
            ow = 0; oh = 0;
            int n = Math.Min(frames.Count, 63);
            if (n == 0 || rots.Count < n) return null;
            float ty = tan, tx = tan * w / h;
            Quaternion inv0 = Quaternion.Inverse(rots[0]);
            var rel = new Quaternion[n];
            for (int i = 0; i < n; i++) rel[i] = inv0 * rots[i];

            // etendue : les neuf points (coins, milieux des bords, centre) de chaque image, projetes sur le cylindre
            double thMin = double.MaxValue, thMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue;
            var fMin = new double[n]; var fMax = new double[n];
            double prevCenter = 0.0;
            for (int i = 0; i < n; i++)
            {
                Vector3 fw = rel[i] * Vector3.forward;
                double c = Math.Atan2(fw.x, fw.z);
                if (i > 0) c += 2.0 * Math.PI * Math.Round((prevCenter - c) / (2.0 * Math.PI));   // continuite d'une image a l'autre
                prevCenter = c;
                fMin[i] = double.MaxValue; fMax[i] = double.MinValue;
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b++)
                    {
                        Vector3 d = rel[i] * new Vector3(a * tx, b * ty, 1f);
                        double th = Math.Atan2(d.x, d.z);
                        th += 2.0 * Math.PI * Math.Round((c - th) / (2.0 * Math.PI));
                        double hr = Math.Sqrt(d.x * d.x + d.z * d.z);
                        if (hr < 0.05) continue;   // presque a la verticale : le cylindre ne sait pas le representer
                        double yc = d.y / hr;
                        fMin[i] = Math.Min(fMin[i], th); fMax[i] = Math.Max(fMax[i], th);
                        thMin = Math.Min(thMin, th); thMax = Math.Max(thMax, th);
                        yMin = Math.Min(yMin, yc); yMax = Math.Max(yMax, yc);
                    }
            }
            if (thMin > thMax || yMin > yMax) return null;
            yMin = Math.Max(yMin, -3.0); yMax = Math.Min(yMax, 3.0);
            double span = Math.Min(thMax - thMin, 2.0 * Math.PI);

            double f = (h * 0.5) / ty;   // pixels par unite de tangente (celle de l'image d'origine)
            double wf = span * f, hf = (yMax - yMin) * f;
            if (wf > maxW) { double k = maxW / wf; f *= k; wf = maxW; hf *= k; }
            if (hf > maxW) { double k = maxW / hf; f *= k; hf = maxW; wf *= k; }
            ow = Math.Max(8, (int)wf); oh = Math.Max(8, (int)hf);

            // matrices inverses (repere de la photo) et colonnes
            var m = new float[n][];
            for (int i = 0; i < n; i++)
            {
                Quaternion q = Quaternion.Inverse(rel[i]);
                Vector3 ex = q * Vector3.right, ey = q * Vector3.up, ez = q * Vector3.forward;
                // local = q * d : trois lignes de la matrice, local.x = ex.x * d.x + ey.x * d.y + ez.x * d.z, etc.
                m[i] = new float[] { ex.x, ey.x, ez.x, ex.y, ey.y, ez.y, ex.z, ey.z, ez.z };
            }
            var sinT = new float[ow]; var cosT = new float[ow];
            var thCol = new double[ow];
            for (int x = 0; x < ow; x++)
            {
                double th = thMin + (x + 0.5) / f;
                thCol[x] = th;
                sinT[x] = (float)Math.Sin(th); cosT[x] = (float)Math.Cos(th);
            }
            // pour chaque colonne, les images qui peuvent y contribuer (a un tour pres) : evite de tout tester pour chaque pixel
            var active = new long[ow];
            for (int x = 0; x < ow; x++)
                for (int i = 0; i < n && i < 63; i++)
                {
                    double dth = thCol[x] - fMin[i];
                    dth -= 2.0 * Math.PI * Math.Floor(dth / (2.0 * Math.PI));
                    if (dth <= fMax[i] - fMin[i] + 0.03) active[x] |= 1L << i;
                }
            var res = new Color32[ow * oh];
            for (int y = 0; y < oh; y++)
            {
                float yc = (float)(yMax - (y + 0.5) / f);
                int rowOut = (oh - 1 - y) * ow;   // resultat « premiere ligne = bas »
                for (int x = 0; x < ow; x++)
                {
                    float dx = sinT[x], dz = cosT[x];
                    float sr = 0f, sg = 0f, sb = 0f, sw = 0f;
                    long mask = active[x];
                    for (int i = 0; i < n; i++)
                    {
                        if (((mask >> i) & 1L) == 0L) continue;
                        float[] mi = m[i];
                        float lz = mi[6] * dx + mi[7] * yc + mi[8] * dz;
                        if (lz < 0.05f) continue;
                        float lx = mi[0] * dx + mi[1] * yc + mi[2] * dz;
                        float ly = mi[3] * dx + mi[4] * yc + mi[5] * dz;
                        float u = lx / lz / tx, v = ly / lz / ty;
                        if (Math.Abs(u) >= 1f || Math.Abs(v) >= 1f) continue;
                        float wgt = (1f - Math.Abs(u)) * (1f - Math.Abs(v));
                        float px = (u + 1f) * 0.5f * w - 0.5f, py = (v + 1f) * 0.5f * h - 0.5f;
                        int x0 = (int)Math.Floor(px), y0 = (int)Math.Floor(py);
                        if (x0 < 0 || y0 < 0 || x0 >= w - 1 || y0 >= h - 1) continue;
                        float fx = px - x0, fy = py - y0;
                        var fr = frames[i];
                        Color32 c00 = fr[y0 * w + x0], c10 = fr[y0 * w + x0 + 1], c01 = fr[(y0 + 1) * w + x0], c11 = fr[(y0 + 1) * w + x0 + 1];
                        float w00 = (1f - fx) * (1f - fy), w10 = fx * (1f - fy), w01 = (1f - fx) * fy, w11 = fx * fy;
                        float r = c00.r * w00 + c10.r * w10 + c01.r * w01 + c11.r * w11;
                        float g = c00.g * w00 + c10.g * w10 + c01.g * w01 + c11.g * w11;
                        float b = c00.b * w00 + c10.b * w10 + c01.b * w01 + c11.b * w11;
                        wgt *= wgt;   // le centre compte beaucoup plus que le bord : raccords discrets
                        sr += r * wgt; sg += g * wgt; sb += b * wgt; sw += wgt;
                    }
                    res[rowOut + x] = sw > 1e-5f
                        ? new Color32((byte)Mathf.Clamp(sr / sw, 0f, 255f), (byte)Mathf.Clamp(sg / sw, 0f, 255f), (byte)Mathf.Clamp(sb / sw, 0f, 255f), 255)
                        : new Color32(24, 24, 24, 255);
                }
            }
            return res;
        }
    }

    /// <summary>
    /// Ecriture d'un fichier video AVI (Motion JPEG : chaque image est un JPEG, lu par Windows Media Player, VLC, les editeurs video...). Sans son. Les images sont ecrites au fil de l'eau
    /// (pas de memoire qui grossit) ; a la fermeture, la cadence reelle (images / duree) est inscrite pour que la lecture ait la bonne vitesse.
    /// </summary>
    public sealed class Ar6dofAvi
    {
        FileStream fs;
        BinaryWriter bw;
        string path;
        int w, h;
        readonly List<int> sizes = new List<int>();
        readonly List<long> offsets = new List<long>();
        int maxSize;

        public int Frames { get { return sizes.Count; } }
        public string FilePath { get { return path; } }

        static void Four(BinaryWriter b, string s) { b.Write(Encoding.ASCII.GetBytes(s)); }

        /// <summary>Cree le fichier. Rend un message d'erreur, ou une chaine vide.</summary>
        public string Open(string filePath, int width, int height)
        {
            try
            {
                path = filePath; w = width; h = height;
                fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                bw = new BinaryWriter(fs);
                Four(bw, "RIFF"); bw.Write(0); Four(bw, "AVI ");
                Four(bw, "LIST"); bw.Write(192); Four(bw, "hdrl");
                Four(bw, "avih"); bw.Write(56);
                bw.Write(66667);          // 32 : microsecondes par image (ecrit a la fin)
                bw.Write(0);              // 36 : octets par seconde
                bw.Write(0);              // 40
                bw.Write(0x10);           // 44 : possede un index
                bw.Write(0);              // 48 : nombre d'images (ecrit a la fin)
                bw.Write(0);              // 52
                bw.Write(1);              // 56 : un flux
                bw.Write(0);              // 60 : taille de tampon (ecrite a la fin)
                bw.Write(w); bw.Write(h); // 64, 68
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);   // 72..87
                Four(bw, "LIST"); bw.Write(116); Four(bw, "strl");
                Four(bw, "strh"); bw.Write(56);
                Four(bw, "vids"); Four(bw, "MJPG");   // 108, 112
                bw.Write(0);              // 116 : drapeaux
                bw.Write(0);              // 120 : priorite, langue
                bw.Write(0);              // 124
                bw.Write(1000);           // 128 : echelle
                bw.Write(15000);          // 132 : cadence * echelle (ecrite a la fin)
                bw.Write(0);              // 136
                bw.Write(0);              // 140 : longueur (ecrite a la fin)
                bw.Write(0);              // 144 : taille de tampon (ecrite a la fin)
                bw.Write(-1);             // 148 : qualite
                bw.Write(0);              // 152
                bw.Write((short)0); bw.Write((short)0); bw.Write((short)w); bw.Write((short)h);   // 156 : rectangle
                Four(bw, "strf"); bw.Write(40);
                bw.Write(40); bw.Write(w); bw.Write(h);   // 172, 176, 180
                bw.Write((short)1); bw.Write((short)24);  // 184
                Four(bw, "MJPG");                          // 188
                bw.Write(w * h * 3);                       // 192
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);   // 196..211
                Four(bw, "LIST"); bw.Write(0); Four(bw, "movi");       // 212, 216 (taille ecrite a la fin), 220
                return "";
            }
            catch (Exception e)
            {
                Abort();
                return e.Message;
            }
        }

        /// <summary>Ajoute une image JPEG.</summary>
        public void Add(byte[] jpg)
        {
            if (bw == null || jpg == null || jpg.Length == 0) return;
            long start = fs.Position;
            Four(bw, "00dc");
            bw.Write(jpg.Length);
            bw.Write(jpg);
            if ((jpg.Length & 1) != 0) bw.Write((byte)0);
            offsets.Add(start - 220);   // depuis le mot « movi »
            sizes.Add(jpg.Length);
            if (jpg.Length > maxSize) maxSize = jpg.Length;
        }

        /// <summary>Termine le fichier ; 'fps' = cadence reelle. Rend un message d'erreur ou une chaine vide.</summary>
        public string Close(double fps)
        {
            if (bw == null) return "";
            try
            {
                if (fps < 0.5) fps = 0.5;
                long moviEnd = fs.Position;
                Four(bw, "idx1");
                bw.Write(16 * sizes.Count);
                for (int i = 0; i < sizes.Count; i++)
                {
                    Four(bw, "00dc");
                    bw.Write(0x10);
                    bw.Write((int)offsets[i]);
                    bw.Write(sizes[i]);
                }
                long end = fs.Position;
                bw.Flush();
                Patch(4, (int)(end - 8));
                Patch(32, (int)Math.Round(1000000.0 / fps));
                Patch(36, (int)Math.Min(int.MaxValue, maxSize * fps));
                Patch(48, sizes.Count);
                Patch(60, maxSize);
                Patch(132, (int)Math.Round(fps * 1000.0));
                Patch(140, sizes.Count);
                Patch(144, maxSize);
                Patch(216, (int)(moviEnd - 220));
                bw.Flush();
                bw.Close();
                fs.Dispose();
                bw = null; fs = null;
                return "";
            }
            catch (Exception e)
            {
                Abort();
                return e.Message;
            }
        }

        void Patch(long pos, int value)
        {
            long keep = fs.Position;
            fs.Position = pos;
            bw.Write(value);
            fs.Position = keep;
        }

        /// <summary>Ferme et supprime le fichier (video annulee ou en erreur).</summary>
        public void Abort()
        {
            try { if (bw != null) bw.Close(); } catch (Exception) { }
            try { if (fs != null) fs.Dispose(); } catch (Exception) { }
            bw = null; fs = null;
            try { if (path != null && File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }
    }
}
