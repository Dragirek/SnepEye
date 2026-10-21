# SnepEye / ar6dof : casque AR open source (Windows 11)

Caméra stéréo + IMU BNO085 -> tracking 6DoF (`ar6dof_probe.exe`) -> Unity 6 pour le passthrough, les écrans virtuels, la barre de tâches VR,
la télécommande Bluetooth et les menus d'appareils Bluetooth.

## Contenu
- `src/` : `ar6dof_probe` (C++ : caméra, IMU, odométrie visuelle, pose en UDP, mémoire partagée, Bluetooth de la télécommande et des appareils)
  et `src/screen/` (`ar6dof_screen.dll` : capture d'écran GPU pour Unity).
- `firmware/bno085_streamer` : IMU du casque (ESP32-C3, USB série). `firmware/ar6dof_remote` : télécommande gyroscopique (ESP32-C3, Bluetooth LE).
- `unity/` : le dossier `Assets/AR6DoF` à copier dans ton projet Unity 6, et son guide `README_UNITY.md` (installation, réglages, écrans,
  barre de tâches, menus d'appareils Bluetooth).
- `calibration/` : calibration de la caméra (fichiers RTAB-Map `*_left.yaml`, `*_right.yaml`, `*_pose.yaml`).
- `tests/` : tests sans dépendance des parties logiques (voir plus bas).

## Compiler
Poussée sur GitHub, l'onglet *Actions* compile (vcpkg + OpenCV) et publie `ar6dof_probe.exe`, ses DLL et `ar6dof_screen.dll`.

## Première utilisation
1. Flasher `firmware/bno085_streamer` sur l'ESP32-C3 (Arduino IDE : *USB CDC On Boot = Enabled*).
2. Lancer `ar6dof_probe.exe` : choisir la caméra, le port COM, la résolution (3200x1200 = 2 yeux de 1600x1200) et le dossier de calibration.
   Les réponses sont gardées dans `ar6dof_config.txt`. Touches : `r` brut/rectifié, `c` qualité de la calibration, `x` inverser G/D,
   `s` sauver une paire, `g` / `f` / `t` gravité / montage de l'IMU / tracking, `q` quitter.
3. Côté Unity : suivre `unity/README_UNITY.md`.

## Tests (g++)
    for t in tests/*_test.cpp; do g++ -std=c++17 -Isrc "$t" -o /tmp/t && /tmp/t; done
