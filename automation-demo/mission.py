"""Autonomous TAC mission DRAFT for the TacArena scene.

Mission: pipeline inspection -> subsea docking -> visual inspection of the structure -> open valves
A and B with the manipulator -> return to the launch area and surface. Prints a TAC score sheet.

Run the simulator with the automation server enabled (TacArena scene), then:
    uv run mission.py --seed 7
    uv run mission.py --seed 7 --only dock,valves     # tasks: pipeline, dock, inspect, valves

What the vehicle does on its own sensors:
- Pipeline following, marker reading, the docking approach (pose from ArUco 28/7/19/96) and both valve
  approaches (orange bucket in the camera) are camera-based. Depth and heading come from telemetry.
- When the target leaves the camera view close up, the last metre is flown on dead reckoning from
  the simulator's velocity (a stand-in for a DVL).

Sim-only stand-ins, clearly not available on the real ROV yet:
- Transits between tasks and the inspection lap use the simulator pose and the known layout of the
  course (dock and structure position), as if the layout had been surveyed before the run.
- The pinger is "heard" as bearing + range computed from ground truth with noise.
- The manipulator is a draft (rotating wrist + tilt joint); see TacArm.cs.
"""

from __future__ import annotations

import argparse
import math
import random
import time
from collections import Counter
from pathlib import Path

import cv2
import numpy as np

from pipeline_follow import (
    ARUCO,
    CAMERA_BELOW_CENTRE,
    HOME,
    PIPE_DIAMETER,
    POOL_FLOOR_DEPTH,
    clip,
    depth_hold,
    heading,
    pipe_guidance,
    pipe_mask,
    score,
    wrap,
)
from tacsim_client import TacSimClient

FOV_VERTICAL = math.radians(65)
SMALL = (800, 450)
LARGE = (1280, 720)
TRANSIT_DEPTH = 2.5

# Vehicle geometry (TacArm.cs, PilotView.cs), metres from the vehicle centre.
FORWARD_CAMERA_AHEAD = 0.28
DOWN_CAMERA_AHEAD = 0.04
ARM_BELOW = 0.12
ARM_AHEAD = 0.26
ARM_REACH = 0.30

# Valve bucket (booklet): outer radius 120 mm, 85 mm deep. The fork tip is seated 40 mm inside the rim.
VALVE_RADIUS = 0.12
SEAT_INSIDE_RIM = 0.04
VALVE_A_HEIGHT = 0.75  # above the floor (TacCourse.cs, ASSUMED)

# Docking station markers: centre (x, z) in the dock frame, +Z = back (TacCourse.cs, ASSUMED order).
DOCK_MARKER_SIZE = 0.15
DOCK_MARKERS = {28: (-0.475, 0.275), 7: (0.475, 0.275), 19: (0.475, -0.275), 96: (-0.475, -0.275)}


class TaskFailed(Exception):
    pass


def focal(size: tuple[int, int]) -> float:
    return size[1] / 2 / math.tan(FOV_VERTICAL / 2)


def body_velocity(state: dict) -> tuple[float, float]:
    """Horizontal velocity as (right, forward) in the vehicle frame."""
    h, v = heading(state), state["linear_velocity"]
    return v["x"] * math.cos(h) - v["z"] * math.sin(h), v["x"] * math.sin(h) + v["z"] * math.cos(h)


def position_hold(state: dict, right: float, forward: float, limit: float = 0.3, gain: float = 1.2) -> dict:
    """Sway/surge that drive a (right, forward) position error in metres to zero."""
    v_right, v_forward = body_velocity(state)
    return dict(sway=clip(gain * right - v_right, limit), surge=clip(gain * forward - v_forward, limit))


def yaw_hold(state: dict, target: float) -> float:
    return clip(1.2 * wrap(target - heading(state)), 0.5)


