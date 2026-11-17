[← Retour à l'index](../index.md)

# Compiler, première utilisation, tests

Poussée sur GitHub, l'onglet *Actions* compile (vcpkg + OpenCV) et publie `ar6dof_app.exe` (la fenêtre de contrôle), `ar6dof_probe.exe`, `ar6dof_audio.exe` (moteur audio), ses DLL et `ar6dof_screen.dll`.

## Première utilisation
1. Flasher `firmware/bno085_streamer` sur l'ESP32-C3 (Arduino IDE : *USB CDC On Boot = Enabled*).
2. Lancer **`ar6dof_app.exe`** (fenêtre Windows : langue, listes de caméras et de ports, cases à cocher, boutons à la place des touches) : choisir la caméra, le port COM,
   le mode (liste lue sur la caméra ; module double comme l'ELP 3200x1200 = 2 yeux de 1600x1200, ou deux caméras séparées) et le dossier de calibration, *Enregistrer*, puis (page **Suivi**) *Démarrer le suivi*. Les réglages sont gardés dans `ar6dof_config.txt`.
   (`ar6dof_probe.exe` reste le **moteur** : l'application le lance elle-même, tu n'as plus à le double-cliquer.)
3. Côté Unity : suivre `unity/README_UNITY.md`.

## Tests (g++)
    for t in tests/*_test.cpp; do g++ -std=c++17 -Isrc -Isrc/audio "$t" -o /tmp/t -lpthread && /tmp/t; done
