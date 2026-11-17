[← Index](index.md) · [Français](../fr/calibration.md)

# Guide: calibrate the camera properly

Calibration teaches the program **how your camera distorts the image**, its focal length, and **where the two lenses are relative to each other**. Without it tracking drifts and menus are at the wrong distance. Plan **10 to 15 minutes**, once (redo it if you change or move a camera). Everything happens in **`ar6dof_app.exe` > Calibration page**.

## What you need
- A printed **black and white checkerboard**, preferably on **matte paper** (glossy paper reflects light).
- A **flat, rigid** surface to stick it on: thick cardboard, a plate, glass. **Wavy paper ruins the calibration.**
- A **ruler or tape measure**.
- **Strong, even light**, no glare on the board.

## Step 1: print and measure
1. Print the board at **100%** (not "fit to page"). The *Grid (squares)* list offers 10×7, 9×6, 11×8 and 8×5: count the **squares**, not the corners.
2. **Width and height must have different parity** (one even, one odd, like 10×7), otherwise the app refuses the grid.
3. Stick it flat, no bubbles or folds.
4. **Measure the width of the WHOLE grid with a ruler** (edge to edge, in mm). It sets the scale in millimetres: the app divides by the number of squares and shows "One square = ... mm". Check that this number looks right.
5. Optional: enter the **distance between the lenses** if you know it; the app warns you if the result differs by more than 12%.

## Step 2: capture the views
1. Click **▶ Start the calibration** (the page starts tracking by itself if needed).
2. You see the camera preview with detected corners and a **"ghost" outline**: the position to imitate. An **illustrated tip** explains the current pose, and a progress bar shows how far you are.
3. Hold the board like the ghost, **still**, **fully visible** to **both cameras**. The app records a view when it is sharp and different enough from the previous ones ("View saved!").
4. There are **16 poses**: center close / far, the four corners, the four edges, tilted left / right / up / down, very close, and diagonal. **The *Compute now* button enables from 10 views**; do them all for a better result.

### Messages and what to do
| Message | What to do |
|---|---|
| Show the board to the camera | The whole grid must be visible, no corner cut off |
| The other camera cannot see the board | Center it a bit more or step back |
| Too close to a pose already taken | Change place, distance or angle |
| The board touches the image edge | Bring it in a little |
| Too far | Bring the board closer |
| The two cameras seem swapped | Click **Swap L / R**, then redo this pose |
| Not the same way round | Do not flip the board: keep the same side in front |

### For better views
- Move **the board**, keep the camera still (or the reverse, but not both).
- **Hold still** for a second: blur is the first cause of a bad score.
- Really go **into the corners and against the edges**: distortion is strongest there.
- Tilt clearly (30° to 45°) for the tilted poses.

## Step 3: computation and result
The computation usually takes **10 to 60 seconds**; do not close the page. The result shows:
- **Reprojection error**: under 0.5 px = very good.
- **Alignment of the two cameras**: under 0.3 px = very good.
- A **score out of 100**: 85+ excellent, 70+ good, 50+ fair (redo if possible), below that poor (redo).
- **Plain warnings** (image not covered enough, board not flat, cameras swapped...).

Click **Save and apply** (the old file is kept as `ar6dof_calib.prev.yml`). If the score is poor: **Redo the calibration** following the warnings.

## Step 4: check the scale with a ruler (recommended)
1. Click **Check the scale with a ruler**.
2. Hold the board **facing the camera, still**, visible to both cameras, at **50 cm or more** (farther is more accurate).
3. Measure the distance between the **lens glass** and the board, enter it, click **Measure**.
4. If the difference is notable, **Correct the scale**. If it comes back, check the square size (step 1) and measure from farther away.

## Good to know
- The calibration is used for **every camera mode** (resolution, frame rate): no need to redo it when changing mode. The program also learns the **mode zoom** with the IMU gyro: turn your head (at least 50° in total) once per mode, with tracking running.
- Already have an RTAB-Map calibration? Use **Import RTAB-Map (converted)**.
- Technical details (French): [calibration-technique](../fr/reference/calibration-technique.md).
