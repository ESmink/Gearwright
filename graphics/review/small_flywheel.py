"""Build the managed review package for the three-block small flywheel."""

from __future__ import annotations

import argparse
import math
import shutil
from collections import OrderedDict
from pathlib import Path

from gearwright_graphics.compiler import build_package, json_bytes
from gearwright_graphics.model import ElementRef, Face, ModelPackage, Shape, Vec3, animate


REVIEW_ROOT = Path("generated/small-flywheel-review")
MANAGED_NAME = "current"
STAGING_NAME = ".current-build"
BACKUP_NAME = ".current-old"

OAK = "game:block/wood/planks/oak1"
STRIPPED_OAK = "survival:block/wood/debarked/oak"
OAK_END = "survival:block/wood/treetrunk/debarked/oak"
STONE = "game:block/stone/rock/granite1"
BRONZE = "game:block/metal/sheet/tinbronze1"
IRON = "survival:block/metal/sheet-plain/iron2"
WHEEL_X = 24
WHEEL_Y = 27
WHEEL_Z = 8
WHEEL_ORIGIN = Vec3(WHEEL_X, WHEEL_Y, WHEEL_Z)

DESCRIPTIONS = OrderedDict((
    (
        "model-e1-laminated-saddle",
        "Bronze arms enter wraparound sockets at each deeper stone. Full through-shanks connect both face straps, the "
        "side cheeks, stone, and arm. Enlarged oak supports bury their complete top faces in the bearing block.",
    ),
    (
        "model-e2-timber-wheel",
        "Square stripped-oak arms enter the same wraparound sockets and through-bolts. Enlarged stripped-oak supports "
        "bury their complete top faces in the compact wood-aligned bearing block while keeping E2 timber-led.",
    ),
    (
        "model-e3-iron-four-way-hub",
        "Four lean oak arms meet in a vanilla-inspired light four-way socket hub. Iron Y plates follow every timber "
        "fork, paired gussets reinforce all eight bends, and wraparound sockets fasten the long stones.",
    ),
    (
        "model-e4-iron-eight-way-frame",
        "Eight straight stripped-oak arms run from the shaft to the long stones. A large octagonal iron cage expands "
        "the water-wheel hub's socket construction into a full eight-way frame, with a narrow iron strap continuing "
        "along every unbent timber arm.",
    ),
))


def _inside(root: Path, path: Path) -> bool:
    try:
        path.resolve().relative_to(root.resolve())
        return True
    except ValueError:
        return False


class _FlywheelShape(Shape):
    """Author wheel pieces in the review canvas, emit engine-local children."""

    def box(self, name, from_, to, **kwargs):
        parent = kwargs.get("parent")
        if parent is not None and parent.name == "wheel":
            from_ = (Vec3.of(from_) - WHEEL_ORIGIN).values()
            to = (Vec3.of(to) - WHEEL_ORIGIN).values()
            rotation_origin = kwargs.get("rotation_origin")
            if rotation_origin is not None:
                kwargs["rotation_origin"] = (
                    Vec3.of(rotation_origin) - WHEEL_ORIGIN
                ).values()
        return super().box(name, from_, to, **kwargs)


def _base(shape_id: str) -> tuple[Shape, ElementRef]:
    shape = _FlywheelShape(shape_id, 16, 16)
    shape.texture("oak", OAK)
    shape.texture("strippedoak", STRIPPED_OAK)
    shape.texture("oakend", OAK_END)
    shape.texture("stone", STONE)
    shape.texture("bronze", BRONZE)
    shape.texture("iron", IRON)
    wheel = shape.pivot("wheel", (WHEEL_X, WHEEL_Y, WHEEL_Z), group="wheel")
    return shape, wheel


def _log_faces(axis: str) -> OrderedDict[str, Face]:
    ends = {
        "x": {"east", "west"},
        "y": {"up", "down"},
        "z": {"north", "south"},
    }[axis]
    return OrderedDict(
        (face, Face("#oakend" if face in ends else "#strippedoak"))
        for face in ("north", "east", "south", "west", "up", "down")
    )


def _paired_box(
    shape: Shape,
    name: str,
    from_xy: tuple[float, float],
    to_xy: tuple[float, float],
    *,
    texture: str = "#oak",
    depth_inset: float = 0,
) -> None:
    for face, z_range in (
        ("back", (.8 + depth_inset, 4.05 - depth_inset)),
        ("front", (11.95 + depth_inset, 15.2 - depth_inset)),
    ):
        shape.box(
            f"{name}-{face}",
            (from_xy[0], from_xy[1], z_range[0]),
            (to_xy[0], to_xy[1], z_range[1]),
            texture=texture,
            group="frame",
        )


def _paired_beam(
    shape: Shape,
    name: str,
    start: tuple[float, float],
    end: tuple[float, float],
    width: float,
    *,
    texture: str = "#oak",
    group: str = "frame",
    depth_inset: float = 0,
    log: bool = False,
) -> None:
    length = math.dist(start, end)
    angle = math.degrees(math.atan2(end[1] - start[1], end[0] - start[0]))
    z_ranges = (
        ("back", (.8 + depth_inset, 3.25 - depth_inset)),
        ("front", (12.75 + depth_inset, 15.2 - depth_inset)),
    )
    for face, z_range in z_ranges:
        kwargs = {"faces": _log_faces("x")} if log else {"texture": texture}
        shape.box(
            f"{name}-{face}",
            (start[0], start[1] - width / 2, z_range[0]),
            (start[0] + length, start[1] + width / 2, z_range[1]),
            rotation_origin=(start[0], start[1], sum(z_range) / 2),
            rotation=(0, 0, angle),
            group=group,
            **kwargs,
        )


