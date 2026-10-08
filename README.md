# ar6dof — casque AR open source (Windows 11)

Étape 1 : `ar6dof_probe` vérifie le matériel (caméra stéréo + BNO085) ET la
calibration (faite ailleurs) avant de brancher le tracking 6DoF.

## Utilisation
1. Flasher `firmware/bno085_streamer/bno085_streamer.ino` sur l'ESP32-C3
   (Arduino IDE : Outils > USB CDC On Boot > Enabled).
2. Mettre le dossier sur GitHub (voir le guide), l'onglet Actions compile l'exe
   (artifact `ar6dof-probe-win64`).
3. Dézipper, lancer `ar6dof_probe.exe` : choisir la caméra, le port COM, la
   résolution (3200x1200 = 2 yeux de 1600x1200) et le dossier de calibration.
4. Touches : `r` brut/rectifié · `c` mesure de qualité de la calibration ·
   `x` inverser gauche/droite · `s` sauver une paire · `q` quitter.

## Calibration
Le programme ne calibre pas : il LIT une calibration. Format supporté pour
l'instant : les 3 fichiers RTAB-Map (`*_left.yaml`, `*_right.yaml`, `*_pose.yaml`)
placés dans le dossier `calibration/`. Elle est mise à l'échelle automatiquement
si la résolution diffère, avec un avertissement.

## Test du parseur IMU (sans dépendance)
    g++ -std=c++17 tests/parser_test.cpp -o parser_test && ./parser_test
