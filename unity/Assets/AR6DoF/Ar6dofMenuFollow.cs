using System.Collections.Generic;
using UnityEngine;

namespace AR6DoF
{
    /// <summary>
    /// « Suivi actif » : les menus ouverts te suivent quand tu te deplaces. Chaque menu garde sa place par rapport a TOI (a ta gauche, a 80 cm, a telle hauteur
    /// au-dessus du sol) ; le repere de ton corps suit ta position en douceur et ne tourne que quand tu detournes la tete de plus de 45 degres (pas a chaque
    /// petit mouvement de tete). Si tu deplaces un menu a la main, il garde sa nouvelle place par rapport a toi. Inactif (reglage coupe) : rien ne tourne,
    /// les menus restent ancres dans la piece. Tout est calcule sur place : aucune allocation par image.
    /// </summary>
    public class Ar6dofMenuFollow
    {
        class E { public Vector3 off; public Quaternion rel; public Vector3 lastPos; public Quaternion lastRot; public bool init; }
        readonly Dictionary<Canvas, E> map = new Dictionary<Canvas, E>();
        Vector3 bodyPos;
        float bodyYaw, lastHeadYaw;
        bool bodyInit, turning;

        public void Reset() { map.Clear(); bodyInit = false; turning = false; }

        static float Yaw(Vector3 f, float fallback)
        {
            f.y = 0f;
            return f.sqrMagnitude < 1e-4f ? fallback : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        public void Tick(Transform head, List<Canvas> canvases, float dt)
        {
            if (head == null) return;
            Vector3 hp = head.position;
            float hy = Yaw(head.forward, lastHeadYaw);
            lastHeadYaw = hy;
            if (!bodyInit) { bodyPos = hp; bodyYaw = hy; bodyInit = true; map.Clear(); }
            float kp = 1f - Mathf.Exp(-Mathf.Min(dt, 0.1f) / 0.45f);
            bodyPos.x += (hp.x - bodyPos.x) * kp;
            bodyPos.z += (hp.z - bodyPos.z) * kp;
            float d = Mathf.DeltaAngle(bodyYaw, hy);
            if (!turning && Mathf.Abs(d) > 45f) turning = true;
            if (turning)
            {
                bodyYaw += d * (1f - Mathf.Exp(-Mathf.Min(dt, 0.1f) / 0.5f));
                if (Mathf.Abs(Mathf.DeltaAngle(bodyYaw, hy)) < 4f) turning = false;
            }
            Quaternion br = Quaternion.Euler(0f, bodyYaw, 0f);
            Quaternion bri = Quaternion.Inverse(br);
            Vector3 origin = new Vector3(bodyPos.x, 0f, bodyPos.z);
            for (int i = 0; i < canvases.Count; i++)
            {
                var c = canvases[i];
                if (c == null) continue;
                var t = c.transform;
                E e;
                if (!map.TryGetValue(c, out e)) { e = new E(); map[c] = e; }
                bool moved = e.init && ((t.position - e.lastPos).sqrMagnitude > 1e-4f || Quaternion.Angle(t.rotation, e.lastRot) > 1f);
                if (!e.init || moved)
                {
                    e.off = bri * (t.position - origin);
                    e.rel = bri * t.rotation;
                    e.init = true;
                }
                else t.SetPositionAndRotation(origin + br * e.off, br * e.rel);
                e.lastPos = t.position;
                e.lastRot = t.rotation;
            }
        }
    }
}
