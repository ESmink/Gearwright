"""Approved A11 lateral crank and compact reciprocating pump runtime shapes.

The approval model uses the crank block at y=0 and the pump block below it.
Runtime shapes split those two blocks and translate each into a 0..16 block.
The combined pump mechanism retains the approved ``singleacting`` animation
as a compatibility and review contract. Runtime rendering uses additional
static layers copied from the same approved elements so Vintage Story cannot
drop the piston and connecting-rod translations while retaining rotation.
"""

from __future__ import annotations

from collections import OrderedDict
import math

from gearwright_graphics.model import Animation, ElementRef, Keyframe, ModelPackage, ReviewScene, Shape, Vec3
from graphics.review.lateral_motion_system import (
    SINGLE_CRANK_THROW,
    _crank_state,
    _singleacting_pump_shape,
    _slider_state,
)


APPROVED = "a11-one-block-full-frame"
PUMP_MOUNT = "gw-pump-mount"
PUMP_OFFSET = Vec3(8, 24, 8)
CRANK_OFFSET = Vec3(8, 8, 8)
DYNAMIC_ROOTS = {
    "gw-piston-motion",
    "gw-connecting-rod-motion",
    "gw-wet-intake-check-motion",
    "gw-wet-output-check-motion",
    "gw-breather-intake-check-motion",
    "gw-breather-exhaust-check-motion",
}
MOTION_LAYERS = OrderedDict((
    ("piston", "gw-piston-motion"),
    ("connecting-rod", "gw-connecting-rod-motion"),
    ("wet-intake-check", "gw-wet-intake-check-motion"),
    ("wet-output-check", "gw-wet-output-check-motion"),
    ("breather-intake-check", "gw-breather-intake-check-motion"),
    ("breather-exhaust-check", "gw-breather-exhaust-check-motion"),
))


def _copy_textures(source: Shape, target: Shape) -> None:
    for texture in source.textures.values():
        target.texture(texture.key, texture.location, size=texture.size)


def _descendants(source: Shape, roots: set[str]) -> set[str]:
    selected = set(roots)
    changed = True
    while changed:
        changed = False
        for element in source.elements:
            if element.parent in selected and element.name not in selected:
                selected.add(element.name)
                changed = True
    return selected


def _pump_names(source: Shape) -> set[str]:
    return _descendants(source, {PUMP_MOUNT})


def _copy_elements(
    source: Shape,
    target: Shape,
    names: set[str],
    *,
    root_offset: Vec3,
) -> None:
    _copy_textures(source, target)
    copied: set[str] = set()
    for element in source.elements:
        if element.name not in names:
            continue
        parent = ElementRef(target.id, element.parent) if element.parent in copied else None
        offset = Vec3(0, 0, 0) if parent else root_offset
        origin = element.rotation_origin + offset if element.rotation_origin else None
        target._element(
            element.name,
            (element.from_ + offset).values(),
            (element.to + offset).values(),
            faces=element.faces,
            rotation_origin=origin.values() if origin else None,
            rotation=element.rotation.values(),
            scale=element.scale.values() if element.scale else None,
            shade=element.shade,
            gradient_shade=element.gradient_shade,
            render_pass=element.render_pass,
            group=element.group,
            parent=parent,
            pivot=element.pivot,
        )
        copied.add(element.name)


def _copy_animations(source: Shape, target: Shape, names: set[str]) -> None:
    for animation in source.animations:
        keyframes = []
        for keyframe in animation.keyframes:
            elements = OrderedDict(
                (name, values)
                for name, values in keyframe.elements.items()
                if name in names
            )
            if elements:
                keyframes.append(Keyframe(keyframe.frame, elements))
        if not keyframes:
            continue
        target.add_animation(Animation(
            animation.name,
            animation.code,
            animation.quantityframes,
            tuple(keyframes),
            animation.version,
            animation.on_activity_stopped,
            animation.on_animation_end,
        ))


def _crank(shape_id: str, *, through: bool, animated: bool = True) -> Shape:
    source = _crank_state(through=through, throw=SINGLE_CRANK_THROW)
    target = Shape(shape_id, source.texture_width, source.texture_height)
    names = {element.name for element in source.elements}
    _copy_elements(source, target, names, root_offset=CRANK_OFFSET)
    if animated:
        _copy_animations(source, target, names)
    return target