def _face_box(
    shape: Shape,
    name: str,
    from_xy: tuple[float, float],
    to_xy: tuple[float, float],
    *,
    texture: str = "#bronze",
) -> None:
    for face, z_range in (("back", (.2, .75)), ("front", (15.25, 15.8))):
        shape.box(
            f"{name}-{face}",
            (from_xy[0], from_xy[1], z_range[0]),
            (to_xy[0], to_xy[1], z_range[1]),
            texture=texture,
            group="frame-bronze",
        )


def _paired_log_box(
    shape: Shape,
    name: str,
    from_xy: tuple[float, float],
    to_xy: tuple[float, float],
    *,
    inward_depth: float | None = None,
) -> None:
    log_size = inward_depth if inward_depth is not None else to_xy[1] - from_xy[1]
    for face, z_range in (("back", (.8, .8 + log_size)), ("front", (15.2 - log_size, 15.2))):
        shape.box(
            f"{name}-{face}",
            (from_xy[0], from_xy[1], z_range[0]),
            (to_xy[0], to_xy[1], z_range[1]),
            faces=_log_faces("x"),
            group="frame-log",
        )


def _outer_face_bolt(
    shape: Shape,
    name: str,
    x: float,
    y: float,
    *,
    size: float = 1.05,
    texture: str = "#bronze",
) -> None:
    for face, z_range in (("back", (.12, .68)), ("front", (15.32, 15.88))):
        shape.box(
            f"{name}-{face}",
            (x - size / 2, y - size / 2, z_range[0]),
            (x + size / 2, y + size / 2, z_range[1]),
            texture=texture,
            rotation_origin=(x, y, sum(z_range) / 2),
            rotation=(0, 0, 45),
            group="frame-iron" if texture == "#iron" else "frame-bronze",
        )


def _depth_log(shape: Shape, name: str, center_x: float) -> None:
    half_size = 1.625
    shape.box(
        name,
        (center_x - half_size, 3.275, 3.95),
        (center_x + half_size, 6.525, 12.05),
        faces=_log_faces("z"),
        group="frame-depth-support",
    )


def _bearing_inner_ring(shape: Shape, *, metal_texture: str = "#bronze") -> None:
    for face, z_range in (("back", (4.02, 4.57)), ("front", (11.43, 11.98))):
        for part, from_xy, to_xy in (
            ("left", (20.6, 24.2), (22.2, 29.8)),
            ("right", (25.8, 24.2), (27.4, 29.8)),
            ("bottom", (22.2, 24.2), (25.8, 25.5)),
            ("top", (22.2, 28.5), (25.8, 29.8)),
        ):
            shape.box(
                f"bearing-bushing-{part}-{face}",
                (from_xy[0], from_xy[1], z_range[0]),
                (to_xy[0], to_xy[1], z_range[1]),
                texture=metal_texture,
                group="frame-bearing",
            )


def _add_bearings(shape: Shape, style: str, *, metal_texture: str = "#bronze") -> None:
    _paired_box(shape, "bearing-block", (20.5, 23.2), (27.5, 30.8), depth_inset=.03)
    _bearing_inner_ring(shape, metal_texture=metal_texture)

    if style == "saddle":
        pieces = (
            ("left", (20.5, 23.2), (21.8, 30.8)),
            ("right", (26.2, 23.2), (27.5, 30.8)),
            ("top", (21.8, 29.5), (26.2, 30.8)),
            ("seat", (21.8, 23.2), (26.2, 24.5)),
        )
        bolts = ((21.15, 23.85), (26.85, 23.85), (21.15, 30.15), (26.85, 30.15))
    elif style == "timber":
        pieces = (
            ("top", (20.5, 29.35), (27.5, 30.8)),
            ("seat", (20.5, 23.2), (27.5, 24.65)),
        )
        bolts = ((21.4, 30.05), (26.6, 30.05), (21.4, 23.95), (26.6, 23.95))
    else:
        raise ValueError(f"unknown bearing style: {style}")

    for part, from_xy, to_xy in pieces:
        _face_box(shape, f"bearing-saddle-{part}", from_xy, to_xy, texture=metal_texture)
    for index, (x, y) in enumerate(bolts):
        _outer_face_bolt(
            shape,
            f"bearing-saddle-bolt-{index:02d}",
            x,
            y,
            size=.9,
            texture=metal_texture,
        )


def _laminated_log_frame(
    shape: Shape,
    *,
    bearing_style: str,
    timber_supports: bool,
    metal_texture: str = "#bronze",
) -> None:
    # Each front/rear foot is a laminated pair of stripped logs. The cradle beams
    # sink into the continuous upper log and are inset in depth to avoid coplanar faces.
    _paired_log_box(shape, "base-bottom-log", (0, 0), (48, 3.25), inward_depth=6.55)
    _paired_log_box(shape, "base-upper-log", (0, 3.25), (48, 6.55), inward_depth=6.55)

    support_width = 4.4 if timber_supports else 5
    left_end = (22.45, 27) if timber_supports else (22.65, 27)
    right_end = (25.55, 27) if timber_supports else (25.35, 27)
    _paired_beam(shape, "left-cradle", (8, 4.9), left_end, support_width, depth_inset=.05, log=timber_supports)
    _paired_beam(shape, "right-cradle", (40, 4.9), right_end, support_width, depth_inset=.05, log=timber_supports)

    for index, x in enumerate((14.5, 33.5)):
        _outer_face_bolt(
            shape,
            f"base-laminate-bolt-{index:02d}",
            x,
            4.9,
            size=1.2,
            texture=metal_texture,
        )

    # These two depth logs are seated inside the lower foot logs, so they tie the
    # front and rear support frames together without appearing to float.
    _depth_log(shape, "left-depth-log", 14.5)
    _depth_log(shape, "right-depth-log", 33.5)
    _add_bearings(shape, bearing_style, metal_texture=metal_texture)


