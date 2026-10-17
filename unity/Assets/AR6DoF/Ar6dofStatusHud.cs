using UnityEngine;

namespace AR6DoF
{
    /// <summary>Petit affichage de diagnostic (état de la connexion, du tracking, du sol).</summary>
    public class Ar6dofStatusHud : MonoBehaviour
    {
        public Ar6dofReceiver receiver;
        public Ar6dofHeadRig rig;
        public bool show = true;

        void OnGUI()
        {
            if (!show || receiver == null || rig == null) return;
            string s;
            if (receiver.PacketCount == 0) s = "AR6DoF : aucun paquet recu (ar6dof_probe lance ? 't' appuye ? port " + receiver.port + ")";
            else if (rig.ConnectionLost) s = "AR6DoF : connexion perdue";
            else
            {
                s = "AR6DoF : " + (rig.TrackingOk ? "tracking OK" : "TRACKING PERDU") +
                    " | sol " + (rig.FloorDetected ? "detecte" : "suppose") +
                    " | hauteur des yeux " + (rig.HeadHeight * 100f).ToString("0") + " cm" +
                    " | R recentrer, M rappeler le menu, F suivi";
            }
            GUI.Label(new Rect(10, 10, 1400, 24), s);
        }
    }
}
