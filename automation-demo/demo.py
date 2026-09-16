"""OpenCV observation viewer and deliberately simple vision controller."""

from __future__ import annotations

import argparse
import time

import cv2
import numpy as np

from tacsim_client import TacSimClient


def yellow_target(image: np.ndarray) -> tuple[np.ndarray, float, float, bool]:
    """Return an annotated image and normalized target offsets."""
    hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
    mask = cv2.inRange(hsv, np.array((16, 90, 90)), np.array((42, 255, 255)))
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return image, 0.0, 0.0, False
    contour = max(contours, key=cv2.contourArea)
    if cv2.contourArea(contour) < 40:
        return image, 0.0, 0.0, False
    x, y, width, height = cv2.boundingRect(contour)
    center_x = x + width / 2
    center_y = y + height / 2
    offset_x = center_x / image.shape[1] * 2 - 1
    offset_y = center_y / image.shape[0] * 2 - 1
    cv2.rectangle(image, (x, y), (x + width, y + height), (0, 255, 255), 2)
    cv2.line(image, (image.shape[1] // 2, image.shape[0] // 2),
             (int(center_x), int(center_y)), (0, 255, 255), 1)
    return image, offset_x, offset_y, True


def overlay(image: np.ndarray, state: dict, automatic: bool) -> None:
    collision = "CONTACT" if state["is_colliding"] else "clear"
    lines = (
        f"depth {state['depth']:.2f} m  throttle {state['throttle']:.0%}",
        f"collision {collision}  force {state['collision_force']:.1f} N  peak {state['peak_collision_force']:.1f} N",
        f"mode {'yellow-target auto' if automatic else 'manual/latching'}",
        "W/S surge  A/D sway  I/K heave  Q/E yaw  0 neutral  M auto  R reset  ESC quit",
    )
    for row, text in enumerate(lines):
        cv2.putText(image, text, (10, 24 + row * 23), cv2.FONT_HERSHEY_SIMPLEX,
                    0.48, (235, 245, 245), 1, cv2.LINE_AA)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8765)
    parser.add_argument("--auto", action="store_true", help="start the yellow-target controller")
    args = parser.parse_args()
    automatic = args.auto
    command = dict(surge=0.0, sway=0.0, heave=0.0, yaw=0.0)

    with TacSimClient(port=args.port) as sim:
        sim.request(action="reset", camera="forward")
        while True:
            started = time.monotonic()
            state = sim.command(**command)
            image = sim.decode_image(state)
            image, target_x, target_y, found = yellow_target(image)
            if automatic:
                command["yaw"] = float(np.clip(target_x * 0.8, -0.65, 0.65)) if found else 0.25
                command["heave"] = float(np.clip(target_y * -0.35, -0.25, 0.25)) if found else 0.0
                command["surge"] = 0.35 if found and abs(target_x) < 0.35 else 0.0
                command["sway"] = 0.0
            overlay(image, state, automatic)
            cv2.imshow("TAC Sim automation", image)
            key = cv2.waitKey(max(1, int((1 / 15 - (time.monotonic() - started)) * 1000))) & 0xFF
            if key == 27:
                break
            if key == ord("m"):
                automatic = not automatic
                command = dict.fromkeys(command, 0.0)
            elif key == ord("r"):
                sim.request(action="reset", camera="forward")
                command = dict.fromkeys(command, 0.0)
            elif not automatic:
                changes = {
                    ord("w"): ("surge", 0.65), ord("s"): ("surge", -0.65),
                    ord("a"): ("sway", -0.65), ord("d"): ("sway", 0.65),
                    ord("i"): ("heave", 0.65), ord("k"): ("heave", -0.65),
                    ord("q"): ("yaw", -0.65), ord("e"): ("yaw", 0.65),
                }
                if key == ord("0"):
                    command = dict.fromkeys(command, 0.0)
                elif key in changes:
                    axis, value = changes[key]
                    command[axis] = value
        cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