def _segment(
    shape: Shape,
    parent: ElementRef,
    name: str,
    radius: float,
    angle: float,
    length: float,
    thickness: float,
    depth: tuple[float, float],
    texture: str,
    group: str,
) -> None:
    radians = math.radians(angle)
    center_x = WHEEL_X + radius * math.cos(radians)
    center_y = WHEEL_Y + radius * math.sin(radians)
    shape.box(
        name,
        (center_x - length / 2, center_y - thickness / 2, WHEEL_Z + depth[0]),
        (center_x + length / 2, center_y + thickness / 2, WHEEL_Z + depth[1]),
        texture=texture,
        rotation_origin=(center_x, center_y, WHEEL_Z + sum(depth) / 2),
        rotation=(0, 0, angle + 90),
        parent=parent,
        group=group,
    )


def _rim(
    shape: Shape,
    parent: ElementRef,
    prefix: str,
    *,
    radius: float,
    count: int,
    length: float,
    thickness: float,
    depth: tuple[float, float],
    texture: str = "#bronze",
    group: str = "wheel-bronze",
    angle_offset: float = 0,
) -> None:
    for index in range(count):
        _segment(
            shape,
            parent,
            f"{prefix}-{index:02d}",
            radius,
            angle_offset + index * 360 / count,
            length,
            thickness,
            depth,
            texture,
            group,
        )


def _hub(shape: Shape, parent: ElementRef, *, timber: bool) -> None:
    if timber:
        shape.box(
            "stripped-oak-hub",
            (WHEEL_X - 4, WHEEL_Y - 4, WHEEL_Z - 2.8),
            (WHEEL_X + 4, WHEEL_Y + 4, WHEEL_Z + 2.8),
            faces=_log_faces("z"),
            parent=parent,
            group="wheel-wood",
        )
        for face, z_range in (("back", (-3.35, -2.8)), ("front", (2.8, 3.35))):
            shape.box(
                f"timber-hub-collar-{face}",
                (WHEEL_X - 3.2, WHEEL_Y - 3.2, WHEEL_Z + z_range[0]),
                (WHEEL_X + 3.2, WHEEL_Y + 3.2, WHEEL_Z + z_range[1]),
                texture="#bronze",
                parent=parent,
                group="wheel-bronze",
            )
    else:
        shape.box(
            "bronze-hub-body",
            (WHEEL_X - 4, WHEEL_Y - 4, WHEEL_Z - 2.8),
            (WHEEL_X + 4, WHEEL_Y + 4, WHEEL_Z + 2.8),
            texture="#bronze",
            parent=parent,
            group="wheel-bronze",
        )
        for face, z_range in (("back", (-3.35, -2.8)), ("front", (2.8, 3.35))):
            shape.box(
                f"bronze-hub-flange-{face}",
                (WHEEL_X - 4.8, WHEEL_Y - 4.8, WHEEL_Z + z_range[0]),
                (WHEEL_X + 4.8, WHEEL_Y + 4.8, WHEEL_Z + z_range[1]),
                texture="#bronze",
                parent=parent,
                group="wheel-bronze",
            )

    # Match the vanilla wooden axle's compact plus-shaped cross section.
    shape.box(
        "wooden-axle-horizontal",
        (WHEEL_X - 2, WHEEL_Y - 1, 0),
        (WHEEL_X + 2, WHEEL_Y + 1, 16),
        texture="#strippedoak" if timber else "#oak",
        parent=parent,
        group="wheel-wood",
    )
    shape.box(
        "wooden-axle-vertical",
        (WHEEL_X - 1, WHEEL_Y - 2, 0),
        (WHEEL_X + 1, WHEEL_Y + 2, 16),
        texture="#strippedoak" if timber else "#oak",
        parent=parent,
        group="wheel-wood",
    )


def _spokes(shape: Shape, parent: ElementRef, *, count: int, width: float, timber: bool) -> None:
    for index in range(count):
        angle = index * 360 / count
        half_depth = width / 2 if timber else 2.1
        kwargs = {"faces": _log_faces("x")} if timber else {"texture": "#bronze"}
        shape.box(
            f"spoke-{index:02d}",
            (WHEEL_X + 3.3, WHEEL_Y - width / 2, WHEEL_Z - half_depth),
            (WHEEL_X + 14.2, WHEEL_Y + width / 2, WHEEL_Z + half_depth),
            rotation_origin=(WHEEL_X, WHEEL_Y, WHEEL_Z),
            rotation=(0, 0, angle),
            parent=parent,
            group="wheel-wood" if timber else "wheel-bronze",
            **kwargs,
        )


def _wheel_beam(
    shape: Shape,
    parent: ElementRef,
    name: str,
    start: tuple[float, float],
    end: tuple[float, float],
    *,
    width: float,
    depth: float,
) -> None:
    length = math.dist(start, end)
    angle = math.degrees(math.atan2(end[1] - start[1], end[0] - start[0]))
    shape.box(
        name,
        (start[0], start[1] - width / 2, WHEEL_Z - depth / 2),
        (start[0] + length, start[1] + width / 2, WHEEL_Z + depth / 2),
        rotation_origin=(start[0], start[1], WHEEL_Z),
        rotation=(0, 0, angle),
        faces=_log_faces("x"),
        parent=parent,
        group="wheel-wood",
    )


