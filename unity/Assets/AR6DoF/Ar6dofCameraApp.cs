using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Appareil photo : photographie ce que voit le casque (image de la caméra corrigée, sans la déformation des lentilles) avec les interfaces,
    /// sans les interfaces, ou les deux. Minuteur, qualité, format, les deux yeux (3D). Les fichiers PNG vont dans Images\AR6DoF\Photos.
    /// La fenêtre se masque toute seule juste avant la prise de vue. Rien ne tourne tant que la fenêtre est fermée.
    /// </summary>
    public class Ar6dofCameraApp : Ar6dofAppWindow
    {
        static readonly string[] ModeNames = { "Avec interfaces", "Sans interfaces", "Les deux" };
        static readonly int[] Timers = { 0, 3, 5, 10 };
        static readonly int[] Sizes = { 1080, 1440, 2160 };
        static readonly string[] AspectNames = { "Carre", "4:3", "16:9" };
        static readonly float[] Aspects = { 1f, 4f / 3f, 16f / 9f };

        int mode, timer, size = 1, aspect, stereo, flip;
        string status = "";
        string lastPath = "";
        Texture2D thumb;
        Text shootLabel;
        Canvas countCanvas;
        Text countText;

        public Ar6dofCameraApp(Ar6dofDock d) : base(d, "camera", "Appareil photo", "camera", "Photo du passthrough, avec ou sans les interfaces", 700f)
        {
            mode = Get("mode", 2); timer = Get("timer", 1); size = Get("size", 1); aspect = Get("aspect", 0); stereo = Get("stereo", 0); flip = Get("flip", 0);
        }

        static int Get(string k, int def) { try { return PlayerPrefs.GetInt("ar6dof_cam_" + k, def); } catch (Exception) { return def; } }
        static void Put(string k, int v) { try { PlayerPrefs.SetInt("ar6dof_cam_" + k, v); } catch (Exception) { } }

        protected override void OnRebuildStart() { shootLabel = null; }

        protected override void OnVisible(bool on)
        {
            if (!on) { var pt = dock.passthrough; if (pt != null && pt.PhotoBusy) pt.CancelPhoto(); DestroyCount(); }
        }

        public override void Destroy()
        {
            DestroyCount();
            if (thumb != null) UnityEngine.Object.Destroy(thumb);
            thumb = null;
            base.Destroy();
        }

        Ar6dofPassthrough Pt { get { return dock.passthrough; } }

        protected override void BuildMain()
        {
            if (Pt == null) { Para("Le passthrough n'est pas disponible dans cette scene.", 40, 20); return; }

            Lbl("Contenu de la photo", 30, 20).MakeAccent();
            Tabs(ModeNames, mode, t => { mode = t; Put("mode", t); });
            Para(mode == 0 ? "Comme tu le vois : la camera avec les fenetres, l'horloge, le dock..." :
                 mode == 1 ? "Seulement la camera : aucune interface, comme une vraie photo." :
                 "Deux fichiers : _avec et _sans les interfaces.", 30, 17);

            Lbl("Minuteur", 30, 20).MakeAccent();
            var tn = new string[Timers.Length];
            for (int i = 0; i < tn.Length; i++) tn[i] = Timers[i] == 0 ? "Aucun" : Timers[i] + " s";
            Tabs(tn, timer, t => { timer = t; Put("timer", t); });

            Lbl("Qualite (cote d'un oeil)", 30, 20).MakeAccent();
            var sn = new string[Sizes.Length];
            for (int i = 0; i < sn.Length; i++) sn[i] = Sizes[i] + " px";
            Tabs(sn, size, t => { size = t; Put("size", t); });

            Lbl("Format", 30, 20).MakeAccent();
            Tabs(AspectNames, aspect, t => { aspect = t; Put("aspect", t); });

            ToggleRow("Les deux yeux (3D)", () => stereo != 0, v => { stereo = v ? 1 : 0; Put("stereo", stereo); }, 48f, 21);
            ToggleRow("Retourner la photo", () => flip != 0, v => { flip = v ? 1 : 0; Put("flip", flip); }, 48f, 21);
            Para("Si la photo sort a l'envers, active « Retourner la photo ».", 30, 16);

            Space(6f);
            Text lab;
            var shoot = BtnT("", OnShoot, out lab, 76f, 28);
            shootLabel = lab;
            Ar6dofTheme.SetSelected(shoot, true);
            refreshers.Add(UpdateShootLabel);

            var st = Para("", 56, 17);
            refreshers.Add(() => st.text = status.Length > 0 ? Ar6dofLoc.T(status) : "");

            if (thumb != null)
            {
                float maxW = IW, maxH = 300f;
                float k = Mathf.Min(maxW / thumb.width, maxH / thumb.height);
                Vector2 sz = new Vector2(thumb.width * k, thumb.height * k);
                float cy = RowY(sz.y);
                var r = Ar6dofVrUi.Rect(content, "Vignette", new Vector2(0f, cy), sz);
                var ri = r.gameObject.AddComponent<RawImage>();
                ri.texture = thumb;
                ri.raycastTarget = false;
            }
            var row = new List<object>();
            row.Add("Ouvrir le dossier"); row.Add((Action)(() => OpenFolder()));
            BtnRow(48f, 21, row.ToArray());
            Para("Dossier : Images\\AR6DoF\\Photos", 28, 16);
        }

        static void OpenFolder()
        {
            string dir = Ar6dofPassthrough.PhotosFolder;
            try { Directory.CreateDirectory(dir); } catch (Exception) { }
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); }
            catch (Exception)
            {
                try { Application.OpenURL("file:///" + dir.Replace('\\', '/')); } catch (Exception) { }
            }
        }

        void UpdateShootLabel()
        {
            if (shootLabel == null) return;
            var pt = Pt;
            shootLabel.text = Ar6dofLoc.T(pt != null && pt.PhotoBusy ? "Annuler" : "Prendre la photo");
        }

        void OnShoot()
        {
            var pt = Pt;
            if (pt == null) return;
            if (pt.PhotoBusy) { pt.CancelPhoto(); return; }
            var o = new Ar6dofPassthrough.PhotoOptions
            {
                withUi = mode != 1, withoutUi = mode != 0,
                stereo = stereo != 0, flipV = flip != 0,
                size = Sizes[Mathf.Clamp(size, 0, Sizes.Length - 1)],
                aspect = Aspects[Mathf.Clamp(aspect, 0, Aspects.Length - 1)],
            };
            int delay = Timers[Mathf.Clamp(timer, 0, Timers.Length - 1)];
            status = delay > 0 ? "Minuteur lance..." : "";
            if (delay > 0) ShowCount(delay);
            pt.TakePhoto(o, delay, OnCount, HideUi, OnDone);
            UpdateShootLabel();
        }

        // ------------------------------------------------------------------ minuteur
        void ShowCount(int sec)
        {
            DestroyCount();
            var head = Pt != null ? Pt.HeadView : null;
            if (head == null) return;
            countCanvas = Ar6dofVrUi.NewCanvas("Compte a rebours", new Vector2(260f, 260f));
            countText = Ar6dofVrUi.Label(countCanvas.transform, sec.ToString(), Vector2.zero, new Vector2(240f, 240f), 150, TextAnchor.MiddleCenter);
            Vector3 f = head.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            var tr = countCanvas.transform;
            tr.position = head.position + f * 1.3f;
            tr.rotation = Quaternion.LookRotation(tr.position - head.position, Vector3.up);
        }

        void DestroyCount()
        {
            if (countCanvas != null) UnityEngine.Object.Destroy(countCanvas.gameObject);
            countCanvas = null; countText = null;
        }

        void OnCount(int sec)
        {
            if (countText != null) countText.text = sec > 0 ? sec.ToString() : "";
            status = sec > 0 ? Ar6dofLoc.F("Photo dans {0} s...", sec) : "";
            if (canvas != null && canvas.gameObject.activeSelf) Refresh();
        }

        void HideUi(bool hide)
        {
            if (hide) DestroyCount();
            if (canvas != null) canvas.gameObject.SetActive(!hide && open);
        }

        void OnDone(List<string> files, Texture2D tex, string err)
        {
            DestroyCount();
            if (thumb != null) UnityEngine.Object.Destroy(thumb);
            thumb = tex != null ? Shrink(tex, 640) : null;
            if (tex != null) UnityEngine.Object.Destroy(tex);
            if (!string.IsNullOrEmpty(err)) status = err == "Annule" ? "Photo annulee" : Ar6dofLoc.F("Echec : {0}", err);
            else if (files.Count == 0) status = "Rien a enregistrer";
            else
            {
                lastPath = files[0];
                status = files.Count == 1 ? Ar6dofLoc.F("Photo enregistree : {0}", Path.GetFileName(files[0]))
                                          : Ar6dofLoc.F("{0} photos enregistrees : {1}...", files.Count, Path.GetFileName(files[0]));
            }
            if (canvas != null && canvas.gameObject.activeSelf) Rebuild();
        }

        // vignette : on ne garde pas une image de 4000 pixels en mémoire graphique pour un aperçu
        static Texture2D Shrink(Texture2D src, int maxW)
        {
            int w = Mathf.Min(maxW, src.width);
            int h = Mathf.Max(1, Mathf.RoundToInt((float)src.height * w / src.width));
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Texture2D res = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                res = new Texture2D(w, h, TextureFormat.RGB24, false);
                res.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                res.Apply(false, false);
            }
            finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
            return res;
        }
    }
}