class Rov:
    """One request per control cycle: send the command, get telemetry and a camera image back."""

    def __init__(self, sim: TacSimClient, headless: bool, record: Path | None) -> None:
        self.sim, self.headless, self.record = sim, headless, record
        self.state: dict = {}
        self.time: float | None = None
        self.dt = 0.0
        self.dr = np.zeros(2)  # dead-reckoned (x, z) from integrated velocity; drifts, origin arbitrary
        self.task = ""
        self.frames = 0

    def tick(self, camera: str = "downward", size: tuple[int, int] = SMALL, **cmd: float) -> tuple[dict, np.ndarray]:
        state = self.sim.request(action="command", camera=camera, capture=True, image_width=size[0],
                                 image_height=size[1], **{k: clip(cmd.get(k, 0.0), 1) for k in ("surge", "sway", "heave", "yaw")})
        now = state["sim_time"]
        self.dt = 0.0 if self.time is None else max(0.0, now - self.time)
        v = state["linear_velocity"]
        self.dr += self.dt * np.array((v["x"], v["z"]))
        self.time, self.state = now, state
        return state, self.sim.decode_image(state)

    def manipulator(self, wrist: float, tilt: float) -> None:
        self.sim.request(action="manipulator", wrist=wrist, tilt=tilt)

    def truth(self) -> dict:
        return self.sim.request(action="observe", ground_truth=True)

    def show(self, image: np.ndarray, text: str) -> None:
        self.frames += 1
        if self.headless and self.record is None:
            return
        view = image if image.shape[1] <= 800 else cv2.resize(image, SMALL)
        view = view.copy()
        cv2.putText(view, f"{self.task}: {text}", (10, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 0, 0), 4, cv2.LINE_AA)
        cv2.putText(view, f"{self.task}: {text}", (10, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1, cv2.LINE_AA)
        if self.record is not None and self.frames % 15 == 0:
            cv2.imwrite(str(self.record / f"{self.frames:06d}.jpg"), view)
        if not self.headless:
            cv2.imshow("TAC mission", view)
            if cv2.waitKey(1) & 0xFF == 27:
                raise KeyboardInterrupt


class Target:
    """A fixed point remembered in dead-reckoning coordinates, so it can be approached without seeing it."""

    def __init__(self, rov: Rov) -> None:
        self.rov = rov
        self.point: np.ndarray | None = None

    def fix(self, right: float, forward: float) -> None:
        h = heading(self.rov.state)
        self.point = self.rov.dr + right * np.array((math.cos(h), -math.sin(h))) + forward * np.array((math.sin(h), math.cos(h)))

    def error(self) -> tuple[float, float]:
        h = heading(self.rov.state)
        d = self.point - self.rov.dr
        return float(d[0] * math.cos(h) - d[1] * math.sin(h)), float(d[0] * math.sin(h) + d[1] * math.cos(h))


def goto(rov: Rov, x: float, z: float, depth: float, *, final_heading: float | None = None,
         face: tuple[float, float] | None = None, tolerance: float = 0.15, settle: bool = True, camera: str = "downward",
         size: tuple[int, int] = SMALL, on_frame=None, label: str = "transit", timeout: float = 120) -> None:
    """Fly to a world position using the simulator pose (stand-in for a navigation system)."""
    cmd: dict = {}
    started = None
    while True:
        s, image = rov.tick(camera, size, **cmd)
        started = s["sim_time"] if started is None else started
        if on_frame is not None:
            on_frame(image)
        p, h = s["position"], heading(s)
        dx, dz = x - p["x"], z - p["z"]
        distance = math.hypot(dx, dz)
        if face is not None:
            want = math.atan2(face[0] - p["x"], face[1] - p["z"])
        elif final_heading is not None and distance < 1.0:
            want = final_heading
        else:
            want = math.atan2(dx, dz) if distance > 0.6 else h
        cmd = position_hold(s, dx * math.cos(h) - dz * math.sin(h), dx * math.sin(h) + dz * math.cos(h),
                            limit=0.5 if distance > 1.5 else 0.3, gain=0.9)
        cmd["yaw"] = yaw_hold(s, want)
        cmd["heave"] = depth_hold(s, depth)
        v = s["linear_velocity"]
        steady = math.hypot(v["x"], v["z"]) < 0.06 and (final_heading is None or abs(wrap(final_heading - h)) < math.radians(4))
        if distance < tolerance and abs(s["depth"] - depth) < (0.03 if settle else 0.12) and (steady or not settle):
            return
        if s["sim_time"] - started > timeout:
            raise TaskFailed(f"{label}: did not arrive within {timeout:.0f} s")
        rov.show(image, f"{label} {distance:.1f} m")


def change_depth(rov: Rov, depth: float, label: str = "depth") -> None:
    p = rov.state["position"]
    goto(rov, p["x"], p["z"], depth, final_heading=heading(rov.state), label=label, timeout=60)


# ----------------------------------------------------------------------------- pipeline

def task_pipeline(rov: Rov, layout: dict, altitude: float) -> dict:
    """Same guidance as pipeline_follow.py: pinger -> align -> follow the yellow pipe, logging markers."""
    target_depth = POOL_FLOOR_DEPTH - PIPE_DIAMETER - altitude - CAMERA_BELOW_CENTRE
    pinger, truth = layout["pinger_position"], layout["pipeline_marker_ids"]
    phase, seen, cmd = "descend", [], {}
    started = follow_started = lost_since = track = None  # track: world bearing travelled along the pipe
    while True:
        s, image = rov.tick("downward", **cmd)
        now = s["sim_time"]
        started = now if started is None else started
        if now - started > 300:
            raise TaskFailed(f"pipeline: timeout in phase {phase}, markers so far {seen}")
        mask = pipe_mask(image)
        status, pipe_error, lateral = pipe_guidance(mask, None if track is None else wrap(track - heading(s)))
        cmd = dict(surge=0.0, sway=0.0, yaw=0.0, heave=depth_hold(s, target_depth))
        corners, ids, _ = ARUCO.detectMarkers(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY))
        if ids is not None and phase == "follow":
            for marker in ids.flatten().tolist():
                if 1 <= marker <= 99 and marker not in seen:
                    seen.append(marker)
                    print(f"  pipeline marker {marker}")

        if phase == "descend":
            if abs(s["depth"] - target_depth) < 0.3:
                phase = "to pinger"
        elif phase == "to pinger":
            # Simulated acoustic fix with noise (placeholder for a hydrophone array).
            p = s["position"]
            dx, dz = pinger["x"] - p["x"], pinger["z"] - p["z"]
            error = wrap(math.atan2(dx, dz) - heading(s)) + math.radians(random.gauss(0, 4))
            distance = math.hypot(dx, dz)
            cmd["yaw"] = clip(1.2 * error, 0.6)
            cmd["surge"] = clip(0.5 * distance, 0.5) * max(0.0, math.cos(error))
            if distance < 0.35:
                phase = "align"
        elif phase == "align":
            if status == "follow":
                cmd["yaw"] = clip(1.0 * pipe_error, 0.5)
                cmd["sway"] = clip(0.8 * lateral, 0.3)
                if abs(pipe_error) < math.radians(10):
                    phase, follow_started, track = "follow", now, heading(s)
            else:
                cmd["yaw"] = 0.25  # rotate until the pipe lies ahead
        elif phase == "follow":
            if status == "follow":
                lost_since = None
                track += wrap(heading(s) + pipe_error - track) * min(1.0, rov.dt / 2.5)
                cmd["yaw"] = clip(1.1 * pipe_error, 0.55)
                cmd["sway"] = clip(0.9 * lateral, 0.35)
                cmd["surge"] = 0.3 * max(0.15, math.cos(pipe_error) ** 3)
            else:
                lost_since = now if lost_since is None else lost_since
                if now - lost_since > 2.5 and now - follow_started > 5:
                    break
        view = image.copy()
        view[mask > 0] = (0.5 * view[mask > 0] + (0, 90, 90)).astype(np.uint8)
        rov.show(view, f"{phase}  pipe {status}  ids {seen}")

    points, notes = score(seen, truth)
    correct = len(set(seen) & set(truth))
    print(f"  reported {seen}\n  truth    {truth} (from the pinger)")
    return dict(points=points, notes=notes, bonus=10 * correct + 200, bonus_notes=f"{correct} markers +{10 * correct}, found +100, followed +100")