def _iron_face_beam(
    shape: Shape,
    parent: ElementRef,
    name: str,
    start: tuple[float, float],
    end: tuple[float, float],
    *,
    width: float,
    z_range: tuple[float, float],
) -> None:
    length = math.dist(start, end)
    angle = math.degrees(math.atan2(end[1] - start[1], end[0] - start[0]))
    shape.box(
        name,
        (start[0], start[1] - width / 2, WHEEL_Z + z_range[0]),
        (start[0] + length, start[1] + width / 2, WHEEL_Z + z_range[1]),
        texture="#iron",
        rotation_origin=(start[0], start[1], WHEEL_Z + sum(z_range) / 2),
        rotation=(0, 0, angle),
        parent=parent,
        group="wheel-iron",
    )


def _iron_face_bolt(
    shape: Shape,
    parent: ElementRef,
    name: str,
    center: tuple[float, float],
    *,
    z_range: tuple[float, float],
    angle: float,
    size: float = .84,
) -> None:
    shape.box(
        name,
        (center[0] - size / 2, center[1] - size / 2, WHEEL_Z + z_range[0]),
        (center[0] + size / 2, center[1] + size / 2, WHEEL_Z + z_range[1]),
        texture="#iron",
        rotation_origin=(center[0], center[1], WHEEL_Z + sum(z_range) / 2),
        rotation=(0, 0, angle + 45),
        parent=parent,
        group="wheel-iron",
    )


def _iron_y_brace(
    shape: Shape,
    parent: ElementRef,
    quadrant: int,
    main_angle: float,
    fork_start: tuple[float, float],
    fork_ends: tuple[tuple[float, float], tuple[float, float]],
) -> None:
    radians = math.radians(main_angle)
    stem_start = (WHEEL_X + 6.15 * math.cos(radians), WHEEL_Y + 6.15 * math.sin(radians))
    branch_ends = tuple(
        (fork_start[0] + (end[0] - fork_start[0]) * .52, fork_start[1] + (end[1] - fork_start[1]) * .52)
        for end in fork_ends
    )
    for face, base_z in (("back", (-1.08, -.8)), ("front", (.8, 1.08))):
        _iron_face_beam(
            shape,
            parent,
            f"iron-fork-y-{quadrant:02d}-stem-{face}",
            stem_start,
            fork_start,
            width=.58,
            z_range=base_z,
        )
        for branch_index, branch_end in enumerate(branch_ends):
            layer = (branch_index + 1) * .012
            z_range = (base_z[0] - layer, base_z[1] - layer) if face == "back" else (
                base_z[0] + layer,
                base_z[1] + layer,
            )
            _iron_face_beam(
                shape,
                parent,
                f"iron-fork-y-{quadrant:02d}-branch-{branch_index}-{face}",
                fork_start,
                branch_end,
                width=.58,
                z_range=z_range,
            )
        bolt_z = (-1.4, -1.05) if face == "back" else (1.05, 1.4)
        _iron_face_bolt(
            shape,
            parent,
            f"iron-fork-y-{quadrant:02d}-bolt-{face}",
            fork_start,
            z_range=bolt_z,
            angle=main_angle,
        )


def _iron_bend_brace(
    shape: Shape,
    parent: ElementRef,
    index: int,
    fork_start: tuple[float, float],
    bend: tuple[float, float],
    target_angle: float,
) -> None:
    incoming_length = math.dist(fork_start, bend)
    incoming_start = (
        bend[0] + (fork_start[0] - bend[0]) * 1.55 / incoming_length,
        bend[1] + (fork_start[1] - bend[1]) * 1.55 / incoming_length,
    )
    radians = math.radians(target_angle)
    outgoing_end = (WHEEL_X + 12.55 * math.cos(radians), WHEEL_Y + 12.55 * math.sin(radians))
    for face, base_z in (("back", (-1.05, -.77)), ("front", (.77, 1.05))):
        _iron_face_beam(
            shape,
            parent,
            f"iron-bend-brace-{index:02d}-incoming-{face}",
            incoming_start,
            bend,
            width=.5,
            z_range=base_z,
        )
        layer = .012
        z_range = (base_z[0] - layer, base_z[1] - layer) if face == "back" else (
            base_z[0] + layer,
            base_z[1] + layer,
        )
        _iron_face_beam(
            shape,
            parent,
            f"iron-bend-brace-{index:02d}-outgoing-{face}",
            bend,
            outgoing_end,
            width=.5,
            z_range=z_range,
        )
        bolt_z = (-1.36, -1.02) if face == "back" else (1.02, 1.36)
        _iron_face_bolt(
            shape,
            parent,
            f"iron-bend-brace-{index:02d}-bolt-{face}",
            bend,
            z_range=bolt_z,
            angle=target_angle,
            size=.76,
        )


