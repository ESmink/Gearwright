"""Small, immutable-at-output authoring API for Vintage Story cuboid shapes.

Definition files use model units: 16 units equal one block.  Renderer code is
kept out of this module so the same definitions feed the compiler and the
diagnostic photoshoot.
"""

from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass, field
from math import isfinite
from typing import Any, Iterable, Mapping, MutableMapping, Sequence

FACES = ("north", "east", "south", "west", "up", "down")
TRANSFORM_FIELDS = (
    "offsetX", "offsetY", "offsetZ", "rotationX", "rotationY", "rotationZ",
    "rotShortestDistanceX", "rotShortestDistanceY", "rotShortestDistanceZ",
)


class ModelError(ValueError):
    """A model definition or compiler contract error."""


def _number(value: float | int, label: str = "number") -> float:
    value = float(value)
    if not isfinite(value):
        raise ModelError(f"{label} must be finite")
    return value


@dataclass(frozen=True)
class Vec3:
    x: float
    y: float
    z: float

    def __post_init__(self) -> None:
        object.__setattr__(self, "x", _number(self.x, "Vec3.x"))
        object.__setattr__(self, "y", _number(self.y, "Vec3.y"))
        object.__setattr__(self, "z", _number(self.z, "Vec3.z"))

    def __add__(self, other: "Vec3") -> "Vec3":
        return Vec3(self.x + other.x, self.y + other.y, self.z + other.z)

    def __sub__(self, other: "Vec3") -> "Vec3":
        return Vec3(self.x - other.x, self.y - other.y, self.z - other.z)

    def __mul__(self, value: float) -> "Vec3":
        return Vec3(self.x * value, self.y * value, self.z * value)

    __rmul__ = __mul__

    def values(self) -> tuple[float, float, float]:
        return self.x, self.y, self.z

    @classmethod
    def of(cls, value: Sequence[float | int]) -> "Vec3":
        if len(value) != 3:
            raise ModelError("a vector needs exactly three values")
        return cls(float(value[0]), float(value[1]), float(value[2]))


@dataclass(frozen=True)
class UVRect:
    u0: float
    v0: float
    u1: float
    v1: float

    def __post_init__(self) -> None:
        for name in ("u0", "v0", "u1", "v1"):
            object.__setattr__(self, name, _number(getattr(self, name), f"UVRect.{name}"))

    @classmethod
    def of(cls, value: Sequence[float | int]) -> "UVRect":
        if len(value) != 4:
            raise ModelError("a UV rectangle needs exactly four values")
        return cls(*(float(part) for part in value))


@dataclass(frozen=True)
class Face:
    texture: str
    enabled: bool = True
    uv: UVRect | None = None
    rotation: int = 0
    glow: int = 0
    reflective_mode: int | None = None
    auto_uv: bool | None = None


@dataclass(frozen=True)
class Texture:
    key: str
    location: str
    size: tuple[int, int] | None = None


def model_creator_box_faces(texture: str, tile: float) -> OrderedDict[str, Face]:
    """Return the six-face atlas layout emitted by the VS model creator.

    Blender's Vintage Story exporter used this fixed cross layout for the
    migrated passive-pump shapes. Keeping it explicit preserves their UVs
    while allowing newer definition-authored cuboids to use ordinary auto UVs.
    """
    rectangles = {
        "north": (1.5 * tile, 3 * tile, 2.5 * tile, 4 * tile),
        "east": (1.5 * tile, 2 * tile, 2.5 * tile, 3 * tile),
        "south": (1.5 * tile, tile, 2.5 * tile, 2 * tile),
        "west": (1.5 * tile, 0, 2.5 * tile, tile),
        "up": (2.5 * tile, tile, 3.5 * tile, 2 * tile),
        "down": (1.5 * tile, 2 * tile, .5 * tile, tile),
    }
    return OrderedDict(
        (name, Face(texture, uv=UVRect.of(rectangles[name]), rotation=270, auto_uv=False))
        for name in FACES
    )


