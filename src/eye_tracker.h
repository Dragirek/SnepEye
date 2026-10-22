#pragma once
// Eye tracking (2 caméras infrarouges d'oeil, ex. GC0308 320x240@120) -> UDP local pour Unity.
// Tout est inactif tant que Start() n'est pas appelé : aucun thread, aucune caméra, aucun socket.
//   UDP 9883  sortie : paquets de regard (44 octets, voir eye_math.h)
//   UDP 9884  entrée : commandes texte de Unity (EYE 1|0, CAL_BEGIN, CAL_POINT, CAL_SOLVE, ...)
//   UDP 9885  sortie : évènements texte (STATUS, POINT_DONE, CAL_RESULT, PONG)
#include <functional>
#include <string>

#include <opencv2/core.hpp>

namespace ar6eye {

using LogFn = std::function<void(const std::string&)>;

struct Options {
  int camL = -1, camR = -1;     // numéros de caméra OpenCV (-1 = pas de caméra pour cet oeil)
  int fps = 90;                 // cadence de traitement visée par oeil (la caméra peut aller plus vite : les images en trop sont ignorées)
  int backend = 0;              // 0 = MSMF (même numérotation que la liste des caméras), 1 = DSHOW
  bool startEnabled = true;     // ouvrir les caméras tout de suite (sinon attendre la commande EYE 1)
  std::string dataDir;          // dossier de ar6dof_eye.txt (réglages) et ar6dof_eye_cal.txt (étalonnage)
};

void Start(const Options& o, LogFn log);
void Stop();
bool Running();

// Image de contrôle (les 2 yeux côte à côte avec pupille et reflet dessinés). Faux si rien à montrer.
// Les images ne sont copiées par les threads que depuis le premier appel : sans fenêtre, aucun coût.
bool GetPreview(cv::Mat* out);

}  // namespace ar6eye