def _iron_four_way_spokes(shape: Shape, parent: ElementRef) -> None:
    # As on the vanilla water wheel, only four heavy arms leave the hub. Each
    # arm forks near the rim so a single cross can carry all eight stone masses.
    for quadrant in range(4):
        main_angle = quadrant * 90
        radians = math.radians(main_angle)
        start = (WHEEL_X + 1.75 * math.cos(radians), WHEEL_Y + 1.75 * math.sin(radians))
        end = (WHEEL_X + 7.9 * math.cos(radians), WHEEL_Y + 7.9 * math.sin(radians))
        _wheel_beam(
            shape,
            parent,
            f"four-way-arm-{quadrant:02d}",
            start,
            end,
            width=1.8,
            depth=1.8,
        )
        fork_start = (WHEEL_X + 7.5 * math.cos(radians), WHEEL_Y + 7.5 * math.sin(radians))
        fork_ends = []
        for side_index, offset in enumerate((-22.5, 22.5)):
            target_angle = main_angle + offset
            target_radians = math.radians(target_angle)
            fork_end = (
                WHEEL_X + 11.3 * math.cos(target_radians),
                WHEEL_Y + 11.3 * math.sin(target_radians),
            )
            _wheel_beam(
                shape,
                parent,
                f"four-way-yoke-{quadrant:02d}-{side_index}",
                fork_start,
                fork_end,
                width=1.35,
                depth=1.5,
            )
            fork_ends.append(fork_end)
            stone_index = int(((target_angle - 22.5) % 360) / 45)
            _iron_bend_brace(shape, parent, stone_index, fork_start, fork_end, target_angle)
        _iron_y_brace(
            shape,
            parent,
            quadrant,
            main_angle,
            fork_start,
            (fork_ends[0], fork_ends[1]),
        )

    for index in range(8):
        angle = 22.5 + index * 45
        shape.box(
            f"spoke-{index:02d}",
            (WHEEL_X + 10.3, WHEEL_Y - .725, WHEEL_Z - .725),
            (WHEEL_X + 14.2, WHEEL_Y + .725, WHEEL_Z + .725),
            rotation_origin=(WHEEL_X, WHEEL_Y, WHEEL_Z),
            rotation=(0, 0, angle),
            faces=_log_faces("x"),
            parent=parent,
            group="wheel-wood",
        )


def _iron_four_way_hub(shape: Shape, parent: ElementRef) -> None:
    # The vanilla light four-way hub reads as four discrete iron sockets around
    # a wooden cross. Keep that construction logic, scaled to this wheel, rather
    # than using a flat iron plus plate.
    for socket_index, angle in enumerate((0, 90, 180, 270)):
        radians = math.radians(angle)
        strap_start = (WHEEL_X + 1.5 * math.cos(radians), WHEEL_Y + 1.5 * math.sin(radians))
        strap_end = (WHEEL_X + 3.85 * math.cos(radians), WHEEL_Y + 3.85 * math.sin(radians))
        for face, z_range in (("back", (-1.18, -.92)), ("front", (.92, 1.18))):
            _iron_face_beam(
                shape,
                parent,
                f"iron-hub-socket-{socket_index:02d}-face-{face}",
                strap_start,
                strap_end,
                width=1.45,
                z_range=z_range,
            )

        for side, tangent_range in (("left", (-1.08, -.9)), ("right", (.9, 1.08))):
            shape.box(
                f"iron-hub-socket-{socket_index:02d}-side-{side}",
                (WHEEL_X + 1.45, WHEEL_Y + tangent_range[0], WHEEL_Z - 1.12),
                (WHEEL_X + 3.9, WHEEL_Y + tangent_range[1], WHEEL_Z + 1.12),
                texture="#iron",
                rotation_origin=(WHEEL_X, WHEEL_Y, WHEEL_Z),
                rotation=(0, 0, angle),
                parent=parent,
                group="wheel-iron",
            )
        for cap, radial_range in (("inner-cap", (1.42, 1.72)), ("outer-cap", (3.62, 3.98))):
            shape.box(
                f"iron-hub-socket-{socket_index:02d}-{cap}",
                (WHEEL_X + radial_range[0], WHEEL_Y - 1.08, WHEEL_Z - 1.12),
                (WHEEL_X + radial_range[1], WHEEL_Y + 1.08, WHEEL_Z + 1.12),
                texture="#iron",
                rotation_origin=(WHEEL_X, WHEEL_Y, WHEEL_Z),
                rotation=(0, 0, angle),
                parent=parent,
                group="wheel-iron",
            )
        bolt_center = (WHEEL_X + 2.82 * math.cos(radians), WHEEL_Y + 2.82 * math.sin(radians))
        for face, z_range in (("back", (-1.5, -1.16)), ("front", (1.16, 1.5))):
            _iron_face_bolt(
                shape,
                parent,
                f"iron-hub-bolt-{socket_index:02d}-{face}",
                bolt_center,
                z_range=z_range,
                angle=angle,
                size=.9,
            )

    for gusset_index, start_angle in enumerate((0, 90, 180, 270)):
        end_angle = start_angle + 90
        start_radians = math.radians(start_angle)
        end_radians = math.radians(end_angle)
        start = (WHEEL_X + 1.72 * math.cos(start_radians), WHEEL_Y + 1.72 * math.sin(start_radians))
        end = (WHEEL_X + 1.72 * math.cos(end_radians), WHEEL_Y + 1.72 * math.sin(end_radians))
        for face, z_range in (("back", (-1.22, -.96)), ("front", (.96, 1.22))):
            _iron_face_beam(
                shape,
                parent,
                f"iron-hub-gusset-{gusset_index:02d}-{face}",
                start,
                end,
                width=.34,
                z_range=z_range,
            )

    shape.box(
        "wooden-axle-horizontal",
        (WHEEL_X - 2, WHEEL_Y - 1, 0),
        (WHEEL_X + 2, WHEEL_Y + 1, 16),
        texture="#strippedoak",
        parent=parent,
        group="wheel-wood",
    )
    shape.box(
        "wooden-axle-vertical",
        (WHEEL_X - 1, WHEEL_Y - 2, 0),
        (WHEEL_X + 1, WHEEL_Y + 2, 16),
        texture="#strippedoak",
        parent=parent,
        group="wheel-wood",
    )


