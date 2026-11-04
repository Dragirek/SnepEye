# SnepEye / ar6dof : casque AR open source (Windows 11)

Caméra stéréo + IMU BNO085 -> tracking 6DoF (`ar6dof_probe.exe`) -> Unity 6 pour le passthrough, les écrans virtuels, la barre de tâches VR,
la télécommande Bluetooth, les menus d'appareils Bluetooth et l'eye tracking (2 caméras infrarouges d'oeil).

## Contenu
- `src/` : `ar6dof_probe` (C++ : caméra, IMU, odométrie visuelle, pose en UDP, mémoire partagée, Bluetooth de la télécommande et des appareils)
  et `src/screen/` (`ar6dof_screen.dll` : capture d'écran GPU pour Unity).
- `firmware/bno085_streamer` : IMU du casque (ESP32-C3, USB série). `firmware/ar6dof_remote` : télécommande gyroscopique (ESP32-C3, Bluetooth LE).
- `unity/` : le dossier `Assets/AR6DoF` à copier dans ton projet Unity 6, et son guide `README_UNITY.md` (installation, réglages, écrans,
  barre de tâches, menus d'appareils Bluetooth).
- `calibration/` : calibration de la caméra (fichiers RTAB-Map `*_left.yaml`, `*_right.yaml`, `*_pose.yaml`).
- `tests/` : tests sans dépendance des parties logiques (voir plus bas).

## Eye tracking (facultatif)
Deux caméras d'oeil infrarouges (ex. GC0308 320x240 @ 120 fps, avec LED IR) -> position de la pupille et du reflet IR -> angle du regard de chaque oeil
(yaw / pitch par rapport à la tête), clignements, ouverture des paupières -> Unity, pour des animations de fursuit liées aux yeux.
- **Désactivé par défaut** : `ar6dof_probe` pose la question *Eye tracking* ; répondre 0 = rien n'est créé (ni caméra, ni thread, ni socket).
  Côté Unity, tant que l'onglet *Yeux* (réglages en VR) n'est pas activé, aucun port n'est ouvert non plus.
- Réponses 1 : numéros des caméras gauche / droite (ceux de la liste affichée au démarrage), cadence de traitement (60 = très léger, 120 = maximum).
  Un fichier `ar6dof_eye.txt` (créé à côté de l'exe) règle la détection et le lissage ; `ar6dof_eye_cal.txt` garde l'étalonnage.
- Algorithme maison : pupille sombre sous éclairage IR (seuil adaptatif, morphologie, ellipse), reflet IR (glint) pour compenser le glissement du casque,
  carte polynomiale pupille -> angle apprise sur 9 points, lissage One Euro, détection des clignements. Une pupille suivie coûte ~0,3 ms par image.
- `tests/eye_math_test.cpp` (maths, g++) et `tests/eye_pupil_ref.py` (détecteur de référence sur images synthétiques, Python + OpenCV).
- Ports UDP locaux : 9883 regard (44 octets), 9884 commandes, 9885 évènements. Voir `src/eye_math.h`.

## Mouth tracking (facultatif)
Une caméra de bouche (`mouthcam` dans `ar6dof_config.txt`, -1 = aucune) -> ouverture de la mâchoire, largeur, côté -> Unity (UDP 9886 données, 9887 commandes, 9888 évènements, voir `src/mouth_math.h`).
Indépendant de l'eye tracking (l'un sans l'autre, ou les deux). Désactivé par défaut : aucun thread ni caméra. Détection classique de la cavité sombre de la bouche (`src/mouth_tracker.cpp`, réglages `ar6dof_mouth.txt`), étalonnage en 4 poses (`ar6dof_mouth_cal.txt`) : approximatif, à régler selon l'éclairage.

## Mode services et aperçus
`ar6dof_probe --services` (lancé par `ar6dof_app.exe`) n'a ni suivi stéréo, ni Bluetooth, ni passthrough : seulement l'IMU (étalonnage du montage), les caméras d'oeil / de bouche (étalonnage) et les aperçus, pour tout régler **sans lancer le suivi**. Les aperçus (`PV n` sur UDP 9877 : 1 stéréo, 2 yeux, 3 bouche, renouvelé au moins chaque seconde, expire après 3 s) passent par la mémoire partagée `Local\ar6dof_preview` (800x300 BGRA) et ne coûtent rien tant qu'on ne les demande pas, dans l'application comme dans le menu Unity.
Les fenêtres OpenCV et les touches du clavier ont été retirées : tout se commande depuis l'application, Unity ou l'UDP 9877.

## Performances (CPU et latence)
- **Threads d'OpenCV** : par défaut 2 (`--cv-threads N` pour changer). Sans cette limite OpenCV disperse chaque petit calcul (flot optique, remap) sur tous les coeurs : sur un CPU à 16 threads, ~50 % de CPU partout pour un suivi léger.
- **Plus de fenêtres OpenCV** : elles ont été remplacées par les aperçus à la demande de l'application (`--no-windows` est accepté sans effet).
- **Compilation** : `/O2 /GL /arch:AVX2 /LTCG` pour le code d'ar6dof, OpenCV minimal (JPEG + PNG seulement, sans TIFF / WebP / lecteur de QR code) : build plus court et exe plus léger.
- **Lecteur caméra direct (option `mfdirect`, réponds `1`)** : le MJPEG de la caméra est lu par Media Foundation sans décodeur ni convertisseur Windows intercalés (mode faible latence), puis décodé par libjpeg-turbo (SIMD) directement en BGR. Moins de copies, moins de CPU, moins de délai sur du 3200x1200. Au démarrage un auto-test (5 images sur 40 lectures) valide le mode ; en cas d'échec (pas de MJPEG à cette taille...) le programme revient tout seul à OpenCV. Le journal affiche `decodage X ms`.
- `ar6dof_screen.dll` capture aussi des **fenêtres** (Windows.Graphics.Capture, C++/WinRT) : elle se lie à `windowsapp`.
- Le journal de l'exe affiche `cpu NN%` une fois par seconde : compare avant / après.

## Compiler
Poussée sur GitHub, l'onglet *Actions* compile (vcpkg + OpenCV) et publie `ar6dof_app.exe` (la fenêtre de contrôle), `ar6dof_probe.exe`, ses DLL et `ar6dof_screen.dll`.

## Première utilisation
1. Flasher `firmware/bno085_streamer` sur l'ESP32-C3 (Arduino IDE : *USB CDC On Boot = Enabled*).
2. Lancer **`ar6dof_app.exe`** (fenêtre Windows : langue, listes de caméras et de ports, cases à cocher, boutons à la place des touches) : choisir la caméra, le port COM,
   le mode (liste lue sur la caméra ; module double comme l'ELP 3200x1200 = 2 yeux de 1600x1200, ou deux caméras séparées) et le dossier de calibration, *Enregistrer*, puis *Démarrer le suivi*. Les réglages sont gardés dans `ar6dof_config.txt`.
   (`ar6dof_probe.exe` reste le **moteur** : l'application le lance elle-même, tu n'as plus à le double-cliquer.)
3. Côté Unity : suivre `unity/README_UNITY.md`.

## Tests (g++)
    for t in tests/*_test.cpp; do g++ -std=c++17 -Isrc "$t" -o /tmp/t && /tmp/t; done
