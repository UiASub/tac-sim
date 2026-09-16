# TAC ROV training simulator

A Unity pool prototype for TAC Challenge ROV pilot practice. The vehicle is a four-DOF, generic eight-thruster ROV: forward/back, strafe, vertical movement, and yaw, with pitch and roll locked. Buoyancy, drag, and thruster geometry are configurable. The landing pad is a practice target; official mission geometry and scoring are not implemented yet.

## Open and run

Use **Unity 6000.3.23f1 LTS**. In Unity Hub, add the `Unity/` directory as a project. Open `Assets/TacSim/Scenes/TrainingPool.unity` and press Play.

The project uses URP 17.3.0 and the Input System. The training basin includes tiled surfaces, animated water and caustics, suspended particles, underwater lights, poolside railings, a ladder, service pipework, depth markings, and collision-enabled practice hoops. Geometry, materials, and tile textures are generated locally and remain editable. Blender is reserved for detailed reference-based assets.

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Forward/back, strafe | W/S, A/D | Left stick |
| Ascend/descend | Space / Left Ctrl | Right / left trigger |
| Yaw | Q/E | Right stick X |
| Precision movement (35%) | Hold Shift | Hold left shoulder |
| Cycle chase, forward, downward view | V | South / A |
| Toggle headlights | L | North / Y |
| Cut thrust / re-arm | Escape | East / B |
| Reset vehicle | R | Start |
| Cycle water preset | F | Right shoulder |
| Open visual settings | H or click Filters | — |
| Toggle camera effects | P | — |

## Water and camera filters

Open **Filters [H]** to choose Clear Pool, Coastal Water, Turbid Water, or Night Dive. Adjust the approximate visibility range and independently toggle camera colour grading/bloom/grain/vignette, suspended particles, and animated caustics. Camera effects can be disabled while retaining physical-scene fog; use Clear Pool and the visibility slider for a clearer view. Night Dive suppresses caustics and reduces facility lighting, making the ROV headlights useful.

These are artistic training conditions, not calibrated visibility measurements or image-processing algorithms. They apply consistently across all three camera views and do not modify vehicle physics. Fog and underwater camera effects switch off when the camera rises above the surface. Settings are session-local; launch starts in Clear Pool.

Losing window focus cuts thrust. Re-arm after returning to the window. Reset restores the initial pose, clears velocity and commands, and re-arms the vehicle. Quit the standalone window through the window manager.

## Automation API and OpenCV demo

Launch the built player with `./run.sh -automationPort 8765` to enable the
loopback-only JSON API. It exposes four-DOF commands, pose and velocity,
arm/reset state, depth and throttle, current collision/contact force, peak force
since reset, and optional JPEG observations from any camera. See
[the protocol reference](docs/AUTOMATION.md) for the request schema and safety
behavior.

An accompanying `uv` project lives in `automation-demo/`, outside the Unity
project. After starting the player, run `cd automation-demo && uv sync && uv run
demo.py`. It displays observations and telemetry with OpenCV and can toggle a
small yellow-target vision controller with `M`; its client class is intended as
a starting point for a learned policy. Add `-screen-fullscreen 0` when launching
the simulator if you want the Unity and OpenCV windows side by side.

## Build and verify

Use **TAC → Build Linux player** in the Editor. Output: `Unity/Builds/Linux/TacSim.x86_64` (ignored by git). Run `./run.sh` from the repository root for piloting. The launcher opens fullscreen and selects native Wayland when running on a Wayland desktop, including niri. Windowed mode is also resizable.

The earlier fixed-size XWayland window floated under niri and did not provide usable input in this setup. Native Wayland fullscreen was verified with keyboard movement and camera switching. If launching the executable directly on Wayland, pass `-force-wayland -screen-fullscreen 1`.

Command-line examples from the repository root, with the Editor at its default Linux installation path:

```bash
UNITY_EDITOR=/home/dl/Unity/Hub/Editor/6000.3.23f1/Editor/Unity
"$UNITY_EDITOR" -batchmode -nographics -quit \
  -projectPath "$PWD/Unity" \
  -executeMethod TacSim.Editor.TrainingProject.BuildLinux \
  -logFile /tmp/tac-build.log

"$UNITY_EDITOR" -batchmode -nographics \
  -projectPath "$PWD/Unity" -runTests -testPlatform EditMode \
  -testResults /tmp/tac-tests.xml -logFile /tmp/tac-tests.log

./run.sh -smokeTest \
  -captureScreenshot /tmp/tac-pool.png -logFile /tmp/tac-player.log

# Also capture all four visual presets, the settings panel, and effects disabled.
./run.sh -smokeTest -capturePresets /tmp/tac-presets \
  -logFile /tmp/tac-visual-check.log
```

The standalone smoke check uses the vehicle command API to check neutral buoyancy, all three translation axes, yaw, rejected pitch/roll commands, level-hull constraints under external torque and off-centre force, emergency cut, reset, and camera switching. It also checks visibility changes, independent effect toggles, and unchanged vehicle state, then reports a short frame-rate sample. It exits with code 0 on success or 1 on a failed check. Run it with a display available to verify rendering and capture screenshots. Automated checks do not replace a hands-on gamepad check.

**TAC → Create training pool** rebuilds the generated scene and materials. It replaces edits to that scene; use it only when intentionally regenerating the prototype. Normal project opening does not regenerate anything.

## Model and limits

- One Unity unit is one metre; local X is right, Y is up, Z is forward.
- Physics advances at 50 Hz. Thruster commands are mixed and limited per motor, with a response ramp. This is a training approximation, not a calibrated UiASub vehicle model.
- Buoyancy uses displaced volume and a simple surface-submersion factor. Drag is evaluated relative to configurable water current. The top floats are modeled as ideal stabilization: physics locks pitch and roll, including under external forces and collisions, rather than simulating finite righting motion. Spawn and reset preserve heading but keep the hull level. Pitch/roll commands are ignored; arrows, right-stick Y, and D-pad do not rotate the vehicle.
- Forward and downward cameras follow the hull; chase view is a training aid. Fog approximates visibility underwater. Surface ripples and caustics are procedural visual effects, not a fluid or optical simulation.
- No tether, manipulator, sensor noise, deterministic stepping, or TAC scoring yet. The included vision controller is an integration demo, not a trained controller. No claim of deterministic replay or real-world hydrodynamic accuracy.
- CachyOS is the current development host; Unity officially targets Ubuntu on Linux. A transient Bee closed-pipe error occurred during initial tool setup and cleared on an unchanged retry.

Reference: [TAC Challenge](https://tacchallenge.com/) and its [2026 mission booklet](https://tacchallenge.com/wp-content/uploads/2026/03/Mission-Booklet-2026.pdf). The local `PLAN.md` remains an uncommitted working draft.

For the next fidelity pass, see [asset requests](docs/ASSETS.md). No external assets or paid packages are required to run this version.