def _iron_eight_way_spokes(shape: Shape, parent: ElementRef) -> None:
    for index in range(8):
        angle = index * 45
        shape.box(
            f"spoke-{index:02d}",
            (WHEEL_X + 1.75, WHEEL_Y - .8, WHEEL_Z - .8),
            (WHEEL_X + 14.2, WHEEL_Y + .8, WHEEL_Z + .8),
            rotation_origin=(WHEEL_X, WHEEL_Y, WHEEL_Z),
            rotation=(0, 0, angle),
            faces=_log_faces("x"),
            parent=parent,
            group="wheel-wood",
        )


def _iron_eight_way_hub(shape: Shape, parent: ElementRef) -> None:
    # Scale the water wheel's socket frame into an octagonal cage. Short ring
    # rails stop before each vertex; deeper corner blocks close those joints
    # without stacking coplanar faces.
    for index in range(8):
        _segment(
            shape,
            parent,
            f"eight-way-hub-ring-segment-{index:02d}",
            6,
            index * 45,
            4.55,
            .48,
            (-1.12, 1.12),
            "#iron",
            "wheel-iron",
        )
    vertex_radius = 6 / math.cos(math.radians(22.5))
    for index in range(8):
        angle = 22.5 + index * 45
        radians = math.radians(angle)
        x = WHEEL_X + vertex_radius * math.cos(radians)
        y = WHEEL_Y + vertex_radius * math.sin(radians)
        shape.box(
            f"eight-way-hub-ring-corner-{index:02d}",
            (x - .43, y - .43, WHEEL_Z - 1.18),
            (x + .43, y + .43, WHEEL_Z + 1.18),
            texture="#iron",
            rotation_origin=(x, y, WHEEL_Z),
            rotation=(0, 0, angle),
            parent=parent,
            group="wheel-iron",
        )

    for socket_index in range(8):
        angle = socket_index * 45
        radians = math.radians(angle)
        strap_start = (WHEEL_X + 1.5 * math.cos(radians), WHEEL_Y + 1.5 * math.sin(radians))
        strap_end = (WHEEL_X + 6.05 * math.cos(radians), WHEEL_Y + 6.05 * math.sin(radians))
        for face, z_range in (("back", (-1.15, -.82)), ("front", (.82, 1.15))):
            _iron_face_beam(
                shape,
                parent,
                f"eight-way-hub-socket-{socket_index:02d}-face-{face}",
                strap_start,
                strap_end,
                width=1.08,
                z_range=z_range,
            )
        for side, tangent_range in (("left", (-1.02, -.8)), ("right", (.8, 1.02))):
            shape.box(
                f"eight-way-hub-socket-{socket_index:02d}-side-{side}",
                (WHEEL_X + 1.45, WHEEL_Y + tangent_range[0], WHEEL_Z - 1.08),
                (WHEEL_X + 6.08, WHEEL_Y + tangent_range[1], WHEEL_Z + 1.08),
                texture="#iron",
                rotation_origin=(WHEEL_X, WHEEL_Y, WHEEL_Z),
                rotation=(0, 0, angle),
                parent=parent,
                group="wheel-iron",
            )
        for cap, radial_range in (("inner-cap", (1.42, 1.76)), ("outer-cap", (5.72, 6.08))):
            shape.box(
                f"eight-way-hub-socket-{socket_index:02d}-{cap}",
                (WHEEL_X + radial_range[0], WHEEL_Y - 1.02, WHEEL_Z - 1.08),
                (WHEEL_X + radial_range[1], WHEEL_Y + 1.02, WHEEL_Z + 1.08),
                texture="#iron",
                rotation_origin=(WHEEL_X, WHEEL_Y, WHEEL_Z),
                rotation=(0, 0, angle),
                parent=parent,
                group="wheel-iron",
            )
        for bolt_index, radius in enumerate((3.05, 5.25)):
            center = (WHEEL_X + radius * math.cos(radians), WHEEL_Y + radius * math.sin(radians))
            for face, z_range in (("back", (-1.46, -1.13)), ("front", (1.13, 1.46))):
                _iron_face_bolt(
                    shape,
                    parent,
                    f"eight-way-hub-bolt-{socket_index:02d}-{bolt_index}-{face}",
                    center,
                    z_range=z_range,
                    angle=angle,
                    size=.78,
                )

        strip_start = (WHEEL_X + 6.18 * math.cos(radians), WHEEL_Y + 6.18 * math.sin(radians))
        strip_end = (WHEEL_X + 12.38 * math.cos(radians), WHEEL_Y + 12.38 * math.sin(radians))
        strip_bolt = (WHEEL_X + 9.35 * math.cos(radians), WHEEL_Y + 9.35 * math.sin(radians))
        for face, z_range in (("back", (-1.04, -.82)), ("front", (.82, 1.04))):
            _iron_face_beam(
                shape,
                parent,
                f"eight-way-arm-strip-{socket_index:02d}-{face}",
                strip_start,
                strip_end,
                width=.42,
                z_range=z_range,
            )
            bolt_z = (-1.31, -1.02) if face == "back" else (1.02, 1.31)
            _iron_face_bolt(
                shape,
                parent,
                f"eight-way-arm-strip-bolt-{socket_index:02d}-{face}",
                strip_bolt,
                z_range=bolt_z,
                angle=angle,
                size=.56,
            )

    shape.box(
        "wooden-axle-horizontal",
        (WHEEL_X - 2, WHEEL_Y - 1, 0),
        (WHEEL_X + 2, WHEEL_Y + 1, 16),
        texture="#strippedoak",
        parent=parent,
        group="wheel-wood",
    )
    shape.box(
        "wooden-axle-vertical",
        (WHEEL_X - 1, WHEEL_Y - 2, 0),
        (WHEEL_X + 1, WHEEL_Y + 2, 16),
        texture="#strippedoak",
        parent=parent,
        group="wheel-wood",
    )