# ----------------------------------------------------------------------------- docking

def dock_fix(image: np.ndarray) -> tuple[float, float, float, float] | None:
    """Locate the docking station in a downward image from its ArUco markers.

    Returns (right, forward, yaw error, camera height): the dock puck relative to the vehicle centre in
    metres, the turn that lines the vehicle up with the plate's long sides, and the height above the plate.
    """
    h, w = image.shape[:2]
    corners, ids, _ = ARUCO.detectMarkers(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY))
    if ids is None:
        return None
    plate, pixels = [], []
    half = DOCK_MARKER_SIZE / 2
    for quad, marker in zip(corners, ids.flatten().tolist()):
        if marker not in DOCK_MARKERS:
            continue
        mx, mz = DOCK_MARKERS[marker]
        # Detector corner order: top-left, top-right, bottom-right, bottom-left of the printed marker.
        for (u, v), (x, z) in zip(quad.reshape(4, 2), ((mx - half, mz + half), (mx + half, mz + half),
                                                      (mx + half, mz - half), (mx - half, mz - half))):
            plate.append((x, z))
            pixels.append((u - w / 2, h / 2 - v))  # image right / up = vehicle right / forward
    if len(plate) < 4:
        return None
    transform, _ = cv2.estimateAffinePartial2D(np.float32(plate), np.float32(pixels))
    if transform is None:
        return None
    a, b = transform[0, 0], transform[1, 0]
    scale = math.hypot(a, b)  # pixels per metre on the plate
    if scale < 1:
        return None
    yaw_error = math.atan2(-b, a)  # bearing of the dock's +Z axis from vehicle forward
    yaw_error = wrap(yaw_error + math.pi) if abs(yaw_error) > math.pi / 2 else yaw_error  # either way round is fine
    return transform[0, 2] / scale, transform[1, 2] / scale + DOWN_CAMERA_AHEAD, yaw_error, focal((w, h)) / scale


