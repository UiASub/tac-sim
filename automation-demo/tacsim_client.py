"""Small dependency-free client for TAC Sim's line-delimited JSON API."""

from __future__ import annotations

import base64
import json
import socket
from typing import Any

import cv2
import numpy as np
from numpy.typing import NDArray


class TacSimClient:
    def __init__(self, host: str = "127.0.0.1", port: int = 8765) -> None:
        self._socket = socket.create_connection((host, port), timeout=5)
        self._socket.settimeout(10)
        self._stream = self._socket.makefile("rwb")
        self._request_id = 0

    def request(self, **request: Any) -> dict[str, Any]:
        self._request_id += 1
        request["request_id"] = self._request_id
        self._stream.write(json.dumps(request, separators=(",", ":")).encode() + b"\n")
        self._stream.flush()
        line = self._stream.readline()
        if not line:
            raise ConnectionError("simulator closed the connection")
        response: dict[str, Any] = json.loads(line)
        if not response.get("ok"):
            raise RuntimeError(response.get("error", "unknown simulator error"))
        return response

    def command(
        self,
        *,
        surge: float = 0,
        sway: float = 0,
        heave: float = 0,
        yaw: float = 0,
        camera: str = "forward",
        capture: bool = True,
        image_width: int = 480,
        image_height: int = 270,
        **sensors: Any,
    ) -> dict[str, Any]:
        """Drive the vehicle. Extra keywords pass through, e.g. ``sonar_image=True`` or
        ``camera_images=["explorehd", "fisheye"]``; ``response["sensors"]`` holds the readings."""
        return self.request(
            action="command",
            surge=surge,
            sway=sway,
            heave=heave,
            yaw=yaw,
            camera=camera,
            capture=capture,
            image_width=image_width,
            image_height=image_height,
            **sensors,
        )

    def observe(self, **sensors: Any) -> dict[str, Any]:
        return self.request(action="observe", **sensors)

    def reset(self, *, seed: int = 0, **settings: Any) -> dict[str, Any]:
        """Reset the vehicle; a non-zero seed redraws every sensor's unit errors."""
        return self.request(action="reset", seed=seed, **settings)

    def describe(self) -> list[dict[str, Any]]:
        """Sensor models, mount poses (FRD) and camera intrinsics."""
        info: list[dict[str, Any]] = self.request(action="describe")["sensor_info"]
        return info

    @staticmethod
    def decode_image(response: dict[str, Any]) -> NDArray[np.uint8]:
        encoded = response.get("image_jpeg_base64")
        if not encoded:
            raise ValueError("response did not contain an image")
        pixels = np.frombuffer(base64.b64decode(encoded), dtype=np.uint8)
        image = cv2.imdecode(pixels, cv2.IMREAD_COLOR)
        if image is None:
            raise ValueError("simulator returned an invalid JPEG")
        # RenderTexture pixels have a bottom-left origin; OpenCV uses top-left.
        return cv2.flip(image, 0)

    @staticmethod
    def decode_camera(image: dict[str, Any]) -> NDArray[np.uint8]:
        """Decode one entry of ``response["images"]`` to a top-left-origin BGR image."""
        return TacSimClient.decode_image({"image_jpeg_base64": image["jpeg_base64"]})

    @staticmethod
    def decode_sonar(response: dict[str, Any]) -> NDArray[np.uint8]:
        """Sonar intensity as (range bins, beams); row 0 is nearest, column 0 is port."""
        sonar = response["sensors"]["sonar"]
        encoded = sonar.get("image_base64")
        if not encoded:
            raise ValueError("request the sonar image with sonar_image=True")
        raw = np.frombuffer(base64.b64decode(encoded), dtype=np.uint8)
        return raw.reshape(sonar["bins"], sonar["beams"])

    @staticmethod
    def sonar_fan(polar: NDArray[np.uint8], aperture_deg: float, height: int = 300) -> NDArray[np.uint8]:
        """Project a (bins, beams) sonar image to a coloured fan, apex at the bottom."""
        bins, beams = polar.shape
        half = np.radians(aperture_deg / 2)
        width = int(np.ceil(2 * height * np.sin(half)))
        rows, columns = np.mgrid[0:height, 0:width]
        dx = columns + 0.5 - width / 2
        dy = height - (rows + 0.5)
        radius = np.hypot(dx, dy) / height
        angle = np.arctan2(dx, dy)
        inside = (radius <= 1) & (np.abs(angle) <= half)
        beam = np.clip(((angle / (2 * half) + 0.5) * beams).astype(int), 0, beams - 1)
        bin_ = np.clip((radius * bins).astype(int), 0, bins - 1)
        fan = np.where(inside, polar[bin_, beam], 0).astype(np.uint8)
        return cv2.applyColorMap(fan, cv2.COLORMAP_HOT)

    def close(self) -> None:
        try:
            self.request(action="command", surge=0, sway=0, heave=0, yaw=0)
        except (ConnectionError, OSError, RuntimeError):
            pass
        self._stream.close()
        self._socket.close()

    def __enter__(self) -> TacSimClient:
        return self

    def __exit__(self, *_: object) -> None:
        self.close()
