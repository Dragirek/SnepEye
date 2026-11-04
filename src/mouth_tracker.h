#pragma once
// Mouth tracking (une caméra dirigée vers la bouche) -> UDP local pour Unity. Indépendant de l'eye tracking.
// Tout est inactif tant que Start() n'est pas appelé : aucun thread, aucune caméra, aucun socket.
//   UDP 9886  sortie : paquets (36 octets, voir mouth_math.h)
//   UDP 9887  entrée : commandes texte (MOUTH 1|0, MCAL neutral|open|wide|narrow, MCAL_CLEAR, PING)
//   UDP 9888  sortie : évènements texte (STATUS, MCAL_DONE, PONG)
#include <functional>
#include <string>

#include <opencv2/core.hpp>

namespace ar6mouth {

using LogFn = std::function<void(const std::string&)>;

struct Options {
  int cam = -1;                 // numéro de caméra OpenCV
  int fps = 60;                 // cadence de traitement visée
  int backend = 0;              // 0 = MSMF (même numérotation que la liste des caméras), 1 = DSHOW
  bool startEnabled = false;    // ouvrir la caméra tout de suite (sinon attendre la commande MOUTH 1)
  std::string dataDir;          // dossier de ar6dof_mouth.txt (réglages) et ar6dof_mouth_cal.txt (étalonnage)
};

void Start(const Options& o, LogFn log);
void Stop();
bool Running();

// Image de contrôle (image de la caméra + cavité détectée). Les images ne sont copiées par le thread que tant que cette
// fonction est appelée régulièrement : sans aperçu, aucun coût.
bool GetPreview(cv::Mat* out);

}  // namespace ar6mouth
