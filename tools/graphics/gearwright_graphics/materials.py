"""Nearest-neighbor materials and simple diagnostic shading."""

from __future__ import annotations

from io import BytesIO
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image


@dataclass(frozen=True)
class Material:
    name: str
    pixels: np.ndarray
    glow: int = 0

    def sample(self, uv: np.ndarray, linear: bool = False) -> np.ndarray:
        height, width = self.pixels.shape[:2]
        x = np.clip(uv[..., 0] * width, 0, width - 1)
        y = np.clip((1.0 - uv[..., 1]) * height, 0, height - 1)
        if not linear:
            return self.pixels[np.rint(y).astype(int), np.rint(x).astype(int)]
        x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
        x1, y1 = np.minimum(x0 + 1, width - 1), np.minimum(y0 + 1, height - 1)
        fx, fy = (x - x0)[..., None], (y - y0)[..., None]
        top = self.pixels[y0, x0] * (1 - fx) + self.pixels[y0, x1] * fx
        bottom = self.pixels[y1, x0] * (1 - fx) + self.pixels[y1, x1] * fx
        return top * (1 - fy) + bottom * fy


def load_material(name: str, path: Path | None, *, glow: int = 0) -> Material:
    if path is None:
        pixels = np.asarray(Image.new("RGBA", (2, 2), (238, 0, 170, 255)), dtype=np.float32) / 255.0
    else:
        pixels = np.asarray(Image.open(path).convert("RGBA"), dtype=np.float32) / 255.0
    return Material(name, pixels, glow)


def load_material_bytes(name: str, data: bytes, *, glow: int = 0) -> Material:
    """Load a resolved texture without creating a project-local cache file."""
    pixels = np.asarray(Image.open(BytesIO(data)).convert("RGBA"), dtype=np.float32) / 255.0
    return Material(name, pixels, glow)


def shade(color: np.ndarray, normal: np.ndarray, *, key: np.ndarray, fill: np.ndarray, rim: np.ndarray, glow: int = 0) -> np.ndarray:
    normal = normal / (np.linalg.norm(normal) or 1)
    ambient = .30
    value = ambient + .55 * max(0.0, float(np.dot(normal, key))) + .20 * max(0.0, float(np.dot(normal, fill))) + .15 * max(0.0, float(np.dot(normal, rim)))
    result = color.copy()
    result[..., :3] *= value
    if glow:
        result[..., :3] = np.clip(result[..., :3] + (glow / 255.0) * np.asarray((1.0, .4, .08)), 0, 1)
    return result
