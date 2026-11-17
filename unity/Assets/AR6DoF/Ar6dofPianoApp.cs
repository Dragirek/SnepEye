using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AR6DoF
{
    /// <summary>Touche de piano : la note part des l'appui du laser (pas au relachement), comme sur un vrai clavier.</summary>
    public sealed class Ar6dofPianoKey : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public int index;
        public Action<int, bool> onChange;   // (touche, enfoncee)
        bool down;

        public void OnPointerDown(PointerEventData e) { Set(true); }
        public void OnPointerUp(PointerEventData e) { Set(false); }
        public void OnPointerExit(PointerEventData e) { Set(false); }
        void OnDisable() { down = false; }

        void Set(bool v)
        {
            if (down == v) return;
            down = v;
            if (onChange != null) onChange(index, v);
        }
    }

    /// <summary>
    /// Piano de deux octaves, joue au laser : quatre timbres (piano, orgue, synthe, cloche), octave reglable, trois morceaux a ecouter ou a apprendre
    /// touche par touche (la touche a jouer s'allume en vert). Les notes sont fabriquees par le code au premier appui (aucun fichier) ;
    /// rien n'existe tant que le piano est ferme.
    /// </summary>
    public sealed class Ar6dofPianoApp : Ar6dofGameWindow
    {
        const int Keys = 24;                      // deux octaves : do a si, deux fois
        const float KeyH = 230f, BlackH = 140f;
        const string PrefVolume = "ar6dof_piano_volume", PrefTimbre = "ar6dof_piano_timbre", PrefNames = "ar6dof_piano_names";
        static readonly string[] TimbreNames = { "Piano", "Orgue", "Synthe", "Cloche" };
        static readonly string[] SongNames = { "Libre", "Gamme", "Au clair de la lune", "Ode a la joie" };
        static readonly string[] NoteFr = { "Do", "Re", "Mi", "Fa", "Sol", "La", "Si" };
        static readonly string[] NoteEn = { "C", "D", "E", "F", "G", "A", "B" };
        static readonly int[] WhiteSemis = { 0, 2, 4, 5, 7, 9, 11 };

        // morceaux : (note MIDI avec do central = 60, duree en temps)
        static readonly int[][] SongNotes =
        {
            null,
            new[] { 60, 62, 64, 65, 67, 69, 71, 72, 71, 69, 67, 65, 64, 62, 60 },
            new[] { 60, 60, 60, 62, 64, 62, 60, 64, 62, 62, 60, 60, 60, 60, 62, 64, 62, 60, 64, 62, 62, 60 },
            new[] { 64, 64, 65, 67, 67, 65, 64, 62, 60, 60, 62, 64, 64, 62, 62, 64, 64, 65, 67, 67, 65, 64, 62, 60, 60, 62, 64, 62, 60, 60 },
        };
        static readonly float[][] SongBeats =
        {
            null,
            new[] { 1f, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2 },
            new[] { 1f, 1, 1, 1, 2, 2, 1, 1, 1, 1, 4, 1, 1, 1, 1, 2, 2, 1, 1, 1, 1, 4 },
            new[] { 1f, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1.5f, 0.5f, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1.5f, 0.5f, 2 },
        };
        const float BeatSeconds = 0.5f;

        int timbre, octave = 4, song;
        bool names = true, learn, playing;
        int pos;                                      // morceau : prochaine note a jouer (ecoute) ou a trouver (apprentissage)
        float nextAt;
        string message = "";

        readonly Image[] keyImg = new Image[Keys];
        readonly Text[] keyName = new Text[Keys];
        readonly bool[] black = new bool[Keys];
        readonly bool[] held = new bool[Keys];
        readonly float[] flash = new float[Keys];     // instant ou le vert / bleu du jeu s'eteint
        Text msgText;

        public Ar6dofPianoApp(Ar6dofDock d) : base(d, "piano", "Piano", "piano", "Piano de deux octaves : joue au laser, ecoute des morceaux ou apprends-les touche par touche.", 800f)
        {
            try
            {
                timbre = Mathf.Clamp(PlayerPrefs.GetInt(PrefTimbre, 0), 0, Ar6dofGameSound.Timbres - 1);
                names = PlayerPrefs.GetInt(PrefNames, 1) != 0;
            }
            catch (Exception) { }
        }

        static float Volume
        {
            get { try { return Mathf.Clamp01(PlayerPrefs.GetFloat(PrefVolume, 0.8f)); } catch (Exception) { return 0.8f; } }
            set { try { PlayerPrefs.SetFloat(PrefVolume, Mathf.Clamp01(value)); } catch (Exception) { } }
        }

        int Start { get { return 12 * (octave + 1); } }                       // note MIDI de la premiere touche (do)
        int Shift { get { return 12 * (octave - 4); } }                       // transposition des morceaux avec l'octave
        int Target { get { return song > 0 && pos < SongNotes[song].Length ? SongNotes[song][pos] + Shift : -1; } }

        protected override void GameRebuildStart() { Array.Clear(keyImg, 0, Keys); Array.Clear(keyName, 0, Keys); msgText = null; }

        protected override void GameHidden() { playing = false; }

        protected override void GameClosed()
        {
            playing = false;
            Ar6dofGameSound.ClearNotes();
        }

        protected override void GameReset()
        {
            playing = false; learn = false; song = 0; pos = 0; message = "";
            Array.Clear(held, 0, Keys); Array.Clear(flash, 0, Keys);
        }

        string NoteName(int semi, bool withOctave)
        {
            int pc = semi % 12;
            int w = Array.IndexOf(WhiteSemis, pc);
            if (w < 0) return "";
            string n = Ar6dofLoc.Code == "fr" ? NoteFr[w] : NoteEn[w];
            return withOctave && pc == 0 ? n + (octave + semi / 12) : n;
        }

        protected override void GameBuild()
        {
            Para("Touche les notes au laser : la note part des l'appui. Choisis un morceau pour l'ecouter, ou active Apprendre : la touche a jouer s'allume en vert.", 56, 18);
            BtnRow(46f, 19,
                Ar6dofLoc.T("Timbre") + " : " + Ar6dofLoc.T(TimbreNames[timbre]), (Action)(() => { timbre = (timbre + 1) % Ar6dofGameSound.Timbres; try { PlayerPrefs.SetInt(PrefTimbre, timbre); } catch (Exception) { } Rebuild(); }),
                Ar6dofLoc.T("Noms des notes") + " : " + Ar6dofLoc.T(names ? "oui" : "non"), (Action)(() => { names = !names; try { PlayerPrefs.SetInt(PrefNames, names ? 1 : 0); } catch (Exception) { } UpdateNames(); Rebuild(); }));
            SliderRow("Octave", 2, 6, () => octave, v => { int o = Mathf.RoundToInt(v); if (o == octave) return; octave = o; pos = 0; playing = false; UpdateNames(); Paint(); }, v => Mathf.RoundToInt(v).ToString(), 50f, true);
            SliderRow("Volume", 0, 100, () => Mathf.Round(Volume * 100f), v => Volume = v / 100f, v => Mathf.RoundToInt(v) + " %", 50f, true);
            BtnRow(46f, 19,
                Ar6dofLoc.T("Morceau") + " : " + Ar6dofLoc.T(SongNames[song]), (Action)(() => { song = (song + 1) % SongNames.Length; pos = 0; playing = false; if (song == 0) learn = false; message = ""; Rebuild(); }),
                Ar6dofLoc.T(playing ? "Arreter" : "Ecouter"), (Action)TogglePlay,
                Ar6dofLoc.T("Apprendre") + " : " + Ar6dofLoc.T(learn ? "oui" : "non"), (Action)ToggleLearn);
            msgText = Lbl(message, 36, 22, TextAnchor.MiddleCenter);
            msgText.fontStyle = FontStyle.Bold;
            Space(2f);
            BuildKeys();
            y += KeyH + Gap;
            Paint();
        }

        void BuildKeys()
        {
            float ww = IW / 14f, top = y;
            int whites = 0;
            for (int pass = 0; pass < 2; pass++)          // d'abord les blanches, puis les noires par-dessus (elles recoivent le laser en premier)
            {
                whites = 0;
                for (int s = 0; s < Keys; s++)
                {
                    int pc = s % 12;
                    bool isBlack = pc == 1 || pc == 3 || pc == 6 || pc == 8 || pc == 10;
                    black[s] = isBlack;
                    if (!isBlack) whites++;
                    if (isBlack != (pass == 1)) continue;
                    int idx = s;
                    Image im;
                    if (!isBlack)
                    {
                        float cx = -IW * 0.5f + ww * (whites - 0.5f);
                        im = Ar6dofGameUi.Panel(content, new Vector2(cx, -(top + KeyH * 0.5f)), new Vector2(ww - 3f, KeyH), Color.white);
                        var t = Ar6dofGameUi.Txt(im.rectTransform, "", new Vector2(0f, -KeyH * 0.5f + 20f), new Vector2(ww - 4f, 28f), 16, new Color(0.3f, 0.3f, 0.34f), false);
                        keyName[s] = t;
                    }
                    else
                    {
                        float cx = -IW * 0.5f + ww * whites;   // sur la limite entre deux touches blanches
                        im = Ar6dofGameUi.Panel(content, new Vector2(cx, -(top + BlackH * 0.5f)), new Vector2(ww * 0.62f, BlackH), Color.black);
                    }
                    im.raycastTarget = true;
                    var key = im.gameObject.AddComponent<Ar6dofPianoKey>();
                    key.index = idx;
                    key.onChange = OnKey;
                    keyImg[s] = im;
                }
            }
            UpdateNames();
        }

        void UpdateNames()
        {
            for (int s = 0; s < Keys; s++)
                if (keyName[s] != null) keyName[s].text = names ? NoteName(s, true) : "";
        }

        Color KeyColor(int s)
        {
            bool b = black[s];
            bool target = (learn || playing) && Target == Start + s;
            if (held[s] || Time.unscaledTime < flash[s]) return b ? new Color(0.2f, 0.48f, 0.9f) : new Color(0.62f, 0.82f, 1f);
            if (target) return b ? new Color(0.08f, 0.55f, 0.25f) : new Color(0.5f, 0.92f, 0.55f);
            return b ? new Color(0.09f, 0.09f, 0.11f) : new Color(0.97f, 0.97f, 0.95f);
        }

        void Paint()
        {
            for (int s = 0; s < Keys; s++) if (keyImg[s] != null) keyImg[s].color = KeyColor(s);
            if (msgText != null) msgText.text = message;
        }

        void Say(string key) { message = Ar6dofLoc.T(key); if (msgText != null) msgText.text = message; }

        void Sound(int midi)
        {
            snd.on = true; snd.volume = Volume;
            snd.Note(midi, timbre, 1f);
        }

        void OnKey(int s, bool down)
        {
            if (s < 0 || s >= Keys) return;
            held[s] = down;
            if (down)
            {
                Sound(Start + s);
                if (learn && !playing)
                {
                    int target = Target;
                    if (target == Start + s)
                    {
                        pos++;
                        if (pos >= SongNotes[song].Length) { pos = 0; Say("Bravo ! Morceau termine."); }
                        else Say("Bien !");
                    }
                    else if (target >= 0) Say("Pas cette touche : cherche la verte.");
                }
            }
            Paint();
        }

        void TogglePlay()
        {
            if (playing) { playing = false; Say(""); Paint(); return; }
            if (song == 0) { Say("Choisis d'abord un morceau."); return; }
            learn = false;
            playing = true; pos = 0; nextAt = Time.unscaledTime + 0.3f;
            Say("Ecoute...");
            Rebuild();
        }

        void ToggleLearn()
        {
            if (song == 0) { Say("Choisis d'abord un morceau."); return; }
            learn = !learn;
            playing = false; pos = 0;
            message = learn ? Ar6dofLoc.T("Joue la touche verte.") : "";
            Rebuild();
        }

        protected override void GameTick()
        {
            bool expire = false;
            for (int s = 0; s < Keys; s++) if (flash[s] > 0f && Time.unscaledTime >= flash[s]) { flash[s] = 0f; expire = true; }
            if (playing && song > 0 && Time.unscaledTime >= nextAt)
            {
                if (pos >= SongNotes[song].Length)
                {
                    playing = false; pos = 0; Say("Fini.");
                    Rebuild();
                    return;
                }
                int midi = SongNotes[song][pos] + Shift, s = midi - Start;
                Sound(midi);
                if (s >= 0 && s < Keys) flash[s] = Time.unscaledTime + 0.28f;
                nextAt = Time.unscaledTime + SongBeats[song][pos] * BeatSeconds;
                pos++;
                expire = true;
            }
            if (expire) Paint();
        }
    }
}
