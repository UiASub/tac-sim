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
    ) -> dict[str, Any]:
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
        )

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
