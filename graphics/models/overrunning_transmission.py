"""Approved A2 overrunning-transmission runtime shapes."""

from __future__ import annotations

import math
from collections import OrderedDict

from gearwright_graphics.model import ElementRef, Face, ModelPackage, Shape


OAK_PLANKS = "game:block/wood/planks/oak1"
PLAIN_OAK = "survival:block/wood/plainoak"
STRIPPED_OAK = "survival:block/wood/debarked/oak"
OAK_END = "survival:block/wood/treetrunk/debarked/oak"
TIN_BRONZE = "game:block/metal/sheet/tinbronze1"

PAWL_ANGLES = (0.0, 120.0, 240.0)


def _shape(shape_id: str) -> Shape:
    shape = Shape(shape_id, 16, 16)
    shape.texture("oakplanks", OAK_PLANKS)
    shape.texture("plainoak", PLAIN_OAK)
    shape.texture("strippedoak", STRIPPED_OAK)
    shape.texture("oakend", OAK_END)
    shape.texture("bronze", TIN_BRONZE)
    return shape


def _shaft_faces() -> OrderedDict[str, Face]:
    return OrderedDict(
        (
            face,
            Face("#oakend" if face in {"east", "west"} else "#strippedoak"),
        )
        for face in ("north", "east", "south", "west", "up", "down")
    )


def _radial_box(
    shape: Shape,
    parent: ElementRef,
    name: str,
    x_range: tuple[float, float],
    radial_range: tuple[float, float],
    width: float,
    angle: float,
    *,
    texture: str,
    group: str,
) -> ElementRef:
    return shape.box(
        name,
        (x_range[0], radial_range[0], -width / 2),
        (x_range[1], radial_range[1], width / 2),
        texture=texture,
        rotation_origin=(0, 0, 0),
        rotation=(angle, 0, 0),
        parent=parent,
        group=group,
    )


def _ring(
    shape: Shape,
    parent: ElementRef,
    prefix: str,
    *,
    x_range: tuple[float, float],
    radius: float,
    thickness: float,
    segment_length: float,
    count: int,
    texture: str,
    group: str,
    angle_offset: float = 0,
) -> None:
    for index in range(count):
        angle = angle_offset + index * 360 / count
        shape.box(
            f"{prefix}-{index:02d}",
            (x_range[0], radius - thickness / 2, -segment_length / 2),
            (x_range[1], radius + thickness / 2, segment_length / 2),
            texture=texture,
            rotation_origin=(0, 0, 0),
            rotation=(angle, 0, 0),
            parent=parent,
            group=group,
        )


def _spokes(
    shape: Shape,
    parent: ElementRef,
    prefix: str,
    *,
    x_range: tuple[float, float],
    inner: float,
    outer: float,
    width: float,
    count: int,
    texture: str = "#strippedoak",
    group: str,
    angle_offset: float = 0,
) -> None:
    for index in range(count):
        _radial_box(
            shape,
            parent,
            f"{prefix}-{index:02d}",
            x_range,
            (inner, outer),
            width,
            angle_offset + index * 360 / count,
            texture=texture,
            group=group,
        )


def _teeth(shape: Shape, parent: ElementRef) -> None:
    for index in range(12):
        _radial_box(
            shape,
            parent,
            f"input-replaceable-ratchet-tooth-{index:02d}",
            (-2.6, -.5),
            (4.85, 5.6),
            .92,
            index * 30,
            texture="#bronze",
            group="ratchet-bronze",
        )


def _add_crossed_axle(
    shape: Shape,
    parent: ElementRef,
    side: str,
    x_range: tuple[float, float],
) -> None:
    shape.box(
        f"{side}-shaft-wide",
        (x_range[0], -1, -2),
        (x_range[1] + (.01 if side == "input" else 0), 1, 2),
        faces=_shaft_faces(),
        parent=parent,
        group=side,
    )
    shape.box(
        f"{side}-shaft-tall",
        (x_range[0] + (.01 if side == "input" else 0), -2, -1),
        (x_range[1], 2, 1),
        faces=_shaft_faces(),
        parent=parent,
        group=side,
    )


