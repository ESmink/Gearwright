"""Validation and deterministic Vintage Story shape JSON compilation."""

from __future__ import annotations

import importlib.util
import json
import math
import os
import tempfile
from collections import OrderedDict
from pathlib import Path
from typing import Any, Iterable

from .model import Animation, Element, ModelError, ModelPackage, Shape, FACES


class CompileError(ModelError):
    pass


def _clean(value: Any) -> Any:
    if isinstance(value, float):
        if not math.isfinite(value):
            raise CompileError("model contains NaN or infinity")
        if abs(value) < 0.0000000001:
            return 0
        return round(value, 10)
    if isinstance(value, (list, tuple)):
        return [_clean(item) for item in value]
    if isinstance(value, dict):
        return OrderedDict((str(key), _clean(item)) for key, item in value.items())
    return value


def _logical(value: str) -> str:
    value = str(value).replace("\\", "/")
    if value.startswith("#"):
        return value
    if ":" in value:
        domain, relative = value.split(":", 1)
        return f"{domain.lower()}:{relative.lstrip('/')}"
    return value


def _element_json(element: Element, child_map: dict[str, list[Element]]) -> OrderedDict[str, Any]:
    output: OrderedDict[str, Any] = OrderedDict()
    output["name"] = element.name
    output["from"] = list(element.from_value.values())
    output["to"] = list(element.to.values())
    if element.rotation_origin is not None:
        output["rotationOrigin"] = list(element.rotation_origin.values())
    for key, value in zip(("rotationX", "rotationY", "rotationZ"), element.rotation.values()):
        if value != 0:
            output[key] = value
    if element.scale is not None:
        for key, value in zip(("scaleX", "scaleY", "scaleZ"), element.scale.values()):
            if value != 1:
                output[key] = value
    for key, value in (("shade", element.shade), ("gradientShade", element.gradient_shade), ("renderPass", element.render_pass), ("group", element.group)):
        if value is not None:
            output[key] = value
    if element.faces:
        faces: OrderedDict[str, Any] = OrderedDict()
        for name in FACES:
            if name not in element.faces:
                continue
            face = element.faces[name]
            data: OrderedDict[str, Any] = OrderedDict((("texture", _logical(face.texture)),))
            if not face.enabled:
                data["enabled"] = False
            if face.uv is not None:
                data["uv"] = [face.uv.u0, face.uv.v0, face.uv.u1, face.uv.v1]
            if face.auto_uv is not None:
                data["autoUv"] = face.auto_uv
            if face.rotation:
                data["rotation"] = face.rotation
            if face.glow:
                data["glow"] = face.glow
            if face.reflective_mode is not None:
                data["reflectiveMode"] = face.reflective_mode
            faces[name] = data
        if faces:
            output["faces"] = faces
    if child_map.get(element.name):
        output["children"] = [_element_json(child, child_map) for child in child_map[element.name]]
    return output


def validate_shape(shape: Shape) -> None:
    names = [element.name for element in shape.elements]
    if len(names) != len(set(names)):
        raise CompileError(f"shape '{shape.id}' contains duplicate element names")
    for key, texture in shape.textures.items():
        if not key or not texture.location:
            raise CompileError(f"shape '{shape.id}' has an empty texture alias")
        seen = {key}
        location = texture.location
        while location.startswith("#"):
            alias = location[1:]
            if alias in seen:
                raise CompileError(f"texture alias cycle involving #{key}")
            if alias not in shape.textures:
                raise CompileError(f"texture alias #{key} points to missing #{alias}")
            seen.add(alias)
            location = shape.textures[alias].location
    for element in shape.elements:
        for face_name, face in element.faces.items():
            if face_name not in FACES:
                raise CompileError(f"shape '{shape.id}' has invalid face '{face_name}'")
            if face.rotation not in (0, 90, 180, 270):
                raise CompileError(f"face rotation must be a quarter turn: {element.name}/{face_name}")
            if face.uv is not None and any(not math.isfinite(value) for value in (face.uv.u0, face.uv.v0, face.uv.u1, face.uv.v1)):
                raise CompileError(f"face UV is not finite: {element.name}/{face_name}")
        if not element.pivot and any(right <= left for left, right in zip(element.from_value.values(), element.to.values())):
            raise CompileError(f"element '{element.name}' must have positive dimensions")
        if element.parent is not None and element.parent not in names:
            raise CompileError(f"element '{element.name}' has missing parent '{element.parent}'")
    for animation in shape.animations:
        _validate_animation(shape, animation)