def _radial_clamps(
    shape: Shape,
    parent: ElementRef,
    *,
    count: int,
    width: float,
    arm_width: float,
    strap_offsets: tuple[float, ...] = (0,),
    bolt_radii: tuple[float, ...] = (),
    bolt_angle_offset: float = 45,
    radial_range: tuple[float, float] = (11.8, 18.8),
    angle_offset: float = 0,
    metal_texture: str = "#bronze",
) -> None:
    for index in range(count):
        angle = angle_offset + index * 360 / count
        radians = math.radians(angle)
        radial_x, radial_y = math.cos(radians), math.sin(radians)
        tangent_x, tangent_y = -radial_y, radial_x
        for strap_index, strap_offset in enumerate(strap_offsets):
            for face, z_range in (("back", (-4.05, -3.6)), ("front", (3.6, 4.05))):
                shape.box(
                    f"stone-clamp-{index:02d}-{strap_index}-{face}",
                    (WHEEL_X + radial_range[0], WHEEL_Y + strap_offset - width / 2, WHEEL_Z + z_range[0]),
                    (WHEEL_X + radial_range[1], WHEEL_Y + strap_offset + width / 2, WHEEL_Z + z_range[1]),
                    texture=metal_texture,
                    rotation_origin=(WHEEL_X, WHEEL_Y, WHEEL_Z + sum(z_range) / 2),
                    rotation=(0, 0, angle),
                    parent=parent,
                    group="wheel-iron" if metal_texture == "#iron" else "wheel-bronze",
                )
            for side, tangent_range in (
                ("left", (strap_offset - width / 2, strap_offset - arm_width / 2)),
                ("right", (strap_offset + arm_width / 2, strap_offset + width / 2)),
            ):
                shape.box(
                    f"arm-socket-{index:02d}-{strap_index}-{side}",
                    (WHEEL_X + 12.35, WHEEL_Y + tangent_range[0], WHEEL_Z - 3.85),
                    (WHEEL_X + 14.35, WHEEL_Y + tangent_range[1], WHEEL_Z + 3.85),
                    texture=metal_texture,
                    rotation_origin=(WHEEL_X, WHEEL_Y, WHEEL_Z),
                    rotation=(0, 0, angle),
                    parent=parent,
                    group="wheel-iron" if metal_texture == "#iron" else "wheel-bronze",
                )
            for bolt_index, radius in enumerate(bolt_radii):
                x = WHEEL_X + radius * radial_x + strap_offset * tangent_x
                y = WHEEL_Y + radius * radial_y + strap_offset * tangent_y
                shape.box(
                    f"stone-through-shank-{index:02d}-{strap_index}-{bolt_index}",
                    (x - .28, y - .28, WHEEL_Z - 4),
                    (x + .28, y + .28, WHEEL_Z + 4),
                    texture=metal_texture,
                    rotation_origin=(x, y, WHEEL_Z),
                    rotation=(0, 0, angle + bolt_angle_offset),
                    parent=parent,
                    group="wheel-iron" if metal_texture == "#iron" else "wheel-bronze",
                )
                for face, z_range in (("back", (-4.4, -3.95)), ("front", (3.95, 4.4))):
                    shape.box(
                        f"stone-clamp-bolt-{index:02d}-{strap_index}-{bolt_index}-{face}",
                        (x - .62, y - .62, WHEEL_Z + z_range[0]),
                        (x + .62, y + .62, WHEEL_Z + z_range[1]),
                        texture=metal_texture,
                        rotation_origin=(x, y, WHEEL_Z + sum(z_range) / 2),
                        rotation=(0, 0, angle + bolt_angle_offset),
                        parent=parent,
                        group="wheel-iron" if metal_texture == "#iron" else "wheel-bronze",
                    )


def _spin(shape: Shape, wheel: ElementRef) -> None:
    animation = animate(shape, "Spin", "spin", 36, version=1, on_animation_end="Repeat")
    for frame, rotation in ((0, 0), (35, 360)):
        animation.keyframe(frame, wheel, rotationZ=rotation, rotShortestDistanceZ=False)
    shape.add_animation(animation.build())


def _long_stone_wheel(
    shape_id: str,
    *,
    bearing_style: str,
    timber_wheel: bool,
    timber_supports: bool,
    stone_length: float,
    strap_width: float,
    strap_offsets: tuple[float, ...],
    bolt_radii: tuple[float, ...],
    radial_range: tuple[float, float],
) -> Shape:
    shape, wheel = _base(shape_id)
    _laminated_log_frame(shape, bearing_style=bearing_style, timber_supports=timber_supports)
    arm_width = 2.4 if timber_wheel else 1.55
    _rim(
        shape,
        wheel,
        "stone-rim-block",
        radius=15.4,
        count=8,
        length=stone_length,
        thickness=5.5,
        depth=(-3.7, 3.7),
        texture="#stone",
        group="wheel-stone",
    )
    _spokes(shape, wheel, count=8, width=arm_width, timber=timber_wheel)
    _radial_clamps(
        shape,
        wheel,
        count=8,
        width=strap_width,
        arm_width=arm_width,
        strap_offsets=strap_offsets,
        bolt_radii=bolt_radii,
        bolt_angle_offset=45,
        radial_range=radial_range,
    )
    _hub(shape, wheel, timber=timber_wheel)
    _spin(shape, wheel)
    return shape


