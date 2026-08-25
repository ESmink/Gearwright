"""Build the managed review package for an automatic overrunning coupling."""

from __future__ import annotations

import argparse
import math
import shutil
from collections import OrderedDict
from pathlib import Path

from gearwright_graphics.compiler import build_package, json_bytes
from gearwright_graphics.model import ElementRef, Face, ModelPackage, Shape, animate


REVIEW_ROOT = Path("generated/overrunning-coupling-review")
MANAGED_NAME = "current"
STAGING_NAME = ".current-build"
BACKUP_NAME = ".current-old"

OAK_PLANKS = "game:block/wood/planks/oak1"
PLAIN_OAK = "survival:block/wood/plainoak"
STRIPPED_OAK = "survival:block/wood/debarked/oak"
OAK_END = "survival:block/wood/treetrunk/debarked/oak"
TIN_BRONZE = "game:block/metal/sheet/tinbronze1"
BRASS = "game:block/metal/sheet/brass1"

CARRIER_CONFIGS = OrderedDict((
    ("carrier-a-clevis", {"style": "clevis", "arm_x": (-.68, 2.35), "arm_outer": 4.62}),
    ("carrier-b-oak-fork", {"style": "oak-fork", "arm_x": (-.68, 2.35), "arm_outer": 4.58}),
    ("carrier-c-bridle", {"style": "bridle", "arm_x": (-.68, 2.35), "arm_outer": 4.64}),
))

ORBITAL_CONFIGS = OrderedDict((
    ("lock-a-stepped-hook", {"teeth": 15, "gear": "stepped-ratchet", "pawl": "restored-hook", "arm_width": 1.58, "lift": 30, "orbit_radius": 5.82, "pivot_lead": .62, "gear_supports": 4, "tooth_angle_offset": 0.0, "drive_approach": 8.0, "ramp_start": .30, "ramp_full": .76, "drop_start": .985}),
    ("lock-b-pocket-bar", {"teeth": 12, "gear": "deep-pocket-ring", "pawl": "drop-bar", "arm_width": 1.58, "lift": 56, "orbit_radius": 5.82, "pivot_lead": .62, "gear_supports": 4, "tooth_angle_offset": 0.0, "drive_approach": 10.0, "ramp_start": .42}),
    ("lock-c-concept-crook", {"teeth": 9, "gear": "swept-concept", "pawl": "concept-crook", "arm_width": 1.58, "lift": 64, "orbit_radius": 5.92, "pivot_lead": .62, "gear_supports": 4, "tooth_angle_offset": 0.0, "drive_approach": 14.0, "ramp_start": .46}),
))

DESCRIPTIONS = OrderedDict((
    (
        "lock-a-stepped-hook",
        "A single narrow bronze contact stick ends at the former club shoulder and drops behind fifteen brass locking shoulders. Its follower cap moves with the stick, while overlapping oak rim blocks and a continuous-looking brass tire make the wheel read as one structure. The diagnostic tooth pass rises gradually to frame 23, then settles during four explicit recovery frames.",
    ),
    (
        "lock-b-pocket-bar",
        "A broad bronze drop bar falls into twelve deep gaps between reinforced-oak lugs. Each lug carries a thin inclined brass glide plate and a replaceable square brass locking face, keeping the pocket obvious while metal use stays low. It tests the simplest workshop-built mechanism.",
    ),
    (
        "lock-c-concept-crook",
        "A tall bronze crook follows the supplied sketch and hooks behind nine large swept brass teeth. The sparse pointed wheel and long hanging latch exaggerate the catch-and-release action. It tests the concept-art silhouette rather than the conventional fine ratchet.",
    ),
))

FOOTPRINTS = OrderedDict((
    (candidate, OrderedDict((("length", 1), ("height", 1), ("depth", 1))))
    for candidate in DESCRIPTIONS
))


def _inside(root: Path, path: Path) -> bool:
    try:
        path.resolve().relative_to(root.resolve())
        return True
    except ValueError:
        return False


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


