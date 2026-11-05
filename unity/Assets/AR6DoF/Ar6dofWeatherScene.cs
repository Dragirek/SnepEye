using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Petit monde météo en 3D, rendu « dessin animé » (shader Ar6dofToon : trois tons nets, liseré, lisse — pas de facettes) :
    /// une île flottante avec un arbre, un pin et un moulin dont les ailes tournent avec le vent ; un soleil à rayons ou une lune et des étoiles ;
    /// des nuages qui dérivent selon le vent ; pluie, grêle, neige, brouillard et éclairs selon le temps.
    /// Tout vit sous le Canvas de la fenêtre Météo : fenêtre fermée, l'objet est désactivé, donc aucun calcul. Ouverte : environ 190 petits objets au maximum
    /// (par temps clair, une dizaine), un seul matériau par couleur, ni ombres ni sondes.
    /// Le vent penche et secoue les arbres (traits de vent et feuilles emportées quand il est fort), la neige recouvre tout (île, troncs, moulin, arbres),
    /// les nuages naissent et s'effacent en grandissant / rétrécissant, le soleil et la lune suivent l'heure, et Burst() accélère le temps un instant
    /// (nuages, moulin, pluie, soleil filent) pour passer à l'heure suivante.
    /// Unités de la scène : 1 unité = la hauteur d'un grand nuage ; l'application met la scène à l'échelle de son cadre (la scène tient dans -1,4 .. +1,65 en hauteur).
    /// </summary>
    public class Ar6dofWeatherScene : MonoBehaviour
    {
        const int RainN = 90, SnowN = 80, CloudN = 6, StarN = 14, StreakN = 8, LeafN = 7;

        /// <summary>0..1 : éclair en cours (la fenêtre éclaircit le ciel).</summary>
        public float Flash { get; private set; }

        class Toon { public Material m; public Color c, shade; public float emit; public bool cloud, fog; public float snowK; }   // snowK : 0 = ne blanchit jamais, 1 = tout blanc sous la neige
        class Cloud { public Transform t; public float x, y, z, size, now, rate, row; }   // row : de combien le nuage est sous le haut du cadre
        class Drop { public Transform t; public float x, y, z, v, ph; }
        class SnowCap { public Transform t; public Vector3 baseScale; }
        class Streak { public Transform t; public float p, y, z, speed; }
        class Leaf { public Transform t; public float x, y, z, age, dur, ph; }

        readonly List<Toon> toons = new List<Toon>();
        readonly Cloud[] clouds = new Cloud[CloudN];
        readonly Drop[] rain = new Drop[RainN];
        readonly Drop[] snow = new Drop[SnowN];
        readonly Transform[] stars = new Transform[StarN];
        readonly float[] starPh = new float[StarN], starY = new float[StarN];
        const int MistN = 10;
        readonly Transform[] mist = new Transform[MistN];
        readonly float[] mistX = new float[MistN], mistY = new float[MistN], mistZ = new float[MistN], mistSx = new float[MistN], mistSy = new float[MistN],
                         mistAmp = new float[MistN], mistSp = new float[MistN], mistPh = new float[MistN];
        readonly List<SnowCap> caps = new List<SnowCap>();
        readonly Streak[] streaks = new Streak[StreakN];
        readonly Leaf[] leaves = new Leaf[LeafN];
        Shader shader, mistShader;
        Transform rig, world, island, sun, sunRays, moon, bolt, hub, treeA, treeB, canopyA, canopyB, blanket;
        Transform[] boltSeg = new Transform[4];
        Toon tCloud, tCloud2, tMist, tLeaves, tRain, tSnow, tHail, tCap, tWind;
        // cadre : le haut du cadre (en unites de la scene) place les nuages, et tout ce qui sort du cadre en largeur / hauteur est coupe (la profondeur reste libre)
        float topY = 1.93f, skyShift = 0.22f, cloudShift;
        RectTransform clipRt;
        Matrix4x4 clipLast;
        float FxTop { get { return topY + cloudShift - 1.0f; } }   // d'ou tombent pluie, neige et eclairs : le dessous des nuages
        bool snowMode;   // vrai : la flaque de flocons est de la grêle

        // état actuel (qui glisse vers la cible) et cible
        float dayMix = 1f, dayTarget = 1f;
        float dark, darkTarget;
        float snowMix, snowTarget;
        float rainAmt, rainTarget, snowAmt, snowAmtTarget, hailTarget, hailAmt, fogAmt, fogTarget;
        float sunScale, sunTarget, moonScale, moonTarget, starAmt, starTarget;
        int cloudTarget = 2;
        float windKmh, windX = 0.2f;
        float time, boltTimer = 2f, boltLeft;
        float burstLeft, burstDur = 1f, fast = 1f;      // Burst : le temps file un instant
        Vector2 sunCur = new Vector2(0.9f, 0.86f), sunGoal = new Vector2(0.9f, 0.86f), moonCur = new Vector2(0.9f, 0.86f), moonGoal = new Vector2(0.9f, 0.86f);

        /// <summary>Faux : ni traits de vent ni feuilles emportées (reglage « effets de vent » de la fenetre).</summary>
        public bool WindFx = true;
        bool thunder, dirty = true;
        int shownRain, shownSnow;

        static Mesh sphere, cylinder, cube, cone;

        // ------------------------------------------------------------------ création
        public static Ar6dofWeatherScene Create(Transform parent)
        {
            var go = new GameObject("Meteo 3D");
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<Ar6dofWeatherScene>();
            s.Build();
            return s;
        }

        static Mesh Prim(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(go);   // le maillage intégré reste ; on ne garde ni objet ni collisionneur
            return m;
        }

        /// <summary>Cône lisse : sommet en haut (y = 0,5), base de rayon 0,5 en bas (y = -0,5).</summary>
        static Mesh MakeCone()
        {
            const int N = 28;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tri = new List<int>();
            for (int i = 0; i < N; i++)
            {
                float a0 = 2f * Mathf.PI * i / N, am = 2f * Mathf.PI * (i + 0.5f) / N;
                v.Add(new Vector3(Mathf.Cos(a0) * 0.5f, -0.5f, Mathf.Sin(a0) * 0.5f));
                n.Add(new Vector3(Mathf.Cos(a0), 0.5f, Mathf.Sin(a0)).normalized);
                v.Add(new Vector3(0f, 0.5f, 0f));
                n.Add(new Vector3(Mathf.Cos(am), 0.5f, Mathf.Sin(am)).normalized);
            }
            for (int i = 0; i < N; i++)
            {
                tri.Add(2 * i); tri.Add(2 * i + 1); tri.Add(2 * ((i + 1) % N));
            }
            int c = v.Count;
            v.Add(new Vector3(0f, -0.5f, 0f)); n.Add(Vector3.down);
            int ring = v.Count;
            for (int i = 0; i < N; i++)
            {
                float a = 2f * Mathf.PI * i / N;
                v.Add(new Vector3(Mathf.Cos(a) * 0.5f, -0.5f, Mathf.Sin(a) * 0.5f)); n.Add(Vector3.down);
            }
            for (int i = 0; i < N; i++) { tri.Add(c); tri.Add(ring + i); tri.Add(ring + (i + 1) % N); }
            var m = new Mesh { name = "Ar6dofCone" };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }

        static Color ShadeOf(Color c)
        {
            return new Color(Mathf.Clamp01(c.r * 0.55f + 0.04f), Mathf.Clamp01(c.g * 0.60f + 0.06f), Mathf.Clamp01(c.b * 0.82f + 0.13f), 1f);
        }

        Toon Mat(Color c, float emit = 0f, float outline = 0.26f)
        {
            var t = new Toon { c = c, shade = ShadeOf(c), emit = emit };
            t.m = new Material(shader);
            t.m.SetFloat("_Outline", outline);
            t.m.SetFloat("_Emit", emit);
            t.m.SetColor("_OutlineColor", new Color(0.10f, 0.12f, 0.22f, 1f));
            t.m.SetColor("_Color", c);
            t.m.SetColor("_Shade", t.shade);
            toons.Add(t);
            return t;
        }

        /// <summary>Matériau du brouillard : translucide, bords doux, sans liseré (voir Ar6dofMist.shader).</summary>
        Toon MistMat()
        {
            var t = new Toon { c = new Color(0.84f, 0.88f, 0.92f), shade = Color.white, fog = true };
            t.m = new Material(mistShader != null ? mistShader : shader);
            t.m.SetColor("_Color", t.c);
            t.m.SetFloat("_Alpha", 0f);
            toons.Add(t);
            return t;
        }

        Transform Part(Transform parent, Mesh mesh, Toon t, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject("p");
            go.layer = gameObject.layer;
            var tr = go.transform;
            tr.SetParent(parent, false);
            tr.localPosition = pos;
            tr.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = t.m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return tr;
        }

        Transform Group(Transform parent, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go.transform;
        }

        /// <summary>Boule de feuillage + son bonnet de neige (invisible tant qu'il ne neige pas).</summary>
        void Blob(Transform parent, Toon t, Vector3 pos, Vector3 scale)
        {
            Part(parent, sphere, t, pos, scale);
            AddCap(parent, sphere, tCap, pos + new Vector3(0f, 0.31f * scale.y, 0f), new Vector3(0.85f * scale.x, 0.5f * scale.y, 0.85f * scale.z));
        }

        /// <summary>Neige en relief : grandit de 0 à 'scale' avec la neige.</summary>
        void AddCap(Transform parent, Mesh mesh, Toon t, Vector3 pos, Vector3 scale)
        {
            var tr = Part(parent, mesh, t, pos, Vector3.zero);
            tr.gameObject.SetActive(false);
            caps.Add(new SnowCap { t = tr, baseScale = scale });
        }

        /// <summary>Remplit un nuage avec la forme n° 'k' (0 cumulus, 1 gros rond, 2 petit rond, 3 petit duo, 4 deux bosses). Plus de voile long ni de tour haute.</summary>
        void BuildCloudShape(Transform g, int k)
        {
            switch (k)
            {
                case 1:   // gros nuage rond et compact (ni long ni haut)
                    Part(g, sphere, tCloud, new Vector3(-0.30f, 0f, 0f), new Vector3(0.62f, 0.56f, 0.56f));
                    Part(g, sphere, tCloud, new Vector3(0.30f, -0.02f, 0.02f), new Vector3(0.58f, 0.52f, 0.54f));
                    Part(g, sphere, tCloud, new Vector3(0f, 0.15f, 0f), new Vector3(0.60f, 0.54f, 0.54f));
                    Part(g, sphere, tCloud, new Vector3(0f, -0.18f, 0f), new Vector3(1.00f, 0.26f, 0.50f));
                    break;
                case 2:   // petit nuage rond
                    Part(g, sphere, tCloud, new Vector3(-0.18f, 0f, 0f), new Vector3(0.50f, 0.46f, 0.46f));
                    Part(g, sphere, tCloud, new Vector3(0.20f, 0.04f, 0.02f), new Vector3(0.54f, 0.50f, 0.48f));
                    Part(g, sphere, tCloud, new Vector3(0f, -0.14f, 0f), new Vector3(0.86f, 0.22f, 0.42f));
                    break;
                case 3:   // petit duo
                    Part(g, sphere, tCloud, new Vector3(-0.22f, 0f, 0f), new Vector3(0.48f, 0.42f, 0.42f));
                    Part(g, sphere, tCloud, new Vector3(0.24f, -0.03f, 0.02f), new Vector3(0.38f, 0.34f, 0.36f));
                    Part(g, sphere, tCloud, new Vector3(0.0f, -0.11f, 0f), new Vector3(0.90f, 0.20f, 0.40f));
                    break;
                case 4:   // deux grosses bosses
                    Part(g, sphere, tCloud, new Vector3(-0.38f, 0.10f, 0f), new Vector3(0.74f, 0.70f, 0.60f));
                    Part(g, sphere, tCloud, new Vector3(0.40f, 0.02f, 0.02f), new Vector3(0.84f, 0.62f, 0.58f));
                    Part(g, sphere, tCloud, new Vector3(0.02f, -0.06f, -0.03f), new Vector3(0.70f, 0.50f, 0.56f));
                    Part(g, sphere, tCloud, new Vector3(0f, -0.20f, 0f), new Vector3(1.45f, 0.26f, 0.52f));
                    break;
                default:  // cumulus classique
                    Part(g, sphere, tCloud, new Vector3(0f, 0f, 0f), new Vector3(0.70f, 0.62f, 0.62f));
                    Part(g, sphere, tCloud, new Vector3(-0.42f, -0.07f, 0.02f), new Vector3(0.52f, 0.46f, 0.50f));
                    Part(g, sphere, tCloud, new Vector3(0.44f, -0.07f, 0f), new Vector3(0.56f, 0.48f, 0.52f));
                    Part(g, sphere, tCloud, new Vector3(0.08f, 0.22f, 0f), new Vector3(0.52f, 0.48f, 0.50f));
                    Part(g, sphere, tCloud, new Vector3(-0.08f, -0.15f, -0.05f), new Vector3(1.10f, 0.30f, 0.52f));   // dessous plus plat
                    break;
            }
        }

        void Build()
        {
            shader = Resources.Load<Shader>("Ar6dofToon");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            mistShader = Resources.Load<Shader>("Ar6dofMist");
            if (sphere == null) sphere = Prim(PrimitiveType.Sphere);
            if (cylinder == null) cylinder = Prim(PrimitiveType.Cylinder);
            if (cube == null) cube = Prim(PrimitiveType.Cube);
            if (cone == null) cone = MakeCone();

            rig = Group(transform, "Plateau", Vector3.zero);
            world = Group(rig, "Monde", Vector3.zero);
            world.localScale = new Vector3(1f, 1f, 0.6f);   // profondeur ecrasee : le relief reste proche de la fenetre
            island = Group(world, "Ile", Vector3.zero);

            // --- île : herbe, terre en pointe vers le bas, quelques cailloux flottants
            var tGrass = Mat(new Color(0.46f, 0.80f, 0.38f)); tGrass.snowK = 1f;
            var tEarth = Mat(new Color(0.60f, 0.42f, 0.28f)); tEarth.snowK = 0.80f;
            var tRock = Mat(new Color(0.66f, 0.60f, 0.56f)); tRock.snowK = 1f;
            tCap = Mat(new Color(0.97f, 0.98f, 1f), 0f, 0.22f);   // neige en relief (couche sur l'ile, bonnets sur les arbres)
            Part(island, cylinder, tGrass, new Vector3(0f, -0.10f, 0f), new Vector3(2.3f, 0.10f, 2.3f));
            var earth = Part(island, cone, tEarth, new Vector3(0f, -0.64f, 0f), new Vector3(2.15f, 0.88f, 2.15f));
            earth.localRotation = Quaternion.Euler(180f, 0f, 0f);
            Part(island, sphere, tRock, new Vector3(-0.55f, -1.05f, 0.35f), new Vector3(0.22f, 0.20f, 0.22f));
            Part(island, sphere, tRock, new Vector3(0.45f, -1.18f, -0.25f), new Vector3(0.15f, 0.14f, 0.15f));
            blanket = Part(island, cylinder, tCap, new Vector3(0f, 0f, 0f), new Vector3(2.36f, 0.001f, 2.36f));
            blanket.gameObject.SetActive(false);

            // --- arbre feuillu : le tronc penche un peu, la cime beaucoup plus (elle plie au vent)
            tLeaves = Mat(new Color(0.28f, 0.66f, 0.34f)); tLeaves.snowK = 1f;
            var tTrunk = Mat(new Color(0.55f, 0.36f, 0.22f)); tTrunk.snowK = 0.70f;
            treeA = Group(island, "Arbre", new Vector3(-0.62f, 0f, 0.05f));
            Part(treeA, cylinder, tTrunk, new Vector3(0f, 0.27f, 0f), new Vector3(0.13f, 0.27f, 0.13f));
            canopyA = Group(treeA, "Cime", new Vector3(0f, 0.50f, 0f));
            Blob(canopyA, tLeaves, new Vector3(0f, 0.30f, 0f), new Vector3(0.86f, 0.80f, 0.86f));
            Blob(canopyA, tLeaves, new Vector3(-0.30f, 0.10f, 0.08f), new Vector3(0.56f, 0.52f, 0.56f));
            Blob(canopyA, tLeaves, new Vector3(0.30f, 0.14f, -0.06f), new Vector3(0.52f, 0.50f, 0.52f));

            // --- pin
            var tPine = Mat(new Color(0.20f, 0.52f, 0.36f)); tPine.snowK = 1f;
            treeB = Group(island, "Pin", new Vector3(0.18f, 0f, 0.62f));
            treeB.localScale = Vector3.one * 0.7f;
            Part(treeB, cylinder, tTrunk, new Vector3(0f, 0.16f, 0f), new Vector3(0.11f, 0.16f, 0.11f));
            canopyB = Group(treeB, "Cime", new Vector3(0f, 0.30f, 0f));
            Part(canopyB, cone, tPine, new Vector3(0f, 0.22f, 0f), new Vector3(0.70f, 0.60f, 0.70f));
            Part(canopyB, cone, tPine, new Vector3(0f, 0.62f, 0f), new Vector3(0.52f, 0.52f, 0.52f));
            AddCap(canopyB, cone, tCap, new Vector3(0f, 0.74f, 0f), new Vector3(0.40f, 0.34f, 0.40f));

            // --- moulin à vent : ses ailes tournent avec le vent
            var tWall = Mat(new Color(0.96f, 0.90f, 0.78f)); tWall.snowK = 0.9f;
            var tRoof = Mat(new Color(0.86f, 0.36f, 0.30f)); tRoof.snowK = 1f;
            var tBlade = Mat(new Color(0.98f, 0.97f, 0.94f));
            var tDark = Mat(new Color(0.34f, 0.26f, 0.22f)); tDark.snowK = 0.35f;
            var mill = Group(island, "Moulin", new Vector3(0.78f, 0f, -0.18f));
            Part(mill, cone, tWall, new Vector3(0f, 0.36f, 0f), new Vector3(0.50f, 0.72f, 0.50f));
            Part(mill, cone, tRoof, new Vector3(0f, 0.80f, 0f), new Vector3(0.42f, 0.30f, 0.42f));
            AddCap(mill, cone, tCap, new Vector3(0f, 0.86f, 0f), new Vector3(0.30f, 0.20f, 0.30f));
            Part(mill, cube, tDark, new Vector3(0f, 0.10f, -0.22f), new Vector3(0.10f, 0.20f, 0.04f));
            hub = Group(mill, "Ailes", new Vector3(0f, 0.62f, -0.20f));
            Part(hub, sphere, tDark, Vector3.zero, new Vector3(0.12f, 0.12f, 0.12f));
            for (int i = 0; i < 4; i++)
            {
                var arm = Group(hub, "Aile", Vector3.zero);
                arm.localRotation = Quaternion.Euler(0f, 0f, i * 90f);
                Part(arm, cube, tBlade, new Vector3(0f, 0.29f, -0.02f), new Vector3(0.12f, 0.50f, 0.025f));
            }

            // --- soleil : disque lumineux + rayons qui tournent
            var tSun = Mat(new Color(1f, 0.82f, 0.22f), 1f, 0.34f);
            var tRay = Mat(new Color(1f, 0.66f, 0.14f), 1f, 0.34f);
            sun = Group(world, "Soleil", new Vector3(1.40f, 1.02f, 0.9f));
            Part(sun, sphere, tSun, Vector3.zero, new Vector3(0.70f, 0.70f, 0.70f));
            sunRays = Group(sun, "Rayons", Vector3.zero);
            for (int i = 0; i < 10; i++)
            {
                float a = i * 36f * Mathf.Deg2Rad;
                var r = Part(sunRays, cone, tRay, new Vector3(Mathf.Cos(a) * 0.55f, Mathf.Sin(a) * 0.55f, 0f), new Vector3(0.12f, 0.20f, 0.12f));
                r.localRotation = Quaternion.Euler(0f, 0f, i * 36f - 90f);
            }

            // --- lune + étoiles
            var tMoon = Mat(new Color(0.96f, 0.95f, 0.84f), 0.7f, 0.24f);
            var tCrater = Mat(new Color(0.78f, 0.78f, 0.70f), 0.5f, 0.1f);
            moon = Group(world, "Lune", new Vector3(1.30f, 0.98f, 0.9f));
            Part(moon, sphere, tMoon, Vector3.zero, new Vector3(0.62f, 0.62f, 0.62f));
            Part(moon, sphere, tCrater, new Vector3(-0.10f, 0.08f, -0.27f), new Vector3(0.16f, 0.16f, 0.06f));
            Part(moon, sphere, tCrater, new Vector3(0.12f, -0.10f, -0.27f), new Vector3(0.11f, 0.11f, 0.05f));
            var tStar = Mat(new Color(1f, 0.97f, 0.80f), 1f, 0.1f);
            var rnd = new System.Random(7);
            for (int i = 0; i < StarN; i++)
            {
                float x = (float)(rnd.NextDouble() * 4.0 - 2.0), y = (float)(rnd.NextDouble() * 1.0 + 0.55);
                starY[i] = y;
                stars[i] = Part(world, sphere, tStar, new Vector3(x, y + skyShift, 1.1f), Vector3.one * 0.05f);
                starPh[i] = (float)rnd.NextDouble() * 6.28f;
            }

            // --- nuages : six formes différentes (cumulus, voile plat, tour haute, petit duo, deux bosses, traînée), toutes en vrais volumes 3D
            tCloud = Mat(new Color(0.98f, 0.98f, 1f), 0f, 0.22f); tCloud.cloud = true;
            tCloud2 = Mat(new Color(0.93f, 0.95f, 0.99f), 0f, 0.22f); tCloud2.cloud = true;   // un peu plus grise, pour les voiles et les traînées
            float[] cx = { -1.6f, -0.85f, -0.1f, 0.65f, 1.45f, -0.45f };
            float[] crow = { 0.50f, 0.70f, 0.58f, 0.78f, 0.52f, 0.86f };   // sous le haut du cadre : les nuages restent hauts, les arbres se voient
            float[] cz = { -0.30f, -0.55f, -0.05f, -0.35f, -0.10f, -0.65f };
            float[] cs = { 1.0f, 0.85f, 0.95f, 0.95f, 1.0f, 0.85f };
            int[] shape = { 0, 1, 4, 3, 2, 0 };
            float[] yaw = { 8f, -22f, 15f, -10f, 28f, 190f };      // tournés dans l'espace : on voit bien qu'ils ont du volume
            float[] roll = { 0f, 0f, 2f, 3f, -2f, 0f };
            for (int i = 0; i < CloudN; i++)
            {
                float cyy = topY - crow[i];
                var g = Group(world, "Nuage", new Vector3(cx[i], cyy, cz[i]));
                BuildCloudShape(g, shape[i]);
                g.localRotation = Quaternion.Euler(0f, yaw[i], roll[i]);
                clouds[i] = new Cloud { t = g, x = cx[i], y = cyy, z = cz[i], size = cs[i], now = 0f, rate = 0.85f + 0.12f * i, row = crow[i] };
                g.localScale = Vector3.zero;
                g.gameObject.SetActive(false);
            }

            // --- brouillard : bancs translucides, bas, à plusieurs profondeurs (derrière, à travers et devant l'île), qui dérivent lentement
            tMist = MistMat();
            float[] mx = { -1.5f, 0.2f, 1.4f, -0.6f, 0.9f, -1.2f, 0.4f, -0.2f, 1.6f, -1.8f };
            float[] my = { 0.30f, 0.16f, 0.36f, 0.04f, 0.10f, -0.12f, 0.42f, 0.22f, -0.04f, 0.12f };
            float[] mz = { 0.80f, 0.70f, 0.55f, 0.15f, -0.05f, -0.15f, -0.35f, -0.60f, -0.75f, -0.95f };
            float[] msx = { 2.4f, 2.8f, 2.2f, 2.6f, 2.4f, 2.0f, 2.2f, 2.8f, 2.4f, 2.0f };
            float[] msy = { 0.34f, 0.30f, 0.38f, 0.26f, 0.30f, 0.24f, 0.34f, 0.30f, 0.26f, 0.30f };
            for (int i = 0; i < MistN; i++)
            {
                mist[i] = Part(world, sphere, tMist, new Vector3(mx[i], my[i], mz[i]), Vector3.zero);
                mistX[i] = mx[i]; mistY[i] = my[i]; mistZ[i] = mz[i]; mistSx[i] = msx[i]; mistSy[i] = msy[i];
                mistAmp[i] = 0.35f + 0.08f * (i % 4);
                mistSp[i] = 0.13f + 0.035f * (i % 5);
                mistPh[i] = i * 1.9f;
                mist[i].gameObject.SetActive(false);
            }

            // --- pluie, neige, grêle
            tRain = Mat(new Color(0.40f, 0.68f, 1f), 1f, 0.2f);
            tSnow = Mat(new Color(1f, 1f, 1f), 1f, 0.2f);
            tHail = Mat(new Color(0.84f, 0.94f, 1f), 1f, 0.2f);
            for (int i = 0; i < RainN; i++)
            {
                var t = Part(world, sphere, tRain, Vector3.zero, new Vector3(0.028f, 0.14f, 0.028f));
                t.gameObject.SetActive(false);
                rain[i] = new Drop { t = t };
            }
            for (int i = 0; i < SnowN; i++)
            {
                var t = Part(world, sphere, tSnow, Vector3.zero, Vector3.one * 0.06f);
                t.gameObject.SetActive(false);
                snow[i] = new Drop { t = t };
            }

            // --- éclair
            var tBolt = Mat(new Color(1f, 0.95f, 0.55f), 1f, 0.3f);
            bolt = Group(world, "Eclair", Vector3.zero);
            for (int i = 0; i < boltSeg.Length; i++) boltSeg[i] = Part(bolt, cube, tBolt, Vector3.zero, new Vector3(0.06f, 0.3f, 0.03f));
            bolt.gameObject.SetActive(false);

            // --- vent : traits qui filent et feuilles emportées (visibles seulement quand il souffle fort)
            tWind = Mat(new Color(1f, 1f, 1f), 1f, 0f);
            var rw = new System.Random(11);
            for (int i = 0; i < StreakN; i++)
            {
                var t = Part(world, cube, tWind, Vector3.zero, Vector3.zero);
                t.gameObject.SetActive(false);
                streaks[i] = new Streak { t = t, p = 0f, y = 0.05f + (float)rw.NextDouble() * 1.1f, z = -0.9f + (float)rw.NextDouble() * 0.5f, speed = 0.8f + (float)rw.NextDouble() * 0.5f };
            }
            for (int i = 0; i < LeafN; i++)
            {
                var t = Part(world, sphere, tLeaves, Vector3.zero, Vector3.zero);
                t.gameObject.SetActive(false);
                leaves[i] = new Leaf { t = t, age = 9f, dur = 1f };
            }

            dirty = true;
            sunScale = 1f; sunTarget = 1f; moonScale = 0f;
        }

        void OnDestroy()
        {
            for (int i = 0; i < toons.Count; i++) if (toons[i].m != null) Destroy(toons[i].m);
        }

        // ------------------------------------------------------------------ réglage selon le temps
        /// <summary>
        /// Dit a la scene ou est son cadre : 'stage' (centre = centre du cadre), sa taille en pixels, le haut du cadre en unites de la scene ('top', pour placer les nuages) et
        /// de combien monter soleil, lune et etoiles ('skyLift') pour qu'ils restent a la meme hauteur dans le cadre quand l'ile descend. Tout ce qui sort du cadre est coupe.
        /// </summary>
        public void SetStage(RectTransform stage, Vector2 size, float top, float skyLift)
        {
            clipRt = stage; topY = top; skyShift = skyLift;
            for (int i = 0; i < StarN; i++) if (stars[i] != null) stars[i].localPosition = new Vector3(stars[i].localPosition.x, starY[i] + skyShift, stars[i].localPosition.z);
            for (int i = 0; i < toons.Count; i++) toons[i].m.SetVector("_ClipHalf", new Vector4(size.x * 0.5f, size.y * 0.5f, 1f, 0f));
            UpdateClip(true);
            dirty = true;
        }

        /// <summary>Monte (+) ou descend (-) les nuages et, avec eux, l'endroit d'ou tombent pluie, neige et eclairs (en unites de la scene).</summary>
        public void SetCloudShift(float v) { cloudShift = v; }

        void UpdateClip(bool force)
        {
            if (clipRt == null) return;
            var m = clipRt.worldToLocalMatrix;
            if (!force && m == clipLast) return;
            clipLast = m;
            for (int i = 0; i < toons.Count; i++) toons[i].m.SetMatrix("_ClipM", m);
        }

        void LateUpdate() { UpdateClip(false); }

        /// <summary>Choisit ce que la scène montre. 'snap' : sans transition (première image) ; les nuages, eux, poussent toujours en grandissant.</summary>
        public void Apply(WxCond cond, bool day, float cloudCover, float windSpeedKmh, float windFromDeg, float tempC, bool snap)
        {
            int n = Mathf.RoundToInt(Mathf.Clamp01(cloudCover / 100f) * CloudN);
            float d = 0f, r = 0f, s = 0f, h = 0f, f = 0f;
            thunder = false;
            switch (cond)
            {
                case WxCond.Clear: n = Mathf.Min(n, 1); break;
                case WxCond.MostlyClear: n = Mathf.Clamp(n, 1, 2); break;
                case WxCond.PartlyCloudy: n = Mathf.Clamp(n, 2, 4); d = 0.05f; break;
                case WxCond.Overcast: n = CloudN; d = 0.35f; break;
                case WxCond.Fog: n = Mathf.Clamp(n, 2, 4); d = 0.25f; f = 1f; break;
                case WxCond.Drizzle: n = Mathf.Max(n, 5); d = 0.40f; r = 0.25f; break;
                case WxCond.Rain: n = Mathf.Max(n, 5); d = 0.55f; r = 0.55f; break;
                case WxCond.HeavyRain: n = CloudN; d = 0.75f; r = 1f; break;
                case WxCond.Showers: n = Mathf.Max(n, 5); d = 0.50f; r = 0.70f; break;
                case WxCond.FreezingRain: n = Mathf.Max(n, 5); d = 0.50f; r = 0.50f; break;
                case WxCond.Snow: n = Mathf.Max(n, 5); d = 0.20f; s = 0.5f; break;
                case WxCond.HeavySnow: n = CloudN; d = 0.35f; s = 1f; break;
                case WxCond.SnowShowers: n = Mathf.Max(n, 4); d = 0.25f; s = 0.7f; break;
                case WxCond.Thunder: n = CloudN; d = 0.85f; r = 0.9f; thunder = true; break;
                default: n = CloudN; d = 0.80f; r = 0.5f; h = 0.6f; thunder = true; break;   // orage et grêle
            }
            cloudTarget = n;
            darkTarget = d;
            rainTarget = r;
            snowAmtTarget = s;
            hailTarget = h;
            fogTarget = f;
            // neige au sol : il neige -> tout blanchit ; gel sans neige -> un peu de givre ; verglas -> givre
            snowTarget = WxCodes.IsSnow(cond) ? 1f : (cond == WxCond.FreezingRain ? 0.45f : (tempC <= -5f ? 0.7f : 0f));
            dayTarget = day ? 1f : 0f;
            sunTarget = day && n <= 4 ? 1f : 0f;
            moonTarget = !day && n <= 5 ? 1f : 0f;
            starTarget = !day && n <= 3 && f < 0.5f ? 1f : 0f;
            windKmh = Mathf.Max(0f, windSpeedKmh);
            float k = Mathf.Clamp01(windKmh / 60f);
            windX = -Mathf.Sin(windFromDeg * Mathf.Deg2Rad) * k;
            if (Mathf.Abs(windX) < 0.04f) windX = windX < 0f ? -0.04f : 0.04f;   // jamais tout à fait immobile
            if (snap)
            {
                dayMix = dayTarget; dark = darkTarget; snowMix = snowTarget;
                rainAmt = rainTarget; snowAmt = snowAmtTarget; hailAmt = hailTarget; fogAmt = fogTarget;
                sunScale = sunTarget; moonScale = moonTarget; starAmt = starTarget;
                sunCur = sunGoal; moonCur = moonGoal;
            }
            dirty = true;
        }

        /// <summary>
        /// Place le soleil et la lune selon l'heure : 'sunFrac' = avancement du jour (0 lever .. 1 coucher), 'moonFrac' = avancement de la nuit (0 coucher .. 1 lever).
        /// Un nombre négatif = inconnu (position par défaut).
        /// </summary>
        public void SetClock(float sunFrac, float moonFrac, bool snap)
        {
            sunGoal = Arc(sunFrac < 0f ? 0.78f : sunFrac);
            moonGoal = Arc(moonFrac < 0f ? 0.72f : moonFrac);
            if (snap) { sunCur = sunGoal; moonCur = moonGoal; }
        }

        static Vector2 Arc(float f)
        {
            f = Mathf.Clamp01(f);
            return new Vector2(Mathf.Lerp(-1.5f, 1.5f, f), 0.62f + 0.42f * Mathf.Sin(f * Mathf.PI));
        }

        /// <summary>Le temps s'accélère pendant 'seconds' (nuages, moulin, pluie, soleil filent, puis ralentissent) : pour passer à l'heure suivante.</summary>
        public void Burst(float seconds)
        {
            burstDur = Mathf.Max(0.2f, seconds);
            burstLeft = burstLeft > 0f ? Mathf.Max(burstLeft, burstDur * 0.5f) : burstDur;   // déjà en route : on reste à pleine vitesse
        }

        // ------------------------------------------------------------------ animation
        static float Glide(float cur, float target, float speed, float dt)
        {
            return Mathf.MoveTowards(cur, target, speed * dt);
        }

        static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        void Repaint()
        {
            Color tint = Color.Lerp(new Color(0.42f, 0.48f, 0.82f), Color.white, dayMix);
            Color cDark = new Color(0.46f, 0.49f, 0.58f), sDark = new Color(0.26f, 0.28f, 0.37f);
            Color snowC = new Color(0.97f, 0.98f, 1f), snowS = new Color(0.70f, 0.78f, 0.94f);
            Color fogC = new Color(0.80f, 0.84f, 0.88f), fogS = new Color(0.64f, 0.68f, 0.76f);
            for (int i = 0; i < toons.Count; i++)
            {
                var t = toons[i];
                if (t.fog)
                {
                    // brouillard : gris-bleu clair le jour, plus sombre la nuit et sous les nuages sombres ; l'opacité suit la quantité
                    Color fc = Color.Lerp(new Color(0.78f, 0.82f, 0.88f), new Color(0.55f, 0.58f, 0.66f), dark * 0.6f);
                    fc = Color.Lerp(fc, new Color(0.98f, 0.99f, 1f), snowMix * 0.5f);
                    t.m.SetColor("_Color", new Color(fc.r * tint.r, fc.g * tint.g, fc.b * tint.b, 1f));
                    t.m.SetFloat("_Alpha", 0.30f * Mathf.Clamp01(fogAmt));
                    continue;
                }
                if (t.emit >= 0.5f) continue;
                Color c = t.c, s = t.shade;
                if (t.cloud) { c = Color.Lerp(c, cDark, dark); s = Color.Lerp(s, sDark, dark); }
                else
                {
                    float sk = snowMix * t.snowK;
                    if (sk > 0f) { c = Color.Lerp(c, snowC, sk); s = Color.Lerp(s, snowS, sk); }
                    if (fogAmt > 0.01f) { c = Color.Lerp(c, fogC, fogAmt * 0.55f); s = Color.Lerp(s, fogS, fogAmt * 0.55f); }
                }
                t.m.SetColor("_Color", new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, 1f));
                t.m.SetColor("_Shade", new Color(s.r * tint.r, s.g * tint.g, s.b * tint.b, 1f));
            }
        }

        /// <summary>Couche de neige sur l'île et bonnets sur les arbres, le moulin : grandissent avec la neige.</summary>
        void SetSnowGeo()
        {
            bool on = snowMix > 0.02f;
            if (blanket.gameObject.activeSelf != on) blanket.gameObject.SetActive(on);
            if (!on) { for (int i = 0; i < caps.Count; i++) if (caps[i].t.gameObject.activeSelf) caps[i].t.gameObject.SetActive(false); return; }
            float th = 0.075f * snowMix;
            blanket.localScale = new Vector3(2.36f, Mathf.Max(0.001f, th), 2.36f);
            blanket.localPosition = new Vector3(0f, th - 0.012f, 0f);
            for (int i = 0; i < caps.Count; i++)
            {
                var c = caps[i];
                if (!c.t.gameObject.activeSelf) c.t.gameObject.SetActive(true);
                c.t.localScale = c.baseScale * snowMix;
            }
        }

        void Respawn(Drop d, bool top, bool isSnow)
        {
            d.x = Random.Range(-1.55f, 1.55f);
            d.z = Random.Range(-0.8f, 0.8f);
            float ft = Mathf.Max(0.4f, FxTop);
            d.y = top ? Random.Range(ft - 0.14f, ft) : Random.Range(0.05f, ft);
            d.v = isSnow ? Random.Range(0.18f, 0.32f) : Random.Range(1.5f, 2.1f);
            d.ph = Random.Range(0f, 6.28f);
        }

        void SetCounts()
        {
            int wantRain = Mathf.RoundToInt(rainAmt * RainN);
            int wantSnow = Mathf.RoundToInt(Mathf.Max(snowAmt, hailAmt) * SnowN);
            bool hail = hailAmt > snowAmt;
            if (hail != snowMode)
            {
                snowMode = hail;
                var mat = hail ? tHail.m : tSnow.m;
                for (int i = 0; i < SnowN; i++) snow[i].t.GetComponent<MeshRenderer>().sharedMaterial = mat;
                float sc = hail ? 0.045f : 0.06f;
                for (int i = 0; i < SnowN; i++) snow[i].t.localScale = Vector3.one * sc;
            }
            while (shownRain < wantRain) { var d = rain[shownRain++]; Respawn(d, false, false); d.t.gameObject.SetActive(true); }
            while (shownRain > wantRain) rain[--shownRain].t.gameObject.SetActive(false);
            while (shownSnow < wantSnow) { var d = snow[shownSnow++]; Respawn(d, false, true); d.t.gameObject.SetActive(true); }
            while (shownSnow > wantSnow) snow[--shownSnow].t.gameObject.SetActive(false);
        }

        void Update()
        {
            float rdt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);   // temps réel
            // Burst : le temps de la scène file (x1 -> x12 -> x1)
            if (burstLeft > 0f)
            {
                burstLeft = Mathf.Max(0f, burstLeft - rdt);
                fast = 1f + 11f * Mathf.Sin(Mathf.PI * (1f - burstLeft / burstDur));
            }
            else fast = 1f;
            float dt = rdt * fast;   // temps de la scène
            float gs = 1f + (fast - 1f) * 0.4f;   // les transitions de couleur / quantité vont plus vite pendant un Burst
            time += dt;

            // transitions douces vers la cible
            float oDay = dayMix, oDark = dark, oSnow = snowMix, oFog = fogAmt;
            dayMix = Glide(dayMix, dayTarget, 1.2f * gs, rdt);
            dark = Glide(dark, darkTarget, 0.8f * gs, rdt);
            snowMix = Glide(snowMix, snowTarget, 0.5f * gs, rdt);
            fogAmt = Glide(fogAmt, fogTarget, 1f * gs, rdt);
            if (dirty || oDay != dayMix || oDark != dark || oSnow != snowMix || oFog != fogAmt) Repaint();
            if (dirty || oSnow != snowMix) SetSnowGeo();
            float oR = rainAmt, oS = snowAmt, oH = hailAmt;
            rainAmt = Glide(rainAmt, rainTarget, 1.2f * gs, rdt);
            snowAmt = Glide(snowAmt, snowAmtTarget, 1.2f * gs, rdt);
            hailAmt = Glide(hailAmt, hailTarget, 1.2f * gs, rdt);
            if (dirty || oR != rainAmt || oS != snowAmt || oH != hailAmt) SetCounts();
            sunScale = Glide(sunScale, sunTarget, 2f * gs, rdt);
            moonScale = Glide(moonScale, moonTarget, 2f * gs, rdt);
            starAmt = Glide(starAmt, starTarget, 1.5f * gs, rdt);
            dirty = false;

            // lumière du dessin animé : fixe par rapport à la scène (donc elle suit le plateau), plus bleutée la nuit
            Vector3 ld = Vector3.Lerp(new Vector3(0.30f, 0.70f, -0.60f), new Vector3(-0.40f, 0.80f, -0.55f), dayMix);
            Vector4 lw = transform.TransformDirection(ld.normalized);
            for (int i = 0; i < toons.Count; i++) toons[i].m.SetVector("_Light", lw);

            // plateau : légère inclinaison vers toi + balancement lent
            rig.localRotation = Quaternion.Euler(-14f, Mathf.Sin(time * 0.35f) * 6f, 0f);
            island.localPosition = new Vector3(0f, Mathf.Sin(time * 0.9f) * 0.035f, 0f);

            // vent : les arbres se penchent (le tronc un peu, la cime beaucoup) et plient par rafales, le moulin tourne
            float k = Mathf.Clamp01(windKmh / 60f);
            float lean = windX * 30f;
            float gust = 0.7f + 0.3f * Mathf.Sin(time * 0.8f) * Mathf.Sin(time * 1.9f + 0.6f);
            float swayA = Mathf.Sin(time * (1.7f + k * 3.4f)) * (1.2f + 12f * k) * gust;
            float swayB = Mathf.Sin(time * (2.3f + k * 3.8f) + 1.3f) * (1.0f + 10f * k) * gust;
            treeA.localRotation = Quaternion.Euler(0f, 0f, -(lean * 0.30f + swayA * 0.30f));
            canopyA.localRotation = Quaternion.Euler(0f, 0f, -(lean * 0.70f + swayA * 0.95f));
            treeB.localRotation = Quaternion.Euler(0f, 0f, -(lean * 0.21f + swayB * 0.25f));
            canopyB.localRotation = Quaternion.Euler(0f, 0f, -(lean * 0.50f + swayB * 0.80f));
            hub.localRotation = Quaternion.Euler(0f, 0f, hub.localEulerAngles.z - Mathf.Clamp(25f + windKmh * 6f, 25f, 520f) * dt);

            // soleil et lune suivent l'heure (glissent sur leur arc ; cachés, ils sautent directement à leur place)
            if (sunScale < 0.05f) sunCur = sunGoal; else sunCur = Vector2.MoveTowards(sunCur, sunGoal, 2.4f * fast * rdt);
            if (moonScale < 0.05f) moonCur = moonGoal; else moonCur = Vector2.MoveTowards(moonCur, moonGoal, 2.4f * fast * rdt);
            sun.localPosition = new Vector3(sunCur.x, sunCur.y + skyShift, 0.9f);
            moon.localPosition = new Vector3(moonCur.x, moonCur.y + skyShift, 0.9f);
            sun.localScale = Vector3.one * sunScale * 0.85f * (1f + 0.03f * Mathf.Sin(time * 2f));
            sun.gameObject.SetActive(sunScale > 0.01f);
            if (sunScale > 0.01f) sunRays.localRotation = Quaternion.Euler(0f, 0f, time * 18f);
            moon.localScale = Vector3.one * moonScale * 0.9f;
            moon.gameObject.SetActive(moonScale > 0.01f);
            for (int i = 0; i < StarN; i++)
            {
                bool on = starAmt > 0.01f;
                if (stars[i].gameObject.activeSelf != on) stars[i].gameObject.SetActive(on);
                if (on) stars[i].localScale = Vector3.one * (0.045f * starAmt * (0.65f + 0.35f * Mathf.Sin(time * 2.2f + starPh[i])));
            }

            // nuages : naissent et s'effacent en grandissant / rétrécissant (jamais d'apparition d'un coup), dérivent avec le vent
            float drift = (windX >= 0f ? 1f : -1f) * Mathf.Clamp(0.035f + windKmh * 0.009f, 0.035f, 0.55f);
            for (int i = 0; i < CloudN; i++)
            {
                var c = clouds[i];
                float target = i < cloudTarget ? c.size : 0f;
                c.now = Mathf.MoveTowards(c.now, target, c.rate * gs * rdt);
                bool on = c.now > 0.004f;
                if (c.t.gameObject.activeSelf != on) c.t.gameObject.SetActive(on);
                if (!on) continue;
                c.x += drift * dt;
                if (c.x > 2.6f) c.x = -2.6f; else if (c.x < -2.6f) c.x = 2.6f;
                c.t.localPosition = new Vector3(c.x, topY - c.row + cloudShift + Mathf.Sin(time * 0.7f + i * 1.7f) * 0.03f, c.z);
                c.t.localScale = Vector3.one * (c.size * Smooth(c.now / c.size));
            }

            // brouillard : bancs translucides à plusieurs profondeurs, qui dérivent lentement (plus vite avec le vent)
            bool fogOn = fogAmt > 0.01f;
            float fogK = 0.6f + Mathf.Clamp01(windKmh / 40f);
            for (int i = 0; i < MistN; i++)
            {
                if (mist[i].gameObject.activeSelf != fogOn) mist[i].gameObject.SetActive(fogOn);
                if (!fogOn) continue;
                float ph = time * mistSp[i] * fogK + mistPh[i];
                float x = mistX[i] + Mathf.Sin(ph) * mistAmp[i] * 2f;
                float y = mistY[i] + Mathf.Sin(ph * 0.7f + 1f) * 0.03f;
                mist[i].localPosition = new Vector3(x, y, mistZ[i]);
                float breathe = 1f + 0.06f * Mathf.Sin(ph * 1.3f);
                mist[i].localScale = new Vector3(mistSx[i] * breathe, mistSy[i], 0.9f) * Smooth(fogAmt);
            }

            // pluie : traînées bleues inclinées par le vent
            if (shownRain > 0)
            {
                float tilt = Mathf.Atan2(windX * 1.2f, 1.8f) * Mathf.Rad2Deg;
                Quaternion q = Quaternion.Euler(0f, 0f, -tilt);
                for (int i = 0; i < shownRain; i++)
                {
                    var d = rain[i];
                    d.y -= d.v * dt;
                    d.x += windX * 1.2f * dt;
                    if (d.y < 0.03f || d.x > 1.8f || d.x < -1.8f) Respawn(d, true, false);
                    d.t.localPosition = new Vector3(d.x, d.y, d.z);
                    d.t.localRotation = q;
                }
            }

            // neige (flocons qui tournoient) ou grêle (billes rapides)
            if (shownSnow > 0)
            {
                bool hail = snowMode;
                for (int i = 0; i < shownSnow; i++)
                {
                    var d = snow[i];
                    float v = hail ? 1.1f + d.v : d.v;
                    d.y -= v * dt;
                    d.x += windX * (hail ? 0.9f : 0.7f) * dt + (hail ? 0f : Mathf.Cos(time * 1.7f + d.ph) * 0.12f * dt);
                    if (d.y < 0.03f || d.x > 1.8f || d.x < -1.8f) Respawn(d, true, !hail);
                    d.t.localPosition = new Vector3(d.x, d.y, d.z + (hail ? 0f : Mathf.Sin(time * 1.3f + d.ph) * 0.05f));
                }
            }

            WindEffects(dt, k);

            // éclairs
            if (thunder)
            {
                boltTimer -= rdt;
                if (boltTimer <= 0f)
                {
                    boltTimer = Random.Range(2.5f, 6f);
                    boltLeft = 0.18f;
                    PlaceBolt();
                }
            }
            if (boltLeft > 0f)
            {
                boltLeft -= rdt;
                bolt.gameObject.SetActive(true);
                Flash = boltLeft > 0.09f ? 1f : 0.55f;
            }
            else
            {
                if (bolt.gameObject.activeSelf) bolt.gameObject.SetActive(false);
                Flash = Mathf.Max(0f, Flash - rdt * 4f);
            }
        }

        /// <summary>Traits blancs qui filent dans le sens du vent et feuilles arrachées à l'arbre : seulement quand il souffle (et si l'option est active).</summary>
        void WindEffects(float dt, float k)
        {
            float dir = windX >= 0f ? 1f : -1f;
            int wantStreaks = WindFx && windKmh >= 18f ? Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01((windKmh - 12f) / 45f) * StreakN), 2, StreakN) : 0;
            for (int i = 0; i < StreakN; i++)
            {
                var st = streaks[i];
                bool wanted = i < wantStreaks;
                if (st.p <= 0f && !wanted)
                {
                    if (st.t.gameObject.activeSelf) st.t.gameObject.SetActive(false);
                    st.p = 0f;
                    continue;
                }
                if (st.p == 0f) st.p = -Random.Range(0.05f, 0.9f);   // attend un peu : les traits ne partent pas tous ensemble
                st.p += st.speed * (0.25f + 0.9f * k) * dt;
                if (st.p <= 0f)
                {
                    if (st.t.gameObject.activeSelf) st.t.gameObject.SetActive(false);
                    continue;
                }
                if (st.p >= 1f)
                {
                    st.p = 0f;
                    st.y = Random.Range(0.05f, 1.15f);
                    st.z = Random.Range(-0.9f, -0.4f);
                    if (st.t.gameObject.activeSelf) st.t.gameObject.SetActive(false);
                    continue;
                }
                if (!st.t.gameObject.activeSelf) st.t.gameObject.SetActive(true);
                float env = Mathf.Sin(st.p * Mathf.PI);
                float x = Mathf.Lerp(-2.3f, 2.3f, dir > 0f ? st.p : 1f - st.p);
                st.t.localPosition = new Vector3(x, st.y + Mathf.Sin(st.p * 9f + i) * 0.03f, st.z);
                st.t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(st.p * 7f + i) * 6f);
                st.t.localScale = new Vector3((0.28f + 0.55f * k) * env, 0.014f, 0.014f);
            }

            int wantLeaves = WindFx && windKmh >= 32f ? Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01((windKmh - 28f) / 40f) * LeafN), 1, LeafN) : 0;
            for (int i = 0; i < LeafN; i++)
            {
                var lf = leaves[i];
                if (lf.age >= lf.dur)
                {
                    if (i >= wantLeaves) { if (lf.t.gameObject.activeSelf) lf.t.gameObject.SetActive(false); continue; }
                    lf.x = -0.62f + Random.Range(-0.35f, 0.35f);
                    lf.y = 0.65f + Random.Range(0f, 0.5f);
                    lf.z = 0.05f + Random.Range(-0.2f, 0.2f);
                    lf.age = 0f;
                    lf.dur = Random.Range(2.2f, 3.6f);
                    lf.ph = Random.Range(0f, 6.28f);
                    lf.t.gameObject.SetActive(true);
                }
                lf.age += dt;
                lf.x += dir * (0.6f + 2.6f * k) * dt;
                lf.y += (-0.10f + Mathf.Sin(time * 5f + lf.ph) * 0.45f) * dt;
                float env = Mathf.Sin(Mathf.Clamp01(lf.age / lf.dur) * Mathf.PI);
                lf.t.localPosition = new Vector3(lf.x, Mathf.Max(0.02f, lf.y), lf.z);
                lf.t.localRotation = Quaternion.Euler(lf.age * 380f, lf.age * 260f, 0f);
                lf.t.localScale = new Vector3(0.075f, 0.022f, 0.05f) * env;
            }
        }

        void PlaceBolt()
        {
            float x = Random.Range(-1.1f, 1.1f);
            float f = Mathf.Max(0.5f, FxTop) / 0.80f;   // l'eclair part du dessous des nuages, ou qu'ils soient
            var p = new Vector2[5];
            p[0] = new Vector2(x, 0.80f * f);
            p[1] = new Vector2(x + 0.18f, 0.58f * f);
            p[2] = new Vector2(x - 0.10f, 0.38f * f);
            p[3] = new Vector2(x + 0.14f, 0.20f * f);
            p[4] = new Vector2(x - 0.02f, 0.04f);
            for (int i = 0; i < boltSeg.Length; i++)
            {
                Vector2 a = p[i], b = p[i + 1];
                Vector2 dv = b - a;
                float len = dv.magnitude;
                boltSeg[i].localPosition = new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, -0.75f);
                boltSeg[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-dv.x, dv.y) * Mathf.Rad2Deg);
                boltSeg[i].localScale = new Vector3(0.06f, len * 1.08f, 0.03f);
            }
        }
    }
}
