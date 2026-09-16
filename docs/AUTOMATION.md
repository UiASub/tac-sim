# Automation protocol

Start a built player with `-automationPort PORT`. The server binds only to
`127.0.0.1`, accepts one TCP client, and exchanges one compact JSON object per
line. This deliberately small protocol works without an extra Unity package and
is easy to drive from Python, C++, or a data-collection process.

Commands use normalized body-local axes in `[-1, 1]`:

- `surge`: forward/back (local Z)
- `sway`: right/left (local X)
- `heave`: up/down (local Y)
- `yaw`: turn right/left around local Y

For example:

```json
{"request_id":1,"action":"command","surge":0.4,"sway":0,"heave":0,"yaw":-0.2,"camera":"forward","capture":true,"image_width":320,"image_height":180}
```

Supported actions are `command`, `observe`, `reset`, `arm`, `disarm`, and
`stop` (`stop` is an alias for `disarm`). `camera`, when present, is `chase`,
`forward`, or `downward`. A captured image is a base64 JPEG; width is clamped to
64–1280 px, height to 64–720 px, and `jpeg_quality` to 20–95. Defaults are
320×180 at quality 75.

Each response echoes `request_id` and includes:

```json
{
  "ok": true,
  "request_id": 1,
  "sim_time": 12.34,
  "fixed_time": 12.32,
  "armed": true,
  "position": {"x": 0, "y": -2.5, "z": -4.8},
  "rotation": {"x": 0, "y": 0, "z": 0, "w": 1},
  "linear_velocity": {"x": 0, "y": 0, "z": 0.2},
  "angular_velocity": {"x": 0, "y": 0, "z": 0},
  "depth": 2.5,
  "throttle": 0.4,
  "is_colliding": false,
  "collision_force": 0,
  "peak_collision_force": 18.2,
  "camera": "forward",
  "image_width": 320,
  "image_height": 180,
  "image_jpeg_base64": "..."
}
```

`collision_force` is the collision impulse divided by the 20 ms physics step,
reported in newtons for the current contact. `peak_collision_force` is the
largest value since the last reset. It is an approximate game-physics contact
force, not a calibrated load-cell measurement.

Connecting hands control to the API; disconnecting returns it to the local
pilot. A client must send a `command` at least every 500 ms while driving. The
watchdog neutralizes stale commands, and a disconnect also commands neutral.
The simulation continues in real time: this first interface does not pause or
advance physics deterministically.