def _pump_shape(
    shape_id: str,
    source: Shape,
    *,
    part: str,
    supported: bool = False,
) -> Shape:
    pump = _pump_names(source)
    dynamic = _descendants(source, DYNAMIC_ROOTS)
    if part == "body":
        names = pump - dynamic
        if not supported:
            names = {
                name for name in names
                if not (next(element for element in source.elements if element.name == name).group or "")
                    .startswith("pump-downward-stand")
            }
    elif part == "mechanism":
        names = dynamic | {PUMP_MOUNT}
    elif part == "inventory":
        names = pump
    else:
        raise ValueError(f"unknown pump runtime part: {part}")

    target = Shape(shape_id, source.texture_width, source.texture_height)
    _copy_elements(source, target, names, root_offset=PUMP_OFFSET)
    if part == "mechanism":
        _copy_animations(source, target, names)
    return target


def _motion_layer(shape_id: str, source: Shape, root: str) -> Shape:
    target = Shape(shape_id, source.texture_width, source.texture_height)
    _copy_elements(
        source,
        target,
        _descendants(source, {root}),
        root_offset=PUMP_OFFSET,
    )
    return target


def _direct_pose_scene(phase: int) -> ReviewScene:
    """Diagnostic assembly of the actual static runtime layers, not the old animator."""
    _, _, crosshead, middle_y, middle_z, rod_angle = _slider_state(
        phase, radius=3, rod_length=6,
    )
    def placed(name, angle=0, pivot=(0, 0, 0), offset=(0, 0, 0)):
        radians = math.radians(angle)
        _, y, z = pivot
        return {
            "name": name,
            "asset": f"assets/gearwright/shapes/block/{name}.json",
            "rotation": (angle, 0, 0),
            "placement": (
                offset[0],
                offset[1] + y - y * math.cos(radians) + z * math.sin(radians),
                offset[2] + z - y * math.sin(radians) - z * math.cos(radians),
            ),
        }
    travel = abs(math.sin(math.radians(phase)))
    pressure = travel if 0 < phase < 180 else 0
    suction = travel if 180 < phase < 360 else 0
    objects = [
        placed("reciprocating-pump-body-supported"),
        placed("lateral-crank-one-sided", phase, (.5, .5, .5), (0, 1, 0)),
        placed("reciprocating-pump-piston", offset=(0, (crosshead + 3) / 16, 0)),
        placed("reciprocating-pump-connecting-rod", rod_angle, (.5, 1.5, .5),
               (0, middle_y / 16, middle_z / 16)),
    ]
    for suffix, offset_y in (
        ("wet-intake-check", .36 * suction),
        ("wet-output-check", -.36 * pressure),
        ("breather-intake-check", -.22 * pressure),
        ("breather-exhaust-check", .22 * suction),
    ):
        objects.append(placed(f"reciprocating-pump-{suffix}", offset=(0, offset_y / 16, 0)))
    return ReviewScene(f"direct-pose-{phase}", tuple(objects), ("front-right", "right"))


def build() -> ModelPackage:
    approved = _singleacting_pump_shape(APPROVED, label="pump-bottom")
    package = ModelPackage("reciprocating_pump")
    package.shape(
        Shape("gearwright-empty"),
        "assets/gearwright/shapes/block/gearwright-empty.json",
    )
    package.shape(
        _crank("lateral-crank-one-sided", through=False),
        "assets/gearwright/shapes/block/lateral-crank-one-sided.json",
    )
    package.shape(
        _crank("lateral-crank-through", through=True),
        "assets/gearwright/shapes/block/lateral-crank-through.json",
    )
    package.shape(
        _crank("lateral-crank-inventory", through=False, animated=False),
        "assets/gearwright/shapes/block/lateral-crank-inventory.json",
    )
    package.shape(
        _pump_shape("reciprocating-pump-body", approved, part="body"),
        "assets/gearwright/shapes/block/reciprocating-pump-body.json",
    )
    package.shape(
        _pump_shape("reciprocating-pump-body-supported", approved, part="body", supported=True),
        "assets/gearwright/shapes/block/reciprocating-pump-body-supported.json",
    )
    package.shape(
        _pump_shape("reciprocating-pump-mechanism", approved, part="mechanism"),
        "assets/gearwright/shapes/block/reciprocating-pump-mechanism.json",
    )
    for suffix, root in MOTION_LAYERS.items():
        shape_id = f"reciprocating-pump-{suffix}"
        package.shape(
            _motion_layer(shape_id, approved, root),
            f"assets/gearwright/shapes/block/{shape_id}.json",
        )
    package.shape(
        _pump_shape("reciprocating-pump-inventory", approved, part="inventory", supported=True),
        "assets/gearwright/shapes/block/reciprocating-pump-inventory.json",
    )
    for phase in (0, 90, 180, 270):
        package.add_scene(_direct_pose_scene(phase))
    return package