@dataclass(frozen=True)
class Element:
    name: str
    from_: Vec3
    to: Vec3
    faces: OrderedDict[str, Face] = field(default_factory=OrderedDict)
    children: tuple["Element", ...] = ()
    parent: str | None = None
    rotation_origin: Vec3 | None = None
    rotation: Vec3 = Vec3(0, 0, 0)
    scale: Vec3 | None = None
    shade: bool | None = None
    gradient_shade: bool | None = None
    render_pass: int | None = None
    group: str | None = None
    pivot: bool = False

    @property
    def from_value(self) -> Vec3:
        return self.from_


@dataclass(frozen=True)
class ElementRef:
    """Stable definition-local reference used by animations and review groups."""

    shape_id: str
    name: str


@dataclass(frozen=True)
class Keyframe:
    frame: float
    elements: OrderedDict[str, OrderedDict[str, float | bool]]


@dataclass(frozen=True)
class Animation:
    name: str
    code: str
    quantityframes: int
    keyframes: tuple[Keyframe, ...]
    version: int | None = None
    on_activity_stopped: str | None = None
    on_animation_end: str | None = None


@dataclass(frozen=True)
class Transform:
    translation: Vec3 = Vec3(0, 0, 0)
    rotation: Vec3 = Vec3(0, 0, 0)
    scale: Vec3 = Vec3(1, 1, 1)


@dataclass(frozen=True)
class ReviewAssembly:
    name: str
    members: tuple[str, ...]
    vector: Vec3 = Vec3(0, 0, 0)
    magnitude: float = 1.0


@dataclass(frozen=True)
class ReviewScene:
    name: str
    objects: tuple[dict[str, Any], ...]
    default_views: tuple[str, ...] = ("front-right",)
    assemblies: tuple[ReviewAssembly, ...] = ()


class Shape:
    """Mutable definition builder that freezes into deterministic data."""

    def __init__(self, shape_id: str, texture_width: int = 16, texture_height: int = 16) -> None:
        self.id = shape_id
        self.texture_width = int(texture_width)
        self.texture_height = int(texture_height)
        self.textures: OrderedDict[str, Texture] = OrderedDict()
        self.elements: list[Element] = []
        self.animations: list[Animation] = []
        self._refs: dict[str, ElementRef] = {}

    def texture(self, key: str, location: str, *, size: tuple[int, int] | None = None) -> str:
        key = key.lstrip("#")
        self.textures[key] = Texture(key, location, size)
        return f"#{key}"

    def _element(
        self,
        name: str,
        from_: Sequence[float | int],
        to: Sequence[float | int],
        *,
        faces: Iterable[str] | Mapping[str, Face] | None = None,
        texture: str = "#all",
        uv: Sequence[float | int] | None = None,
        face_uvs: Mapping[str, Sequence[float | int]] | None = None,
        face_rotations: Mapping[str, int] | None = None,
        glow: int = 0,
        face_glow: Mapping[str, int] | None = None,
        rotation_origin: Sequence[float | int] | None = None,
        rotation: Sequence[float | int] = (0, 0, 0),
        scale: Sequence[float | int] | None = None,
        shade: bool | None = None,
        gradient_shade: bool | None = None,
        render_pass: int | None = None,
        group: str | None = None,
        parent: ElementRef | None = None,
        pivot: bool = False,
    ) -> ElementRef:
        if not name or name in self._refs:
            raise ModelError(f"duplicate or empty element name '{name}' in shape '{self.id}'")
        start, end = Vec3.of(from_), Vec3.of(to)
        if not pivot and any(right <= left for left, right in zip(start.values(), end.values())):
            raise ModelError(f"element '{name}' must have positive dimensions")
        face_map: OrderedDict[str, Face] = OrderedDict()
        if pivot:
            face_map = OrderedDict()
        elif isinstance(faces, Mapping):
            face_map.update(faces)
        else:
            selected = FACES if faces is None else tuple(faces)
            for face_name in selected:
                if face_name not in FACES:
                    raise ModelError(f"unknown face '{face_name}'")
                face_map[face_name] = Face(
                    texture=texture,
                    uv=UVRect.of(face_uvs[face_name]) if face_uvs and face_name in face_uvs else (UVRect.of(uv) if uv else None),
                    rotation=(face_rotations or {}).get(face_name, 0),
                    glow=(face_glow or {}).get(face_name, glow),
                )
        element = Element(
            name=name,
            from_=start,
            to=end,
            faces=face_map,
            rotation_origin=Vec3.of(rotation_origin) if rotation_origin is not None else None,
            rotation=Vec3.of(rotation),
            scale=Vec3.of(scale) if scale is not None else None,
            shade=shade,
            gradient_shade=gradient_shade,
            render_pass=render_pass,
            group=group,
            parent=parent.name if parent else None,
            pivot=pivot,
        )
        self.elements.append(element)
        ref = ElementRef(self.id, name)
        self._refs[name] = ref
        return ref

    def box(self, name: str, from_: Sequence[float | int], to: Sequence[float | int], **kwargs: Any) -> ElementRef:
        return self._element(name, from_, to, **kwargs)

    def pivot(self, name: str, origin: Sequence[float | int], *, parent: ElementRef | None = None, group: str | None = None) -> ElementRef:
        return self._element(name, origin, origin, parent=parent, group=group, pivot=True, rotation_origin=origin)

    def ref(self, name: str) -> ElementRef:
        try:
            return self._refs[name]
        except KeyError as exc:
            raise ModelError(f"unknown element reference '{name}' in shape '{self.id}'") from exc

    def add_animation(self, animation: Animation) -> None:
        if any(existing.code == animation.code for existing in self.animations):
            raise ModelError(f"duplicate animation code '{animation.code}'")
        self.animations.append(animation)

    def freeze(self) -> tuple[Element, ...]:
        """Return elements in authoring order; compiler performs hierarchy validation."""
        return tuple(self.elements)


