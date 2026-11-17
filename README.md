# SnepEye / AR6DoF : casque AR open source (Windows 11)

**Langue / Language :** [Français](docs/fr/index.md) · [English](docs/en/index.md)

Un casque de réalité augmentée fait maison : deux caméras + une IMU (BNO085) donnent un suivi de position 6DoF, et Unity 6 affiche le passthrough, des écrans virtuels, une barre de tâches VR, des applications (photo, scanner, météo, horloge, audio...), plus le suivi des yeux et de la bouche (facultatif).

*A home-made open-source AR headset: two cameras + an IMU give 6DoF tracking, Unity 6 shows passthrough, virtual screens, a VR taskbar, apps, and optional eye and mouth tracking.*

---

## Par où commencer ? / Where do I start?

| Je veux... | Va ici |
|---|---|
| **Télécharger le programme** (je n'ai jamais utilisé GitHub) | [Guide débutant](docs/fr/commencer.md) · [Beginner guide](docs/en/getting-started.md) |
| **Calibrer ma caméra** correctement | [Calibration](docs/fr/calibration.md) · [Calibration](docs/en/calibration.md) |
| **Installer Unity** (projet, scène, réglages) | [unity/README_UNITY.md](unity/README_UNITY.md) |
| **Voir ce que le casque sait faire** | [Index des fonctions](docs/fr/index.md#fonctions) · [Features](docs/en/index.md#features) |
| **Résoudre un problème** | [Dépannage](docs/fr/reference/depannage.md) · [Troubleshooting](docs/en/index.md#troubleshooting) |

## En 5 lignes

1. Sur GitHub, onglet **Actions** : télécharge le dernier programme compilé (`ar6dof-probe-win64`).
2. Décompresse-le, lance **`ar6dof_app.exe`**, choisis ta caméra et le port de l'IMU, *Enregistrer*.
3. Page **Calibration** : imprime un damier et suis les conseils (une dizaine de minutes).
4. Copie `unity/Assets/AR6DoF` dans ton projet Unity 6 et suis [README_UNITY.md](unity/README_UNITY.md).
5. Appuie sur *Play* : le casque affiche le passthrough et la barre de tâches VR.

## Contenu du dépôt

| Dossier | Rôle |
|---|---|
| `src/` | Programmes Windows en C++ : `ar6dof_probe` (suivi), `ar6dof_app` (fenêtre de contrôle), `ar6dof_audio` (moteur audio), `ar6dof_screen.dll` (capture d'écran pour Unity) |
| `unity/` | Le dossier `Assets/AR6DoF` à copier dans Unity 6 |
| `firmware/` | Programmes des cartes ESP32-C3 : IMU du casque et télécommande Bluetooth |
| `calibration/` | Calibration d'exemple de la caméra |
| `docs/` | Toute la documentation, par catégories |
| `tests/` | Tests des parties logiques (pour les développeurs) |

## Compilation automatique

GitHub compile les programmes Windows tout seul (workflow `build-windows`). La compilation ne démarre que si `src/`, `CMakeLists.txt`, `vcpkg.json`, `calibration/` ou le workflow changent : modifier `unity/`, `firmware/`, `tests/`, `docs/` ou un `.md` ne lance rien. Pour lancer une compilation à la main : onglet **Actions** > **build-windows** > **Run workflow**.