def _teeth(
    shape: Shape,
    parent: ElementRef,
    prefix: str,
    *,
    x_range: tuple[float, float],
    radius: float,
    tooth_length: float,
    tooth_width: float,
    count: int,
    angle_offset: float = 0,
) -> None:
    for index in range(count):
        _radial_box(
            shape,
            parent,
            f"{prefix}-{index:02d}",
            x_range,
            (radius, radius + tooth_length),
            tooth_width,
            angle_offset + index * 360 / count,
            texture="#bronze",
            group="ratchet-bronze",
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
    group: str = "rotating-oak",
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


def _axles(
    shape: Shape,
    input_pivot: ElementRef,
    output_pivot: ElementRef,
    *,
    half_length: float,
    hub_gap: float,
) -> None:
    # Vanilla's wooden axle is a crossed 2x4 + 4x2 section. The tiny X offset
    # mirrors the stock shape and prevents coincident end faces at the hub seam.
    for side, parent, x_range in (
        ("input", input_pivot, (-half_length, -hub_gap)),
        ("output", output_pivot, (hub_gap, half_length)),
    ):
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


def _bearing_stand(shape: Shape, *, length: float, inner_clearance: tuple[float, float]) -> None:
    left_end, right_start = inner_clearance
    for side, x_range in (("input", (1.2, left_end)), ("output", (right_start, length - 1.2))):
        shape.box(
            f"{side}-separate-foot",
            (x_range[0] - .2, 0, 3.62),
            (x_range[1] + .2, 1.72, 12.38),
            texture="#oakplanks",
            group="frame",
        )
        shape.box(f"{side}-bearing-post", (x_range[0], 1.7, 4.6), (x_range[1], 7.25, 11.4), texture="#oakplanks", group="frame")
        shape.box(f"{side}-bearing-cap", (x_range[0] - .15, 5.4, 4.1), (x_range[1] + .15, 10.75, 11.9), texture="#bronze", group="frame-bronze")
        shape.box(f"{side}-bearing-wood", (x_range[0], 5.75, 4.55), (x_range[1], 10.35, 11.45), texture="#plainoak", group="frame")
        for z in (4.25, 11.25):
            shape.box(
                f"{side}-bearing-bolt-{z:g}",
                (x_range[0] - .35, 7.55, z),
                (x_range[0] + .2, 8.45, z + .7),
                texture="#bronze",
                group="fasteners",
            )

    # One square crossbeam closes each end of the two shortened foundations.
    # A .02-unit clearance removes every coincident timber face. The bolt heads
    # sit on the outward faces, in line with the foundations they fasten into.
    crossbeams = (
        ("front", (2, 3.6), (1.74, 2.02)),
        ("back", (12.4, 14), (13.98, 14.26)),
    )
    for edge, z_range, bolt_z in crossbeams:
        shape.box(
            f"base-crossbeam-{edge}",
            (1, 0, z_range[0]),
            (length - 1, 1.6, z_range[1]),
            faces=_shaft_faces(),
            group="frame",
        )
        for side, bolt_x in (("input", 2.5), ("output", length - 2.5)):
            shape.box(
                f"base-crossbeam-{edge}-bolt-{side}",
                (bolt_x - .42, .38, bolt_z[0]),
                (bolt_x + .42, 1.22, bolt_z[1]),
                texture="#bronze",
                group="fasteners",
            )


def _pawl(
    shape: Shape,
    output_pivot: ElementRef,
    name: str,
    *,
    mount_angle: float,
    mount_radius: float,
    x_range: tuple[float, float],
    width: float,
    body_inner: float,
    hook_inner: float,
    hook_outer: float,
) -> ElementRef:
    radians = math.radians(mount_angle)
    mount = shape.pivot(
        f"{name}-mount",
        (0, math.cos(radians) * mount_radius, math.sin(radians) * mount_radius),
        parent=output_pivot,
        group="pawl-carrier",
    )
    pivot = shape.pivot(
        name,
        (0, 0, 0),
        parent=mount,
        group="pawl",
    )
    hook_input = x_range[0] - .35
    shape.box(
        f"{name}-hook-body",
        (hook_input, body_inner, -width / 2),
        (x_range[1], hook_outer - .05, width / 2),
        texture="#bronze",
        rotation_origin=(0, 0, 0),
        rotation=(mount_angle, 0, 0),
        parent=pivot,
        group="pawl",
    )
    shape.box(
        f"{name}-tooth-hook",
        (hook_input - .15, hook_inner, -width * .72),
        (x_range[1] + .15, hook_outer, width * .72),
        texture="#bronze",
        rotation_origin=(0, 0, 0),
        rotation=(mount_angle, 0, 0),
        parent=pivot,
        group="pawl",
    )
    shape.box(
        f"{name}-counterweight-heel",
        (x_range[0] - .25, .45, -width * .72),
        (x_range[1] + .25, .85, width * .72),
        texture="#bronze",
        rotation_origin=(0, 0, 0),
        rotation=(mount_angle, 0, 0),
        parent=pivot,
        group="pawl",
    )
    shape.box(
        f"{name}-leaf-spring",
        (x_range[0] + .25, .52, -.18),
        (x_range[1] - .25, 1.5, .18),
        texture="#bronze",
        parent=pivot,
        rotation_origin=(0, 0, 0),
        rotation=(mount_angle, 0, 0),
        group="pawl-spring",
    )
    for side, fork_x in (("input", (x_range[0] - .38, x_range[0] - .08)), ("output", (x_range[1] + .08, x_range[1] + .38))):
        shape.box(
            f"{name}-carrier-cheek-{side}",
            (fork_x[0], -.48, -.78),
            (fork_x[1], .72, .78),
            texture="#bronze",
            rotation_origin=(0, 0, 0),
            rotation=(mount_angle, 0, 0),
            parent=mount,
            group="pawl-carrier",
        )
    shape.box(
        f"{name}-spring-stop",
        (x_range[0] + .35, 1.28, -.52),
        (x_range[1] - .35, 1.78, .52),
        texture="#strippedoak",
        rotation_origin=(0, 0, 0),
        rotation=(mount_angle, 0, 0),
        parent=mount,
        group="pawl-carrier",
    )
    shape.box(
        f"{name}-through-pin",
        (x_range[0] - .55, -.38, -.38),
        (x_range[1] + .55, .38, .38),
        texture="#bronze",
        parent=mount,
        group="fasteners",
    )
    for side, pin_x in (("input", x_range[0] - .72), ("output", x_range[1] + .52)):
        shape.box(
            f"{name}-pin-head-{side}",
            (pin_x, -.55, -.55),
            (pin_x + .2, .55, .55),
            texture="#bronze",
            parent=mount,
            group="fasteners",
        )
    shape.box(
        f"{name}-carrier-socket",
        (x_range[0] + .2, 1.55, -.65),
        (x_range[1] - .2, 2.05, .65),
        texture="#strippedoak",
        rotation_origin=(0, 0, 0),
        rotation=(mount_angle, 0, 0),
        parent=mount,
        group="pawl-carrier",
    )
    return pivot


def _joint_box(
    shape: Shape,
    parent: ElementRef,
    name: str,
    bounds: tuple[tuple[float, float, float], tuple[float, float, float]],
    *,
    angle: float,
    texture: str,
    group: str,
) -> ElementRef:
    return shape.box(
        name,
        bounds[0],
        bounds[1],
        texture=texture,
        rotation_origin=(0, 0, 0),
        rotation=(angle, 0, 0),
        parent=parent,
        group=group,
    )


def _pawl_joint(
    shape: Shape,
    output_pivot: ElementRef,
    name: str,
    *,
    mount_angle: float,
    style: str,
) -> ElementRef:
    mount_radius = 5.08
    radians = math.radians(mount_angle)
    mount = shape.pivot(
        f"{name}-mount",
        (0, math.cos(radians) * mount_radius, math.sin(radians) * mount_radius),
        parent=output_pivot,
        group="pawl-carrier",
    )

    if style == "clevis":
        for side, x_range, inner in (("input", (-.46, -.30), .06), ("output", (.08, .22), -.34)):
            _joint_box(shape, mount, f"{name}-bronze-clevis-{side}", ((x_range[0], inner, -.50), (x_range[1], .82, .50)), angle=mount_angle, texture="#bronze", group="pawl-carrier")
        _joint_box(shape, mount, f"{name}-bronze-clevis-bridge", ((-.30, .74, -.50), (.08, .90, .50)), angle=mount_angle, texture="#bronze", group="pawl-carrier")
    elif style == "oak-fork":
        for side, x_range, inner in (("input", (-.48, -.30), .08), ("output", (.08, .28), -.36)):
            _joint_box(shape, mount, f"{name}-oak-fork-{side}", ((x_range[0], inner, -.48), (x_range[1], .86, .48)), angle=mount_angle, texture="#strippedoak", group="pawl-carrier")
            plate_x = (x_range[1], x_range[1] + .10) if side == "input" else (x_range[0] - .10, x_range[0])
            _joint_box(shape, mount, f"{name}-bronze-wear-plate-{side}", ((plate_x[0], max(inner, .10), -.40), (plate_x[1], .60, .40)), angle=mount_angle, texture="#bronze", group="pawl-carrier")
        _joint_box(shape, mount, f"{name}-oak-fork-bridge", ((-.30, .78, -.48), (.08, .96, .48)), angle=mount_angle, texture="#strippedoak", group="pawl-carrier")
    elif style == "bridle":
        for side, x_range, inner in (("input", (-.46, -.28), .06), ("output", (.06, .22), -.34)):
            _joint_box(shape, mount, f"{name}-bronze-bridle-lug-{side}", ((x_range[0], inner, -.54), (x_range[1], .82, .54)), angle=mount_angle, texture="#bronze", group="pawl-carrier")
        for edge, radial_range in (("inner", (-.46, -.30)), ("outer", (.74, .90))):
            _joint_box(shape, mount, f"{name}-bronze-bridle-{edge}", ((-.28, radial_range[0], -.54), (.06, radial_range[1], .54)), angle=mount_angle, texture="#bronze", group="pawl-carrier")
    else:
        raise ValueError(f"unknown carrier joint style: {style}")

    pivot = shape.pivot(name, (0, 0, 0), parent=mount, group="pawl")
    # The arm supplies the axial reach. The narrow hook is the only part that
    # crosses the tooth face; its raised neck clears the wooden shoulder.
    for suffix, lower, upper in (
        ("body", (-.26, .08, -.26), (.04, .58, .26)),
        ("hook-neck", (-.38, .05, -.25), (-.22, .30, .25)),
        ("tooth-hook", (-.51, -.22, -.36), (-.34, .13, .36)),
        ("rounded-heel", (-.24, .48, -.31), (.02, .72, .31)),
    ):
        shape.box(
            f"{name}-{suffix}",
            lower,
            upper,
            texture="#bronze",
            rotation_origin=(0, 0, 0),
            rotation=(mount_angle, 0, 0),
            parent=pivot,
            group="pawl",
        )
    shape.box(
        f"{name}-leaf-spring",
        (-.10, .58, -.10),
        (.04, .84, .10),
        texture="#bronze",
        rotation_origin=(0, 0, 0),
        rotation=(mount_angle, 0, 0),
        parent=mount,
        group="pawl-spring",
    )
    shape.box(f"{name}-through-pin", (-.54, -.18, -.18), (.32, .18, .18), texture="#bronze", parent=mount, group="fasteners")
    for side, x_range in (("input", (-.62, -.50)), ("output", (.28, .40))):
        shape.box(f"{name}-pin-head-{side}", (x_range[0], -.29, -.29), (x_range[1], .29, .29), texture="#bronze", parent=mount, group="fasteners")
    return pivot


def _outer_arm_joint(shape: Shape, output_pivot: ElementRef, *, angle: float, index: int) -> None:
    # The shoulder approaches the input face but remains clear of the teeth.
    _radial_box(
        shape,
        output_pivot,
        f"output-oak-joint-shoulder-{index:02d}",
        (-.32, .76),
        (4.34, 4.98),
        .92,
        angle,
        texture="#strippedoak",
        group="output-oak",
    )
    # A short brace at the hub is enough to stop the arm splitting. A long
    # decorative metal spine would obscure the wooden load-bearing member.
    _radial_box(
        shape,
        output_pivot,
        f"output-bronze-arm-root-{index:02d}",
        (2.05, 2.42),
        (1.42, 2.42),
        .46,
        angle,
        texture="#bronze",
        group="output-bronze",
    )


def _animations(
    shape: Shape,
    input_pivot: ElementRef,
    output_pivot: ElementRef,
    pawls: tuple[tuple[ElementRef, float], ...],
) -> None:
    pawl_lift = 16
    drive = animate(shape, "Driving together", "drive", 36, version=1, on_animation_end="Repeat")
    for frame, rotation in ((0, 0), (35, 360)):
        drive.keyframe(frame, input_pivot, rotationX=rotation, rotShortestDistanceX=False)
        drive.keyframe(frame, output_pivot, rotationX=rotation, rotShortestDistanceX=False)
    shape.add_animation(drive.build())

    overrun = animate(shape, "Output overruns input", "overrun", 72, version=1, on_animation_end="Repeat")
    frames = [*range(0, 70, 2), 71]
    for frame in frames:
        progress = frame / 71
        input_rotation = progress * 120
        output_rotation = progress * 360
        overrun.keyframe(frame, input_pivot, rotationX=input_rotation, rotShortestDistanceX=False)
        overrun.keyframe(frame, output_pivot, rotationX=output_rotation, rotShortestDistanceX=False)
        tooth_cycles = (output_rotation - input_rotation) / 30
        for pawl, phase in pawls:
            cycle = (tooth_cycles + phase) % 1
            lift = pawl_lift * (cycle / .7 if cycle < .7 else (1 - cycle) / .3)
            overrun.keyframe(
                frame,
                pawl,
                rotationX=max(0, lift),
                rotShortestDistanceX=True,
            )
    shape.add_animation(overrun.build())


def _compact_variant(
    shape_id: str,
    *,
    pawl_angles: tuple[float, ...],
    pawl_phases: tuple[float, ...],
    latch_config: dict[str, float],
) -> Shape:
    shape = _shape(shape_id)
    _bearing_stand(shape, length=16, inner_clearance=(3.8, 12.2))
    input_pivot = shape.pivot("input-rotor", (8, 8, 8), group="input")
    output_pivot = shape.pivot("output-rotor", (8, 8, 8), group="output")
    _axles(shape, input_pivot, output_pivot, half_length=8, hub_gap=2.75)

    shape.box("input-oak-hub", (-2.9, -2.05, -2.05), (-.55, 2.05, 2.05), texture="#strippedoak", parent=input_pivot, group="input-oak")
    shape.box("input-hub-bronze-band", (-2.95, -2.3, -2.3), (-2.45, 2.3, 2.3), texture="#bronze", parent=input_pivot, group="ratchet-bronze")
    _spokes(shape, input_pivot, "input-oak-wheel-spoke", x_range=(-2.35, -.75), inner=1.35, outer=4.65, width=1.1, count=6, group="input-oak")
    _ring(shape, input_pivot, "input-oak-ratchet-wheel", x_range=(-2.35, -.75), radius=4.5, thickness=1.05, segment_length=2.9, count=12, texture="#strippedoak", group="input-oak", angle_offset=15)
    _ring(shape, input_pivot, "input-thin-bronze-tire", x_range=(-2.5, -2.1), radius=4.65, thickness=.45, segment_length=2.75, count=12, texture="#bronze", group="ratchet-bronze", angle_offset=15)
    _teeth(shape, input_pivot, "input-replaceable-ratchet-tooth", x_range=(-2.6, -.5), radius=4.85, tooth_length=.75, tooth_width=.92, count=12)

    shape.box("output-oak-hub", (.55, -2.05, -2.05), (2.9, 2.05, 2.05), texture="#strippedoak", parent=output_pivot, group="output-oak")
    shape.box("output-hub-bronze-band", (2.45, -2.3, -2.3), (2.95, 2.3, 2.3), texture="#bronze", parent=output_pivot, group="output-bronze")
    count = len(pawl_angles)
    angle_offset = pawl_angles[0]
    _spokes(shape, output_pivot, "output-oak-pawl-carrier", x_range=(.75, 2.35), inner=1.3, outer=5.2, width=1.05, count=count, group="output-oak", angle_offset=angle_offset)
    _spokes(shape, output_pivot, "output-carrier-bronze-inlay", x_range=(2.05, 2.42), inner=1.55, outer=5.05, width=.32, count=count, texture="#bronze", group="output-bronze", angle_offset=angle_offset)

    pawls = tuple(
        (
            _pawl(
                shape,
                output_pivot,
                f"orbiting-pawl-{index + 1}",
                mount_angle=angle,
                mount_radius=5.15,
                x_range=(-.2, 2.25),
                width=.92,
                **latch_config,
            ),
            pawl_phases[index],
        )
        for index, angle in enumerate(pawl_angles)
    )
    _animations(shape, input_pivot, output_pivot, pawls)
    return shape


def _three_pawl_spider(candidate: str, state: str) -> Shape:
    if state not in {"rest", "interface", "output-side"}:
        raise ValueError(f"unknown review state: {state}")
    config = CARRIER_CONFIGS[candidate]
    shape = _shape(f"overrunning-coupling-{candidate}-{state}")
    input_pivot = shape.pivot("input-rotor", (8, 8, 8), group="input")
    output_pivot = shape.pivot("output-rotor", (8, 8, 8), group="output")
    if state == "rest":
        _bearing_stand(shape, length=16, inner_clearance=(3.8, 12.2))
        _axles(shape, input_pivot, output_pivot, half_length=8, hub_gap=2.75)

    if state != "output-side":
        shape.box("input-oak-hub", (-2.9, -2.05, -2.05), (-.55, 2.05, 2.05), texture="#strippedoak", parent=input_pivot, group="input-oak")
        shape.box("input-hub-bronze-band", (-2.95, -2.3, -2.3), (-2.45, 2.3, 2.3), texture="#bronze", parent=input_pivot, group="ratchet-bronze")
        _spokes(shape, input_pivot, "input-oak-wheel-spoke", x_range=(-2.35, -.75), inner=1.35, outer=4.65, width=1.1, count=6, group="input-oak")
        _ring(shape, input_pivot, "input-oak-ratchet-wheel", x_range=(-2.35, -.75), radius=4.5, thickness=1.05, segment_length=2.9, count=12, texture="#strippedoak", group="input-oak", angle_offset=15)
        _ring(shape, input_pivot, "input-thin-bronze-tire", x_range=(-2.5, -2.1), radius=4.65, thickness=.45, segment_length=2.75, count=12, texture="#bronze", group="ratchet-bronze", angle_offset=15)
        _teeth(shape, input_pivot, "input-replaceable-ratchet-tooth", x_range=(-2.6, -.5), radius=4.85, tooth_length=.75, tooth_width=.92, count=12)

    shape.box("output-oak-hub", (.55, -2.05, -2.05), (2.9, 2.05, 2.05), texture="#strippedoak", parent=output_pivot, group="output-oak")
    shape.box("output-hub-bronze-band", (2.45, -2.3, -2.3), (2.95, 2.3, 2.3), texture="#bronze", parent=output_pivot, group="output-bronze")
    pawl_angles = (0, 120, 240)
    pawl_phases = (0, 1 / 3, 2 / 3)
    _spokes(shape, output_pivot, "output-oak-overarm", x_range=config["arm_x"], inner=1.3, outer=config["arm_outer"], width=1.02, count=3, group="output-oak")
    for index, angle in enumerate(pawl_angles, 1):
        _outer_arm_joint(shape, output_pivot, angle=angle, index=index)

    pawls = tuple(
        (
            _pawl_joint(shape, output_pivot, f"orbiting-pawl-{index + 1}", mount_angle=angle, style=config["style"]),
            pawl_phases[index],
        )
        for index, angle in enumerate(pawl_angles)
    )
    _animations(shape, input_pivot, output_pivot, pawls)
    return shape


def _four_sector_input(shape: Shape, input_pivot: ElementRef, config: dict[str, float | int | str]) -> None:
    tooth_count = int(config["teeth"])
    if tooth_count not in {4, 8}:
        raise ValueError("the separated four-arm gear requires four or eight contacts")
    gear_style = str(config["gear"])
    gear_radius = 4.08
    sector_width = float(config["sector_width"])

    if gear_style == "corner-frame":
        sector_angles = (0.0, 90.0, 180.0, 270.0)
        sector_radial = (3.05, 4.15)
        sector_width = 5.10
    else:
        sector_angles = (22.5, 112.5, 202.5, 292.5) if gear_style == "open-sectors" else (0.0, 90.0, 180.0, 270.0)
        sector_radial = (3.30, 4.30)
    shape.box("input-solid-brass-hub", (-2.58, -1.52, -1.52), (-.58, 1.52, 1.52), texture="#brass", parent=input_pivot, group="input-brass")
    shape.box("input-hub-raised-collar", (-2.68, -1.80, -1.80), (-2.30, 1.80, 1.80), texture="#brass", parent=input_pivot, group="input-brass")
    _spokes(
        shape,
        input_pivot,
        "input-four-arm-gear-spoke",
        x_range=(-2.24, -.64),
        inner=1.22,
        outer=3.58 if gear_style == "corner-frame" else gear_radius + .04,
        width=1.44 if gear_style != "boxed-cross" else 1.66,
        count=4,
        texture="#brass",
        group="input-brass",
        angle_offset=22.5 if gear_style == "open-sectors" else 0,
    )
    for index, angle in enumerate(sector_angles):
        _radial_box(
            shape,
            input_pivot,
            f"input-separated-brass-sector-{index:02d}",
            (-2.30, -.56),
            sector_radial,
            sector_width,
            angle,
            texture="#brass",
            group="input-brass",
        )
        _radial_box(
            shape,
            input_pivot,
            f"input-sector-spine-{index:02d}",
            (-2.43, -2.29),
            ((2.10, 4.08) if gear_style == "corner-frame" else (1.72, gear_radius + .04)),
            .66 if gear_style == "open-sectors" else .84,
            angle,
            texture="#brass",
            group="gear-reinforcement",
        )
        if gear_style == "boxed-cross":
            for edge_index, tangent in enumerate((-1, 1)):
                _radial_box(
                    shape,
                    input_pivot,
                    f"input-sector-box-brace-{index:02d}-{edge_index}",
                    (-.55, -.41),
                    (2.10, gear_radius + .06),
                    .42,
                    angle + tangent * 11.5,
                    texture="#brass",
                    group="gear-reinforcement",
                )

    # Four square corner blocks and four half-buried diamonds share an exact
    # 45-degree pitch. The embedded half of each diamond reads as a triangle.
    point_angles = tuple(index * 45.0 for index in range(8)) if gear_style == "open-sectors" else (0.0, 90.0, 180.0, 270.0)
    for index, angle in enumerate(point_angles):
        radians = math.radians(angle)
        tooth_mount = shape.pivot(
            f"input-point-tooth-{index:02d}-mount",
            (0, math.cos(radians) * (gear_radius + .18), math.sin(radians) * (gear_radius + .18)),
            parent=input_pivot,
            group="input-teeth",
        )
        point_size = 1.18 if gear_style == "boxed-cross" else (.76 if gear_style == "open-sectors" else .98)
        shape.box(
            f"input-point-tooth-{index:02d}",
            (-2.38, -point_size / 2, -point_size / 2),
            (-.48, point_size / 2, point_size / 2),
            texture="#brass",
            rotation_origin=(0, 0, 0),
            rotation=(angle + 45, 0, 0),
            parent=tooth_mount,
            group="input-teeth",
        )
    if gear_style == "corner-frame":
        for index, angle in enumerate((45.0, 135.0, 225.0, 315.0)):
            _radial_box(
                shape,
                input_pivot,
                f"input-corner-tooth-{index:02d}",
                (-2.38, -.48),
                (3.95, 4.82),
                .82,
                angle,
                texture="#brass",
                group="input-teeth",
            )
    if gear_style == "corner-frame":
        for index, angle in enumerate((45.0, 135.0, 225.0, 315.0)):
            _radial_box(
                shape,
                input_pivot,
                f"input-corner-gusset-{index:02d}",
                (-2.42, -2.28),
                (2.40, 3.72),
                .54,
                angle,
                texture="#brass",
                group="gear-reinforcement",
            )


def _balanced_input_gear(shape: Shape, input_pivot: ElementRef, config: dict[str, float | int | str]) -> None:
    tooth_count = int(config["teeth"])
    if tooth_count % 3:
        raise ValueError("a three-pawl balanced gear needs a tooth count divisible by three")
    gear_style = str(config["gear"])
    if gear_style not in {"stepped-ratchet", "deep-pocket-ring", "swept-concept"}:
        raise ValueError(f"unknown balanced input gear style: {gear_style}")

    tooth_pitch = 360 / tooth_count
    tooth_angle_offset = float(config["tooth_angle_offset"])
    if gear_style == "stepped-ratchet":
        ring_radius, ring_thickness, segment_length, tire_segment_scale = 4.16, .62, 1.94, 1.04
    elif gear_style == "deep-pocket-ring":
        ring_radius, ring_thickness, segment_length, tire_segment_scale = 4.04, .76, 1.92, .96
    else:
        ring_radius, ring_thickness, segment_length, tire_segment_scale = 3.96, .68, 2.28, .96

    shape.box(
        "input-oak-ring-hub",
        (-2.54, -1.38, -1.38),
        (-.60, 1.38, 1.38),
        texture="#strippedoak",
        parent=input_pivot,
        group="input-oak",
    )
    shape.box(
        "input-oak-hub-brass-band",
        (-2.66, -1.65, -1.65),
        (-2.34, 1.65, 1.65),
        texture="#brass",
        parent=input_pivot,
        group="input-brass",
    )
    _spokes(
        shape,
        input_pivot,
        "input-oak-ring-spoke",
        x_range=(-2.18, -.70),
        inner=1.10,
        outer=ring_radius - .12,
        width=1.20,
        count=int(config["gear_supports"]),
        texture="#strippedoak",
        group="input-oak",
    )
    _ring(
        shape,
        input_pivot,
        "input-locking-oak-ring",
        x_range=(-2.20, -.66),
        radius=ring_radius,
        thickness=ring_thickness,
        segment_length=segment_length,
        count=tooth_count,
        texture="#strippedoak",
        group="input-oak",
        angle_offset=tooth_angle_offset + tooth_pitch / 2,
    )
    if gear_style != "deep-pocket-ring":
        _ring(
            shape,
            input_pivot,
            "input-thin-brass-tire",
            x_range=(-2.34, -2.18),
            radius=ring_radius + ring_thickness / 2 + .06,
            thickness=.18,
            segment_length=segment_length * tire_segment_scale,
            count=tooth_count,
            texture="#brass",
            group="gear-reinforcement",
            angle_offset=tooth_angle_offset + tooth_pitch / 2,
        )

    def tooth_box(
        name: str,
        index: int,
        radial: tuple[float, float],
        tangent: tuple[float, float],
        *,
        texture: str,
        group: str,
    ) -> None:
        angle = tooth_angle_offset + index * tooth_pitch
        shape.box(
            f"{name}-{index:02d}",
            (-2.38, radial[0], tangent[0]),
            (-.48, radial[1], tangent[1]),
            texture=texture,
            rotation_origin=(0, 0, 0),
            rotation=(angle, 0, 0),
            parent=input_pivot,
            group=group,
        )

    def sloped_tooth_box(
        name: str,
        index: int,
        *,
        center_radius: float,
        tangent_offset: float,
        length: float,
        thickness: float,
        slope: float,
        texture: str,
        group: str,
    ) -> None:
        angle = tooth_angle_offset + index * tooth_pitch
        radians = math.radians(angle)
        mount = shape.pivot(
            f"{name}-{index:02d}-mount",
            (
                0,
                math.cos(radians) * center_radius - math.sin(radians) * tangent_offset,
                math.sin(radians) * center_radius + math.cos(radians) * tangent_offset,
            ),
            parent=input_pivot,
            group=group,
        )
        shape.box(
            f"{name}-{index:02d}",
            (-2.38, -length / 2, -thickness / 2),
            (-.48, length / 2, thickness / 2),
            texture=texture,
            rotation_origin=(0, 0, 0),
            rotation=(angle + slope, 0, 0),
            parent=mount,
            group=group,
        )

    for index in range(tooth_count):
        if gear_style == "stepped-ratchet":
            sloped_tooth_box(
                "input-smooth-ramp",
                index,
                center_radius=4.55,
                tangent_offset=-.76,
                length=1.15,
                thickness=.40,
                slope=55,
                texture="#brass",
                group="input-teeth",
            )
            tooth_box("input-ratchet-lock-face", index, (4.18, 4.98), (-.34, 0), texture="#brass", group="input-teeth")
        elif gear_style == "deep-pocket-ring":
            tooth_box("input-pocket-lug-oak", index, (4.02, 4.78), (-1.08, -.14), texture="#strippedoak", group="input-oak")
            sloped_tooth_box(
                "input-pocket-ramp-wear",
                index,
                center_radius=4.50,
                tangent_offset=-.70,
                length=1.37,
                thickness=.16,
                slope=54,
                texture="#brass",
                group="input-teeth",
            )
            tooth_box("input-ratchet-lock-face", index, (4.02, 4.94), (-.14, 0), texture="#brass", group="input-teeth")
        else:
            angle = tooth_angle_offset + index * tooth_pitch
            radians = math.radians(angle)
            center_radius = 4.55
            tangent_offset = -.66
            mount = shape.pivot(
                f"input-concept-swept-point-{index:02d}-mount",
                (
                    0,
                    math.cos(radians) * center_radius - math.sin(radians) * tangent_offset,
                    math.sin(radians) * center_radius + math.cos(radians) * tangent_offset,
                ),
                parent=input_pivot,
                group="input-teeth",
            )
            shape.box(
                f"input-concept-swept-point-{index:02d}",
                (-2.38, -.42, -.42),
                (-.48, .42, .42),
                texture="#brass",
                rotation_origin=(0, 0, 0),
                rotation=(angle + 45, 0, 0),
                parent=mount,
                group="input-teeth",
            )
            tooth_box("input-ratchet-lock-face", index, (4.08, 5.08), (-.24, 0), texture="#brass", group="input-teeth")


def _large_orbital_pawl(
    shape: Shape,
    output_pivot: ElementRef,
    *,
    index: int,
    angle: float,
    config: dict[str, float | int | str],
) -> tuple[ElementRef, ElementRef]:
    orbit_radius = float(config["orbit_radius"])
    pivot_lead = float(config["pivot_lead"])
    radians = math.radians(angle)
    mount = shape.pivot(
        f"orbiting-pawl-{index}-mount",
        (
            0,
            math.cos(radians) * orbit_radius - math.sin(radians) * pivot_lead,
            math.sin(radians) * orbit_radius + math.cos(radians) * pivot_lead,
        ),
        parent=output_pivot,
        group="pawl-carrier",
    )
    pawl = shape.pivot(f"orbiting-pawl-{index}", (0, 0, 0), parent=mount, group="pawl")
    style = str(config["pawl"])

    if style == "restored-hook":
        pieces = (
            ("contact-stick", (-1.30, -1.08, -.62), (-.68, .30, -.34)),
            ("pivot-collar", (-1.42, -.38, -.42), (-.50, .32, .42)),
        )
    elif style == "drop-bar":
        pieces = (
            ("drop-bar", (-1.34, -1.44, -.76), (-.56, -.18, -.28)),
            ("locking-tip", (-1.48, -1.62, -.62), (-.42, -1.28, -.12)),
            ("pivot-block", (-1.38, -.30, -.36), (-.52, .30, .36)),
        )
    elif style == "concept-crook":
        pieces = (
            ("crook-stem", (-1.25, -1.62, -.78), (-.66, .20, -.28)),
            ("crook-elbow", (-1.40, -1.70, -.82), (-.52, -1.34, .06)),
            ("locking-tip", (-1.48, -1.62, -.62), (-.44, -1.22, .06)),
            ("pivot-block", (-1.34, -.24, -.38), (-.56, .34, .38)),
        )
    else:
        raise ValueError(f"unknown orbital pawl style: {style}")
    for suffix, lower, upper in pieces:
        shape.box(
            f"orbiting-pawl-{index}-{suffix}",
            lower,
            upper,
            texture="#bronze",
            rotation_origin=(0, 0, 0),
            rotation=(angle, 0, 0),
            parent=pawl,
            group="pawl",
        )

    spring_anchor_y = .32
    spring_anchor_z = .12
    spring = shape.pivot(
        f"orbiting-pawl-{index}-spring-flex",
        (
            0,
            math.cos(radians) * spring_anchor_y - math.sin(radians) * spring_anchor_z,
            math.sin(radians) * spring_anchor_y + math.cos(radians) * spring_anchor_z,
        ),
        parent=mount,
        group="pawl-spring",
    )
    shape.box(
        f"orbiting-pawl-{index}-spring-follower-cap",
        (-1.28, .22, 0),
        (-.88, .58, .24),
        texture="#bronze",
        rotation_origin=(0, 0, 0),
        rotation=(angle, 0, 0),
        parent=pawl,
        group="pawl-spring",
    )
    shape.box(
        f"orbiting-pawl-{index}-leaf-spring-back",
        (-1.18, -.78, -.34),
        (-.98, 0, -.10),
        texture="#bronze",
        rotation_origin=(0, 0, 0),
        rotation=(angle, 0, 0),
        parent=spring,
        group="pawl-spring",
    )
    shape.box(
        f"orbiting-pawl-{index}-leaf-spring-tip",
        (-1.20, -.98, -.56),
        (-.96, -.74, -.32),
        texture="#bronze",
        rotation_origin=(0, 0, 0),
        rotation=(angle, 0, 0),
        parent=spring,
        group="pawl-spring",
    )
    shape.box(
        f"orbiting-pawl-{index}-large-pin",
        (-1.50, -.24, -.24),
        (.54, .24, .24),
        texture="#bronze",
        parent=mount,
        group="fasteners",
    )
    for side, x_range in (("gear", (-1.62, -1.48)), ("carrier", (.52, .66))):
        shape.box(
            f"orbiting-pawl-{index}-pin-head-{side}",
            (x_range[0], -.36, -.36),
            (x_range[1], .36, .36),
            texture="#bronze",
            parent=mount,
            group="fasteners",
        )
    return pawl, spring


def _three_arm_orbital_output(
    shape: Shape,
    output_pivot: ElementRef,
    config: dict[str, float | int | str],
) -> tuple[tuple[ElementRef, ElementRef, float], ...]:
    arm_width = float(config["arm_width"])
    orbit_radius = float(config["orbit_radius"])
    pawl_angles = (0.0, 120.0, 240.0)
    shape.box(
        "output-oak-hub",
        (.55, -1.56, -1.56),
        (2.90, 1.56, 1.56),
        texture="#strippedoak",
        parent=output_pivot,
        group="output-oak",
    )
    shape.box(
        "output-hub-bronze-band",
        (2.45, -1.84, -1.84),
        (2.95, 1.84, 1.84),
        texture="#bronze",
        parent=output_pivot,
        group="output-bronze",
    )
    _spokes(
        shape,
        output_pivot,
        "output-broad-orbital-arm",
        x_range=(.72, 2.30),
        inner=1.30,
        outer=orbit_radius - .58,
        width=arm_width,
        count=3,
        group="output-oak",
    )

    for index, angle in enumerate(pawl_angles, 1):
        head_radial = (orbit_radius - .78, orbit_radius + .24)
        _radial_box(
            shape,
            output_pivot,
            f"output-gearward-head-{index:02d}",
            (.34, 2.32),
            head_radial,
            arm_width,
            angle,
            texture="#strippedoak",
            group="output-oak",
        )
        _radial_box(
            shape,
            output_pivot,
            f"output-reinforced-head-plate-{index:02d}-carrier",
            (2.29, 2.45),
            head_radial,
            arm_width,
            angle,
            texture="#bronze",
            group="output-bronze",
        )
        _radial_box(
            shape,
            output_pivot,
            f"output-gearward-neck-{index:02d}",
            (-.40, .48),
            (orbit_radius - .38, orbit_radius + .20),
            arm_width * .54,
            angle,
            texture="#bronze",
            group="output-bronze",
        )
        bolt_radius = orbit_radius - .08
        _radial_box(
            shape,
            output_pivot,
            f"output-head-clamp-bolt-{index:02d}",
            (.22, 2.53),
            (bolt_radius - .16, bolt_radius + .16),
            .42,
            angle,
            texture="#bronze",
            group="fasteners",
        )
        for side, x_range in (("gear", (.10, .24)), ("carrier", (2.51, 2.63))):
            _radial_box(
                shape,
                output_pivot,
                f"output-head-clamp-nut-{index:02d}-{side}",
                x_range,
                (bolt_radius - .27, bolt_radius + .27),
                .58,
                angle,
                texture="#bronze",
                group="fasteners",
            )
        _radial_box(
            shape,
            output_pivot,
            f"output-arm-root-brace-{index:02d}",
            (2.04, 2.43),
            (1.42, 2.58),
            arm_width,
            angle,
            texture="#bronze",
            group="output-bronze",
        )

    tooth_pitch = 360 / int(config["teeth"])
    pawls = []
    for index, angle in enumerate(pawl_angles, 1):
        pawl, spring = _large_orbital_pawl(
            shape,
            output_pivot,
            index=index,
            angle=angle,
            config=config,
        )
        pawls.append((pawl, spring, (angle / tooth_pitch) % 1))
    return tuple(pawls)


def _orbital_animations(
    shape: Shape,
    input_pivot: ElementRef,
    output_pivot: ElementRef,
    pawls: tuple[tuple[ElementRef, ElementRef, float], ...],
    config: dict[str, float | int | str],
) -> None:
    drive = animate(shape, "Input catches and drives output", "drive", 49, version=1, on_animation_end="Hold")
    approach = float(config["drive_approach"])
    for frame, input_rotation, output_rotation in (
        (0, -approach, 0),
        (10, 0, 0),
        (48, 240, 240),
    ):
        drive.keyframe(frame, input_pivot, rotationX=input_rotation, rotShortestDistanceX=False)
        drive.keyframe(frame, output_pivot, rotationX=output_rotation, rotShortestDistanceX=False)
    shape.add_animation(drive.build())

    tooth_pitch = 360 / int(config["teeth"])
    pawl_lift = float(config["lift"])
    ramp_start = float(config["ramp_start"])
    ramp_full = float(config.get("ramp_full", .80))
    drop_start = float(config.get("drop_start", .985))

    def lift_for_cycle(cycle: float) -> float:
        if cycle < ramp_start:
            return 0.0
        if cycle < ramp_full:
            return pawl_lift * (cycle - ramp_start) / (ramp_full - ramp_start)
        if cycle < drop_start:
            return pawl_lift
        return max(0.0, pawl_lift * (1 - cycle) / (1 - drop_start))

    overrun = animate(shape, "Output overruns stopped input", "overrun", 73, version=1, on_animation_end="Repeat")
    for frame in range(73):
        output_rotation = frame * 5
        overrun.keyframe(frame, input_pivot, rotationX=0, rotShortestDistanceX=False)
        overrun.keyframe(frame, output_pivot, rotationX=output_rotation, rotShortestDistanceX=False)
        tooth_cycles = output_rotation / tooth_pitch
        for pawl, spring, phase in pawls:
            lift = lift_for_cycle((tooth_cycles + phase) % 1)
            overrun.keyframe(frame, pawl, rotationX=lift, rotShortestDistanceX=True)
            overrun.keyframe(frame, spring, rotationX=lift * .35, rotShortestDistanceX=True)
    shape.add_animation(overrun.build())

    lift_start_frame = 7
    peak_frame = 23
    tooth_end_frame = 24
    drop_start_frame = 25
    neutral_frame = 28

    def contact_lift_for_frame(frame: int) -> float:
        if frame <= lift_start_frame:
            return 0.0
        if frame <= peak_frame:
            progress = (frame - lift_start_frame) / (peak_frame - lift_start_frame)
            return pawl_lift * progress ** 1.5
        if frame <= drop_start_frame:
            return pawl_lift
        progress = (frame - drop_start_frame) / (neutral_frame - drop_start_frame)
        eased = progress * progress * (3 - 2 * progress)
        return pawl_lift * (1 - eased)

    contact_cycle = animate(
        shape,
        "One tooth passes a stationary pawl",
        "contactcycle",
        neutral_frame + 1,
        version=1,
        on_animation_end="Repeat",
    )
    for frame in range(neutral_frame + 1):
        tooth_progress = min(frame, tooth_end_frame) / tooth_end_frame
        contact_cycle.keyframe(
            frame,
            input_pivot,
            rotationX=-tooth_pitch * tooth_progress,
            rotShortestDistanceX=False,
        )
        contact_cycle.keyframe(frame, output_pivot, rotationX=0, rotShortestDistanceX=False)
        lift = contact_lift_for_frame(frame)
        for pawl, spring, _phase in pawls:
            contact_cycle.keyframe(frame, pawl, rotationX=lift, rotShortestDistanceX=True)
            contact_cycle.keyframe(frame, spring, rotationX=lift * .35, rotShortestDistanceX=True)
    shape.add_animation(contact_cycle.build())


def _orbital_candidate(candidate: str, state: str) -> Shape:
    if state not in {"rest", "interface", "input-side", "output-side"}:
        raise ValueError(f"unknown review state: {state}")
    config = ORBITAL_CONFIGS[candidate]
    shape = _shape(f"overrunning-coupling-{candidate}-{state}")
    input_pivot = shape.pivot("input-rotor", (8, 8, 8), group="input")
    output_pivot = shape.pivot("output-rotor", (8, 8, 8), group="output")
    if state == "rest":
        _bearing_stand(shape, length=16, inner_clearance=(3.8, 12.2))
        _axles(shape, input_pivot, output_pivot, half_length=8, hub_gap=2.75)
    if state != "output-side":
        _balanced_input_gear(shape, input_pivot, config)
    pawls: tuple[tuple[ElementRef, ElementRef, float], ...] = ()
    if state != "input-side":
        pawls = _three_arm_orbital_output(shape, output_pivot, config)
    _orbital_animations(shape, input_pivot, output_pivot, pawls, config)
    return shape


def build_review(root: Path, *, approved_candidate: str | None = None) -> Path:
    root = root.resolve()
    review_root = (root / REVIEW_ROOT).resolve()
    managed, staging, backup = (review_root / name for name in (MANAGED_NAME, STAGING_NAME, BACKUP_NAME))
    if not all(_inside(root, path) for path in (review_root, managed, staging, backup)):
        raise ValueError("review output must remain inside the project")
    review_root.mkdir(parents=True, exist_ok=True)
    for transient in (staging, backup):
        if transient.exists():
            shutil.rmtree(transient)

    candidates = OrderedDict(
        (candidate, OrderedDict((state, _orbital_candidate(candidate, state)) for state in ("rest", "interface", "input-side", "output-side")))
        for candidate in DESCRIPTIONS
    )
    if approved_candidate is not None and approved_candidate not in candidates:
        raise ValueError(f"unknown approved candidate: {approved_candidate}")
    selected = candidates if approved_candidate is None else OrderedDict(((approved_candidate, candidates[approved_candidate]),))

    package = ModelPackage("overrunning_coupling_review")
    for candidate, states in selected.items():
        for state, shape in states.items():
            package.shape(shape, f"{REVIEW_ROOT.as_posix()}/{STAGING_NAME}/{candidate}/{state}.shape.json")
    build_package(package, root, write=True)

    readme = [
        "# Overrunning coupling review",
        "",
        "Review-only candidates for an automatic transmission that couples only while its designated input shaft is faster in the allowed direction.",
        "The output may overrun a slow, stopped, or reversed input without back-driving it. The two shafts remain separate Vanilla mechanical networks.",
        "",
        "Material tier: oak carries every hub, four-spoke wheel, carrier arm, and broad low-stress rim. Brass is limited to the input tire, inclined wear faces, and replaceable locking shoulders; B uses reinforced-oak lug bodies beneath especially thin brass plates. Tin bronze remains the cheapest adequate material for the impact-loaded pawls, pins, clamps, and leaf springs. Copper is too soft and iron is unnecessary at this scale.",
        "Brass input faces mate with tin-bronze pawls, never brass against brass. Every working face remains visible and replaceable.",
        "",
        f"Decision: `{'open' if approved_candidate is None else approved_candidate + ' approved'}`.",
        "The approved A2 frame, support spacing, shafts, one-block footprint, four wooden input supports, and three equal-width orbital arms remain unchanged. This round replaces both sides of the working interface: A uses fifteen smooth ramps with square shoulders, B uses twelve deep pockets with thin glide plates, and C follows the supplied nine-point hook-and-ratchet sketch.",
        "The circled clevis side plates are removed. Each pawl pin now passes directly through a compact bronze neck in the reinforced wooden arm head, leaving the locking face unobstructed from either side.",
        "",
        "Every candidate has four reviewer states: `rest` shows the complete one-block assembly, `interface` isolates both rotors and their contact, `input-side` isolates the input gear, and `output-side` isolates the three-arm carrier and pawls.",
        "Each state includes `drive`, `overrun`, and `contactcycle` animations. In `drive`, the input begins in a real gap, advances alone to the flat locking shoulder at frame 10, then carries the output through 240 degrees. The initial approach is 8 degrees for A, 10 for B, and 14 for C so each different tooth pitch starts clear.",
        "In `overrun`, the input stops while the output turns 360 degrees. Every tooth count is divisible by three, so all three pawls remain equally phased. Each pawl stays seated through the open pocket, climbs only the rising back of the next tooth, and snaps down immediately behind its locking shoulder.",
        "The 25-frame `contactcycle` subtracts the shared wheel rotation: one pawl remains stationary while a complete tooth pitch passes beneath it, exposing the inclined climb, maximum clearance, and drop into the next pocket. These review animations are deterministic clearance checks. The in-game renderer will derive both rotor angles and all three synchronized pawl lifts from the two live mechanical-network angles.",
        "Inspect `interface` from both sides. Scrub `drive` through frames 0, 10, and 11 for gap, contact, and carried motion; then scrub `contactcycle` from frame 0 through 24 to check the complete ramp climb and release.",
        "",
    ]
    readme.extend(f"- `{candidate}` - {DESCRIPTIONS[candidate]} Footprint: {FOOTPRINTS[candidate]['length']}x{FOOTPRINTS[candidate]['height']}x{FOOTPRINTS[candidate]['depth']} blocks." for candidate in selected)
    (staging / "README.md").write_text("\n".join([*readme, ""]), encoding="utf-8")

    ownership = OrderedDict((
        ("version", 1),
        ("purpose", "visual approval for an automatic overrunning coupling"),
        ("source", "graphics/review/overrunning_coupling.py"),
        ("managedPath", f"{REVIEW_ROOT.as_posix()}/{MANAGED_NAME}"),
        ("footprintsBlocks", OrderedDict((candidate, FOOTPRINTS[candidate]) for candidate in selected)),
        ("candidates", list(selected)),
        ("states", ["rest", "interface", "input-side", "output-side"]),
        ("animations", ["drive", "overrun", "contactcycle"]),
        ("decision", OrderedDict((
            ("status", "open" if approved_candidate is None else "approved"),
            ("candidate", approved_candidate),
            ("runtimePromotion", True),
        ))),
    ))
    (staging / ".gearwright-review.json").write_bytes(json_bytes(ownership))

    if managed.exists():
        managed.replace(backup)
    staging.replace(managed)
    if backup.exists():
        shutil.rmtree(backup)
    return managed


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Build the managed overrunning-coupling review package.")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--approved", choices=tuple(DESCRIPTIONS), default=None)
    args = parser.parse_args(argv)
    managed = build_review(args.root, approved_candidate=args.approved)
    count = len(DESCRIPTIONS) if args.approved is None else 1
    print(f"Built {count} overrunning-coupling candidate(s) in {managed.relative_to(args.root.resolve()).as_posix()}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