def _add_frame(shape: Shape) -> None:
    left_end, right_start = 3.8, 12.2
    for side, x_range in (
        ("input", (1.2, left_end)),
        ("output", (right_start, 14.8)),
    ):
        shape.box(
            f"{side}-separate-foot",
            (x_range[0] - .2, 0, 3.62),
            (x_range[1] + .2, 1.72, 12.38),
            texture="#oakplanks",
            group="frame",
        )
        shape.box(
            f"{side}-bearing-post",
            (x_range[0], 1.7, 4.6),
            (x_range[1], 7.25, 11.4),
            texture="#oakplanks",
            group="frame",
        )
        shape.box(
            f"{side}-bearing-cap",
            (x_range[0] - .15, 5.4, 4.1),
            (x_range[1] + .15, 10.75, 11.9),
            texture="#bronze",
            group="frame-bronze",
        )
        shape.box(
            f"{side}-bearing-wood",
            (x_range[0], 5.75, 4.55),
            (x_range[1], 10.35, 11.45),
            texture="#plainoak",
            group="frame",
        )
        for z in (4.25, 11.25):
            shape.box(
                f"{side}-bearing-bolt-{z:g}",
                (x_range[0] - .35, 7.55, z),
                (x_range[0] + .2, 8.45, z + .7),
                texture="#bronze",
                group="fasteners",
            )

    for edge, z_range, bolt_z in (
        ("front", (2, 3.6), (1.74, 2.02)),
        ("back", (12.4, 14), (13.98, 14.26)),
    ):
        shape.box(
            f"base-crossbeam-{edge}",
            (1, 0, z_range[0]),
            (15, 1.6, z_range[1]),
            faces=_shaft_faces(),
            group="frame",
        )
        for side, bolt_x in (("input", 2.5), ("output", 13.5)):
            shape.box(
                f"base-crossbeam-{edge}-bolt-{side}",
                (bolt_x - .42, .38, bolt_z[0]),
                (bolt_x + .42, 1.22, bolt_z[1]),
                texture="#bronze",
                group="fasteners",
            )


def _add_input(shape: Shape) -> None:
    pivot = shape.pivot("input-rotor", (8, 8, 8), group="input")
    _add_crossed_axle(shape, pivot, "input", (-8, -2.75))
    shape.box(
        "input-oak-hub",
        (-2.9, -2.05, -2.05),
        (-.55, 2.05, 2.05),
        texture="#strippedoak",
        parent=pivot,
        group="input-oak",
    )
    shape.box(
        "input-hub-bronze-band",
        (-2.95, -2.3, -2.3),
        (-2.45, 2.3, 2.3),
        texture="#bronze",
        parent=pivot,
        group="ratchet-bronze",
    )
    _spokes(
        shape, pivot, "input-oak-wheel-spoke",
        x_range=(-2.35, -.75), inner=1.35, outer=4.65,
        width=1.1, count=6, group="input-oak",
    )
    _ring(
        shape, pivot, "input-oak-ratchet-wheel",
        x_range=(-2.35, -.75), radius=4.5, thickness=1.05,
        segment_length=2.9, count=12, texture="#strippedoak",
        group="input-oak", angle_offset=15,
    )
    _ring(
        shape, pivot, "input-thin-bronze-tire",
        x_range=(-2.5, -2.1), radius=4.65, thickness=.45,
        segment_length=2.75, count=12, texture="#bronze",
        group="ratchet-bronze", angle_offset=15,
    )
    _teeth(shape, pivot)


def _pawl_mount(shape: Shape, parent: ElementRef, index: int, angle: float) -> ElementRef:
    radians = math.radians(angle)
    return shape.pivot(
        f"orbiting-pawl-{index}-mount",
        (0, math.cos(radians) * 5.15, math.sin(radians) * 5.15),
        parent=parent,
        group="pawl-carrier",
    )


def _add_fixed_pawl_mount(
    shape: Shape,
    parent: ElementRef,
    index: int,
    angle: float,
) -> ElementRef:
    mount = _pawl_mount(shape, parent, index, angle)
    x_range = (-.2, 2.25)
    for side, fork_x in (
        ("input", (x_range[0] - .38, x_range[0] - .08)),
        ("output", (x_range[1] + .08, x_range[1] + .38)),
    ):
        shape.box(
            f"orbiting-pawl-{index}-carrier-cheek-{side}",
            (fork_x[0], -.48, -.78),
            (fork_x[1], .72, .78),
            texture="#bronze",
            rotation_origin=(0, 0, 0),
            rotation=(angle, 0, 0),
            parent=mount,
            group="pawl-carrier",
        )
    shape.box(
        f"orbiting-pawl-{index}-spring-stop",
        (.15, 1.28, -.52),
        (1.9, 1.78, .52),
        texture="#strippedoak",
        rotation_origin=(0, 0, 0),
        rotation=(angle, 0, 0),
        parent=mount,
        group="pawl-carrier",
    )
    return mount
    shape.box(
        f"orbiting-pawl-{index}-through-pin",
        (-.75, -.38, -.38),
        (2.8, .38, .38),
        texture="#bronze",
        parent=mount,
        group="fasteners",
    )
    for side, pin_x in (("input", -.92), ("output", 2.77)):
        shape.box(
            f"orbiting-pawl-{index}-pin-head-{side}",
            (pin_x, -.55, -.55),
            (pin_x + .2, .55, .55),
            texture="#bronze",
            parent=mount,
            group="fasteners",
        )
    shape.box(
        f"orbiting-pawl-{index}-carrier-socket",
        (0, 1.55, -.65),
        (2.05, 2.05, .65),
        texture="#strippedoak",
        rotation_origin=(0, 0, 0),
        rotation=(angle, 0, 0),
        parent=mount,
        group="pawl-carrier",
    )


