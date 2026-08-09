"""Live OpenGL viewport for compiled Vintage Story cuboid shapes."""

from __future__ import annotations

import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
from PySide6.QtCore import QPoint, Qt, Signal
from PySide6.QtGui import QImage, QMatrix4x4, QMouseEvent, QVector3D, QWheelEvent
from PySide6.QtOpenGL import (
    QOpenGLBuffer,
    QOpenGLShader,
    QOpenGLShaderProgram,
    QOpenGLTexture,
    QOpenGLVertexArrayObject,
)
from PySide6.QtOpenGLWidgets import QOpenGLWidget

from .animation import apply_animation
from .assets import AssetError, AssetResolver, permissive_json, texture_mapping
from .geometry import Triangle, bounds, triangles


GL_BLEND = 0x0BE2
GL_COLOR_BUFFER_BIT = 0x00004000
GL_CULL_FACE = 0x0B44
GL_DEPTH_BUFFER_BIT = 0x00000100
GL_DEPTH_TEST = 0x0B71
GL_FLOAT = 0x1406
GL_LEQUAL = 0x0203
GL_ONE_MINUS_SRC_ALPHA = 0x0303
GL_SRC_ALPHA = 0x0302
GL_TRIANGLES = 0x0004


VERTEX_SHADER = """
#version 330 core
layout(location = 0) in vec3 position;
layout(location = 1) in vec2 uv;
layout(location = 2) in vec3 normal;
layout(location = 3) in float glow;
layout(location = 4) in float opacity;

uniform mat4 mvp;
out vec2 fragUv;
out vec3 fragNormal;
out float fragGlow;
out float fragOpacity;

void main() {
    gl_Position = mvp * vec4(position, 1.0);
    fragUv = uv;
    fragNormal = normal;
    fragGlow = glow;
    fragOpacity = opacity;
}
"""


FRAGMENT_SHADER = """
#version 330 core
in vec2 fragUv;
in vec3 fragNormal;
in float fragGlow;
in float fragOpacity;

uniform sampler2D colorTexture;
uniform int transparentPass;
out vec4 outputColor;

void main() {
    vec4 sampled = texture(colorTexture, fragUv);
    if (transparentPass == 0) {
        if (sampled.a < 0.5) discard;
        sampled.a = 1.0;
    } else {
        sampled.a *= fragOpacity;
        if (sampled.a < 0.01) discard;
    }

    vec3 n = normalize(fragNormal);
    vec3 key = normalize(vec3(-0.45, 0.82, -0.35));
    vec3 fill = normalize(vec3(0.60, 0.35, 0.45));
    float light = 0.46 + 0.42 * max(dot(n, key), 0.0) + 0.12 * max(dot(n, fill), 0.0);
    vec3 lit = sampled.rgb * light;
    lit += vec3(1.0, 0.38, 0.08) * fragGlow * 0.35;
    outputColor = vec4(clamp(lit, 0.0, 1.0), sampled.a);
}
"""


@dataclass
class _GpuBatch:
    texture: str
    transparent: bool
    center: np.ndarray
    count: int
    buffer: QOpenGLBuffer
    vao: QOpenGLVertexArrayObject


def camera_position(azimuth: float, elevation: float, distance: float, target: list[float]) -> np.ndarray:
    elevation_radians = math.radians(max(-89.0, min(89.0, elevation)))
    azimuth_radians = math.radians(azimuth)
    horizontal = math.cos(elevation_radians) * distance
    return np.asarray((
        target[0] + math.sin(azimuth_radians) * horizontal,
        target[1] + math.sin(elevation_radians) * distance,
        target[2] + math.cos(azimuth_radians) * horizontal,
    ), dtype=float)


def _triangle_rows(items: list[Triangle]) -> np.ndarray:
    rows: list[np.ndarray] = []
    for triangle in items:
        normal = np.repeat(triangle.normal[np.newaxis, :], 3, axis=0)
        glow = np.full((3, 1), float(triangle.glow) / 255.0)
        opacity = np.full((3, 1), float(triangle.opacity))
        rows.append(np.concatenate((triangle.vertices, triangle.uvs, normal, glow, opacity), axis=1))
    return np.asarray(np.concatenate(rows), dtype=np.float32)