class AnimationBuilder:
    def __init__(self, shape: Shape, name: str, code: str, quantityframes: int, **kwargs: Any) -> None:
        self.shape, self.name, self.code, self.quantityframes = shape, name, code, int(quantityframes)
        self.kwargs = kwargs
        self.frames: OrderedDict[float, OrderedDict[str, OrderedDict[str, float | bool]]] = OrderedDict()

    def keyframe(self, frame: float, target: ElementRef, **values: float | bool) -> "AnimationBuilder":
        if target.shape_id != self.shape.id:
            raise ModelError("animation target belongs to another shape")
        if target.name not in self.shape._refs:
            raise ModelError(f"animation target '{target.name}' does not exist")
        if frame not in self.frames:
            self.frames[frame] = OrderedDict()
        self.frames[frame][target.name] = OrderedDict((key, value) for key, value in values.items())
        return self

    def build(self) -> Animation:
        keyframes = tuple(Keyframe(frame, values) for frame, values in self.frames.items())
        return Animation(self.name, self.code, self.quantityframes, keyframes, **self.kwargs)


@dataclass
class ModelPackage:
    package_id: str
    outputs: OrderedDict[str, Shape] = field(default_factory=OrderedDict)
    scenes: OrderedDict[str, ReviewScene] = field(default_factory=OrderedDict)

    def shape(self, shape: Shape, output: str) -> Shape:
        if output in self.outputs:
            raise ModelError(f"duplicate output '{output}'")
        self.outputs[output] = shape
        return shape

    def add_scene(self, scene: ReviewScene) -> None:
        if scene.name in self.scenes:
            raise ModelError(f"duplicate review scene '{scene.name}'")
        self.scenes[scene.name] = scene


def animate(shape: Shape, name: str, code: str, quantityframes: int, **kwargs: Any) -> AnimationBuilder:
    return AnimationBuilder(shape, name, code, quantityframes, **kwargs)