def _laminated_saddle() -> Shape:
    return _long_stone_wheel(
        "small-flywheel-laminated-saddle",
        bearing_style="saddle",
        timber_wheel=False,
        timber_supports=False,
        stone_length=10.4,
        strap_width=2.45,
        strap_offsets=(0,),
        bolt_radii=(13.5, 17),
        radial_range=(12.9, 17.9),
    )


def _timber_wheel() -> Shape:
    return _long_stone_wheel(
        "small-flywheel-timber-wheel",
        bearing_style="timber",
        timber_wheel=True,
        timber_supports=True,
        stone_length=10.4,
        strap_width=3.3,
        strap_offsets=(0,),
        bolt_radii=(13.5, 17),
        radial_range=(12.9, 17.9),
    )


def _iron_four_way_wheel() -> Shape:
    shape, wheel = _base("small-flywheel-iron-four-way-hub")
    _laminated_log_frame(
        shape,
        bearing_style="timber",
        timber_supports=True,
        metal_texture="#iron",
    )
    _rim(
        shape,
        wheel,
        "stone-rim-block",
        radius=15.4,
        count=8,
        length=10.4,
        thickness=5.5,
        depth=(-3.7, 3.7),
        texture="#stone",
        group="wheel-stone",
        angle_offset=22.5,
    )
    _iron_four_way_spokes(shape, wheel)
    _radial_clamps(
        shape,
        wheel,
        count=8,
        width=2.1,
        arm_width=1.45,
        strap_offsets=(0,),
        bolt_radii=(13.5, 17),
        bolt_angle_offset=45,
        radial_range=(12.9, 17.9),
        angle_offset=22.5,
        metal_texture="#iron",
    )
    _iron_four_way_hub(shape, wheel)
    _spin(shape, wheel)
    return shape


def _iron_eight_way_frame() -> Shape:
    shape, wheel = _base("small-flywheel-iron-eight-way-frame")
    _laminated_log_frame(
        shape,
        bearing_style="timber",
        timber_supports=True,
        metal_texture="#iron",
    )
    _rim(
        shape,
        wheel,
        "stone-rim-block",
        radius=15.4,
        count=8,
        length=10.4,
        thickness=5.5,
        depth=(-3.7, 3.7),
        texture="#stone",
        group="wheel-stone",
    )
    _iron_eight_way_spokes(shape, wheel)
    _radial_clamps(
        shape,
        wheel,
        count=8,
        width=2.2,
        arm_width=1.6,
        strap_offsets=(0,),
        bolt_radii=(13.5, 17),
        bolt_angle_offset=45,
        radial_range=(12.9, 17.9),
        metal_texture="#iron",
    )
    _iron_eight_way_hub(shape, wheel)
    _spin(shape, wheel)
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

    shapes = OrderedDict((
        ("model-e1-laminated-saddle", _laminated_saddle()),
        ("model-e2-timber-wheel", _timber_wheel()),
        ("model-e3-iron-four-way-hub", _iron_four_way_wheel()),
        ("model-e4-iron-eight-way-frame", _iron_eight_way_frame()),
    ))
    if approved_candidate is not None and approved_candidate not in shapes:
        raise ValueError(f"unknown approved candidate: {approved_candidate}")
    selected = shapes if approved_candidate is None else OrderedDict(((approved_candidate, shapes[approved_candidate]),))

    package = ModelPackage("small_flywheel_review")
    for candidate, shape in selected.items():
        package.shape(shape, f"{REVIEW_ROOT.as_posix()}/{STAGING_NAME}/{candidate}/rest.shape.json")
    build_package(package, root, write=True)

    readme = [
        "# Small flywheel review",
        "",
        "Review candidates for a 3-block-wide, 3-block-high, 1-block-thick native mechanical-power device.",
        "The wheel uses hard granite as the review material with an oak frame. E1 and E2 use bronze; E3 uses forged iron.",
        "The approved runtime block is planned to offer granite, andesite, basalt, and peridotite stone variants.",
        "This round carries E1 through E3 forward unchanged and adds E4's eight straight arms inside a large octagonal iron socket frame.",
        "Each front and rear ground foot is as deep as its two-log stack is high. The two square depth logs cross the stack seam, and their visible bolt heads share the same horizontal and vertical centers.",
        "The entire hub, shaft, arm, clamp, bolt, and stone assembly is parented to one wheel pivot and uses one uniform 360-degree animation curve.",
        "",
        f"Decision: `{'open' if approved_candidate is None else approved_candidate + ' approved'}`.",
        "",
        "Each candidate includes a `spin` animation. Compare the front, side, top, and oblique views and scrub the animation for frame clearance.",
        "",
    ]
    readme.extend(f"- `{candidate}` - {DESCRIPTIONS[candidate]}" for candidate in selected)
    (staging / "README.md").write_text("\n".join([*readme, ""]), encoding="utf-8")

    ownership = OrderedDict((
        ("version", 1),
        ("purpose", "visual approval for the runtime small flywheel feature"),
        ("source", "graphics/review/small_flywheel.py"),
        ("managedPath", f"{REVIEW_ROOT.as_posix()}/{MANAGED_NAME}"),
        ("footprintBlocks", OrderedDict((("width", 3), ("height", 3), ("depth", 1)))),
        ("candidates", list(selected)),
        ("animations", ["spin"]),
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
    parser = argparse.ArgumentParser(description="Build the managed small-flywheel review package.")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--approved", choices=tuple(DESCRIPTIONS), default=None)
    args = parser.parse_args(argv)
    managed = build_review(args.root, approved_candidate=args.approved)
    print(f"Built {len(DESCRIPTIONS) if args.approved is None else 1} small-flywheel candidate(s) in {managed.relative_to(args.root.resolve()).as_posix()}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
