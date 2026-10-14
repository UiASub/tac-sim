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

Supported actions are `command`, `observe`, `reset`, `arm`, `disarm`, `stop`
(`stop` is an alias for `disarm`), and `describe`. `camera`, when present, is
`chase`, `forward` (`explorehd`), or `downward` (`fisheye`). In the forward and
downward views, `capture` returns the simulated exploreHD or IMX307 feed. A captured image is a base64 JPEG; width is clamped to
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
  "image_time": 12.2,
  "image_jpeg_base64": "...",
  "sensors": {"...": "see Sensors below"}
}
```

## Sensors

Every response includes a `sensors` object with the latest sample of each payload
sensor and its `time` (simulation seconds). Units are SI unless the field name
says otherwise, vectors are body FRD (X forward, Y starboard, Z down), and
headings are clockwise from pool north (Unity +Z). See [the sensor models](SENSORS.md).

```json
"sensors": {
  "seed": 1, "noise": true,
  "imu": {"time": 3.2, "model": "VectorNav VN-100 IMU/AHRS",
          "acceleration": {"x": 0.01, "y": -0.01, "z": -9.81},
          "angular_rate": {"x": 0, "y": 0, "z": 0.12},
          "magnetic": {"x": 0.15, "y": 0.0, "z": 0.49},
          "yaw": 12.3, "pitch": -0.4, "roll": 0.2,
          "quaternion": [0.0, 0.0, 0.107, 0.994],
          "temperature": 35.0, "pressure_kpa": 101.3},
  "dvl": {"time": 3.18, "model": "Water Linked DVL A50",
          "vx": 0.41, "vy": -0.01, "vz": 0.0, "fom": 0.004, "altitude": 2.32,
          "time_ms": 67, "velocity_valid": true, "status": 0,
          "transducers": [{"id": 0, "velocity": 0.11, "distance": 2.51,
                           "rssi": -46, "nsd": -95, "beam_valid": true}],
          "dead_reckoning": {"x": 1.2, "y": 0.0, "z": 0.0, "std": 0.01,
                             "roll": 0, "pitch": 0, "yaw": 1.7}},
  "pressure": {"time": 3.2, "model": "Blue Robotics Bar30 (MS5837-30BA)",
               "pressure_mbar": 1290.6, "temperature_c": 27.6,
               "depth_m": 2.464, "surface_pressure_mbar": 1049.6},
  "sonar": {"time": 3.13, "model": "Sonoptix ECHO multibeam imaging sonar",
            "frequency_khz": 700, "beams": 256, "bins": 512,
            "horizontal_aperture": 120, "vertical_aperture": 20,
            "range": 15, "min_range": 0.2, "ping_rate": 15,
            "beam_ranges": [0, 0, 7.9, "... 256 values"],
            "image_base64": "..."}
}
```

The DVL array has four transducers (0–3: forward-starboard, aft-starboard,
aft-port, forward-port); the example shows one. `dead_reckoning` is NED metres
since the last reset. `beam_ranges` holds the first strong sonar return per beam
in metres, 0 when nothing is within range.

Optional request fields:

| Field | Effect |
| --- | --- |
| `camera_images` | `["explorehd", "fisheye"]` returns `images`: `name`, `model`, `width`, `height`, the capture `time` of the latency-delayed frame, and `jpeg_base64`. Size comes from `image_width`/`image_height` (up to 1920×1080). Cameras render only while requested or displayed; the first request after a pause renders immediately. |
| `sonar_image` | `true` adds `image_base64`: raw `uint8`, `bins` rows (nearest first) × `beams` columns (port first). |
| `sonar_range` | Sonar range in metres (1–30 at 700 kHz, up to 200 at 400 kHz). |
| `sonar_band` | `"700"` (120°, high resolution) or `"400"` (90°, long range). |
| `sensor_noise` | `"on"` or `"off"`; off leaves truth plus quantisation. |
| `seed` | On `reset`, a non-zero seed redraws every sensor's unit errors (biases, scale factors, offsets) for domain randomisation. The launch default is `-sensorSeed N`, or 1. |

`describe` adds `sensor_info`: each sensor's `name`, `kind`, `model`,
`mount_position` (FRD m from the hull centre), `mount_rpy` (degrees) and `rate`.
For cameras it also has the equidistant fisheye intrinsics: `native_width`,
`native_height`, `focal_px` (native pixels, principal point at the centre),
`image_circle`, `port_refraction`, `latency` and `field_of_view`.

All JPEGs keep the bottom-left origin of the original `capture`. Flip them
vertically for top-left image libraries; `TacSimClient.decode_image` and
`decode_camera` do this.

## Collision and control

`collision_force` is the collision impulse divided by the 20 ms physics step,
reported in newtons for the current contact. `peak_collision_force` is the
largest value since the last reset. It is an approximate game-physics contact
force, not a calibrated load-cell measurement.

Connecting hands control to the API; disconnecting returns it to the local
pilot. A client must send a `command` at least every 500 ms while driving. The
watchdog neutralizes stale commands, and a disconnect also commands neutral.
The simulation continues in real time: this first interface does not pause or
advance physics deterministically.
