"""Autonomous TAC pipeline inspection demo for the TacArena scene.

Mission: descend -> go to the pinger -> follow the yellow pipeline with the downward camera while
reading ArUco markers -> report the ID list and its TAC score -> return to the launch area.

Run the simulator with the automation server enabled (TacArena scene), then:
    uv run pipeline_follow.py --seed 7

Simplifications that are sim-only placeholders, clearly not available on the real ROV yet:
- The pinger is "heard" as bearing + range computed from ground truth with noise (a stand-in for a
  hydrophone array). The real pinger gives bearing only.
- Altitude above the pipe comes from the known pool depth (a stand-in for an altimeter/DVL).
- Return home uses the simulator pose (a stand-in for DVL dead reckoning).
"""

from __future__ import annotations

import argparse
import math
import random
import time

import cv2
import numpy as np

from tacsim_client import TacSimClient

POOL_FLOOR_DEPTH = 5.0
PIPE_DIAMETER = 0.2
CAMERA_BELOW_CENTRE = 0.18
HOME = (0.0, -10.2)
IMAGE_SIZE = (800, 450)

ARUCO = cv2.aruco.ArucoDetector(
    cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_ARUCO_ORIGINAL), cv2.aruco.DetectorParameters()
)


def heading(state: dict) -> float:
    """Yaw in radians, 0 = +Z, positive turning right (towards +X)."""
    q = state["rotation"]
    x, y, z, w = q["x"], q["y"], q["z"], q["w"]
    forward_x = 2 * (x * z + w * y)
    forward_z = 1 - 2 * (x * x + y * y)
    return math.atan2(forward_x, forward_z)


def wrap(angle: float) -> float:
    return (angle + math.pi) % (2 * math.pi) - math.pi


def clip(value: float, limit: float) -> float:
    return float(max(-limit, min(limit, value)))


def depth_hold(state: dict, target: float) -> float:
    # Positive heave is up: ascend when deeper than target, damped by vertical velocity.
    return clip(1.6 * (state["depth"] - target) - 0.8 * state["linear_velocity"]["y"], 0.6)


def steer_to(state: dict, x: float, z: float) -> tuple[float, float]:
    """Return (bearing error, horizontal distance) from the vehicle to a world point."""
    p = state["position"]
    dx, dz = x - p["x"], z - p["z"]
    return wrap(math.atan2(dx, dz) - heading(state)), math.hypot(dx, dz)


def pipe_mask(image: np.ndarray) -> np.ndarray:
    hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
    mask = cv2.inRange(hsv, np.array((15, 110, 80)), np.array((40, 255, 255)))
    return cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))


def pipe_guidance(mask: np.ndarray) -> tuple[str, float, float]:
    """Pure-pursuit style guidance from the downward image (image top = vehicle forward).

    Returns (status, heading error rad, lateral offset -1..1). Status is "follow", "end" or "lost".
    """
    h, w = mask.shape
    ys, xs = np.nonzero(mask)
    if len(xs) < 150:
        return "lost", 0.0, 0.0
    rx = xs - w / 2
    ry = h / 2 - ys  # up = forward
    lateral = float(np.clip(np.mean(rx[np.abs(ry) < h * 0.15]) / (w / 2), -1, 1)) if np.any(np.abs(ry) < h * 0.15) else 0.0
    ahead = (np.hypot(rx, ry) > h * 0.3) & (ry > -h * 0.05)
    if np.count_nonzero(ahead) < 80:
        return "end", 0.0, lateral
    return "follow", math.atan2(float(np.mean(rx[ahead])), float(np.mean(ry[ahead]))), lateral