def _validate_animation(shape: Shape, animation: Animation) -> None:
    if not animation.name or not animation.code or animation.quantityframes < 2 or not animation.keyframes:
        raise CompileError(f"animation '{animation.code}' is incomplete")
    frames = [keyframe.frame for keyframe in animation.keyframes]
    if frames != sorted(frames) or len(frames) != len(set(frames)):
        raise CompileError(f"animation '{animation.code}' keyframes must be ordered and unique")
    if any(frame < 0 or frame >= animation.quantityframes for frame in frames):
        raise CompileError(f"animation '{animation.code}' contains a frame outside its declared range")
    if animation.on_activity_stopped not in (None, "PlayTillEnd", "Rewind", "Stop", "EaseOut"):
        raise CompileError(
            f"animation '{animation.code}' has invalid onActivityStopped value "
            f"'{animation.on_activity_stopped}'"
        )
    if animation.on_animation_end not in (None, "Repeat", "Hold", "Stop", "EaseOut"):
        raise CompileError(
            f"animation '{animation.code}' has invalid onAnimationEnd value "
            f"'{animation.on_animation_end}'"
        )
    names = {element.name for element in shape.elements}
    for keyframe in animation.keyframes:
        for target, values in keyframe.elements.items():
            if target not in names:
                raise CompileError(f"animation '{animation.code}' targets missing element '{target}'")
            for key, value in values.items():
                if key not in ("offsetX", "offsetY", "offsetZ", "rotationX", "rotationY", "rotationZ", "rotShortestDistanceX", "rotShortestDistanceY", "rotShortestDistanceZ"):
                    raise CompileError(f"unsupported animation property '{key}'")
                if isinstance(value, float) and not math.isfinite(value):
                    raise CompileError(f"animation '{animation.code}' contains a non-finite value")


def _engine_animation_values(values: OrderedDict[str, float | bool]) -> OrderedDict[str, float | bool]:
    """Complete transform vectors required by Vintage Story's animator.

    The engine marks a whole translation or rotation group as present when any
    component exists, then reads all three nullable components during frame
    generation. Neutral values keep concise authoring safe at runtime.
    """
    output = OrderedDict(values)
    for keys, neutral in (
        (("offsetX", "offsetY", "offsetZ"), 0),
        (("rotationX", "rotationY", "rotationZ"), 0),
    ):
        if any(key in output for key in keys):
            for key in keys:
                output.setdefault(key, neutral)
    return output


def compile_shape(shape: Shape) -> OrderedDict[str, Any]:
    validate_shape(shape)
    children: dict[str, list[Element]] = {}
    roots: list[Element] = []
    for element in shape.elements:
        if element.parent:
            children.setdefault(element.parent, []).append(element)
        else:
            roots.append(element)
    result: OrderedDict[str, Any] = OrderedDict((
        ("textureWidth", shape.texture_width),
        ("textureHeight", shape.texture_height),
        ("textures", OrderedDict((key, _logical(texture.location)) for key, texture in shape.textures.items())),
    ))
    texture_sizes = OrderedDict((key, list(texture.size)) for key, texture in shape.textures.items() if texture.size is not None)
    if texture_sizes:
        result["textureSizes"] = texture_sizes
    result["elements"] = [_element_json(element, children) for element in roots]
    if shape.animations:
        animations: list[OrderedDict[str, Any]] = []
        for animation in shape.animations:
            item: OrderedDict[str, Any] = OrderedDict((
                ("name", animation.name), ("code", animation.code), ("quantityframes", animation.quantityframes),
            ))
            if animation.version is not None:
                item["version"] = animation.version
            if animation.on_activity_stopped is not None:
                item["onActivityStopped"] = animation.on_activity_stopped
            if animation.on_animation_end is not None:
                item["onAnimationEnd"] = animation.on_animation_end
            item["keyframes"] = [OrderedDict((
                ("frame", keyframe.frame),
                ("elements", OrderedDict(
                    (target, _engine_animation_values(values))
                    for target, values in keyframe.elements.items()
                )),
            )) for keyframe in animation.keyframes]
            animations.append(item)
        result["animations"] = animations
    return _clean(result)


