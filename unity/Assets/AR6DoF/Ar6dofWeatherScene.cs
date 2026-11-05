using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// Petit monde météo en 3D, rendu « dessin animé » (shader Ar6dofToon : trois tons nets, liseré, lisse — pas de facettes) :
    /// une île flottante avec un arbre, un pin et un moulin dont les ailes tournent avec le vent ; un soleil à rayons ou une lune et des étoiles ;
    /// des nuages qui dérivent selon le vent ; pluie, grêle, neige, brouillard et éclairs selon le temps.
    /// Tout vit sous le Canvas de la fenêtre Météo : fenêtre fermée, l'objet est désactivé, donc aucun calcul. Ouverte : environ 170 petits objets au maximum
    /// (par temps clair, une dizaine), un seul matériau par couleur, ni ombres ni sondes.
    /// Unités de la scène : 1 unité = la hauteur d'un grand nuage ; l'application met la scène à l'échelle de son cadre.
    /// </summary>
    public class Ar6dofWeatherScene : MonoBehaviour
    {
        const int RainN = 90, SnowN = 80, CloudN = 6, StarN = 14;

        /// <summary>0..1 : éclair en cours (la fenêtre éclaircit le ciel).</summary>
        public float Flash { get; private set; }

        class Toon { public Material m; public Color c, shade; public float emit; public bool cloud, snowy; }
        class Cloud { public Transform t; public float x, y, z, size, now; }
        class Drop { public Transform t; public float x, y, z, v, ph; }

        readonly List<Toon> toons = new List<Toon>();
        readonly Cloud[] clouds = new Cloud[CloudN];
        readonly Drop[] rain = new Drop[RainN];
        readonly Drop[] snow = new Drop[SnowN];
        readonly Transform[] stars = new Transform[StarN];
        readonly float[] starPh = new float[StarN];
        readonly Transform[] mist = new Transform[3];
        readonly float[] mistX = new float[3];
        Shader shader;
        Transform rig, world, island, sun, sunRays, moon, bolt, hub, treeA, treeB;
        Transform[] boltSeg = new Transform[4];
        Toon tCloud, tGrass, tLeaves, tPine, tRoof, tRain, tSnow, tHail;
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

        void Build()
        {
            shader = Resources.Load<Shader>("Ar6dofToon");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (sphere == null) sphere = Prim(PrimitiveType.Sphere);
            if (cylinder == null) cylinder = Prim(PrimitiveType.Cylinder);
            if (cube == null) cube = Prim(PrimitiveType.Cube);
            if (cone == null) cone = MakeCone();

            rig = Group(transform, "Plateau", Vector3.zero);
            world = Group(rig, "Monde", Vector3.zero);
            world.localScale = new Vector3(1f, 1f, 0.6f);   // profondeur ecrasee : le relief reste proche de la fenetre
            island = Group(world, "Ile", Vector3.zero);

            // --- île : herbe, terre en pointe vers le bas, quelques cailloux flottants
            tGrass = Mat(new Color(0.46f, 0.80f, 0.38f)); tGrass.snowy = true;
            var tEarth = Mat(new Color(0.60f, 0.42f, 0.28f));
            var tRock = Mat(new Color(0.66f, 0.60f, 0.56f));
            Part(island, cylinder, tGrass, new Vector3(0f, -0.10f, 0f), new Vector3(2.3f, 0.10f, 2.3f));
            var earth = Part(island, cone, tEarth, new Vector3(0f, -0.64f, 0f), new Vector3(2.15f, 0.88f, 2.15f));
            earth.localRotation = Quaternion.Euler(180f, 0f, 0f);
            Part(island, sphere, tRock, new Vector3(-0.55f, -1.05f, 0.35f), new Vector3(0.22f, 0.20f, 0.22f));
            Part(island, sphere, tRock, new Vector3(0.45f, -1.18f, -0.25f), new Vector3(0.15f, 0.14f, 0.15f));

            // --- arbre feuillu
            tLeaves = Mat(new Color(0.28f, 0.66f, 0.34f)); tLeaves.snowy = true;
            var tTrunk = Mat(new Color(0.55f, 0.36f, 0.22f));
            treeA = Group(island, "Arbre", new Vector3(-0.62f, 0f, 0.05f));
            Part(treeA, cylinder, tTrunk, new Vector3(0f, 0.27f, 0f), new Vector3(0.13f, 0.27f, 0.13f));
            Part(treeA, sphere, tLeaves, new Vector3(0f, 0.80f, 0f), new Vector3(0.86f, 0.80f, 0.86f));
            Part(treeA, sphere, tLeaves, new Vector3(-0.30f, 0.60f, 0.08f), new Vector3(0.56f, 0.52f, 0.56f));
            Part(treeA, sphere, tLeaves, new Vector3(0.30f, 0.64f, -0.06f), new Vector3(0.52f, 0.50f, 0.52f));

            // --- pin
            tPine = Mat(new Color(0.20f, 0.52f, 0.36f)); tPine.snowy = true;
            treeB = Group(island, "Pin", new Vector3(0.18f, 0f, 0.62f));
            treeB.localScale = Vector3.one * 0.7f;
            Part(treeB, cylinder, tTrunk, new Vector3(0f, 0.16f, 0f), new Vector3(0.11f, 0.16f, 0.11f));
            Part(treeB, cone, tPine, new Vector3(0f, 0.52f, 0f), new Vector3(0.70f, 0.60f, 0.70f));
            Part(treeB, cone, tPine, new Vector3(0f, 0.92f, 0f), new Vector3(0.52f, 0.52f, 0.52f));

            // --- moulin à vent : ses ailes tournent avec le vent
            var tWall = Mat(new Color(0.96f, 0.90f, 0.78f));
            tRoof = Mat(new Color(0.86f, 0.36f, 0.30f)); tRoof.snowy = true;
            var tBlade = Mat(new Color(0.98f, 0.97f, 0.94f));
            var tDark = Mat(new Color(0.34f, 0.26f, 0.22f));
            var mill = Group(island, "Moulin", new Vector3(0.78f, 0f, -0.18f));
            Part(mill, cone, tWall, new Vector3(0f, 0.36f, 0f), new Vector3(0.50f, 0.72f, 0.50f));
            Part(mill, cone, tRoof, new Vector3(0f, 0.80f, 0f), new Vector3(0.42f, 0.30f, 0.42f));
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
                stars[i] = Part(world, sphere, tStar, new Vector3(x, y, 1.1f), Vector3.one * 0.05f);
                starPh[i] = (float)rnd.NextDouble() * 6.28f;
            }

            // --- nuages
            tCloud = Mat(new Color(0.98f, 0.98f, 1f), 0f, 0.22f); tCloud.cloud = true;
            float[] cx = { -1.6f, -0.85f, -0.1f, 0.65f, 1.45f, -0.45f };
            float[] cy = { 0.92f, 1.12f, 0.86f, 1.08f, 0.90f, 1.20f };
            float[] cz = { -0.30f, -0.55f, -0.05f, -0.35f, -0.10f, -0.65f };
            float[] cs = { 1.0f, 0.85f, 1.1f, 0.9f, 1.0f, 0.75f };
            for (int i = 0; i < CloudN; i++)
            {
                var g = Group(world, "Nuage", new Vector3(cx[i], cy[i], cz[i]));
                Part(g, sphere, tCloud, new Vector3(0f, 0f, 0f), new Vector3(0.70f, 0.62f, 0.62f));
                Part(g, sphere, tCloud, new Vector3(-0.42f, -0.07f, 0.02f), new Vector3(0.52f, 0.46f, 0.50f));
                Part(g, sphere, tCloud, new Vector3(0.44f, -0.07f, 0f), new Vector3(0.56f, 0.48f, 0.52f));
                Part(g, sphere, tCloud, new Vector3(0.08f, 0.22f, 0f), new Vector3(0.52f, 0.48f, 0.50f));
                Part(g, sphere, tCloud, new Vector3(-0.08f, -0.15f, -0.05f), new Vector3(1.10f, 0.30f, 0.52f));   // dessous plus plat
                clouds[i] = new Cloud { t = g, x = cx[i], y = cy[i], z = cz[i], size = cs[i], now = 0f };
                g.localScale = Vector3.zero;
                g.gameObject.SetActive(false);
            }

            // --- bancs de brouillard
            for (int i = 0; i < mist.Length; i++)
            {
                mist[i] = Part(world, sphere, tCloud, new Vector3(-0.8f + i * 0.8f, 0.12f + 0.05f * i, -0.55f - 0.1f * i), Vector3.zero);
                mistX[i] = -0.8f + i * 0.8f;
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

            dirty = true;
            sunScale = 1f; sunTarget = 1f; moonScale = 0f;
        }

        void OnDestroy()
        {
            for (int i = 0; i < toons.Count; i++) if (toons[i].m != null) Destroy(toons[i].m);
        }

        // ------------------------------------------------------------------ réglage selon le temps
        /// <summary>Choisit ce que la scène montre. 'snap' : sans transition (première image).</summary>
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
                case WxCond.Fog: n = Mathf.Clamp(n, 3, 5); d = 0.25f; f = 1f; break;
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
            snowTarget = WxCodes.IsSnow(cond) ? 1f : (tempC <= -5f ? 0.7f : 0f);
            dayTarget = day ? 1f : 0f;
            sunTarget = day && n <= 4 ? 1f : 0f;
            moonTarget = !day && n <= 5 ? 1f : 0f;
            starTarget = !day && n <= 3 && f < 0.5f ? 1f : 0f;
            windKmh = Mathf.Max(0f, windSpeedKmh);
            float k = Mathf.Clamp01(windKmh / 50f);
            windX = -Mathf.Sin(windFromDeg * Mathf.Deg2Rad) * k;
            if (Mathf.Abs(windX) < 0.04f) windX = windX < 0f ? -0.04f : 0.04f;   // jamais tout à fait immobile
            if (snap)
            {
                dayMix = dayTarget; dark = darkTarget; snowMix = snowTarget;
                rainAmt = rainTarget; snowAmt = snowAmtTarget; hailAmt = hailTarget; fogAmt = fogTarget;
                sunScale = sunTarget; moonScale = moonTarget; starAmt = starTarget;
                for (int i = 0; i < CloudN; i++) clouds[i].now = i < cloudTarget ? clouds[i].size : 0f;
            }
            dirty = true;
        }

        // ------------------------------------------------------------------ animation
        static float Glide(float cur, float target, float speed, float dt)
        {
            return Mathf.MoveTowards(cur, target, speed * dt);
        }

        void Repaint()
        {
            Color tint = Color.Lerp(new Color(0.42f, 0.48f, 0.82f), Color.white, dayMix);
            Color cDark = new Color(0.46f, 0.49f, 0.58f), sDark = new Color(0.26f, 0.28f, 0.37f);
            Color snowC = new Color(0.97f, 0.98f, 1f), snowS = new Color(0.70f, 0.78f, 0.94f);
            for (int i = 0; i < toons.Count; i++)
            {
                var t = toons[i];
                if (t.emit >= 0.5f) continue;
                Color c = t.c, s = t.shade;
                if (t.cloud) { c = Color.Lerp(c, cDark, dark); s = Color.Lerp(s, sDark, dark); }
                if (t.snowy) { c = Color.Lerp(c, snowC, snowMix); s = Color.Lerp(s, snowS, snowMix); }
                t.m.SetColor("_Color", new Color(c.r * tint.r, c.g * tint.g, c.b * tint.b, 1f));
                t.m.SetColor("_Shade", new Color(s.r * tint.r, s.g * tint.g, s.b * tint.b, 1f));
            }
        }

        void Respawn(Drop d, bool top, bool isSnow)
        {
            d.x = Random.Range(-1.55f, 1.55f);
            d.z = Random.Range(-0.8f, 0.8f);
            d.y = top ? Random.Range(0.78f, 0.92f) : Random.Range(0.05f, 0.9f);
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
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            time += dt;

            // transitions douces vers la cible
            float oDay = dayMix, oDark = dark, oSnow = snowMix;
            dayMix = Glide(dayMix, dayTarget, 1.2f, dt);
            dark = Glide(dark, darkTarget, 0.8f, dt);
            snowMix = Glide(snowMix, snowTarget, 0.5f, dt);
            if (dirty || oDay != dayMix || oDark != dark || oSnow != snowMix) { Repaint(); }
            float oR = rainAmt, oS = snowAmt, oH = hailAmt;
            rainAmt = Glide(rainAmt, rainTarget, 1.2f, dt);
            snowAmt = Glide(snowAmt, snowAmtTarget, 1.2f, dt);
            hailAmt = Glide(hailAmt, hailTarget, 1.2f, dt);
            if (dirty || oR != rainAmt || oS != snowAmt || oH != hailAmt) SetCounts();
            fogAmt = Glide(fogAmt, fogTarget, 1f, dt);
            sunScale = Glide(sunScale, sunTarget, 2f, dt);
            moonScale = Glide(moonScale, moonTarget, 2f, dt);
            starAmt = Glide(starAmt, starTarget, 1.5f, dt);
            dirty = false;

            // lumière du dessin animé : fixe par rapport à la scène (donc elle suit le plateau), plus bleutée la nuit
            Vector3 ld = Vector3.Lerp(new Vector3(0.30f, 0.70f, -0.60f), new Vector3(-0.40f, 0.80f, -0.55f), dayMix);
            Vector4 lw = transform.TransformDirection(ld.normalized);
            for (int i = 0; i < toons.Count; i++) toons[i].m.SetVector("_Light", lw);

            // plateau : légère inclinaison vers toi + balancement lent
            rig.localRotation = Quaternion.Euler(-14f, Mathf.Sin(time * 0.35f) * 6f, 0f);
            island.localPosition = new Vector3(0f, Mathf.Sin(time * 0.9f) * 0.035f, 0f);

            // vent : arbres qui se penchent et oscillent, moulin qui tourne
            float k = Mathf.Clamp01(windKmh / 50f);
            float lean = windX * 16f;
            treeA.localRotation = Quaternion.Euler(0f, 0f, -(lean + Mathf.Sin(time * (1.6f + k * 2f)) * (1.5f + 5f * k)));
            treeB.localRotation = Quaternion.Euler(0f, 0f, -(lean * 0.7f + Mathf.Sin(time * (2.1f + k * 2f) + 1.3f) * (1f + 4f * k)));
            hub.localRotation = Quaternion.Euler(0f, 0f, hub.localEulerAngles.z - Mathf.Clamp(25f + windKmh * 6f, 25f, 420f) * dt);

            // soleil, lune, étoiles
            sun.localScale = Vector3.one * sunScale * (1f + 0.03f * Mathf.Sin(time * 2f));
            sun.gameObject.SetActive(sunScale > 0.01f);
            if (sunScale > 0.01f) sunRays.localRotation = Quaternion.Euler(0f, 0f, time * 18f);
            moon.localScale = Vector3.one * moonScale;
            moon.gameObject.SetActive(moonScale > 0.01f);
            for (int i = 0; i < StarN; i++)
            {
                bool on = starAmt > 0.01f;
                if (stars[i].gameObject.activeSelf != on) stars[i].gameObject.SetActive(on);
                if (on) stars[i].localScale = Vector3.one * (0.045f * starAmt * (0.65f + 0.35f * Mathf.Sin(time * 2.2f + starPh[i])));
            }

            // nuages : apparaissent / disparaissent en grossissant, dérivent avec le vent
            float drift = (windX >= 0f ? 1f : -1f) * Mathf.Clamp(0.035f + windKmh * 0.009f, 0.035f, 0.55f);
            for (int i = 0; i < CloudN; i++)
            {
                var c = clouds[i];
                float target = i < cloudTarget ? c.size : 0f;
                c.now = Mathf.MoveTowards(c.now, target, 1.4f * dt);
                bool on = c.now > 0.01f;
                if (c.t.gameObject.activeSelf != on) c.t.gameObject.SetActive(on);
                if (!on) continue;
                c.x += drift * dt;
                if (c.x > 2.6f) c.x = -2.6f; else if (c.x < -2.6f) c.x = 2.6f;
                c.t.localPosition = new Vector3(c.x, c.y + Mathf.Sin(time * 0.7f + i * 1.7f) * 0.03f, c.z);
                c.t.localScale = Vector3.one * c.now;
            }

            // brouillard
            for (int i = 0; i < mist.Length; i++)
            {
                bool on = fogAmt > 0.01f;
                if (mist[i].gameObject.activeSelf != on) mist[i].gameObject.SetActive(on);
                if (!on) continue;
                float x = mistX[i] + Mathf.Sin(time * 0.25f + i * 2f) * 0.35f;
                mist[i].localPosition = new Vector3(x, 0.14f + 0.04f * i, -0.55f - 0.12f * i);
                mist[i].localScale = new Vector3(1.9f, 0.30f, 0.7f) * fogAmt;
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

            // éclairs
            if (thunder)
            {
                boltTimer -= dt;
                if (boltTimer <= 0f)
                {
                    boltTimer = Random.Range(2.5f, 6f);
                    boltLeft = 0.18f;
                    PlaceBolt();
                }
            }
            if (boltLeft > 0f)
            {
                boltLeft -= dt;
                bolt.gameObject.SetActive(true);
                Flash = boltLeft > 0.09f ? 1f : 0.55f;
            }
            else
            {
                if (bolt.gameObject.activeSelf) bolt.gameObject.SetActive(false);
                Flash = Mathf.Max(0f, Flash - dt * 4f);
            }
        }

        void PlaceBolt()
        {
            float x = Random.Range(-1.1f, 1.1f);
            var p = new Vector2[5];
            p[0] = new Vector2(x, 0.80f);
            p[1] = new Vector2(x + 0.18f, 0.58f);
            p[2] = new Vector2(x - 0.10f, 0.38f);
            p[3] = new Vector2(x + 0.14f, 0.20f);
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