def task_dock(rov: Rov, layout: dict) -> dict:
    dock = layout["dock_position"]
    goto(rov, dock["x"], dock["z"], TRANSIT_DEPTH, label="to dock")
    target, hold = Target(rov), Target(rov)
    hold.fix(0, 0)
    phase, cmd = "search", {}
    depth_setpoint = rov.state["depth"]
    yaw_target = heading(rov.state)
    last_fix = height = docked_at = None
    started = rov.time
    while True:
        s, image = rov.tick("downward", **cmd)
        now, dt = s["sim_time"], rov.dt
        fix = dock_fix(image)
        if fix is not None:
            right, forward, yaw_error, height = fix
            target.fix(right, forward)
            yaw_target = heading(s) + yaw_error
            last_fix = now
            if phase == "search":
                phase = "approach"
        fresh = last_fix is not None and now - last_fix < 0.6
        right, forward = (target if target.point is not None else hold).error()
        offset = math.hypot(right, forward)
        cmd = position_hold(s, right, forward, limit=0.25)
        cmd["yaw"] = yaw_hold(s, yaw_target)

        if phase == "search":
            # Sink slowly until the markers are large enough to read.
            depth_setpoint = min(depth_setpoint + 0.1 * dt, 3.7)
            if now - started > 40:
                raise TaskFailed("dock: markers not found")
        elif phase == "approach":
            if fresh and offset < 0.06 and abs(wrap(yaw_target - heading(s))) < math.radians(8):
                depth_setpoint += 0.15 * dt
            if not fresh and height is not None and height < 0.9:
                phase = "blind"  # the markers have left the view; finish on dead reckoning
            elif not fresh and now - last_fix > 10:
                raise TaskFailed("dock: lost the markers")
        elif phase == "blind":
            if offset < 0.04:
                depth_setpoint += 0.08 * dt
            if depth_setpoint - s["depth"] > 0.1 and abs(s["linear_velocity"]["y"]) < 0.02:
                phase, docked_at = "docked", now
        cmd["heave"] = depth_hold(s, depth_setpoint)
        if phase == "docked":
            # Press down gently; the real vehicle would hold on with its magnets.
            cmd = dict(position_hold(s, right, forward, limit=0.1), yaw=0.0, heave=-0.25)
            if now - docked_at > 13:
                break
        if now - started > 150:
            raise TaskFailed(f"dock: timeout in phase {phase}")
        rov.show(image, f"{phase}  offset {offset * 100:.0f} cm  height {height if height is not None else float('nan'):.2f} m")

    truth = rov.truth()
    offset = truth["puck_offset"]
    print(f"  on the plate {truth['dock_seconds']:.1f} s, pucks in contact {truth['dock_puck_seconds']:.1f} s, "
          f"puck error {math.hypot(offset['x'], offset['z']) * 100:.1f} cm")
    change_depth(rov, TRANSIT_DEPTH, "leave dock")
    points = truth["dock_points"]
    notes = ("docked 10 s +20" if truth["dock_seconds"] >= 10 else "not docked for 10 s") + \
            (", precision +50, power puck light +50" if truth["dock_puck_seconds"] >= 10 else ", pucks not in contact")
    return dict(points=points, notes=notes, bonus=100 if points else 0, bonus_notes="autonomous docking +100" if points else "")


