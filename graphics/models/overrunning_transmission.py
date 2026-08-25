"""Approved stepped-ratchet overrunning-transmission runtime shapes."""

from __future__ import annotations

import math
from collections import OrderedDict

from gearwright_graphics.model import ElementRef, Face, ModelPackage, Shape


OAK_PLANKS = "game:block/wood/planks/oak1"
PLAIN_OAK = "survival:block/wood/plainoak"
STRIPPED_OAK = "survival:block/wood/debarked/oak"
OAK_END = "survival:block/wood/treetrunk/debarked/oak"
TIN_BRONZE = "game:block/metal/sheet/tinbronze1"
BRASS = "game:block/metal/sheet/brass1"

PAWL_ANGLES = (0.0, 120.0, 240.0)
TOOTH_COUNT = 15
TOOTH_PITCH = 360.0 / TOOTH_COUNT
ORBIT_RADIUS = 5.82
PIVOT_LEAD = .62
ARM_WIDTH = 1.58


def _shape(shape_id: str) -> Shape:
    shape = Shape(shape_id, 16, 16)
    shape.texture("oakplanks", OAK_PLANKS)
    shape.texture("plainoak", PLAIN_OAK)
    shape.texture("strippedoak", STRIPPED_OAK)
    shape.texture("oakend", OAK_END)
    shape.texture("bronze", TIN_BRONZE)
    shape.texture("brass", BRASS)
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
    handedness: int = 1,
) -> None:
    for index in range(count):
        angle = handedness * (angle_offset + index * 360 / count)
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
    handedness: int = 1,
) -> None:
    for index in range(count):
        _radial_box(
            shape,
            parent,
            f"{prefix}-{index:02d}",
            x_range,
            (inner, outer),
            width,
            handedness * (angle_offset + index * 360 / count),
            texture=texture,
            group=group,
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
    for side, x_range in (("input", (1.2, left_end)), ("output", (right_start, 14.8))):
        shape.box(f"{side}-separate-foot", (x_range[0] - .2, 0, 3.62), (x_range[1] + .2, 1.72, 12.38), texture="#oakplanks", group="frame")
        shape.box(f"{side}-bearing-post", (x_range[0], 1.7, 4.6), (x_range[1], 7.25, 11.4), texture="#oakplanks", group="frame")
        shape.box(f"{side}-bearing-cap", (x_range[0] - .15, 5.4, 4.1), (x_range[1] + .15, 10.75, 11.9), texture="#bronze", group="frame-bronze")
        shape.box(f"{side}-bearing-wood", (x_range[0], 5.75, 4.55), (x_range[1], 10.35, 11.45), texture="#plainoak", group="frame")
        for z in (4.25, 11.25):
            shape.box(f"{side}-bearing-bolt-{z:g}", (x_range[0] - .35, 7.55, z), (x_range[0] + .2, 8.45, z + .7), texture="#bronze", group="fasteners")

    for edge, z_range, bolt_z in (("front", (2, 3.6), (1.74, 2.02)), ("back", (12.4, 14), (13.98, 14.26))):
        shape.box(f"base-crossbeam-{edge}", (1, 0, z_range[0]), (15, 1.6, z_range[1]), faces=_shaft_faces(), group="frame")
        for side, bolt_x in (("input", 2.5), ("output", 13.5)):
            shape.box(f"base-crossbeam-{edge}-bolt-{side}", (bolt_x - .42, .38, bolt_z[0]), (bolt_x + .42, 1.22, bolt_z[1]), texture="#bronze", group="fasteners")


def _add_input(shape: Shape, handedness: int) -> None:
    pivot = shape.pivot("input-rotor", (8, 8, 8), group="input")
    _add_crossed_axle(shape, pivot, "input", (-8, -2.75))
    shape.box("input-oak-ring-hub", (-2.54, -1.38, -1.38), (-.60, 1.38, 1.38), texture="#strippedoak", parent=pivot, group="input-oak")
    shape.box("input-oak-hub-brass-band", (-2.66, -1.65, -1.65), (-2.34, 1.65, 1.65), texture="#brass", parent=pivot, group="input-brass")
    _spokes(shape, pivot, "input-oak-ring-spoke", x_range=(-2.18, -.70), inner=1.10, outer=4.04, width=1.20, count=4, texture="#strippedoak", group="input-oak", handedness=handedness)
    _ring(shape, pivot, "input-locking-oak-ring", x_range=(-2.20, -.66), radius=4.16, thickness=.62, segment_length=1.94, count=TOOTH_COUNT, texture="#strippedoak", group="input-oak", angle_offset=TOOTH_PITCH / 2, handedness=handedness)
    _ring(shape, pivot, "input-thin-brass-tire", x_range=(-2.34, -2.18), radius=4.53, thickness=.18, segment_length=1.94 * 1.04, count=TOOTH_COUNT, texture="#brass", group="gear-reinforcement", angle_offset=TOOTH_PITCH / 2, handedness=handedness)

    for index in range(TOOTH_COUNT):
        base_angle = index * TOOTH_PITCH
        angle = handedness * base_angle
        tangent_offset = handedness * -.76
        radians = math.radians(angle)
        mount = shape.pivot(
            f"input-smooth-ramp-{index:02d}-mount",
            (0, math.cos(radians) * 4.55 - math.sin(radians) * tangent_offset, math.sin(radians) * 4.55 + math.cos(radians) * tangent_offset),
            parent=pivot,
            group="input-teeth",
        )
        shape.box(f"input-smooth-ramp-{index:02d}", (-2.38, -.575, -.20), (-.48, .575, .20), texture="#brass", rotation_origin=(0, 0, 0), rotation=(handedness * (base_angle + 55), 0, 0), parent=mount, group="input-teeth")
        tangent = (-.34, 0) if handedness > 0 else (0, .34)
        shape.box(f"input-ratchet-lock-face-{index:02d}", (-2.38, 4.18, tangent[0]), (-.48, 4.98, tangent[1]), texture="#brass", rotation_origin=(0, 0, 0), rotation=(angle, 0, 0), parent=pivot, group="input-teeth")


def _mount_position(angle: float, handedness: int) -> tuple[float, float, float]:
    radians = math.radians(angle)
    y = math.cos(radians) * ORBIT_RADIUS - math.sin(radians) * PIVOT_LEAD
    z = math.sin(radians) * ORBIT_RADIUS + math.cos(radians) * PIVOT_LEAD
    return 0, y, handedness * z


def _pawl_mount(shape: Shape, parent: ElementRef, index: int, angle: float, handedness: int) -> ElementRef:
    return shape.pivot(f"orbiting-pawl-{index}-mount", _mount_position(angle, handedness), parent=parent, group="pawl-carrier")


def _add_output(shape: Shape, handedness: int) -> tuple[ElementRef, dict[int, ElementRef]]:
    pivot = shape.pivot("output-rotor", (8, 8, 8), group="output")
    _add_crossed_axle(shape, pivot, "output", (2.75, 8))
    shape.box("output-oak-hub", (.55, -1.56, -1.56), (2.90, 1.56, 1.56), texture="#strippedoak", parent=pivot, group="output-oak")
    shape.box("output-hub-bronze-band", (2.45, -1.84, -1.84), (2.95, 1.84, 1.84), texture="#bronze", parent=pivot, group="output-bronze")
    _spokes(shape, pivot, "output-broad-orbital-arm", x_range=(.72, 2.30), inner=1.30, outer=ORBIT_RADIUS - .58, width=ARM_WIDTH, count=3, group="output-oak", handedness=handedness)

    mounts: dict[int, ElementRef] = {}
    for index, base_angle in enumerate(PAWL_ANGLES, 1):
        angle = handedness * base_angle
        head_radial = (ORBIT_RADIUS - .78, ORBIT_RADIUS + .24)
        _radial_box(shape, pivot, f"output-gearward-head-{index:02d}", (.34, 2.32), head_radial, ARM_WIDTH, angle, texture="#strippedoak", group="output-oak")
        _radial_box(shape, pivot, f"output-reinforced-head-plate-{index:02d}-carrier", (2.29, 2.45), head_radial, ARM_WIDTH, angle, texture="#bronze", group="output-bronze")
        _radial_box(shape, pivot, f"output-gearward-neck-{index:02d}", (-.40, .48), (ORBIT_RADIUS - .38, ORBIT_RADIUS + .20), ARM_WIDTH * .54, angle, texture="#bronze", group="output-bronze")
        bolt_radius = ORBIT_RADIUS - .08
        _radial_box(shape, pivot, f"output-head-clamp-bolt-{index:02d}", (.22, 2.53), (bolt_radius - .16, bolt_radius + .16), .42, angle, texture="#bronze", group="fasteners")
        for side, x_range in (("gear", (.10, .24)), ("carrier", (2.51, 2.63))):
            _radial_box(shape, pivot, f"output-head-clamp-nut-{index:02d}-{side}", x_range, (bolt_radius - .27, bolt_radius + .27), .58, angle, texture="#bronze", group="fasteners")
        _radial_box(shape, pivot, f"output-arm-root-brace-{index:02d}", (2.04, 2.43), (1.42, 2.58), ARM_WIDTH, angle, texture="#bronze", group="output-bronze")

        mount = _pawl_mount(shape, pivot, index, base_angle, handedness)
        mounts[index] = mount
        shape.box(f"orbiting-pawl-{index}-large-pin", (-1.50, -.24, -.24), (.54, .24, .24), texture="#bronze", parent=mount, group="fasteners")
        for side, x_range in (("gear", (-1.62, -1.48)), ("carrier", (.52, .66))):
            shape.box(f"orbiting-pawl-{index}-pin-head-{side}", (x_range[0], -.36, -.36), (x_range[1], .36, .36), texture="#bronze", parent=mount, group="fasteners")
    return pivot, mounts


def _mirrored_z_bounds(lower: tuple[float, float, float], upper: tuple[float, float, float], handedness: int) -> tuple[tuple[float, float, float], tuple[float, float, float]]:
    if handedness > 0:
        return lower, upper
    return (lower[0], lower[1], -upper[2]), (upper[0], upper[1], -lower[2])


def _add_moving_pawl(shape: Shape, index: int, base_angle: float, handedness: int, root: ElementRef | None = None, mount: ElementRef | None = None) -> None:
    if mount is None:
        if root is None:
            root = shape.pivot("output-rotor", (8, 8, 8), group="output")
        mount = _pawl_mount(shape, root, index, base_angle, handedness)
    pivot = shape.pivot(f"orbiting-pawl-{index}", (0, 0, 0), parent=mount, group="pawl")
    for suffix, lower, upper in (
        ("contact-stick", (-1.30, -1.08, -.62), (-.68, .30, -.34)),
        ("pivot-collar", (-1.42, -.38, -.42), (-.50, .32, .42)),
        ("spring-follower-cap", (-1.28, .22, 0), (-.88, .58, .24)),
    ):
        lower, upper = _mirrored_z_bounds(lower, upper, handedness)
        shape.box(f"orbiting-pawl-{index}-{suffix}", lower, upper, texture="#bronze", rotation_origin=(0, 0, 0), rotation=(handedness * base_angle, 0, 0), parent=pivot, group="pawl-spring" if suffix == "spring-follower-cap" else "pawl")


def _add_spring(shape: Shape, index: int, base_angle: float, handedness: int, root: ElementRef | None = None, mount: ElementRef | None = None) -> None:
    if mount is None:
        if root is None:
            root = shape.pivot("output-rotor", (8, 8, 8), group="output")
        mount = _pawl_mount(shape, root, index, base_angle, handedness)
    radians = math.radians(base_angle)
    spring_y = math.cos(radians) * .32 - math.sin(radians) * .12
    spring_z = handedness * (math.sin(radians) * .32 + math.cos(radians) * .12)
    spring = shape.pivot(f"orbiting-pawl-{index}-spring-flex", (0, spring_y, spring_z), parent=mount, group="pawl-spring")
    for suffix, lower, upper in (
        ("leaf-spring-back", (-1.18, -.78, -.34), (-.98, 0, -.10)),
        ("leaf-spring-tip", (-1.20, -.98, -.56), (-.96, -.74, -.32)),
    ):
        lower, upper = _mirrored_z_bounds(lower, upper, handedness)
        shape.box(f"orbiting-pawl-{index}-{suffix}", lower, upper, texture="#bronze", rotation_origin=(0, 0, 0), rotation=(handedness * base_angle, 0, 0), parent=spring, group="pawl-spring")


def _frame_shape() -> Shape:
    shape = _shape("overrunning-transmission-frame")
    _add_frame(shape)
    return shape


def _input_shape(handedness: int) -> Shape:
    suffix = "" if handedness > 0 else "-reverse"
    shape = _shape(f"overrunning-transmission-input{suffix}")
    _add_input(shape, handedness)
    return shape


def _output_shape(handedness: int) -> Shape:
    suffix = "" if handedness > 0 else "-reverse"
    shape = _shape(f"overrunning-transmission-output{suffix}")
    _add_output(shape, handedness)
    return shape


def _pawl_shape(index: int, angle: float, handedness: int) -> Shape:
    suffix = "" if handedness > 0 else "-reverse"
    shape = _shape(f"overrunning-transmission-pawl-{index}{suffix}")
    _add_moving_pawl(shape, index, angle, handedness)
    return shape


def _spring_shape(index: int, angle: float, handedness: int) -> Shape:
    suffix = "" if handedness > 0 else "-reverse"
    shape = _shape(f"overrunning-transmission-spring-{index}{suffix}")
    _add_spring(shape, index, angle, handedness)
    return shape


def _inventory_shape() -> Shape:
    shape = _shape("overrunning-transmission-inventory")
    _add_frame(shape)
    _add_input(shape, 1)
    output, mounts = _add_output(shape, 1)
    for index, angle in enumerate(PAWL_ANGLES, start=1):
        _add_moving_pawl(shape, index, angle, 1, output, mounts[index])
        _add_spring(shape, index, angle, 1, output, mounts[index])
    return shape


def build() -> ModelPackage:
    package = ModelPackage("overrunning_transmission")
    package.shape(_frame_shape(), "assets/gearwright/shapes/block/overrunning-transmission-frame.json")
    for handedness, suffix in ((1, ""), (-1, "-reverse")):
        package.shape(_input_shape(handedness), f"assets/gearwright/shapes/block/overrunning-transmission-input{suffix}.json")
        package.shape(_output_shape(handedness), f"assets/gearwright/shapes/block/overrunning-transmission-output{suffix}.json")
        for index, angle in enumerate(PAWL_ANGLES, start=1):
            package.shape(_pawl_shape(index, angle, handedness), f"assets/gearwright/shapes/block/overrunning-transmission-pawl-{index}{suffix}.json")
            package.shape(_spring_shape(index, angle, handedness), f"assets/gearwright/shapes/block/overrunning-transmission-spring-{index}{suffix}.json")
    package.shape(_inventory_shape(), "assets/gearwright/shapes/block/overrunning-transmission-inventory.json")
    return package
