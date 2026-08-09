"""Tiny deterministic NumPy depth-buffer renderer.

The transform contract is Vintage Story model coordinates (X east, Y up,
Z south) expressed in blocks.  Camera coordinates use +Z forward, +X right,
and +Y up.  This is intentionally diagnostic, not a replacement for the
game's renderer.
"""

from __future__ import annotations

from dataclasses import dataclass
from math import radians, tan
from typing import Iterable, Mapping

import numpy as np
from PIL import Image

from .geometry import Triangle
from .materials import Material, shade


@dataclass(frozen=True)
class Camera:
    position: np.ndarray
    target: np.ndarray
    width: int
    height: int
    fov: float = 42.0
    orthographic: bool = False
    orthographic_scale: float = 2.5
    roll: float = 0.0
    near: float = .001

    def __post_init__(self) -> None:
        object.__setattr__(self, "position", np.asarray(self.position, dtype=float))
        object.__setattr__(self, "target", np.asarray(self.target, dtype=float))


def _basis(camera: Camera) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    forward = camera.target - camera.position
    forward = forward / (np.linalg.norm(forward) or 1)
    up = np.asarray((0.0, 1.0, 0.0))
    right = np.cross(forward, up)
    right = right / (np.linalg.norm(right) or 1)
    up = np.cross(right, forward)
    if camera.roll:
        amount = radians(camera.roll)
        rotated_right = right * np.cos(amount) + up * np.sin(amount)
        up = -right * np.sin(amount) + up * np.cos(amount)
        right = rotated_right
    return right, up, forward


def _camera_points(points: np.ndarray, camera: Camera) -> np.ndarray:
    right, up, forward = _basis(camera)
    delta = points - camera.position
    return np.stack((delta @ right, delta @ up, delta @ forward), axis=1)


def _clip(points: np.ndarray, uvs: np.ndarray, near: float) -> tuple[np.ndarray, np.ndarray]:
    polygon = [(points[index], uvs[index]) for index in range(len(points))]
    clipped = []
    for index, current in enumerate(polygon):
        previous = polygon[index - 1]
        current_inside, previous_inside = current[0][2] >= near, previous[0][2] >= near
        if current_inside != previous_inside:
            amount = (near - previous[0][2]) / (current[0][2] - previous[0][2])
            clipped.append((previous[0] + amount * (current[0] - previous[0]), previous[1] + amount * (current[1] - previous[1])))
        if current_inside:
            clipped.append(current)
    if len(clipped) < 3:
        return np.empty((0, 3)), np.empty((0, 2))
    return np.asarray([item[0] for item in clipped]), np.asarray([item[1] for item in clipped])


def _project(points: np.ndarray, camera: Camera) -> tuple[np.ndarray, np.ndarray]:
    if camera.orthographic:
        scale = camera.orthographic_scale
        x = points[:, 0] / scale * camera.height + camera.width / 2
        y = -points[:, 1] / scale * camera.height + camera.height / 2
    else:
        focal = .5 * camera.height / tan(radians(camera.fov) / 2)
        x = points[:, 0] / points[:, 2] * focal + camera.width / 2
        y = -points[:, 1] / points[:, 2] * focal + camera.height / 2
    return np.stack((x, y), axis=1), points[:, 2]