# ----------------------------------------------------------------------------- structure

def structure_frame(layout: dict):
    centre, yaw = layout["structure_position"], math.radians(layout["structure_yaw"])

    def world(x: float, z: float) -> tuple[float, float]:
        return centre["x"] + x * math.cos(yaw) + z * math.sin(yaw), centre["z"] - x * math.sin(yaw) + z * math.cos(yaw)

    return world, yaw


def task_inspect(rov: Rov, layout: dict) -> dict:
    """Fly a lap around the structure and one pass over it, reading every marker the cameras catch."""
    world, yaw = structure_frame(layout)
    truth = layout["structure_marker_ids"]
    sightings: Counter = Counter()

    def read(image: np.ndarray) -> None:
        _, ids, _ = ARUCO.detectMarkers(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY))
        if ids is not None:
            sightings.update(m for m in ids.flatten().tolist() if 1 <= m <= 99)

    lap = [world(2.3 * math.cos(a), 1.9 * math.sin(a)) for a in (math.radians(180 + 45 * i) for i in range(9))]
    goto(rov, *lap[0], TRANSIT_DEPTH, label="to structure")
    change_depth(rov, 4.2, "inspection depth")
    for i, point in enumerate(lap[1:]):
        goto(rov, *point, 4.2, face=world(0, 0), tolerance=0.35, settle=False, camera="forward", size=LARGE,
             on_frame=read, label=f"lap {i + 1}/8  ids {sorted(sightings)}")
    change_depth(rov, 2.3, "above structure")
    for x in (-0.7, 0.7):
        goto(rov, *world(x, 0), 2.3, final_heading=yaw, tolerance=0.25, settle=False, size=LARGE, on_frame=read,
             label=f"top pass  ids {sorted(sightings)}")

    reported = sorted(m for m, count in sightings.items() if count >= 3)  # ignore one-frame misreads
    correct = [m for m in reported if m in truth]
    wrong = len(reported) - len(correct)
    print(f"  reported {reported}\n  truth    {sorted(truth)}")
    return dict(points=max(0, 20 * len(correct) - 10 * wrong), notes=f"{len(correct)} correct (+{20 * len(correct)}), {wrong} wrong (-{10 * wrong})",
                bonus=20 * len(correct), bonus_notes=f"{len(correct)} markers detected autonomously")


