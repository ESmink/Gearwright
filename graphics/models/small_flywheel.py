"""Approved E4 small flywheel promoted into runtime render shapes.

The review model is authored around a 48 x 48 x 16 decision canvas. Runtime
mechanical rendering rotates around the controller block center, so the E4
shaft is translated to (8, 8, 8). The frame and wheel are emitted separately:
the frame joins the terrain mesh while Vintage Story's mechanical renderer
rotates the wheel from the vanilla network angle.
"""

from __future__ import annotations

from gearwright_graphics.model import ElementRef, ModelPackage, Shape, Vec3
from graphics.review.small_flywheel import _iron_eight_way_frame


RUNTIME_OFFSET = Vec3(-16, -19, 0)


def _copy_textures(source: Shape, target: Shape) -> None:
    for texture in source.textures.values():
        # Review asset roots distinguish the base-game packages. Runtime asset
        # locations use Vintage Story's public `game` domain for both roots.
        location = texture.location.replace("survival:", "game:")
        target.texture(texture.key, location, size=texture.size)


def _copy_elements(source: Shape, target: Shape, *, wheel: bool | None) -> None:
    """Copy E4 elements while retaining its hierarchy and approved geometry."""
    _copy_textures(source, target)
    included: set[str] = set()
    for element in source.elements:
        is_wheel = element.name == "wheel" or element.parent == "wheel"
        if wheel is not None and is_wheel != wheel:
            continue
        parent = ElementRef(target.id, element.parent) if element.parent in included else None
        # Roots live in controller model space. Children are already local to
        # their parent and must not receive the controller translation again.
        offset = Vec3(0, 0, 0) if element.parent else RUNTIME_OFFSET
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
        included.add(element.name)


def _shape(shape_id: str, source: Shape, wheel: bool | None) -> Shape:
    shape = Shape(shape_id, source.texture_width, source.texture_height)
    _copy_elements(source, shape, wheel=wheel)
    return shape


def build() -> ModelPackage:
    approved = _iron_eight_way_frame()
    package = ModelPackage("small_flywheel")
    package.shape(
        _shape("small-flywheel-frame", approved, False),
        "assets/gearwright/shapes/block/small-flywheel-frame.json",
    )
    package.shape(
        _shape("small-flywheel-wheel", approved, True),
        "assets/gearwright/shapes/block/small-flywheel-wheel.json",
    )
    package.shape(
        _shape("small-flywheel-inventory", approved, None),
        "assets/gearwright/shapes/block/small-flywheel-inventory.json",
    )
    return package