def json_bytes(document: Any) -> bytes:
    return (json.dumps(_clean(document), ensure_ascii=False, indent=2, separators=(",", ": ")) + "\n").encode("utf-8")


def safe_output(root: Path, relative: str) -> Path:
    value = Path(relative.replace("\\", "/"))
    if value.is_absolute() or ".." in value.parts:
        raise CompileError(f"output must be project-relative: {relative}")
    normalized = value.as_posix()
    if not (normalized.startswith("assets/gearwright/") or normalized.startswith("generated/")):
        raise CompileError(f"output is outside the allowed graphics directories: {relative}")
    return (root / value).resolve()


def build_package(package: ModelPackage, root: Path, *, write: bool = False) -> dict[str, bytes]:
    if not package.package_id or "/" in package.package_id or "\\" in package.package_id:
        raise CompileError("package identifiers must be stable simple names")
    seen: set[str] = set()
    output: dict[str, bytes] = {}
    for relative, shape in package.outputs.items():
        destination = safe_output(root, relative)
        key = destination.as_posix().lower()
        if key in seen:
            raise CompileError(f"duplicate output ownership: {relative}")
        seen.add(key)
        output[relative.replace("\\", "/")] = json_bytes(compile_shape(shape))
    if write:
        for relative, data in output.items():
            destination = safe_output(root, relative)
            destination.parent.mkdir(parents=True, exist_ok=True)
            handle, temp_name = tempfile.mkstemp(prefix=f".{destination.name}.", suffix=".tmp", dir=destination.parent)
            try:
                with os.fdopen(handle, "wb") as stream:
                    stream.write(data)
                    stream.flush()
                    os.fsync(stream.fileno())
                os.replace(temp_name, destination)
            finally:
                if os.path.exists(temp_name):
                    os.unlink(temp_name)
        write_review_manifest(package, root)
    return output


def write_review_manifest(package: ModelPackage, root: Path) -> None:
    if not package.scenes:
        return
    destination = root / "generated/model-review" / f"{package.package_id}.json"
    data = OrderedDict((("version", 1), ("package", package.package_id), ("scenes", OrderedDict())))
    for name, scene in package.scenes.items():
        data["scenes"][name] = OrderedDict((
            ("objects", list(scene.objects)),
            ("defaultViews", list(scene.default_views)),
            ("assemblies", [OrderedDict((("name", group.name), ("members", list(group.members)), ("vector", list(group.vector.values())), ("magnitude", group.magnitude))) for group in scene.assemblies]),
        ))
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(json_bytes(data))


def load_definition(root: Path, path: Path) -> ModelPackage:
    path = path.resolve()
    models_root = (root / "graphics/models").resolve()
    if path.parent != models_root and models_root not in path.parents:
        raise CompileError(f"definition must live below graphics/models: {path}")
    spec = importlib.util.spec_from_file_location(f"gearwright_model_{path.stem}", path)
    if spec is None or spec.loader is None:
        raise CompileError(f"could not load model definition {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    factory = getattr(module, "build", None)
    if not callable(factory):
        raise CompileError(f"model definition {path.name} must expose build()")
    package = factory()
    if not isinstance(package, ModelPackage):
        raise CompileError(f"build() in {path.name} did not return ModelPackage")
    return package


def definition_paths(root: Path) -> list[Path]:
    return sorted((root / "graphics/models").glob("*.py"), key=lambda value: value.name)