def valve_circle(image: np.ndarray) -> tuple[float, float, float, bool] | None:
    """Largest round orange (RAL 2004) blob: (u, v, radius in pixels, touches the image edge)."""
    hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
    mask = cv2.inRange(hsv, np.array((0, 120, 50)), np.array((14, 255, 255)))
    # The white S/O marks and the handle cut the orange ring into pieces; close the gaps.
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, np.ones((7, 7), np.uint8))
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    best, best_area = None, 250.0
    for contour in contours:
        hull = cv2.convexHull(contour)
        area = cv2.contourArea(hull)
        (u, v), radius = cv2.minEnclosingCircle(hull)
        if area > best_area and area > 0.75 * math.pi * radius * radius:  # round, not a bucket seen from the side
            best, best_area = (u, v, radius), area
    if best is None:
        return None
    u, v, radius = best
    h, w = image.shape[:2]
    return u, v, radius, u - radius < 2 or v - radius < 2 or u + radius > w - 2 or v + radius > h - 2


def turn_valve(rov: Rov, target: Target, depth: float, yaw: float, camera: str) -> None:
    """With the fork seated: sweep the wrist over its full travel, which catches the handle and opens it."""
    rov.manipulator(wrist=-1, tilt=1 if camera == "downward" else 0)
    started = rov.time
    while rov.time - started < 4:
        cmd = position_hold(rov.state, *target.error(), limit=0.1)
        _, image = rov.tick(camera, yaw=yaw_hold(rov.state, yaw), heave=depth_hold(rov.state, depth), **cmd)
        rov.show(image, "turning")


def task_valve_a(rov: Rov, layout: dict) -> bool:
    """Valve A on the vertical panel: line up with the forward camera, push the fork in, turn."""
    world, yaw = structure_frame(layout)
    f, (w, h) = focal(SMALL), SMALL
    depth = POOL_FLOOR_DEPTH - VALVE_A_HEIGHT - ARM_BELOW
    rov.manipulator(wrist=1, tilt=0)
    goto(rov, *world(-0.5, -1.75), rov.state["depth"], label="to valve A")
    goto(rov, *world(-0.5, -1.75), depth, final_heading=yaw, label="valve A standoff")
    rov.sim.request(action="lights_off")  # up close the headlights wash the orange out to white

    target = Target(rov)
    phase, cmd, steady, seated = "align", {}, 0, 0
    started = rov.time
    while True:
        s, image = rov.tick("forward", **cmd)
        circle = valve_circle(image)
        if circle is not None and not circle[3]:
            u, v, radius, _ = circle
            distance = f * VALVE_RADIUS / radius  # camera to the bucket rim
            right, up = (u - w / 2) / f * distance, (h / 2 - v) / f * distance
            depth = s["depth"] - (up + ARM_BELOW)  # the tool axis runs ARM_BELOW under the camera axis
            if phase == "align":
                target.fix(right, distance - 0.75)
                steady = steady + 1 if abs(right) < 0.015 and abs(up + ARM_BELOW) < 0.015 and abs(distance - 0.75) < 0.05 else 0
                if steady >= 10:
                    phase = "insert"
            else:
                # Fork tip is ARM_AHEAD + ARM_REACH ahead of the centre, the camera FORWARD_CAMERA_AHEAD.
                target.fix(right, distance + SEAT_INSIDE_RIM - (ARM_AHEAD + ARM_REACH - FORWARD_CAMERA_AHEAD))
            cv2.circle(image, (int(u), int(v)), int(radius), (255, 255, 255), 1)
        if target.point is None:
            if s["sim_time"] - started > 15:
                raise TaskFailed("valve A: bucket not found")
            cmd = dict(yaw=yaw_hold(s, yaw), heave=depth_hold(s, depth))
        else:
            right, forward = target.error()
            cmd = position_hold(s, right, forward, limit=0.2 if phase == "align" else 0.12)
            cmd.update(yaw=yaw_hold(s, yaw), heave=depth_hold(s, depth))
            seated = seated + 1 if phase == "insert" and abs(forward) < 0.01 and abs(right) < 0.015 else 0
            if seated >= 10:
                break
        if s["sim_time"] - started > 90:
            raise TaskFailed(f"valve A: timeout in phase {phase}")
        rov.show(image, f"valve A {phase}")

    turn_valve(rov, target, depth, yaw, "forward")
    target.fix(0, -0.7)
    while target.error()[1] < -0.05:  # back straight out
        cmd = position_hold(rov.state, *target.error(), limit=0.2)
        _, image = rov.tick("forward", yaw=yaw_hold(rov.state, yaw), heave=depth_hold(rov.state, depth), **cmd)
        rov.show(image, "valve A retreat")
    rov.sim.request(action="lights_on")
    return rov.truth()["valve_a_open"]


