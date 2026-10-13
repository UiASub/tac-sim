# TAC Challenge arena

`Assets/TacSim/Scenes/TacArena.unity` is the training pool with the practice pad and hoops hidden
and a procedural `TacCourse` (`Scripts/Tac/`) built from the TAC Challenge 2026 Mission Booklet:

- **Pipeline**: yellow Ø200 mm tubes, 6–10 m, 2–5 straight segments joined at −90°…90°, lying on the
  floor. 4–10 Original-ArUco markers (unique IDs 1–99, random rotation, ≥ 0.2 m apart) on top, and
  a pinger at one end.
- **Docking station**: 1200 × 800 mm white plate with a primary power puck in the centre, four steel
  plates and ArUco 28, 7, 19, 96 in the corners.
- **Subsea structure** (RAL 1004) with 5–10 markers (IDs may repeat, some hard to see) and orange
  valves A (vertical surface) and B (horizontal surface) starting near S (0°…30°).
- **Docking referee** (`TacDock.cs`): the vehicle's puck is the centre of the hull underside. Docked =
  resting on the plate for 10 s (+20); precision = pucks within 30 mm for 10 s (+50, and +50 for the
  power-puck light, shown as a green lamp). The tolerances are `ASSUMED`.
- **Manipulator** (`TacArm.cs`, a draft, not the real Malstrøm arm): a boom under the bow with a
  rotating wrist (±50°, 45°/s like Topside's manipulator channel) carrying a fork, and a tilt joint
  (forward ↔ straight down, `ASSUMED`). It has no colliders. When the fork is seated in a valve bucket
  and its slot lines up with the handle, the handle follows the fork. A valve counts as done when the
  fork has opened it (≥ 85°).

The course is generated in Play mode from `seed`; the same seed always gives the same course.
Values marked `ASSUMED` in `TacCourse.cs` (marker sizes, structure size, dock height, marker
corner order) are not in the booklet text and must be checked against the official drawings.

## Create and play

1. Open the `Unity/` project and run **TAC → Create TAC arena scene**. This copies `TrainingPool`
   (which is left unchanged) to `TacArena`.
2. Open `Assets/TacSim/Scenes/TacArena.unity` and press Play (or **TAC → Play TAC arena**). Pilot
   controls are unchanged; **N** builds a new random course, **Z / X** turn the wrist and **C** swings
   the arm between forward and down. The bottom line shows the docking and valve status.

## Automation

Enable **TAC → Automation server in Play mode (port 8765)** to use the TCP API from the Editor,
or start a player with `-automationPort 8765` or the environment variable `TAC_AUTOMATION_PORT`.
Additional API in this scene:

- `{"action":"scenario","seed":7}` builds course 7 (seed ≤ 0: random) and resets the vehicle.
- `"ground_truth": true` on any request adds `course_seed`, `pipeline_marker_ids` (ordered from
  the pinger), `structure_marker_ids`, `dock_marker_ids`, `pinger_position`, `dock_offset`
  (vehicle relative to the puck) and `valve_a_angle` / `valve_b_angle`. It also adds the layout
  (`dock_position`, `dock_yaw`, `structure_position`, `structure_yaw`), the docking referee
  (`puck_offset`, `dock_on_plate`, `dock_puck_contact`, `dock_seconds`, `dock_puck_seconds`,
  `dock_points`) and the valves (`valve_a_open`, `valve_b_open`, `valve_engaged`, `valve_a_tool`,
  `valve_b_tool`).
- `{"action":"manipulator","wrist":-1,"tilt":1}` sets the arm targets: `wrist` −1…1 (±50°), `tilt`
  0 (forward) … 1 (down). Every response carries `has_manipulator`, `wrist_deg` and `tilt_deg`.
- `{"action":"lights_off"}` / `{"action":"lights_on"}` switch the headlights.
- Captured images carry `image_bottom_up`. On Direct3D the JPEG is already upright and the client
  must not flip it; `tacsim_client.py` handles this. Before this flag the client always flipped,
  which turned every image upside down on Windows.

Without a signed player build, start a scripted session in the Editor instead:
`Unity -batchmode -projectPath Unity -executeMethod TacSim.Editor.TacArenaSetup.PlayArena -automationPort 8765`.

`automation-demo/pipeline_follow.py` is an autonomous pipeline inspection demo: descend, go to the
pinger, follow the pipe with the downward camera while reading ArUco markers, print the ID list with
its TAC standard score, and return home. The pinger fix, altitude and return-home pose come from the
simulator and stand in for sensors the real vehicle does not have yet.

`automation-demo/mission.py` is a draft of the whole run: pipeline, docking, a lap around the
structure reading markers, opening valves A and B, then home and up to the surface, ending with a
TAC score sheet. `--only dock,valves` runs selected tasks. Docking (pose from ArUco 28/7/19/96) and
both valve approaches (orange bucket in the camera) are camera-based, with the last blind stretch on
dead reckoning from the simulator velocity. Transits and the inspection lap use the simulator pose
and the course layout; see the docstring for the full list of stand-ins.

Build a standalone player with **TAC → Build Windows player (TAC arena)**
(`Unity/Builds/Windows/TacSim.exe`).

`scripts/gen_aruco_table.py` regenerates `ArucoOriginal.cs` from OpenCV's `DICT_ARUCO_ORIGINAL`.
