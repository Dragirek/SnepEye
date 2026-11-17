[← README](../../README.md) · [Français](../fr/index.md)

# AR6DoF documentation (English)

Pick what you want to do. Each page is short and standalone.

> Only the start-up guides and the overview are translated. The detailed feature reference pages (linked under *Features*) are in French; the in-app texts and the VR menus are available in 5 languages.

## 1. Getting started
| Page | For |
|---|---|
| [Beginner GitHub guide](getting-started.md) | Find the Actions tab, download the programs, first launch |
| [Calibrate the camera](calibration.md) | Print a checkerboard, capture 16 poses, check the scale |
| [Install Unity](../../unity/README_UNITY.md) | Unity 6 project, one-click scene, settings *(French)* |

## Features
| Category | What it does | Details (French) |
|---|---|---|
| **Tracking** | Stereo camera + IMU give 6DoF position; low-latency direct camera reader | [Performance](../fr/reference/performances.md) |
| **Face** *(optional, off by default)* | Eye tracking with two IR cameras (gaze, blinks, eyelid opening); mouth tracking with a camera (45 ARKit-style parameters with an ONNX model you supply, or a basic classic detector) | [Eyes](../fr/reference/yeux.md) · [Mouth](../fr/reference/bouche.md) |
| **Sound** *(optional)* | Voice changer to external speakers, soundboard, mixer console, music player, two-deck DJ view | [Audio](../fr/reference/audio.md) |
| **VR apps** | Camera (photo, panorama, video, burst), scanner (QR code, text, translation, poster schedule, business card), 3D objects and stickers, clock, calendar, weather | [Apps](../fr/reference/apps.md) · [Weather](../fr/reference/meteo.md) |
| **Extra cameras** | Night vision, thermal, mirror view | [Extra cameras](../fr/reference/cameras-supplementaires.md) |
| **Interface** | VR taskbar (dock), movable windows, window-button styles Windows / Mac / Quest | [Dock](../fr/reference/dock-et-fenetres.md) |

Optional features cost **nothing** while they are switched off. The control app also has a **Help** page (last in the left list) with animated diagrams that show the recommended choice for each step.

## Troubleshooting
- **No Actions tab / no Artifacts section / no download button:** see the last section of the [beginner guide](getting-started.md).
- **IMU at 0 Hz, ports, control app pages:** [French troubleshooting page](../fr/reference/depannage.md).
- Still stuck: open an *Issue* on the repository and attach the **Journal** (log) page text from `ar6dof_app.exe`.

## How it fits together
`ar6dof_app.exe` configures and launches `ar6dof_probe.exe` (camera + IMU + 6DoF tracking). Unity receives the pose and the image and shows everything in the headset.