def task_valve_b(rov: Rov, layout: dict) -> bool:
    """Valve B on the horizontal shelf: arm straight down, line up with the downward camera, lower, turn."""
    world, yaw = structure_frame(layout)
    f, (w, h) = focal(SMALL), SMALL
    ahead = ARM_AHEAD - DOWN_CAMERA_AHEAD            # the tool axis ahead of the downward camera
    seated_height = ARM_BELOW + ARM_REACH - CAMERA_BELOW_CENTRE - SEAT_INSIDE_RIM  # camera above the rim when seated
    change_depth(rov, 2.4, "above structure")
    rov.manipulator(wrist=1, tilt=1)
    goto(rov, *world(0.5, 0.15 - ARM_AHEAD), 2.4, final_heading=yaw, label="over valve B")

    target = Target(rov)
    depth_setpoint = final_depth = rov.state["depth"]
    cmd, seated = {}, 0
    started = rov.time
    while True:
        s, image = rov.tick("downward", **cmd)
        circle = valve_circle(image)
        if circle is not None and not circle[3]:
            u, v, radius, _ = circle
            height = f * VALVE_RADIUS / radius  # camera above the bucket rim
            target.fix((u - w / 2) / f * height, (h / 2 - v) / f * height - ahead)
            final_depth = s["depth"] + height - seated_height
            cv2.circle(image, (int(u), int(v)), int(radius), (255, 255, 255), 1)
        if target.point is None:
            if s["sim_time"] - started > 15:
                raise TaskFailed("valve B: bucket not found")
            cmd = dict(yaw=yaw_hold(s, yaw), heave=depth_hold(s, depth_setpoint))
        else:
            right, forward = target.error()
            offset = math.hypot(right, forward)
            if offset < 0.03:
                depth_setpoint = min(depth_setpoint + 0.1 * rov.dt, final_depth)
            cmd = position_hold(s, right, forward, limit=0.15)
            cmd.update(yaw=yaw_hold(s, yaw), heave=depth_hold(s, depth_setpoint))
            seated = seated + 1 if abs(s["depth"] - final_depth) < 0.01 and offset < 0.015 else 0
            if seated >= 10:
                break
        if s["sim_time"] - started > 90:
            raise TaskFailed("valve B: timeout")
        rov.show(image, f"valve B  {final_depth - s['depth']:.2f} m to go")

    turn_valve(rov, target, final_depth, yaw, "downward")
    change_depth(rov, 2.4, "leave valve B")
    rov.manipulator(wrist=0, tilt=0)
    return rov.truth()["valve_b_open"]


