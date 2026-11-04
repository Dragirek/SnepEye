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
Une caméra de bouche (`mouthcam` dans `ar6dof_config.txt`, -1 = aucune) -> paramètres de la bouche -> Unity (UDP 9886 données, 9887 commandes, 9888 évènements).
Indépendant de l'eye tracking (l'un sans l'autre, ou les deux). Désactivé par défaut : aucun thread ni caméra. Deux méthodes :
- **Modèle IA (recommandé)** : pose un modèle ONNX de 45 paramètres compatible *Project Babble* sous le nom **`babble_model.onnx`** à côté de `ar6dof_probe.exe` (ou `model=chemin` dans `ar6dof_mouth.txt`). `ar6dof_probe` le charge avec le module IA d'OpenCV (feature `dnn` de `vcpkg.json`) et envoie les 45 paramètres façon ARKit / VRChat (`jawOpen`, `mouthSmileLeft/Right`, `mouthFunnel`, `mouthPucker`, `tongueOut`, `cheekPuff`...) dans un paquet `MTH2` (196 octets, `src/mouth_math.h`). Entrée du modèle : image grise 224x224 (zone `roi`, `rotate`, `flip` de `ar6dof_mouth.txt`), valeurs de sortie 0..1. **Le modèle n'est PAS fourni** : la licence de Project Babble est non commerciale par défaut, lis-la avant de l'utiliser ; il a été entraîné sur la caméra de leur casque, donc cadre ta caméra de la même façon (mâchoire et bouche, infrarouge si possible). L'aperçu montre exactement ce que voit le modèle. Étalonnage : *neutre* (visage détendu) puis *expressions* (8 s de toutes les expressions, met chaque paramètre à l'échelle 0..1).
- **Détection classique** (sans fichier modèle) : cavité sombre de la bouche -> ouverture, largeur, côté, étalonnage en 4 poses. Approximatif, à régler selon l'éclairage (`ar6dof_mouth.txt`).
Côté Unity : `Ar6dofMouthTracker.Instance.GetShapes(float[45])` (noms et index dans `Ar6dofMouthShapes`), et `Ar6dofMouthDisplay` dessine la bouche sur les **écrans du costume** (1 écran de face, ou 2 écrans de côté), avec déformation réglable (museau long, écran incurvé, ouverture en coin). Tout est coupé tant que rien n'est activé.

## Audio : voix, enceintes externes, soundboard, musique (facultatif)
`ar6dof_audio.exe` (petit programme séparé, sans OpenCV, à côté de `ar6dof_probe.exe`) est lancé par Unity **seulement** quand on active *Moteur audio* (application *Voix* du Store). Désactivé : aucun processus, aucun thread, aucun port.
- **Voix** : micro (WASAPI) -> porte de bruit, coupe-bas / coupe-haut, égaliseur 3 bandes, changement de hauteur (-12..+12 demi-tons), robot, distorsion, qualité réduite, écho, réverbération, compresseur, limiteur doux -> **les enceintes choisies seulement** (le casque garde le son de Windows et de la RA). Spectre et niveaux du micro et de la sortie dans la fenêtre. Latence ~ 40-60 ms (pitch inclus). Tout le traitement est dans `src/audio/audio_dsp.h` (aucune bibliothèque externe).
- **Soundboard** : boutons polyphoniques (jusqu'à 64 sons en même temps, un appui n'en coupe jamais un autre), qui **ne passent pas** par la modification de voix. Chaque bouton : fichier, nom, couleur, volume, vitesse, panoramique, boucle, taille en cases. Dispositions : grille, liste, flux, cercle.
- **Table de mixage** : volumes, muets et niveaux voix / sons / musique / général, spectre de la sortie, lecteur de musique en flux avec liste de lecture (répéter, aléatoire).
- Formats lus (Media Foundation, déjà dans Windows, rien à installer) : wav, mp3, m4a, aac, wma, flac. Un son du soundboard est chargé en mémoire (2 minutes maximum) ; pour les longues pistes, utilise le lecteur de musique.
- UDP 127.0.0.1 : commandes 9890 (texte : `IN id`, `OUT id`, `V cle valeur...`, `VOL canal x`, `MUTE canal 0|1`, `LOAD slot chemin`, `PLAY slot vol pan vitesse boucle`, `STOP slot`, `STOPALL`, `MUSIC chemin`, `MPAUSE`, `MSEEK`, `MLOOP`, `METER`, `DEVLIST`, `PING`, `QUIT`), niveaux 9891 (binaire `AUD1`, 308 octets, seulement tant qu'une fenêtre les demande), évènements 9892.

## Store d'applications (dans la barre de tâches VR)
Une icône *Store* est toujours dans le dock, juste à gauche des réglages. Elle ajoute ou retire des applications de la barre (Voix, Soundboard, Table de mixage, Appareil photo, Objets 3D...) ; une application fermée est désactivée, donc ne coûte rien. Pour en créer une : une classe qui dérive de `Ar6dofAppWindow` et une ligne dans `Ar6dofCatalog`.

### Appareil photo
Photographie ce que voit le casque : l'image de la caméra **corrigée comme une vraie photo** (la pré-déformation des lentilles du casque est retirée, le champ est celui de l'affichage) avec les interfaces (fenêtres, dock, objets 3D...), **sans** les interfaces, ou les deux (deux fichiers `_avec` et `_sans`). Minuteur (3 / 5 / 10 s, avec un grand compte à rebours devant toi), qualité 1080 / 1440 / 2160 px, format carré / 4:3 / 16:9, un oeil ou les deux (3D côte à côte). La fenêtre de l'appareil se masque toute seule juste avant la prise de vue. Les PNG vont dans `Images\AR6DoF\Photos`. Rien ne tourne tant que la fenêtre est fermée ; la photo est faite en une seule image, sans capture d'écran Windows. Si une photo sort à l'envers, case *Retourner la photo*.

### Objets 3D, stickers et formes
Pose dans le monde des **modèles 3D `.obj`** (avec leur `.mtl` : couleur Kd et texture `map_Kd`, plusieurs matériaux, tout en un seul maillage ; lecteur écrit pour AR6DoF, aucun programme à installer), des **stickers** (png / jpg, transparence respectée) et des **formes** (cube, sphère, cylindre, capsule). Chaque objet se règle (taille, teinte, opacité, éclairage, position, cap / inclinaison / roulis, *toujours face à moi*), se **déplace au laser** (option *Déplacer au laser* : on le vise et on garde le bouton appuyé, il suit à la même distance), se duplique, se masque et se supprime (avec confirmation). La liste est enregistrée dans `%APPDATA%\AR6DoF\objects.json`. Les positions sont celles du monde Unity, c'est-à-dire le repère du suivi 6DoF de la **session en cours** (il repart du point où le suivi démarre) : les objets reviennent donc au même endroit *par rapport au point de départ*. Retirer l'application du dock masque les objets (les données restent). Pas d'animation ni de squelette (glTF / FBX pas encore).

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
Poussée sur GitHub, l'onglet *Actions* compile (vcpkg + OpenCV) et publie `ar6dof_app.exe` (la fenêtre de contrôle), `ar6dof_probe.exe`, `ar6dof_audio.exe` (moteur audio), ses DLL et `ar6dof_screen.dll`.

## Première utilisation
1. Flasher `firmware/bno085_streamer` sur l'ESP32-C3 (Arduino IDE : *USB CDC On Boot = Enabled*).
2. Lancer **`ar6dof_app.exe`** (fenêtre Windows : langue, listes de caméras et de ports, cases à cocher, boutons à la place des touches) : choisir la caméra, le port COM,
   le mode (liste lue sur la caméra ; module double comme l'ELP 3200x1200 = 2 yeux de 1600x1200, ou deux caméras séparées) et le dossier de calibration, *Enregistrer*, puis *Démarrer le suivi*. Les réglages sont gardés dans `ar6dof_config.txt`.
   (`ar6dof_probe.exe` reste le **moteur** : l'application le lance elle-même, tu n'as plus à le double-cliquer.)
3. Côté Unity : suivre `unity/README_UNITY.md`.

## Tests (g++)
    for t in tests/*_test.cpp; do g++ -std=c++17 -Isrc -Isrc/audio "$t" -o /tmp/t -lpthread && /tmp/t; done