def score(reported: list[int], truth: list[int]) -> tuple[int, str]:
    """TAC 2026 pipeline standard points (booklet section 3.3.1)."""
    correct = [i for i in reported if i in truth]
    wrong = len(reported) - len(correct)
    points = max(0, 10 * len(correct) - 5 * wrong)
    notes = [f"{len(correct)} correct (+{10 * len(correct)}), {wrong} wrong (-{5 * wrong})"]
    order = [truth.index(i) for i in correct]
    if len(correct) > 2 and (order == sorted(order) or order == sorted(order, reverse=True)):
        points += 25
        notes.append("order +25")
    if len(reported) > 1 and reported[0] in truth and all(
        truth.index(reported[0]) <= truth.index(i) for i in correct
    ):
        points += 25
        notes.append("start direction +25")
    return points, ", ".join(notes)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--seed", type=int, default=0, help="course seed (0 = random)")
    parser.add_argument("--altitude", type=float, default=0.8, help="camera height above pipe top, m")
    parser.add_argument("--headless", action="store_true", help="no OpenCV window")
    parser.add_argument("--timeout", type=float, default=300)
    args = parser.parse_args()
    target_depth = POOL_FLOOR_DEPTH - PIPE_DIAMETER - args.altitude - CAMERA_BELOW_CENTRE

    with TacSimClient(port=args.port) as sim:
        truth_state = sim.request(action="scenario", seed=args.seed)
        truth = truth_state["pipeline_marker_ids"]
        pinger = truth_state["pinger_position"]
        print(f"course seed {truth_state['course_seed']}: {len(truth)} pipeline markers (hidden from the controller)")

        phase, seen, started = "descend", [], time.monotonic()
        follow_started = lost_since = None
        while time.monotonic() - started < args.timeout:
            cmd = dict(surge=0.0, sway=0.0, heave=0.0, yaw=0.0)
            state = sim.request(action="observe", camera="downward", capture=True,
                                image_width=IMAGE_SIZE[0], image_height=IMAGE_SIZE[1])
            image = sim.decode_image(state)
            mask = pipe_mask(image)
            status, pipe_error, lateral = pipe_guidance(mask)
            cmd["heave"] = depth_hold(state, target_depth)

            corners, ids, _ = ARUCO.detectMarkers(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY))
            if ids is not None:
                cv2.aruco.drawDetectedMarkers(image, corners, ids)
                if phase == "follow":
                    for marker in ids.flatten().tolist():
                        if 1 <= marker <= 99 and marker not in seen:
                            seen.append(marker)
                            print(f"  marker {marker}")

            if phase == "descend":
                if abs(state["depth"] - target_depth) < 0.3:
                    phase = "to_pinger"
                    print("phase: go to pinger")
            elif phase == "to_pinger":
                # Simulated acoustic fix with noise (placeholder for a hydrophone array).
                error, distance = steer_to(state, pinger["x"], pinger["z"])
                error += math.radians(random.gauss(0, 4))
                cmd["yaw"] = clip(1.2 * error, 0.6)
                cmd["surge"] = clip(0.5 * distance, 0.5) * max(0.0, math.cos(error))
                if distance < 0.35:
                    phase = "align"
                    print("phase: align with pipe")
            elif phase == "align":
                # At the pinger end the only pipe ahead leads away from the pinger.
                if status == "follow":
                    cmd["yaw"] = clip(1.0 * pipe_error, 0.5)
                    cmd["sway"] = clip(0.8 * lateral, 0.3)
                    if abs(pipe_error) < math.radians(10):
                        phase, follow_started = "follow", time.monotonic()
                        print("phase: follow pipeline")
                else:
                    cmd["yaw"] = 0.25  # rotate until the pipe lies ahead
            elif phase == "follow":
                if status == "follow":
                    lost_since = None
                    cmd["yaw"] = clip(1.1 * pipe_error, 0.55)
                    cmd["sway"] = clip(0.9 * lateral, 0.35)
                    cmd["surge"] = 0.3 * max(0.15, math.cos(pipe_error) ** 3)
                else:
                    lost_since = lost_since or time.monotonic()
                    if time.monotonic() - lost_since > 2.5 and time.monotonic() - follow_started > 5:
                        points, notes = score(seen, truth)
                        print(f"\nPIPELINE RESULT: {','.join(map(str, seen))}")
                        print(f"truth (from pinger): {','.join(map(str, truth))}")
                        print(f"standard points: {points} ({notes}); autonomous detection bonus: {10 * len(set(seen) & set(truth))}")
                        phase = "home"
                        print("phase: return home")
            elif phase == "home":
                error, distance = steer_to(state, *HOME)
                cmd["yaw"] = clip(1.2 * error, 0.6)
                cmd["surge"] = clip(0.5 * distance, 0.6) * max(0.0, math.cos(error))
                cmd["heave"] = depth_hold(state, 0.6)
                if distance < 0.5:
                    print("home: mission complete")
                    break

            sim.request(action="command", **cmd)
            if not args.headless:
                view = image.copy()
                view[mask > 0] = (0.5 * view[mask > 0] + (0, 90, 90)).astype(np.uint8)
                text = f"{phase}  pipe:{status}  err {math.degrees(pipe_error):+.0f}  lat {lateral:+.2f}  ids {seen}"
                cv2.putText(view, text, (10, 24), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2, cv2.LINE_AA)
                cv2.imshow("TAC pipeline autonomy", view)
                if cv2.waitKey(1) & 0xFF == 27:
                    break
        else:
            print(f"timeout in phase {phase}; markers so far: {seen}")
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