def task_valves(rov: Rov, layout: dict) -> dict:
    opened = []
    for name, task in (("A", task_valve_a), ("B", task_valve_b)):
        try:
            if task(rov, layout):
                opened.append(name)
                print(f"  valve {name} opened")
            else:
                print(f"  valve {name} NOT opened")
        except TaskFailed as failure:
            print(f"  {failure}")
            rov.manipulator(wrist=0, tilt=0)
            rov.sim.request(action="lights_on")
    n = len(opened)
    return dict(points=50 * n, notes=f"opened {', '.join(opened) or 'none'}", bonus=100 * n, bonus_notes=f"{n} valves autonomously")


# ----------------------------------------------------------------------------- mission

def task_home(rov: Rov) -> None:
    change_depth(rov, TRANSIT_DEPTH, "transit depth")
    goto(rov, *HOME, TRANSIT_DEPTH, label="return home")
    goto(rov, *HOME, 0.35, tolerance=0.5, settle=False, label="surface", timeout=40)


def main() -> None:
    tasks = {"pipeline": None, "dock": task_dock, "inspect": task_inspect, "valves": task_valves}
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--seed", type=int, default=0, help="course seed (0 = random)")
    parser.add_argument("--only", default=",".join(tasks), help="comma-separated tasks to run, in mission order")
    parser.add_argument("--altitude", type=float, default=0.8, help="camera height above the pipe top, m")
    parser.add_argument("--headless", action="store_true", help="no OpenCV window")
    parser.add_argument("--record", type=Path, help="save every 15th camera frame to this folder")
    args = parser.parse_args()
    wanted = [t.strip() for t in args.only.split(",") if t.strip()]
    if unknown := [t for t in wanted if t not in tasks]:
        parser.error(f"unknown task {unknown[0]}; choose from {', '.join(tasks)}")
    if args.record is not None:
        args.record.mkdir(parents=True, exist_ok=True)

    results: dict[str, dict] = {}
    with TacSimClient(port=args.port) as sim:
        layout = sim.request(action="scenario", seed=args.seed)
        if "dock_position" not in layout or not layout.get("has_manipulator"):
            raise SystemExit("This simulator build has no docking referee or manipulator; rebuild the TacArena scene.")
        print(f"course seed {layout['course_seed']}")
        rov = Rov(sim, args.headless, args.record)
        wall = time.monotonic()
        home, sim_start = False, 0.0
        try:
            rov.tick()
            sim_start = rov.time
            if "pipeline" not in wanted:
                change_depth(rov, TRANSIT_DEPTH, "dive")
            for name in tasks:
                if name not in wanted:
                    continue
                rov.task = name
                print(f"task: {name}")
                try:
                    results[name] = task_pipeline(rov, layout, args.altitude) if name == "pipeline" else tasks[name](rov, layout)
                except TaskFailed as failure:
                    print(f"  FAILED: {failure}")
                    results[name] = dict(points=0, notes=f"failed: {failure}", bonus=0, bonus_notes="")
            rov.task = "home"
            print("task: return home and surface")
            home = True
            try:
                task_home(rov)
            except TaskFailed as failure:
                print(f"  FAILED: {failure}")
                home = False
        except KeyboardInterrupt:
            print("stopped by the operator")
            home = False
        finally:
            cv2.destroyAllWindows()

        if "pipeline" in results and home and results["pipeline"]["bonus"]:
            results["pipeline"]["bonus"] += 50
            results["pipeline"]["bonus_notes"] += ", returned +50"
        print(f"\nTAC SCORE SHEET  (seed {layout['course_seed']}, {(rov.time or 0) - sim_start:.0f} s simulated, {time.monotonic() - wall:.0f} s real)")
        for name, result in results.items():
            print(f"  {name:9s} {result['points']:4d}  {result['notes']}")
            if result["bonus"]:
                print(f"  {'':9s} {result['bonus']:4d}  autonomy bonus: {result['bonus_notes']}")
        total = sum(r["points"] + r["bonus"] for r in results.values())
        print(f"  {'total':9s} {total:4d}  (bonus counts only if nobody touches the controls)   surfaced at home: {'yes' if home else 'no'}")


if __name__ == "__main__":
    main()