def _add_output(shape: Shape) -> tuple[ElementRef, dict[int, ElementRef]]:
    pivot = shape.pivot("output-rotor", (8, 8, 8), group="output")
    _add_crossed_axle(shape, pivot, "output", (2.75, 8))
    shape.box(
        "output-oak-hub",
        (.55, -2.05, -2.05),
        (2.9, 2.05, 2.05),
        texture="#strippedoak",
        parent=pivot,
        group="output-oak",
    )
    shape.box(
        "output-hub-bronze-band",
        (2.45, -2.3, -2.3),
        (2.95, 2.3, 2.3),
        texture="#bronze",
        parent=pivot,
        group="output-bronze",
    )
    _spokes(
        shape, pivot, "output-oak-pawl-carrier",
        x_range=(.75, 2.35), inner=1.3, outer=5.2,
        width=1.05, count=3, group="output-oak",
    )
    _spokes(
        shape, pivot, "output-carrier-bronze-inlay",
        x_range=(2.05, 2.42), inner=1.55, outer=5.05,
        width=.32, count=3, texture="#bronze", group="output-bronze",
    )
    mounts = {
        index: _add_fixed_pawl_mount(shape, pivot, index, angle)
        for index, angle in enumerate(PAWL_ANGLES, start=1)
    }
    return pivot, mounts


def _add_moving_pawl(
    shape: Shape,
    index: int,
    angle: float,
    root: ElementRef | None = None,
    mount: ElementRef | None = None,
) -> None:
    if mount is None:
        if root is None:
            root = shape.pivot("output-rotor", (8, 8, 8), group="output")
        mount = _pawl_mount(shape, root, index, angle)
    pivot = shape.pivot(
        f"orbiting-pawl-{index}",
        (0, 0, 0),
        parent=mount,
        group="pawl",
    )
    x_range = (-.2, 2.25)
    for suffix, start, end in (
        ("hook-body", (-.2, -1.25, -.46), (2.25, .15, .46)),
        ("tooth-hook", (-.35, -1.75, -.6624), (2.4, -1.15, .6624)),
        ("counterweight-heel", (-.45, -.15, -.6624), (2.5, .72, .6624)),
    ):
        shape.box(
            f"orbiting-pawl-{index}-{suffix}",
            start,
            end,
            texture="#bronze",
            rotation_origin=(0, 0, 0),
            rotation=(angle, 0, 0),
            parent=pivot,
            group="pawl",
        )
    shape.box(
        f"orbiting-pawl-{index}-leaf-spring",
        (x_range[0] + .25, .52, -.18),
        (x_range[1] - .25, 1.5, .18),
        texture="#bronze",
        rotation_origin=(0, 0, 0),
        rotation=(angle, 0, 0),
        parent=pivot,
        group="pawl-spring",
    )


def _frame_shape() -> Shape:
    shape = _shape("overrunning-transmission-frame")
    _add_frame(shape)
    return shape


def _input_shape() -> Shape:
    shape = _shape("overrunning-transmission-input")
    _add_input(shape)
    return shape


def _output_shape() -> Shape:
    shape = _shape("overrunning-transmission-output")
    _add_output(shape)
    return shape


def _pawl_shape(index: int, angle: float) -> Shape:
    shape = _shape(f"overrunning-transmission-pawl-{index}")
    _add_moving_pawl(shape, index, angle)
    return shape


def _inventory_shape() -> Shape:
    shape = _shape("overrunning-transmission-inventory")
    _add_frame(shape)
    _add_input(shape)
    output, mounts = _add_output(shape)
    for index, angle in enumerate(PAWL_ANGLES, start=1):
        _add_moving_pawl(shape, index, angle, output, mounts[index])
    return shape


def build() -> ModelPackage:
    package = ModelPackage("overrunning_transmission")
    package.shape(_frame_shape(), "assets/gearwright/shapes/block/overrunning-transmission-frame.json")
    package.shape(_input_shape(), "assets/gearwright/shapes/block/overrunning-transmission-input.json")
    package.shape(_output_shape(), "assets/gearwright/shapes/block/overrunning-transmission-output.json")
    for index, angle in enumerate(PAWL_ANGLES, start=1):
        package.shape(
            _pawl_shape(index, angle),
            f"assets/gearwright/shapes/block/overrunning-transmission-pawl-{index}.json",
        )
    package.shape(
        _inventory_shape(),
        "assets/gearwright/shapes/block/overrunning-transmission-inventory.json",
    )
    return package
