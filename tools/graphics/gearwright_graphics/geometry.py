"""Renderer-neutral cuboid geometry and hierarchy transforms."""

from __future__ import annotations

from dataclasses import dataclass
from math import cos, radians, sin
from typing import Any, Iterable, Mapping

import numpy as np

FACE_CORNERS = {
    "north": ((0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)),
    "east": ((1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)),
    "south": ((0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)),
    "west": ((0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)),
    "up": ((0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)),
    "down": ((0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)),
}
FACE_NORMALS = {
    "north": (0, 0, -1), "east": (1, 0, 0), "south": (0, 0, 1),
    "west": (-1, 0, 0), "up": (0, 1, 0), "down": (0, -1, 0),
}


@dataclass(frozen=True)
class Triangle:
    vertices: np.ndarray
    uvs: np.ndarray
    texture: str
    alpha_mode: str
    glow: int
    normal: np.ndarray
    name: str
    group: str | None
    render_pass: int
    opacity: float = 1.0


def _matrix(element: Mapping[str, Any], scale_units: float = 1 / 16) -> np.ndarray:
    origin = np.asarray(element.get("rotationOrigin", element.get("RotationOrigin", (0, 0, 0))), dtype=float)[:3] * scale_units
    angles = [radians(float(element.get(key, element.get(key[0].upper() + key[1:], 0)) or 0)) for key in ("rotationX", "rotationY", "rotationZ")]
    sx = float(element.get("scaleX", element.get("ScaleX", 1)) or 1)
    sy = float(element.get("scaleY", element.get("ScaleY", 1)) or 1)
    sz = float(element.get("scaleZ", element.get("ScaleZ", 1)) or 1)
    rx, ry, rz = angles
    mx = np.array(((1, 0, 0), (0, cos(rx), -sin(rx)), (0, sin(rx), cos(rx))), dtype=float)
    my = np.array(((cos(ry), 0, sin(ry)), (0, 1, 0), (-sin(ry), 0, cos(ry))), dtype=float)
    mz = np.array(((cos(rz), -sin(rz), 0), (sin(rz), cos(rz), 0), (0, 0, 1)), dtype=float)
    matrix = mz @ my @ mx @ np.diag((sx, sy, sz))
    result = np.eye(4)
    result[:3, :3] = matrix
    offset = np.asarray((
        float(element.get("offsetX", element.get("OffsetX", 0)) or 0),
        float(element.get("offsetY", element.get("OffsetY", 0)) or 0),
        float(element.get("offsetZ", element.get("OffsetZ", 0)) or 0),
    )) * scale_units
    result[:3, 3] = origin - matrix @ origin + offset
    return result


def _face_uv(face: Mapping[str, Any], name: str, dimensions: np.ndarray, width: float, height: float) -> np.ndarray:
    defaults = {
        "north": (0, 0, dimensions[0], dimensions[1]), "east": (0, 0, dimensions[2], dimensions[1]),
        "south": (0, 0, dimensions[0], dimensions[1]), "west": (0, 0, dimensions[2], dimensions[1]),
        "up": (0, 0, dimensions[0], dimensions[2]), "down": (0, 0, dimensions[0], dimensions[2]),
    }
    uv = face.get("uv", face.get("Uv", defaults[name]))
    u0, v0, u1, v1 = [float(value) for value in uv]
    corners = np.asarray(((u0 / width, 1 - v1 / height), (u1 / width, 1 - v1 / height), (u1 / width, 1 - v0 / height), (u0 / width, 1 - v0 / height)), dtype=float)
    turns = int(round(float(face.get("rotation", face.get("Rotation", 0))) / 90.0)) % 4
    return np.roll(corners, turns, axis=0)


def _triangles(element: Mapping[str, Any], parent: np.ndarray, texture_width: float, texture_height: float, texture_sizes: Mapping[str, tuple[float, float]]) -> list[Triangle]:
    start = np.asarray(element.get("from", element.get("From", (0, 0, 0))), dtype=float) / 16.0
    end = np.asarray(element.get("to", element.get("To", start * 16)), dtype=float) / 16.0
    dimensions = end - start
    local = _matrix(element)
    transform = parent @ local
    name = str(element.get("name", element.get("Name", "element")))
    group = element.get("group", element.get("Group"))
    render_pass = int(element.get("renderPass", element.get("RenderPass", 0)) or 0)
    opacity = max(0.0, min(1.0, float(element.get("_ghostOpacity", 1.0))))
    faces = element.get("faces", element.get("Faces", {})) or {}
    result: list[Triangle] = []
    for face_name, corners in FACE_CORNERS.items():
        face = faces.get(face_name)
        if not isinstance(face, Mapping) or face.get("enabled", face.get("Enabled", True)) is False:
            continue
        points = []
        for x, y, z in corners:
            local_point = np.r_[start + dimensions * (x, y, z), 1.0]
            points.append((transform @ local_point)[:3])
        points_np = np.asarray(points, dtype=float)
        texture = str(face.get("texture", face.get("Texture", "missing"))).lstrip("#")
        face_width, face_height = texture_sizes.get(texture, (texture_width, texture_height))
        uv = _face_uv(face, face_name, dimensions * 16, face_width, face_height)
        normal = np.cross(points_np[1] - points_np[0], points_np[2] - points_np[0])
        length = np.linalg.norm(normal)
        if length:
            normal /= length
        glow = int(face.get("glow", face.get("Glow", 0)) or 0)
        alpha_mode = "transparent" if render_pass > 0 else "opaque"
        result.extend((
            Triangle(points_np[[0, 1, 2]], uv[[0, 1, 2]], texture, alpha_mode, glow, normal, name, group, render_pass, opacity),
            Triangle(points_np[[0, 2, 3]], uv[[0, 2, 3]], texture, alpha_mode, glow, normal, name, group, render_pass, opacity),
        ))
    # Vintage Story child coordinates are local to the parent's `from`
    # coordinate. A zero-size pivot at (8, 8, 8), for example, places a
    # child authored around (0, 0, 0) at the block centre.
    child_origin = np.eye(4)
    child_origin[:3, 3] = start
    child_parent = transform @ child_origin
    for child in element.get("children", element.get("Children", [])) or []:
        result.extend(_triangles(child, child_parent, texture_width, texture_height, texture_sizes))
    return result


def triangles(shape: Mapping[str, Any]) -> list[Triangle]:
    width = float(shape.get("textureWidth", shape.get("TextureWidth", 16)) or 16)
    height = float(shape.get("textureHeight", shape.get("TextureHeight", 16)) or 16)
    texture_sizes: dict[str, tuple[float, float]] = {}
    for key, value in (shape.get("textureSizes", shape.get("TextureSizes", {})) or {}).items():
        if isinstance(value, (list, tuple)) and len(value) >= 2:
            texture_sizes[str(key).lstrip("#")] = (float(value[0]), float(value[1]))
    result: list[Triangle] = []
    for element in shape.get("elements", shape.get("Elements", [])) or []:
        result.extend(_triangles(element, np.eye(4), width, height, texture_sizes))
    return result


def bounds(tris: Iterable[Triangle]) -> tuple[np.ndarray, np.ndarray]:
    values = [triangle.vertices for triangle in tris]
    if not values:
        return np.zeros(3), np.zeros(3)
    points = np.concatenate(values)
    return points.min(axis=0), points.max(axis=0)