def _raster_triangle(image: np.ndarray, depth: np.ndarray, triangle: Triangle, camera: Camera, material: Material, *, transparent: bool, linear: bool, key: np.ndarray, fill: np.ndarray, rim: np.ndarray, ghost_opacity: float = 1.0) -> None:
    points = _camera_points(triangle.vertices, camera)
    points, uv = _clip(points, triangle.uvs, camera.near)
    if len(points) < 3:
        return
    # Near-plane clipping can turn one triangle into a quad. Rasterize the
    # clipped polygon as a fan instead of silently dropping its fourth vertex.
    for index in range(1, len(points) - 1):
        clipped_points = points[[0, index, index + 1]]
        clipped_uv = uv[[0, index, index + 1]]
        screen, z = _project(clipped_points, camera)
        min_x = max(0, int(np.floor(screen[:, 0].min()))); max_x = min(camera.width - 1, int(np.ceil(screen[:, 0].max())))
        min_y = max(0, int(np.floor(screen[:, 1].min()))); max_y = min(camera.height - 1, int(np.ceil(screen[:, 1].max())))
        if min_x > max_x or min_y > max_y:
            continue
        a, b, c = screen
        area = float((b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1]))
        if abs(area) < 1e-8:
            continue
        yy, xx = np.mgrid[min_y:max_y + 1, min_x:max_x + 1]
        px, py = xx + .5, yy + .5
        # These weights correspond to vertices a, b, and c in that order.
        # The previous edge-function order was cyclically shifted, so depth
        # and UVs could be borrowed from the wrong corner of a face.
        w0 = ((b[1] - c[1]) * (px - c[0]) + (c[0] - b[0]) * (py - c[1])) / area
        w1 = ((c[1] - a[1]) * (px - c[0]) + (a[0] - c[0]) * (py - c[1])) / area
        w2 = 1 - w0 - w1
        inside = (w0 >= -1e-7) & (w1 >= -1e-7) & (w2 >= -1e-7)
        if not np.any(inside):
            continue
        inverse = 1 / np.maximum(z, camera.near)
        weights = np.stack((w0, w1, w2), axis=-1) * inverse[np.newaxis, np.newaxis, :]
        denominator = weights.sum(axis=-1)
        weights /= np.maximum(denominator[..., None], 1e-12)
        pixel_depth = 1 / np.maximum(denominator, 1e-12)
        uv_values = weights @ clipped_uv
        sampled = material.sample(uv_values, linear=linear)
        sampled = shade(sampled, triangle.normal, key=key, fill=fill, rim=rim, glow=triangle.glow)
        sampled[..., 3] *= ghost_opacity * triangle.opacity
        region = image[min_y:max_y + 1, min_x:max_x + 1]
        region_depth = depth[min_y:max_y + 1, min_x:max_x + 1]
        closer = pixel_depth < region_depth
        if transparent:
            visible = inside & (pixel_depth <= region_depth + 1e-6)
            source_alpha = sampled[..., 3] * visible
            region[..., :3] = sampled[..., :3] * source_alpha[..., None] + region[..., :3] * (1 - source_alpha[..., None])
            region[..., 3] = source_alpha + region[..., 3] * (1 - source_alpha)
        else:
            visible = inside & closer & (sampled[..., 3] >= .5)
            region_rgb = region[..., :3]
            region_alpha = region[..., 3]
            region_rgb[visible] = sampled[..., :3][visible]
            region_alpha[visible] = 1
            region_depth[visible] = pixel_depth[visible]


def render(triangles: Iterable[Triangle], materials: Mapping[str, Material], camera: Camera, *, background=(.08, .09, .11, 1), linear=False, key=(.75, .8, 1), fill=(-.5, .4, .6), rim=(0, -.7, .8), ground=False) -> Image.Image:
    image = np.zeros((camera.height, camera.width, 4), dtype=np.float32)
    image[:] = np.asarray(background, dtype=np.float32)
    depth = np.full((camera.height, camera.width), np.inf, dtype=np.float32)
    key_v, fill_v, rim_v = (np.asarray(vector, dtype=float) / (np.linalg.norm(vector) or 1) for vector in (key, fill, rim))
    opaque, transparent = [], []
    for triangle in triangles:
        (transparent if triangle.alpha_mode == "transparent" else opaque).append(triangle)
    for triangle in opaque:
        _raster_triangle(image, depth, triangle, camera, materials.get(triangle.texture, materials["missing"]), transparent=False, linear=linear, key=key_v, fill=fill_v, rim=rim_v)
    for triangle in sorted(transparent, key=lambda item: float(np.mean(_camera_points(item.vertices, camera)[:, 2])), reverse=True):
        _raster_triangle(image, depth, triangle, camera, materials.get(triangle.texture, materials["missing"]), transparent=True, linear=linear, key=key_v, fill=fill_v, rim=rim_v)
    if ground:
        # A restrained contact tint keeps scale readable without becoming a shadow renderer.
        alpha = np.exp(-((np.arange(camera.height)[:, None] - camera.height * .78) / (camera.height * .15)) ** 2) * .12
        image[..., :3] = np.clip(image[..., :3] * (1 - alpha[..., None]), 0, 1)
    return Image.fromarray(np.uint8(np.clip(image, 0, 1) * 255), "RGBA")
