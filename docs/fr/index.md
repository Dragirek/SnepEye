[← README](../../README.md) · [English](../en/index.md)

# Documentation AR6DoF (français)

Choisis ce que tu veux faire. Chaque page est courte et indépendante.

## 1. Démarrer
| Page | Pour |
|---|---|
| [Guide débutant GitHub](commencer.md) | Trouver l'onglet Actions, télécharger les programmes, premier lancement |
| [Calibrer la caméra](calibration.md) | Imprimer un damier, capturer 16 poses, vérifier l'échelle |
| [Installer Unity](../../unity/README_UNITY.md) | Projet Unity 6, scène en un clic, réglages |

## Fonctions
| Catégorie | Page |
|---|---|
| **Suivi** | [Performances et latence](reference/performances.md) · [Calibration, détails techniques](reference/calibration-technique.md) |
| **Visage** | [Eye tracking](reference/yeux.md) · [Mouth tracking](reference/bouche.md) *(facultatifs, désactivés par défaut)* |
| **Son** | [Voix, enceintes, soundboard, musique, DJ](reference/audio.md) *(facultatif)* |
| **Apps VR** | [Photo, scanner, objets 3D, horloge, calendrier](reference/apps.md) · [Météo](reference/meteo.md) · [Caméras supplémentaires](reference/cameras-supplementaires.md) |
| **Interface** | [Dock, fenêtres, dessins par code](reference/dock-et-fenetres.md) |

## Pour les développeurs
| Page | Contenu |
|---|---|
| [Compiler et tester](reference/compiler-et-tests.md) | Compilation, tests g++ |
| [Application de contrôle et dépannage](reference/depannage.md) | Pages de `ar6dof_app.exe`, IMU à 0 Hz, ports |
| [Historique v60 et v61](reference/historique-v60-v61.md) | Anciennes notes de version |

## Principe
`ar6dof_app.exe` règle et lance `ar6dof_probe.exe` (caméra + IMU + suivi 6DoF). Unity reçoit la pose et l'image, et affiche tout dans le casque. Les options facultatives (yeux, bouche, audio, caméras supplémentaires) ne consomment **rien** tant qu'elles sont désactivées.
