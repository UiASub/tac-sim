# TAC ROV training simulator

A Unity pool prototype for TAC Challenge ROV pilot practice. The vehicle is a generic eight-thruster ROV with configurable buoyancy, drag, and thruster geometry. The landing pad is a practice target; official mission geometry and scoring are not implemented yet.

## Open and run

Use **Unity 6000.3.23f1 LTS**. In Unity Hub, add the `Unity/` directory as a project. Open `Assets/TacSim/Scenes/TrainingPool.unity` and press Play.

The project uses URP 17.3.0 and the Input System. Blender is reserved for detailed assets; this first scene uses editable Unity primitives.

| Action | Keyboard | Gamepad |
| --- | --- | --- |
| Forward/back, strafe | W/S, A/D | Left stick |
| Ascend/descend | Space / Left Ctrl | Right / left trigger |
| Yaw | Q/E | Right stick X |
| Pitch | Up/down arrows | Right stick Y |
| Roll | Left/right arrows | D-pad left/right |
| Cycle chase, forward, downward view | V | South / A |
| Toggle headlights | L | North / Y |
| Cut thrust / re-arm | Escape | East / B |
| Reset vehicle | R | Start |

Losing window focus cuts thrust. Re-arm after returning to the window. Reset restores the initial pose, clears velocity and commands, and re-arms the vehicle. Quit the standalone window through the window manager.

## Build and verify

Use **TAC → Build Linux player** in the Editor. Output: `Unity/Builds/Linux/TacSim.x86_64` (ignored by git). Launch the executable normally for piloting.

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

Unity/Builds/Linux/TacSim.x86_64 -smokeTest \
  -captureScreenshot /tmp/tac-pool.png -logFile /tmp/tac-player.log
```

The standalone smoke check uses the vehicle command API to check neutral buoyancy, translation, yaw, emergency cut, reset, and camera switching. It exits with code 0 on success or 1 on a failed check. Run it with a display available to verify rendering and capture a screenshot. Automated checks do not replace a hands-on gamepad check.

**TAC → Create training pool** rebuilds the generated scene and materials. It replaces edits to that scene; use it only when intentionally regenerating the prototype. Normal project opening does not regenerate anything.

## Model and limits

- One Unity unit is one metre; local X is right, Y is up, Z is forward.
- Physics advances at 50 Hz. Thruster commands are mixed and limited per motor, with a response ramp. This is a training approximation, not a calibrated UiASub vehicle model.
- Buoyancy uses displaced volume and a simple surface-submersion factor. Drag is evaluated relative to configurable water current. The buoyancy centre above the mass centre gives a righting moment.
- Forward and downward cameras follow the hull; chase view is a training aid. Fog approximates visibility and is applied globally, including above the water surface.
- No tether, manipulator, sensor noise, autonomous controller, or TAC scoring yet. No claim of deterministic replay or real-world hydrodynamic accuracy.
- CachyOS is the current development host; Unity officially targets Ubuntu on Linux. A transient Bee closed-pipe error occurred during initial tool setup and cleared on an unchanged retry.

Reference: [TAC Challenge](https://tacchallenge.com/) and its [2026 mission booklet](https://tacchallenge.com/wp-content/uploads/2026/03/Mission-Booklet-2026.pdf). The local `PLAN.md` remains an uncommitted working draft.