def copy_component(source: Shape, target: Shape, prefix: str, transform: Transform = Transform(), group: str | None = None) -> list[ElementRef]:
    """Copy top-level cuboids with a simple translation/scale transform."""
    refs: list[ElementRef] = []
    for element in source.elements:
        start = element.from_value * 1.0 + transform.translation
        end = element.to * 1.0 + transform.translation
        if transform.scale != Vec3(1, 1, 1):
            end = start + Vec3((element.to.x - element.from_.x) * transform.scale.x, (element.to.y - element.from_.y) * transform.scale.y, (element.to.z - element.from_.z) * transform.scale.z)
        refs.append(target._element(prefix + element.name, start.values(), end.values(), faces=element.faces, rotation_origin=element.rotation_origin.values() if element.rotation_origin else None, rotation=element.rotation.values(), group=group or element.group, pivot=element.pivot))
    return refs


def linear_array(shape: Shape, prefix: str, count: int, start: Sequence[float | int], size: Sequence[float | int], step: Sequence[float | int], **kwargs: Any) -> list[ElementRef]:
    refs = []
    base, extent, delta = Vec3.of(start), Vec3.of(size), Vec3.of(step)
    for index in range(count):
        offset = delta * index
        refs.append(shape.box(f"{prefix}{index}", (base + offset).values(), (base + offset + extent).values(), **kwargs))
    return refs


def named_frame(shape: Shape, prefix: str, start: Vec3, end: Vec3, thickness: float, **kwargs: Any) -> list[ElementRef]:
    """Add four rails around the X/Y rectangle at a fixed Z interval."""
    return [
        shape.box(f"{prefix}-left", (start.x, start.y, start.z), (start.x + thickness, end.y, end.z), **kwargs),
        shape.box(f"{prefix}-right", (end.x - thickness, start.y, start.z), (end.x, end.y, end.z), **kwargs),
        shape.box(f"{prefix}-bottom", (start.x + thickness, start.y, start.z), (end.x - thickness, start.y + thickness, end.z), **kwargs),
        shape.box(f"{prefix}-top", (start.x + thickness, end.y - thickness, start.z), (end.x - thickness, end.y, end.z), **kwargs),
    ]


def collar(shape: Shape, prefix: str, start: Vec3, end: Vec3, thickness: float, **kwargs: Any) -> list[ElementRef]:
    return named_frame(shape, prefix, start, end, thickness, **kwargs)


def mirror(shape: Shape, refs: Iterable[ElementRef], axis: str, plane: float, prefix: str = "mirror-") -> list[ElementRef]:
    """Construct mirrored cuboids around a model-unit plane."""
    if axis not in ("x", "y", "z"):
        raise ModelError("mirror axis must be x, y, or z")
    index = ("x", "y", "z").index(axis)
    result: list[ElementRef] = []
    for ref in refs:
        source = next(element for element in shape.elements if element.name == ref.name)
        start, end = list(source.from_value.values()), list(source.to.values())
        start[index], end[index] = 2 * plane - end[index], 2 * plane - start[index]
        result.append(shape.box(prefix + source.name, start, end, faces=source.faces, rotation_origin=source.rotation_origin.values() if source.rotation_origin else None, rotation=source.rotation.values(), group=source.group, pivot=source.pivot))
    return result


def repeat(shape: Shape, refs: Iterable[ElementRef], count: int, axis: str, spacing: float, prefix: str = "repeat-") -> list[ElementRef]:
    """Construct a linear repeat around an axis; rotations remain cuboid-safe."""
    if axis not in ("x", "y", "z") or count < 1:
        raise ModelError("repeat needs a valid axis and positive count")
    index = ("x", "y", "z").index(axis)
    result: list[ElementRef] = []
    for repeat_index in range(count):
        offset = repeat_index * spacing
        for ref in refs:
            source = next(element for element in shape.elements if element.name == ref.name)
            start, end = list(source.from_value.values()), list(source.to.values())
            start[index] += offset; end[index] += offset
            result.append(shape.box(f"{prefix}{repeat_index}-{source.name}", start, end, faces=source.faces, rotation_origin=source.rotation_origin.values() if source.rotation_origin else None, rotation=source.rotation.values(), group=source.group, pivot=source.pivot))
    return result
