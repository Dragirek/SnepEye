[← Retour à l'index](../index.md)

# Performances, mode services, aperçus

`ar6dof_probe --services` (lancé par `ar6dof_app.exe`) n'a ni suivi stéréo, ni Bluetooth, ni passthrough : seulement l'IMU (étalonnage du montage), les caméras d'oeil / de bouche (étalonnage) et les aperçus, pour tout régler **sans lancer le suivi**. Les aperçus (`PV n` sur UDP 9877 : 1 stéréo, 2 yeux, 3 bouche, renouvelé au moins chaque seconde, expire après 3 s) passent par la mémoire partagée `Local\ar6dof_preview` (800x300 BGRA) et ne coûtent rien tant qu'on ne les demande pas, dans l'application comme dans le menu Unity.
Les fenêtres OpenCV et les touches du clavier ont été retirées : tout se commande depuis l'application, Unity ou l'UDP 9877.

## Performances (CPU et latence)
- **Threads d'OpenCV** : par défaut 2 (`--cv-threads N` pour changer). Sans cette limite OpenCV disperse chaque petit calcul (flot optique, remap) sur tous les coeurs : sur un CPU à 16 threads, ~50 % de CPU partout pour un suivi léger.
- **Plus de fenêtres OpenCV** : elles ont été remplacées par les aperçus à la demande de l'application (`--no-windows` est accepté sans effet).
- **Compilation** : `/O2 /GL /arch:AVX2 /LTCG` pour le code d'ar6dof, OpenCV minimal (JPEG + PNG seulement, sans TIFF / WebP / lecteur de QR code) : build plus court et exe plus léger.
- **Lecteur caméra direct (option `mfdirect`, réponds `1`)** : le MJPEG de la caméra est lu par Media Foundation sans décodeur ni convertisseur Windows intercalés (mode faible latence), puis décodé par libjpeg-turbo (SIMD) directement en BGR. Moins de copies, moins de CPU, moins de délai sur du 3200x1200. Au démarrage un auto-test (5 images sur 40 lectures) valide le mode ; en cas d'échec (pas de MJPEG à cette taille...) le programme revient tout seul à OpenCV. Le journal affiche `decodage X ms`.
- `ar6dof_screen.dll` capture aussi des **fenêtres** (Windows.Graphics.Capture, C++/WinRT) : elle se lie à `windowsapp`.
- Le journal de l'exe affiche `cpu NN%` une fois par seconde : compare avant / après.
- **Fenêtre Performances (réglages VR)** : carte *Système* avec l'usage du processeur (total + une barre par coeur / thread), la mémoire (%, Go), la carte graphique (usage, température, mémoire vidéo), la batterie et le temps restant (portable seulement). Les mesures ne sont lues (1 par seconde, dans un fil à part) que **tant que cette page est affichée**. Limites honnêtes : Windows ne donne pas la température du coeur du processeur sans pilote tiers (sur un Ryzen, un capteur ACPI de la carte mère s'affiche s'il existe, sinon « indisponible ») ; la température de la carte graphique est lue pour les cartes **NVIDIA** (NVML) ; pour AMD / Intel seul l'usage est affiché.
- **Écran virtuel qui épouse la source** : quand la fenêtre ou l'écran capturé change de forme (par exemple une fenêtre 4:3 devenue 21:9), l'écran virtuel suit le nouveau rapport en gardant à peu près la même surface (au plus 2,4 m de haut).
