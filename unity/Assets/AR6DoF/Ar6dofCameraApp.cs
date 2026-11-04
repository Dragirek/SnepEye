using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>
    /// Appareil photo : aperçu en direct de ce que voit le casque (image de la caméra corrigée, sans la déformation des lentilles) et photo à la résolution
    /// de la caméra, avec ou sans les interfaces. Minuteur, les deux yeux (3D). Les fichiers PNG vont dans Images\AR6DoF\Photos.
    /// La fenêtre se masque toute seule juste avant la prise de vue. Rien ne tourne tant que la fenêtre est fermée.
    /// </summary>
    public class Ar6dofCameraApp : Ar6dofAppWindow
    {
        static readonly int[] Timers = { 0, 3, 5, 10 };

        int ui, timer, stereo, flip;
        string status = "";
        string lastPath = "";
        Text shootLabel;
        Canvas countCanvas;
        Text countText;
        RenderTexture preview;
        float lastPreview;

        public Ar6dofCameraApp(Ar6dofDock d) : base(d, "camera", "Appareil photo", "camera", "Apercu en direct et photo du passthrough", 700f)
        {
            ui = Get("ui", 1); timer = Get("timer", 1); stereo = Get("stereo", 0); flip = Get("flip", 0);
        }

        static int Get(string k, int def) { try { return PlayerPrefs.GetInt("ar6dof_cam_" + k, def); } catch (Exception) { return def; } }
        static void Put(string k, int v) { try { PlayerPrefs.SetInt("ar6dof_cam_" + k, v); } catch (Exception) { } }

        protected override void OnRebuildStart() { shootLabel = null; }

        protected override void OnVisible(bool on)
        {
            if (!on) { var pt = dock.passthrough; if (pt != null && pt.PhotoBusy) pt.CancelPhoto(); DestroyCount(); ReleasePreview(); }
        }

        public override void Destroy()
        {
            DestroyCount();
            ReleasePreview();
            base.Destroy();
        }

        void ReleasePreview()
        {
            if (preview != null) { preview.Release(); UnityEngine.Object.Destroy(preview); }
            preview = null;
        }

        // apercu en direct : l'image de la camera, redessinee une dizaine de fois par seconde tant que la fenetre est affichee
        protected override void OnTick()
        {
            var pt = Pt;
            if (pt == null || canvas == null || !canvas.gameObject.activeInHierarchy || pt.PhotoBusy) return;
            if (Time.unscaledTime - lastPreview < 0.08f) return;
            lastPreview = Time.unscaledTime;
            if (preview == null)
            {
                preview = new RenderTexture(640, 320, 0, RenderTextureFormat.ARGB32);
                preview.Create();
            }
            pt.RenderPreview(preview, flip != 0);
        }

        Ar6dofPassthrough Pt { get { return dock.passthrough; } }

        protected override void BuildMain()
        {
            if (Pt == null) { Para("Le passthrough n'est pas disponible dans cette scene.", 40, 20); return; }

            // apercu en direct (les deux yeux si "3D", sinon l'oeil gauche)
            bool two = stereo != 0;
            Vector2 sz = two ? new Vector2(IW, IW * 0.5f) : new Vector2(Mathf.Min(IW, 380f), Mathf.Min(IW, 380f));
            float cy = RowY(sz.y);
            var r = Ar6dofVrUi.Rect(content, "Apercu", new Vector2(0f, cy), sz);
            if (preview == null)
            {
                preview = new RenderTexture(640, 320, 0, RenderTextureFormat.ARGB32);
                preview.Create();
            }
            var ri = r.gameObject.AddComponent<RawImage>();
            ri.texture = preview;
            ri.uvRect = two ? new Rect(0f, 0f, 1f, 1f) : new Rect(0f, 0f, 0.5f, 1f);
            ri.raycastTarget = false;

            ToggleRow("Interfaces incluses", () => ui != 0, v => { ui = v ? 1 : 0; Put("ui", ui); }, 48f, 21);
            ToggleRow("Les deux yeux (3D)", () => stereo != 0, v => { stereo = v ? 1 : 0; Put("stereo", stereo); Rebuild(); }, 48f, 21);
            ToggleRow("Retourner la photo", () => flip != 0, v => { flip = v ? 1 : 0; Put("flip", flip); }, 48f, 21);

            Lbl("Minuteur", 30, 20).MakeAccent();
            var tn = new string[Timers.Length];
            for (int i = 0; i < tn.Length; i++) tn[i] = Timers[i] == 0 ? "Aucun" : Timers[i] + " s";
            Tabs(tn, timer, t => { timer = t; Put("timer", t); });

            Text lab;
            var shoot = BtnT("", OnShoot, out lab, 76f, 28);
            shootLabel = lab;
            Ar6dofTheme.SetSelected(shoot, true);
            refreshers.Add(UpdateShootLabel);

            var st = Para("", 56, 17);
            refreshers.Add(() => st.text = status.Length > 0 ? Ar6dofLoc.T(status) : "");

            BtnRow(48f, 21, "Ouvrir le dossier", (Action)(() => OpenFolder()));
            Para("Photos dans Images\\AR6DoF\\Photos, a la resolution de la camera.", 28, 16);
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
                withUi = ui != 0, withoutUi = ui == 0,
                stereo = stereo != 0, flipV = flip != 0,
                size = pt.NativePhotoSize,      // resolution reelle de la camera : agrandir n'ajouterait aucun detail
                aspect = 1f,
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
            if (tex != null) UnityEngine.Object.Destroy(tex);
            if (!string.IsNullOrEmpty(err)) status = err == "Annule" ? "Photo annulee" : Ar6dofLoc.F("Echec : {0}", err);
            else if (files.Count == 0) status = "Rien a enregistrer";
            else
            {
                lastPath = files[0];
                status = Ar6dofLoc.F("Photo enregistree : {0}", Path.GetFileName(files[0]));
            }
            if (canvas != null && canvas.gameObject.activeSelf) Refresh();
        }
    }
}
