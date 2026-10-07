# TAC Challenge arena

`Assets/TacSim/Scenes/TacArena.unity` is the training pool with the practice pad and hoops hidden
and a procedural `TacCourse` (`Scripts/Tac/`) built from the TAC Challenge 2026 Mission Booklet:

- **Pipeline**: yellow Ø200 mm tubes, 6–10 m, 2–5 straight segments joined at −90°…90°, lying on the
  floor. 4–10 Original-ArUco markers (unique IDs 1–99, random rotation, ≥ 0.2 m apart) on top, and
  a pinger at one end.
- **Docking station**: 1200 × 800 mm white plate with a primary power puck in the centre, four steel
  plates and ArUco 28, 7, 19, 96 in the corners.
- **Subsea structure** (RAL 1004) with 5–10 markers (IDs may repeat, some hard to see) and orange
  valves A (vertical surface) and B (horizontal surface) at a random angle between S and O.

The course is generated in Play mode from `seed`; the same seed always gives the same course.
Values marked `ASSUMED` in `TacCourse.cs` (marker sizes, structure size, dock height, marker
corner order) are not in the booklet text and must be checked against the official drawings.

## Create and play

1. Open the `Unity/` project and run **TAC → Create TAC arena scene**. This copies `TrainingPool`
   (which is left unchanged) to `TacArena`.
2. Open `Assets/TacSim/Scenes/TacArena.unity` and press Play. Pilot controls are unchanged;
   **N** builds a new random course.

## Automation

Enable **TAC → Automation server in Play mode (port 8765)** to use the TCP API from the Editor,
or start a player with `-automationPort 8765` or the environment variable `TAC_AUTOMATION_PORT`.
Additional API in this scene:

- `{"action":"scenario","seed":7}` builds course 7 (seed ≤ 0: random) and resets the vehicle.
- `"ground_truth": true` on any request adds `course_seed`, `pipeline_marker_ids` (ordered from
  the pinger), `structure_marker_ids`, `dock_marker_ids`, `pinger_position`, `dock_offset`
  (vehicle relative to the puck) and `valve_a_angle` / `valve_b_angle`.

`automation-demo/pipeline_follow.py` is an autonomous pipeline inspection demo: descend, go to the
pinger, follow the pipe with the downward camera while reading ArUco markers, print the ID list with
its TAC standard score, and return home. The pinger fix, altitude and return-home pose come from the
simulator and stand in for sensors the real vehicle does not have yet.

Build a standalone player with **TAC → Build Windows player (TAC arena)**
(`Unity/Builds/Windows/TacSim.exe`).

`scripts/gen_aruco_table.py` regenerates `ArucoOriginal.cs` from OpenCV's `DICT_ARUCO_ORIGINAL`.