class LiveModelViewport(QOpenGLWidget):
    """Persistent GPU view; camera changes never invoke the photoshoot renderer."""

    cameraChanged = Signal(float, float, float)
    sceneStatus = Signal(str)

    def __init__(self, project_root: Path, vintage_story: Path | None, parent=None) -> None:
        super().__init__(parent)
        self.project_root = project_root.resolve()
        self.vintage_story = vintage_story.resolve() if vintage_story else None
        self.base_shape: dict[str, Any] | None = None
        self.shape_path: Path | None = None
        self.animation_code = ""
        self.frame = 0.0
        self.scene_triangles: list[Triangle] = []
        self.texture_payloads: dict[str, bytes | None] = {"missing": None}
        self.texture_objects: dict[str, QOpenGLTexture] = {}
        self.batches: list[_GpuBatch] = []
        self.program: QOpenGLShaderProgram | None = None
        self.scene_dirty = False
        self.textures_dirty = False
        self.azimuth = 28.0
        self.elevation = 12.0
        self.distance = 6.5
        self.target = [0.5, 0.75, 0.5]
        self.scale = 4.8
        self.drag_start: QPoint | None = None
        self.drag_button = Qt.MouseButton.NoButton
        self.setMinimumSize(560, 420)
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)

    def set_camera(self, azimuth: float, elevation: float, distance: float, target: list[float], scale: float) -> None:
        self.azimuth = float(azimuth)
        self.elevation = max(-89.0, min(89.0, float(elevation)))
        self.distance = max(.25, float(distance))
        self.target = [float(value) for value in target]
        self.scale = max(.05, float(scale))
        self.cameraChanged.emit(self.azimuth, self.elevation, self.scale)
        self.update()

    def set_view(self, azimuth: float, elevation: float) -> None:
        self.azimuth = float(azimuth)
        self.elevation = max(-89.0, min(89.0, float(elevation)))
        self.cameraChanged.emit(self.azimuth, self.elevation, self.scale)
        self.update()

    def fit_model(self) -> None:
        if not self.scene_triangles:
            return
        minimum, maximum = bounds(self.scene_triangles)
        center = (minimum + maximum) * .5
        extent = maximum - minimum
        self.target = [float(value) for value in center]
        self.scale = max(float(extent[1]), float(extent[0]), float(extent[2]), .25) * 1.35
        self.distance = max(float(np.linalg.norm(extent)) * 1.5, 1.0)
        self.cameraChanged.emit(self.azimuth, self.elevation, self.scale)
        self.update()

    def load_shape(self, path: Path, animation: str = "", frame: float = 0.0) -> None:
        path = path.resolve()
        self.base_shape = permissive_json(path.read_bytes(), str(path))
        self.shape_path = path
        self.animation_code = animation
        self.frame = frame
        self.texture_payloads, warnings = self._resolve_textures(self.base_shape)
        self.textures_dirty = True
        self._rebuild_geometry()
        if warnings:
            self.sceneStatus.emit("; ".join(warnings))
        else:
            self.sceneStatus.emit(f"Loaded {path.name} on the GPU")

    def set_pose(self, animation: str, frame: float) -> None:
        if self.base_shape is None:
            return
        if animation == self.animation_code and abs(frame - self.frame) < 1e-8:
            return
        self.animation_code = animation
        self.frame = float(frame)
        self._rebuild_geometry()

    def _rebuild_geometry(self) -> None:
        if self.base_shape is None:
            self.scene_triangles = []
        else:
            posed = apply_animation(self.base_shape, self.animation_code, self.frame, mode="loop") if self.animation_code else self.base_shape
            self.scene_triangles = triangles(posed)
        self.scene_dirty = True
        self.update()

    def _resolve_textures(self, shape: dict[str, Any]) -> tuple[dict[str, bytes | None], list[str]]:
        sources = [path for path in (self.vintage_story, self.project_root) if path is not None]
        resolver = AssetResolver(sources)
        mapping = texture_mapping(shape)
        payloads: dict[str, bytes | None] = {"missing": None}
        warnings: list[str] = []
        for key, initial in mapping.items():
            value = initial
            seen = {key}
            while value.startswith("#"):
                alias = value[1:]
                if alias in seen or alias not in mapping:
                    warnings.append(f"Unresolved texture alias #{key}")
                    value = ""
                    break
                seen.add(alias)
                value = mapping[alias]
            if not value:
                continue
            try:
                payloads[key] = resolver.texture(value).read()
            except (AssetError, OSError) as exc:
                warnings.append(f"#{key}: {exc}")
                payloads[key] = None
        return payloads, warnings

    def initializeGL(self) -> None:
        gl = self.context().functions()
        gl.glEnable(GL_DEPTH_TEST)
        gl.glDepthFunc(GL_LEQUAL)
        gl.glEnable(GL_CULL_FACE)
        self.program = QOpenGLShaderProgram(self)
        if not self.program.addShaderFromSourceCode(QOpenGLShader.ShaderTypeBit.Vertex, VERTEX_SHADER):
            raise RuntimeError(self.program.log())
        if not self.program.addShaderFromSourceCode(QOpenGLShader.ShaderTypeBit.Fragment, FRAGMENT_SHADER):
            raise RuntimeError(self.program.log())
        if not self.program.link():
            raise RuntimeError(self.program.log())
        self.scene_dirty = True

    def _release_batches(self) -> None:
        for batch in self.batches:
            batch.buffer.destroy()
            batch.vao.destroy()
        self.batches.clear()

    def _release_textures(self) -> None:
        for texture in self.texture_objects.values():
            texture.destroy()
        self.texture_objects.clear()

    def _release_scene(self) -> None:
        self._release_batches()
        self._release_textures()

    def _create_texture(self, data: bytes | None) -> QOpenGLTexture:
        if data is None:
            image = QImage(2, 2, QImage.Format.Format_RGBA8888)
            image.fill(0xFFEE00AA)
        else:
            image = QImage.fromData(data).convertToFormat(QImage.Format.Format_RGBA8888)
            if image.isNull():
                image = QImage(2, 2, QImage.Format.Format_RGBA8888)
                image.fill(0xFFEE00AA)
        texture = QOpenGLTexture(image.mirrored())
        texture.setMinificationFilter(QOpenGLTexture.Filter.Nearest)
        texture.setMagnificationFilter(QOpenGLTexture.Filter.Nearest)
        texture.setWrapMode(QOpenGLTexture.CoordinateDirection.DirectionS, QOpenGLTexture.WrapMode.Repeat)
        texture.setWrapMode(QOpenGLTexture.CoordinateDirection.DirectionT, QOpenGLTexture.WrapMode.Repeat)
        return texture

    def _create_batch(self, texture: str, transparent: bool, items: list[Triangle]) -> _GpuBatch:
        if self.program is None:
            raise RuntimeError("OpenGL program is not initialized")
        data = _triangle_rows(items)
        vao = QOpenGLVertexArrayObject(self)
        vao.create()
        vao.bind()
        buffer = QOpenGLBuffer(QOpenGLBuffer.Type.VertexBuffer)
        buffer.create()
        buffer.bind()
        buffer.setUsagePattern(QOpenGLBuffer.UsagePattern.DynamicDraw if transparent else QOpenGLBuffer.UsagePattern.StaticDraw)
        buffer.allocate(data.tobytes(), data.nbytes)
        self.program.bind()
        stride = data.shape[1] * 4
        for location, offset, size in ((0, 0, 3), (1, 12, 2), (2, 20, 3), (3, 32, 1), (4, 36, 1)):
            self.program.enableAttributeArray(location)
            self.program.setAttributeBuffer(location, GL_FLOAT, offset, size, stride)
        self.program.release()
        buffer.release()
        vao.release()
        center = np.mean(np.concatenate([item.vertices for item in items]), axis=0)
        return _GpuBatch(texture, transparent, center, len(data), buffer, vao)

    def _upload_scene(self) -> None:
        self._release_batches()
        if self.textures_dirty:
            self._release_textures()
            for key, payload in self.texture_payloads.items():
                self.texture_objects[key] = self._create_texture(payload)
            self.textures_dirty = False
        opaque: dict[str, list[Triangle]] = {}
        transparent: list[Triangle] = []
        for triangle in self.scene_triangles:
            if triangle.alpha_mode == "transparent":
                transparent.append(triangle)
            else:
                opaque.setdefault(triangle.texture, []).append(triangle)
        for texture, items in opaque.items():
            self.batches.append(self._create_batch(texture, False, items))
        # Transparent triangles stay independent so their global draw order can
        # follow the camera instead of being accidentally grouped by texture.
        for triangle in transparent:
            self.batches.append(self._create_batch(triangle.texture, True, [triangle]))
        self.scene_dirty = False

    def _matrix(self) -> QMatrix4x4:
        aspect = max(1, self.width()) / max(1, self.height())
        projection = QMatrix4x4()
        half_height = self.scale * .5
        projection.ortho(-half_height * aspect, half_height * aspect, -half_height, half_height, .01, 1000.0)
        position = camera_position(self.azimuth, self.elevation, self.distance, self.target)
        view = QMatrix4x4()
        view.lookAt(
            QVector3D(*position),
            QVector3D(*self.target),
            QVector3D(0, 1, 0),
        )
        return projection * view

    def paintGL(self) -> None:
        gl = self.context().functions()
        gl.glClearColor(.18, .20, .22, 1.0)
        gl.glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT)
        if self.program is None:
            return
        if self.scene_dirty:
            self._upload_scene()
        self.program.bind()
        self.program.setUniformValue("mvp", self._matrix())
        self.program.setUniformValue("colorTexture", 0)
        opaque = [batch for batch in self.batches if not batch.transparent]
        transparent = [batch for batch in self.batches if batch.transparent]
        for batch in opaque:
            self._draw_batch(batch, 0)
        if transparent:
            position = camera_position(self.azimuth, self.elevation, self.distance, self.target)
            transparent.sort(key=lambda batch: float(np.linalg.norm(batch.center - position)), reverse=True)
            gl.glEnable(GL_BLEND)
            gl.glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA)
            gl.glDepthMask(False)
            for batch in transparent:
                self._draw_batch(batch, 1)
            gl.glDepthMask(True)
            gl.glDisable(GL_BLEND)
        self.program.release()

    def _draw_batch(self, batch: _GpuBatch, transparent: int) -> None:
        if self.program is None:
            return
        texture = self.texture_objects.get(batch.texture, self.texture_objects.get("missing"))
        if texture is None:
            return
        self.program.setUniformValue("transparentPass", transparent)
        texture.bind(0)
        batch.vao.bind()
        self.context().functions().glDrawArrays(GL_TRIANGLES, 0, batch.count)
        batch.vao.release()
        texture.release(0)

    def mousePressEvent(self, event: QMouseEvent) -> None:
        if event.button() in (Qt.MouseButton.LeftButton, Qt.MouseButton.MiddleButton, Qt.MouseButton.RightButton):
            self.drag_start = event.position().toPoint()
            self.drag_button = event.button()
            event.accept()
            return
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event: QMouseEvent) -> None:
        if self.drag_start is None:
            super().mouseMoveEvent(event)
            return
        point = event.position().toPoint()
        dx, dy = point.x() - self.drag_start.x(), point.y() - self.drag_start.y()
        self.drag_start = point
        if self.drag_button == Qt.MouseButton.LeftButton:
            self.azimuth -= dx * .45
            self.elevation = max(-89.0, min(89.0, self.elevation + dy * .35))
        else:
            position = camera_position(self.azimuth, self.elevation, self.distance, self.target)
            forward = np.asarray(self.target) - position
            forward /= np.linalg.norm(forward) or 1
            right = np.cross(forward, np.asarray((0.0, 1.0, 0.0)))
            right /= np.linalg.norm(right) or 1
            up = np.cross(right, forward)
            amount = self.scale / max(1, self.height())
            self.target = list(np.asarray(self.target) - right * dx * amount + up * dy * amount)
        self.cameraChanged.emit(self.azimuth, self.elevation, self.scale)
        self.update()
        event.accept()

    def mouseReleaseEvent(self, event: QMouseEvent) -> None:
        self.drag_start = None
        self.drag_button = Qt.MouseButton.NoButton
        event.accept()

    def wheelEvent(self, event: QWheelEvent) -> None:
        delta = event.angleDelta().y() or event.pixelDelta().y()
        if delta:
            self.scale = max(.05, min(500.0, self.scale * math.exp(-delta / 1200.0)))
            self.cameraChanged.emit(self.azimuth, self.elevation, self.scale)
            self.update()
            event.accept()
            return
        super().wheelEvent(event)

    def closeEvent(self, event) -> None:
        if self.context() is not None and self.context().isValid():
            self.makeCurrent()
            self._release_scene()
            self.doneCurrent()
        super().closeEvent(event)
