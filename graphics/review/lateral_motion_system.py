"""Build the managed approval package for the one-axle crank and spectacle pump."""

from __future__ import annotations

import argparse
import math
import shutil
from collections import OrderedDict
from pathlib import Path

from gearwright_graphics.compiler import build_package, json_bytes
from gearwright_graphics.model import ElementRef, ModelPackage, Shape, animate


REVIEW_ROOT = Path("generated/lateral-motion-review")
MANAGED_NAME = "current"
STAGING_NAME = ".current-build"
BACKUP_NAME = ".current-old"

DESCRIPTIONS = OrderedDict((
    (
        "a7-rear-pinion-external-openers",
        "An A3-derived refinement with a nearly full-bore piston, close-coupled opaque manifolds, and four inward-opening swing flaps. "
        "A rear crosshead rack reverses a compact pinion; its through-shaft drives a visible lost-motion selector and rocker whose rods stay outside the piston bore.",
    ),
    (
        "a9-single-acting-caged-poppets",
        "The preferred compact single-acting direction: guided lift discs and sliding sleeves work inside the standard 4-by-4 process pipes. "
        "The central glass chamber has no valve compartments, and paired half-scale guided air checks make the output side unmistakable.",
    ),
    (
        "a10-two-block-bottom-pipes",
        "A taller single-acting installation with the same glass pressure chamber in the upper pump block. Two 3-by-3 reservoir-floor ports feed standard 4-by-4 risers that turn once through the lower service gallery and leave through centered side faces. "
        "A reinforced-oak frame uses full rear crosses and front knee triangles while leaving the lower guided checks visible through the cutaway.",
    ),
    (
        "a11-one-block-full-frame",
        "A compact bottom-fed alternative that removes A10's lower service block without shortcutting through the chamber side walls. Each centered side connection turns down, inward, and up through its own reservoir-floor port. "
        "Upward inlet and downward outlet checks sit at the two floor ports. A low oak cradle appears only in the supported downward-facing state; side- and upward-facing states leave the pump body unframed.",
    ),
))

STAGES = (
    "one-axle-crank",
    "through-crank",
    "pump-bottom",
    "pump-cutaway",
    "pump-top-fit",
    "pump-side-fit",
    "pipe-fit",
    "valve-detail",
)

ANIMATIONS = ("rotation", "doubleacting", "valvegear", "singleacting", "passivechecks")
VALVE_NAMES = (
    "upper-intake",
    "lower-intake",
    "upper-output",
    "lower-output",
)


def _inside(root: Path, path: Path) -> bool:
    try:
        path.resolve().relative_to(root.resolve())
        return True
    except ValueError:
        return False


def _shape(name: str) -> Shape:
    shape = Shape(name, 32, 32)
    shape.texture("gw-oak", "game:block/wood/debarked/oak", size=(32, 32))
    shape.texture("gw-iron", "game:block/metal/sheet-plain/iron2", size=(32, 32))
    shape.texture("gw-bronze", "game:block/metal/sheet/tinbronze1", size=(32, 32))
    shape.texture("gw-copper", "game:block/metal/sheet/copper1", size=(32, 32))
    shape.texture("gw-glass", "gearwright:block/inspection-glass", size=(16, 16))
    shape.texture("gw-shadow", "gearwright:block/inspection-shadow", size=(4, 4))
    return shape


def _pivot(
    shape: Shape,
    name: str,
    origin: tuple[float, float, float],
    *,
    rotation: tuple[float, float, float] = (0, 0, 0),
    parent: ElementRef | None = None,
    group: str | None = None,
) -> ElementRef:
    return shape._element(
        name,
        origin,
        origin,
        rotation_origin=origin,
        rotation=rotation,
        parent=parent,
        group=group,
        pivot=True,
    )


def _shaft_segment(
    shape: Shape,
    prefix: str,
    start: float,
    end: float,
    *,
    parent: ElementRef,
) -> None:
    shape.box(
        f"{prefix}-wide",
        (start, -1, -2),
        (end, 1, 2),
        texture="#gw-oak",
        parent=parent,
        group="shaft",
    )
    shape.box(
        f"{prefix}-tall",
        (start, -2, -1),
        (end, 2, 1),
        texture="#gw-oak",
        parent=parent,
        group="shaft",
    )


def _add_crank(
    shape: Shape,
    *,
    through: bool,
    base_angle: float = 0,
    prefix: str = "gw-crank",
    throw: float = 4.0,
) -> ElementRef:
    """Add either a true cantilever crank or the supported through-shaft form."""
    phase = _pivot(shape, f"{prefix}-phase", (0, 0, 0), rotation=(base_angle, 0, 0), group="crank-motion")
    if through:
        _shaft_segment(shape, f"{prefix}-input-shaft", -8, -2.75, parent=phase)
        _shaft_segment(shape, f"{prefix}-output-shaft", 2.75, 8, parent=phase)
    else:
        _shaft_segment(shape, f"{prefix}-input-shaft", -8, -2.05, parent=phase)

    web_top = throw + .70
    if through:
        for side, x_range in (("input", (-2.75, -1.45)), ("output", (1.45, 2.75))):
            shape.box(f"{prefix}-{side}-web", (x_range[0], -1.55, -1.25), (x_range[1], web_top, 1.25), texture="#gw-iron", parent=phase, group="crank-load-path")
            shape.box(f"{prefix}-{side}-root-bush", (x_range[0] - .1, -1.85, -1.55), (x_range[1] + .1, .35, 1.55), texture="#gw-bronze", parent=phase, group="crank-bearing")
        pin_range = (-1.45, 1.45)
    else:
        shape.box(f"{prefix}-single-web", (-2.05, -1.55, -1.25), (-.65, web_top, 1.25), texture="#gw-iron", parent=phase, group="crank-load-path")
        shape.box(f"{prefix}-single-root-bush", (-2.2, -1.85, -1.55), (-.5, .35, 1.55), texture="#gw-bronze", parent=phase, group="crank-bearing")
        pin_range = (-.65, 1.05)

    shape.box(f"{prefix}-bronze-journal", (pin_range[0], throw - .95, -1.15), (pin_range[1], throw + .95, 1.15), texture="#gw-bronze", parent=phase, group="crank-journal")
    if not through:
        shape.box(f"{prefix}-journal-retainer", (.92, throw - .78, -1.34), (1.25, throw + .78, 1.34), texture="#gw-iron", parent=phase, group="crank-journal")
    return phase


def _add_crank_animation(shape: Shape, phase: ElementRef, *, code: str = "rotation", base_angle: float = 0) -> None:
    spin = animate(
        shape,
        "Crank rotation",
        code,
        61,
        version=1,
        on_activity_stopped="EaseOut",
        on_animation_end="Repeat",
    )
    spin.keyframe(0, phase, rotationX=base_angle, rotShortestDistanceX=False)
    spin.keyframe(60, phase, rotationX=base_angle + 360, rotShortestDistanceX=False)
    shape.add_animation(spin.build())


def _crank_state(*, through: bool, throw: float = 4.0) -> Shape:
    label = "through" if through else "one-axle"
    shape = _shape(f"lateral-motion-{label}-crank")
    phase = _add_crank(shape, through=through, throw=throw)
    _add_crank_animation(shape, phase)
    return shape


PIPE_CENTER_Y = -19.1
UPPER_VALVE_Y = -15.2
LOWER_VALVE_Y = -23.0
CYLINDER_TOP = -14.0
CYLINDER_BOTTOM = -25.25
CYLINDER_HALF_X = 4.75
VALVE_SEAT_X = 4.55
VALVE_ROD_X = 5.5
ROCKER_Y = -7.0
ROCKER_ARM = 5.5
PINION_RADIUS = 2.4
PINION_Z = 4.35
SELECTOR_Z = 2.55
SINGLE_CRANK_THROW = 3.0
SINGLE_ROD_LENGTH = 6.0
SINGLE_CYLINDER_HALF_X = 4.75
COMPACT_CYLINDER_HALF_X = 4.5
SINGLE_CYLINDER_TOP = -10.2
SINGLE_HEAD_TOP = SINGLE_CYLINDER_TOP + .5
SINGLE_GLAND_POCKET_HALF_X = .80
SINGLE_GLAND_POCKET_HALF_Z = 1.30
SINGLE_GUIDE_BOTTOM = SINGLE_HEAD_TOP + .08
SINGLE_BORE_BOTTOM = -18.35
SINGLE_CYLINDER_BOTTOM = -19.0
SINGLE_PIPE_CENTER_Y = -16.0
SINGLE_INTAKE_VALVE_SEAT_X = -6.95
SINGLE_OUTPUT_VALVE_SEAT_X = 5.80
SINGLE_CHECK_TRAVEL = .58
SINGLE_CHECK_ANGLE = 22.0
AIR_ROOF_CENTER_X = 2.65
AIR_ROOF_CENTER_Z = 1.55
AIR_ROOF_SEAT_Y = -9.88
AIR_ROOF_COLLAR_HALF = .72
AIR_ROOF_COLLAR_WALL = .24
AIR_ROOF_BORE_HALF = AIR_ROOF_COLLAR_HALF - AIR_ROOF_COLLAR_WALL
AIR_CHECK_TRAVEL = .22
TALL_PUMP_BOTTOM = -40.0
TALL_PIPE_CENTER_Y = -32.0
BOTTOM_RESERVOIR_PORT_X = 2.5
BOTTOM_RISER_BOTTOM = TALL_PIPE_CENTER_Y + 2.0
BOTTOM_RISER_TOP = SINGLE_CYLINDER_BOTTOM
COMPACT_STAND_BOTTOM = -24.0
COMPACT_STAND_BASE_TOP = -23.35
COMPACT_STAND_CRADLE_BOTTOM = -19.65
COMPACT_STAND_CRADLE_TOP = -18.85
COMPACT_LOWER_PIPE_CENTER_Y = -21.25
COMPACT_RISER_BOTTOM = COMPACT_LOWER_PIPE_CENTER_Y - 1.5
COMPACT_INTAKE_CHECK_CENTER_Y = -18.87
COMPACT_OUTPUT_CHECK_CENTER_Y = -18.72
COMPACT_VERTICAL_CHECK_TRAVEL = .36



def _hollow_x_section(
    shape: Shape,
    mount: ElementRef,
    prefix: str,
    x_range: tuple[float, float],
    center_y: float,
    *,
    half_y: float,
    half_z: float,
    wall: float,
    group: str,
    cutaway: bool = False,
    center_z: float = 0.0,
    texture: str = "#gw-copper",
) -> None:
    x0, x1 = x_range
    pieces = (
        ("bottom", (x0, center_y - half_y, center_z - half_z), (x1, center_y - half_y + wall, center_z + half_z)),
        ("top", (x0, center_y + half_y - wall, center_z - half_z), (x1, center_y + half_y, center_z + half_z)),
        ("front", (x0, center_y - half_y + wall, center_z - half_z), (x1, center_y + half_y - wall, center_z - half_z + wall)),
        ("back", (x0, center_y - half_y + wall, center_z + half_z - wall), (x1, center_y + half_y - wall, center_z + half_z)),
    )
    for part, from_, to in pieces:
        if cutaway and part == "front":
            continue
        shape.box(f"{prefix}-{part}", from_, to, texture=texture, parent=mount, group=group)


def _hollow_y_section(
    shape: Shape,
    mount: ElementRef,
    prefix: str,
    y_range: tuple[float, float],
    center_x: float,
    *,
    half_x: float,
    half_z: float,
    wall: float,
    group: str,
    cutaway: bool = False,
    center_z: float = 0.0,
) -> None:
    y0, y1 = y_range
    pieces = (
        ("left", (center_x - half_x, y0, center_z - half_z), (center_x - half_x + wall, y1, center_z + half_z)),
        ("right", (center_x + half_x - wall, y0, center_z - half_z), (center_x + half_x, y1, center_z + half_z)),
        ("front", (center_x - half_x + wall, y0, center_z - half_z), (center_x + half_x - wall, y1, center_z - half_z + wall)),
        ("back", (center_x - half_x + wall, y0, center_z + half_z - wall), (center_x + half_x - wall, y1, center_z + half_z)),
    )
    for part, from_, to in pieces:
        if cutaway and part == "front":
            continue
        shape.box(f"{prefix}-{part}", from_, to, texture="#gw-copper", parent=mount, group=group)


def _add_ports(shape: Shape, mount: ElementRef, *, cutaway: bool) -> None:
    """Terminate identical pipe arms flush against the close-coupled valve chests."""
    del cutaway
    for side, x_range, end_collar, body_collar in (
        ("input", (-11.5, -7.95), (-11.5, -10.8), (-8.45, -7.95)),
        ("output", (7.95, 11.5), (10.8, 11.5), (7.95, 8.45)),
    ):
        _hollow_x_section(
            shape,
            mount,
            f"gw-pump-{side}-pipe",
            x_range,
            PIPE_CENTER_Y,
            half_y=2.0,
            half_z=2.0,
            wall=.5,
            group=f"pump-{side}",
            cutaway=False,
        )
        for collar_name, collar_range in (("end-coupling", end_collar), ("chest-coupling", body_collar)):
            _hollow_x_section(
                shape,
                mount,
                f"gw-pump-{side}-{collar_name}",
                collar_range,
                PIPE_CENTER_Y,
                half_y=2.5,
                half_z=2.5,
                wall=.5,
                group=f"pump-{side}",
                cutaway=False,
            )
        shadow_x = -11.59 if side == "input" else 11.5
        shape.box(
            f"gw-pump-{side}-shadow",
            (shadow_x, PIPE_CENTER_Y - 1.45, -1.45),
            (shadow_x + .09, PIPE_CENTER_Y + 1.45, 1.45),
            texture="#gw-shadow",
            faces=("west",) if side == "input" else ("east",),
            parent=mount,
            group=f"pump-{side}",
        )


def _add_window_frame(
    shape: Shape,
    mount: ElementRef,
    *,
    prefix: str,
    z: float,
    outward_face: str,
    half_width: float,
    top: float,
    bottom: float,
    cutaway: bool,
) -> None:
    front = outward_face == "north"
    edge = -.22 if front else .22
    z0, z1 = (z + edge, z) if front else (z, z + edge)
    if not (cutaway and front):
        for side, x_range in (("left", (-half_width, -half_width + .45)), ("right", (half_width - .45, half_width))):
            shape.box(f"{prefix}-{side}-rail", (x_range[0], bottom, z0), (x_range[1], top, z1), texture="#gw-bronze", parent=mount, group="pump-window-frame")
        for side, y_range in (("bottom", (bottom, bottom + .45)), ("top", (top - .45, top))):
            shape.box(f"{prefix}-{side}-rail", (-half_width, y_range[0], z0), (half_width, y_range[1], z1), texture="#gw-bronze", parent=mount, group="pump-window-frame")
        shape.box(
            f"{prefix}-glass",
            (-half_width + .43, bottom + .43, z - .05),
            (half_width - .43, top - .43, z + .05),
            texture="#gw-glass",
            faces=(outward_face,),
            render_pass=1,
            parent=mount,
            group="pump-glass",
        )


def _add_pressure_body(shape: Shape, mount: ElementRef, *, cutaway: bool) -> None:
    half_x, half_z = CYLINDER_HALF_X, 3.8
    top, bottom = CYLINDER_TOP, CYLINDER_BOTTOM
    shape.box("gw-cylinder-top-cap", (-half_x, top, -half_z), (half_x, top + 1.25, half_z), texture="#gw-copper", parent=mount, group="pump-pressure-body")
    shape.box("gw-cylinder-bottom-cap", (-half_x, bottom, -half_z), (half_x, bottom + 1.25, half_z), texture="#gw-copper", parent=mount, group="pump-pressure-body")
    for side, x_range in (("left", (-half_x, -4.15)), ("right", (4.15, half_x))):
        for segment, y_range in (
            ("bottom", (bottom + 1.2, LOWER_VALVE_Y - .8)),
            ("middle", (LOWER_VALVE_Y + .8, UPPER_VALVE_Y - .8)),
            ("top", (UPPER_VALVE_Y + .8, top + .05)),
        ):
            shape.box(
                f"gw-cylinder-side-{side}-{segment}",
                (x_range[0], y_range[0], -3.15),
                (x_range[1], y_range[1], 3.15),
                texture="#gw-copper",
                parent=mount,
                group="pump-pressure-body",
            )
    for x, z in ((-half_x, -3.8), (-half_x, 3.15), (half_x - .65, -3.8), (half_x - .65, 3.15)):
        if cutaway and z < 0:
            continue
        shape.box(
            f"gw-cylinder-corner-{x:g}-{z:g}",
            (x, bottom + 1.05, z),
            (x + .65, top + .2, z + .65),
            texture="#gw-copper",
            parent=mount,
            group="pump-pressure-body",
        )

    _add_window_frame(shape, mount, prefix="gw-cylinder-front", z=-half_z, outward_face="north", half_width=half_x, top=top + .15, bottom=bottom + 1.1, cutaway=cutaway)
    _add_window_frame(shape, mount, prefix="gw-cylinder-back", z=half_z, outward_face="south", half_width=half_x, top=top + .15, bottom=bottom + 1.1, cutaway=False)

    shape.box("gw-piston-rod-gland", (-1.3, top - .2, -1.3), (1.3, top + 1.0, 1.3), texture="#gw-bronze", parent=mount, group="pump-bearing")
    for side, x_range in (("left", (-4.82, -4.18)), ("right", (4.18, 4.82))):
        shape.box(f"gw-crosshead-guide-{side}", (x_range[0], -12.9, -.95), (x_range[1], -2.85, .95), texture="#gw-iron", parent=mount, group="pump-guide")
    for side, x_range in (("left", (-4.95, -3.95)), ("right", (3.95, 4.95))):
        shape.box(f"gw-crosshead-guide-{side}-foot", (x_range[0], top + .65, -1.2), (x_range[1], top + 1.45, 1.2), texture="#gw-iron", parent=mount, group="pump-guide")


def _valve_positions() -> dict[str, tuple[float, float]]:
    return {
        "upper-intake": (-VALVE_SEAT_X, UPPER_VALVE_Y),
        "lower-intake": (-VALVE_SEAT_X, LOWER_VALVE_Y),
        "upper-output": (VALVE_SEAT_X, UPPER_VALVE_Y),
        "lower-output": (VALVE_SEAT_X, LOWER_VALVE_Y),
    }


def _add_side_housings(shape: Shape, mount: ElementRef, *, cutaway: bool) -> None:
    """Build two closed, full-section valve chests flush with the cylinder and external arms."""
    del cutaway
    for side, center_x in (("input", -6.35), ("output", 6.35)):
        _hollow_y_section(
            shape,
            mount,
            f"gw-{side}-manifold",
            (LOWER_VALVE_Y - 1.5, UPPER_VALVE_Y + 1.5),
            center_x,
            half_x=1.6,
            half_z=2.0,
            wall=.5,
            group="pump-flow-path",
            cutaway=False,
        )
        shape.box(
            f"gw-{side}-manifold-top-cap",
            (center_x - 1.6, UPPER_VALVE_Y + 1.5, -2.0),
            (center_x + 1.6, UPPER_VALVE_Y + 2.0, 2.0),
            texture="#gw-copper",
            parent=mount,
            group="pump-flow-path",
        )
        shape.box(
            f"gw-{side}-manifold-bottom-cap",
            (center_x - 1.6, LOWER_VALVE_Y - 2.0, -2.0),
            (center_x + 1.6, LOWER_VALVE_Y - 1.5, 2.0),
            texture="#gw-copper",
            parent=mount,
            group="pump-flow-path",
        )
        _hollow_y_section(
            shape,
            mount,
            f"gw-{side}-manifold-center-band",
            (PIPE_CENTER_Y - .45, PIPE_CENTER_Y + .45),
            center_x,
            half_x=1.85,
            half_z=2.3,
            wall=.5,
            group="pump-flow-path",
            cutaway=False,
        )

    for level, center_y in (("upper", UPPER_VALVE_Y), ("lower", LOWER_VALVE_Y)):
        for side, x_range in (("intake", (-5.15, -3.80)), ("output", (3.80, 5.15))):
            _hollow_x_section(
                shape,
                mount,
                f"gw-{level}-{side}-valve-collar",
                x_range,
                center_y,
                half_y=1.25,
                half_z=2.2,
                wall=.45,
                group="pump-valve-housing",
                cutaway=False,
            )


def _add_flap(
    shape: Shape,
    mount: ElementRef,
    name: str,
    position: tuple[float, float],
) -> tuple[ElementRef, ElementRef]:
    """Add a side-gallery flap whose external lever matches the rod travel at this level."""
    seat_x, y = position
    inward = 1 if seat_x < 0 else -1
    outward = -inward
    hinge_top = name in ("upper-intake", "lower-output")
    prefix = f"gw-valve-{name}"
    for part, y_range, z_range in (
        ("seat-top", (y + .48, y + .72), (-1.25, 1.1)),
        ("seat-bottom", (y - .72, y - .48), (-1.25, 1.1)),
        ("seat-front", (y - .48, y + .48), (-1.25, -1.02)),
        ("seat-back", (y - .48, y + .48), (.87, 1.1)),
    ):
        shape.box(f"{prefix}-{part}", (seat_x - .13, y_range[0], z_range[0]), (seat_x + .13, y_range[1], z_range[1]), texture="#gw-bronze", parent=mount, group="pump-valves")

    hinge_y = y + (.48 if hinge_top else -.48)
    flap = _pivot(shape, f"{prefix}-flap-motion", (seat_x, hinge_y, 0), parent=mount, group="pump-valves")
    flap_y = (-.92, -.12) if hinge_top else (.12, .92)
    shape.box(f"{prefix}-flap", (-.16, flap_y[0], -1.25), (.16, flap_y[1], 1.1), texture="#gw-bronze", parent=flap, group="pump-valves")

    lever_x = sorted((0.0, outward * .95))
    shape.box(f"{prefix}-actuator-lever", (lever_x[0], -.16, .72), (lever_x[1], .16, 1.12), texture="#gw-iron", parent=flap, group="pump-valves")
    pin_x = sorted((outward * .78, outward * 1.10))
    shape.box(f"{prefix}-actuator-pin", (pin_x[0], -.26, .62), (pin_x[1], .26, 1.22), texture="#gw-bronze", parent=flap, group="pump-valves")
    shape.box(f"{prefix}-hinge-pin", (seat_x - .28, hinge_y - .16, -1.38), (seat_x + .28, hinge_y + .16, 1.32), texture="#gw-iron", parent=mount, group="pump-valves")

    stop_x = (seat_x + .22, seat_x + .50)
    stop_y = (y - .40, y - .10) if hinge_top else (y + .10, y + .40)
    shape.box(f"{prefix}-travel-stop", (stop_x[0], stop_y[0], -1.38), (stop_x[1], stop_y[1], 1.22), texture="#gw-iron", parent=mount, group="pump-valves")

    tappet = _pivot(shape, f"{prefix}-tappet-motion", (seat_x + outward * .95, hinge_y, 1.15), parent=mount, group="pump-valves")
    shape.box(f"{prefix}-tappet", (-.17, -1.0, -.17), (.17, .45, .17), texture="#gw-iron", parent=tappet, group="pump-valves")
    shape.box(f"{prefix}-tappet-clevis-front", (-.38, -.36, -.48), (.38, .36, -.26), texture="#gw-bronze", parent=tappet, group="pump-valves")
    shape.box(f"{prefix}-tappet-clevis-back", (-.38, -.36, .26), (.38, .36, .48), texture="#gw-bronze", parent=tappet, group="pump-valves")
    return flap, tappet

def _add_valve_gear(
    shape: Shape,
    mount: ElementRef,
) -> tuple[ElementRef, ElementRef, ElementRef, ElementRef, ElementRef]:
    """Put the compact rack drive behind the crank and carry it forward on one visible shaft."""
    shape.box(
        "gw-valve-pinion-shaft",
        (-.42, ROCKER_Y - .42, SELECTOR_Z - .48),
        (.42, ROCKER_Y + .42, PINION_Z + .55),
        texture="#gw-bronze",
        parent=mount,
        group="pump-valve-drive",
    )

    pinion = _pivot(shape, "gw-valve-pinion-motion", (0, ROCKER_Y, PINION_Z), parent=mount, group="pump-valve-drive")
    for index in range(12):
        angle = index * 30.0
        shape.box(
            f"gw-valve-pinion-rim-{index:02d}",
            (-.68, 1.72, -.27),
            (.68, 2.12, .27),
            texture="#gw-iron",
            rotation_origin=(0, 0, 0),
            rotation=(0, 0, angle),
            parent=pinion,
            group="pump-valve-pinion",
        )
        shape.box(
            f"gw-valve-pinion-tooth-{index:02d}",
            (-.46, 2.04, -.34),
            (.46, 2.68, .34),
            texture="#gw-bronze",
            rotation_origin=(0, 0, 0),
            rotation=(0, 0, angle),
            parent=pinion,
            group="pump-valve-pinion",
        )
    for index, angle in enumerate((0.0, 60.0, 120.0)):
        shape.box(
            f"gw-valve-pinion-spoke-{index}",
            (-.23, -1.92, -.22),
            (.23, 1.92, .22),
            texture="#gw-iron",
            rotation_origin=(0, 0, 0),
            rotation=(0, 0, angle),
            parent=pinion,
            group="pump-valve-pinion",
        )
    shape.box("gw-valve-pinion-hub", (-.58, -.58, -.44), (.58, .58, .44), texture="#gw-bronze", parent=pinion, group="pump-valve-pinion")

    selector_drive = _pivot(shape, "gw-valve-selector-drive-motion", (0, ROCKER_Y, SELECTOR_Z), parent=mount, group="pump-valve-reverser")
    for index in range(8):
        angle = index * 45.0
        shape.box(
            f"gw-valve-selector-drum-{index:02d}",
            (-.52, .68, -.34),
            (.52, 1.02, .34),
            texture="#gw-bronze",
            rotation_origin=(0, 0, 0),
            rotation=(0, 0, angle),
            parent=selector_drive,
            group="pump-valve-reverser",
        )
    shape.box("gw-valve-selector-hub", (-.52, -.52, -.44), (.52, .52, .44), texture="#gw-iron", parent=selector_drive, group="pump-valve-reverser")
    shape.box("gw-valve-selector-driving-dog", (.72, -.24, -.42), (1.42, .24, .42), texture="#gw-bronze", parent=selector_drive, group="pump-valve-reverser")

    rocker = _pivot(shape, "gw-valve-rocker-motion", (0, ROCKER_Y, SELECTOR_Z), parent=mount, group="pump-valve-drive")
    shape.box("gw-valve-rocker-bar", (-ROCKER_ARM, -.30, -1.25), (ROCKER_ARM, .30, -.82), texture="#gw-iron", parent=rocker, group="pump-valve-drive")
    for side, x in (("left", -ROCKER_ARM), ("right", ROCKER_ARM)):
        shape.box(f"gw-valve-rocker-pin-{side}", (x - .28, -.40, -1.36), (x + .28, .40, -.70), texture="#gw-bronze", parent=rocker, group="pump-valve-drive")
    for part, from_, to in (
        ("top", (-1.15, .72, -.82), (1.15, 1.12, -.38)),
        ("bottom", (-1.15, -1.12, -.82), (1.15, -.72, -.38)),
        ("left", (-1.15, -.72, -.82), (-.75, .72, -.38)),
        ("right", (.75, -.72, -.82), (1.15, .72, -.38)),
    ):
        shape.box(f"gw-valve-reverser-collar-{part}", from_, to, texture="#gw-iron", parent=rocker, group="pump-valve-reverser")
    shape.box("gw-valve-reverser-follower-upper", (.95, .34, -.90), (1.38, .68, -.30), texture="#gw-bronze", parent=rocker, group="pump-valve-reverser")
    shape.box("gw-valve-reverser-follower-lower", (-1.38, -.68, -.90), (-.95, -.34, -.30), texture="#gw-bronze", parent=rocker, group="pump-valve-reverser")
    shape.box("gw-valve-reverser-stop-left", (-1.72, ROCKER_Y - 1.15, 1.15), (-1.25, ROCKER_Y - .60, 2.05), texture="#gw-bronze", parent=mount, group="pump-valve-reverser")
    shape.box("gw-valve-reverser-stop-right", (1.25, ROCKER_Y + .60, 1.15), (1.72, ROCKER_Y + 1.15, 2.05), texture="#gw-bronze", parent=mount, group="pump-valve-reverser")

    rod_refs = []
    rod_z = 1.15
    for side, x in (("left", -ROCKER_ARM), ("right", ROCKER_ARM)):
        rod = _pivot(shape, f"gw-valve-{side}-rod-motion", (x, ROCKER_Y, rod_z), parent=mount, group="pump-valve-drive")
        shape.box(f"gw-valve-{side}-rod", (-.20, LOWER_VALVE_Y - ROCKER_Y - 1.2, -.20), (.20, .08, .20), texture="#gw-iron", parent=rod, group="pump-valve-drive")
        shape.box(f"gw-valve-{side}-rod-clevis", (-.46, -.46, -.34), (.46, .20, .34), texture="#gw-iron", parent=rod, group="pump-valve-drive")
        for level, local_y in (("upper", UPPER_VALVE_Y - ROCKER_Y), ("lower", LOWER_VALVE_Y - ROCKER_Y)):
            shape.box(
                f"gw-valve-{side}-{level}-cam-lug",
                (-.52, local_y - .22, -.28),
                (.52, local_y + .22, .40),
                texture="#gw-bronze",
                parent=rod,
                group="pump-valve-drive",
            )
        rod_refs.append(rod)
    return pinion, selector_drive, rocker, rod_refs[0], rod_refs[1]

def _add_piston(shape: Shape, mount: ElementRef, initial_crosshead_y: float) -> ElementRef:
    moving = _pivot(shape, "gw-piston-motion", (0, initial_crosshead_y, 0), parent=mount, group="pump-motion")
    for side, x_range in (("left", (-4.05, -1.45)), ("right", (1.45, 4.05))):
        shape.box(f"gw-crosshead-shoe-{side}", (x_range[0], -.55, -.78), (x_range[1], .55, .78), texture="#gw-iron", parent=moving, group="pump-motion")
    shape.box("gw-crosshead-front-bridge", (-1.45, -.55, -1.35), (1.45, .55, -1.05), texture="#gw-iron", parent=moving, group="pump-motion")
    shape.box("gw-crosshead-back-bridge", (-1.45, -.55, 1.05), (1.45, .55, 1.35), texture="#gw-iron", parent=moving, group="pump-motion")
    shape.box("gw-crosshead-pin", (-1.32, -.5, -.5), (1.32, .5, .5), texture="#gw-bronze", parent=moving, group="pump-bearing")

    shape.box("gw-valve-drive-rack-spine", (-2.95, -5.50, 3.98), (-2.62, 5.50, 4.55), texture="#gw-iron", parent=moving, group="pump-valve-rack")
    shape.box("gw-valve-drive-rack-rear-bracket", (-2.82, -.46, 1.18), (-2.34, .46, 4.18), texture="#gw-iron", parent=moving, group="pump-valve-rack")
    shape.box("gw-valve-drive-rack-crosshead-bracket", (-2.72, -.46, 1.05), (-1.20, .46, 1.45), texture="#gw-iron", parent=moving, group="pump-valve-rack")
    for index, center_y in enumerate((-5.0, -3.75, -2.5, -1.25, 0.0, 1.25, 2.5, 3.75, 5.0)):
        shape.box(
            f"gw-valve-drive-rack-tooth-{index:02d}",
            (-2.72, center_y - .24, 3.92),
            (-2.08, center_y + .24, 4.68),
            texture="#gw-bronze",
            parent=moving,
            group="pump-valve-rack",
        )

    shape.box("gw-piston-rod", (-.42, -10.5, -.42), (.42, -.62, .42), texture="#gw-bronze", parent=moving, group="pump-motion")
    shape.box("gw-piston-plate", (-3.85, -11.55, -3.05), (3.85, -10.45, 3.05), texture="#gw-bronze", parent=moving, group="pump-piston")
    shape.box("gw-piston-upper-seal", (-4.0, -10.72, -3.20), (4.0, -10.42, 3.20), texture="#gw-iron", parent=moving, group="pump-piston")
    shape.box("gw-piston-lower-seal", (-4.0, -11.58, -3.20), (4.0, -11.28, 3.20), texture="#gw-iron", parent=moving, group="pump-piston")
    return moving

def _slider_state(
    phase_degrees: float,
    *,
    radius: float = 4.0,
    rod_length: float = 8.0,
) -> tuple[float, float, float, float, float, float]:
    radians = math.radians(phase_degrees)
    pin_y = radius * math.cos(radians)
    pin_z = radius * math.sin(radians)
    crosshead_y = pin_y - math.sqrt(max(.001, rod_length * rod_length - pin_z * pin_z))
    middle_y = (pin_y + crosshead_y) / 2
    middle_z = pin_z / 2
    rod_angle = math.degrees(math.atan2(pin_z, pin_y - crosshead_y))
    return pin_y, pin_z, crosshead_y, middle_y, middle_z, rod_angle


def _add_connecting_rod(
    shape: Shape,
    mount: ElementRef,
    initial_middle_y: float,
    initial_middle_z: float,
    *,
    rod_length: float = 8.0,
) -> ElementRef:
    half_length = rod_length / 2
    rod = _pivot(shape, "gw-connecting-rod-motion", (0, initial_middle_y, initial_middle_z), parent=mount, group="pump-motion")
    shape.box("gw-connecting-rod-spine", (-.50, -half_length + 1.1, -.50), (.50, half_length - 1.1, .50), texture="#gw-iron", parent=rod, group="pump-motion")
    for end, center_y, inner_y, inner_z, outer_y, outer_z in (
        ("crank", half_length, 1.05, 1.25, 1.3, 1.5),
        ("crosshead", -half_length, .65, .65, 1.05, 1.08),
    ):
        for part, y_range, z_range in (
            ("top", (center_y + inner_y, center_y + outer_y), (-outer_z, outer_z)),
            ("bottom", (center_y - outer_y, center_y - inner_y), (-outer_z, outer_z)),
            ("front", (center_y - inner_y, center_y + inner_y), (-outer_z, -inner_z)),
            ("back", (center_y - inner_y, center_y + inner_y), (inner_z, outer_z)),
        ):
            shape.box(f"gw-connecting-rod-{end}-bearing-{part}", (-.58, y_range[0], z_range[0]), (.58, y_range[1], z_range[1]), texture="#gw-bronze", parent=rod, group="pump-bearing")
    return rod


def _add_doubleacting_animation(
    shape: Shape,
    crank_phase: ElementRef,
    piston: ElementRef,
    rod: ElementRef,
    valves: dict[str, tuple[ElementRef, ElementRef]],
    gear: tuple[ElementRef, ElementRef, ElementRef, ElementRef, ElementRef],
    *,
    base_angle: float,
) -> None:
    _, _, initial_crosshead, initial_middle_y, initial_middle_z, _ = _slider_state(0)
    cycle = animate(
        shape,
        "Double-acting pump with rear rack-pinion and shaft-coupled reversing gear",
        "doubleacting",
        61,
        version=1,
        on_activity_stopped="EaseOut",
        on_animation_end="Repeat",
    )
    pinion, selector_drive, rocker, left_rod, right_rod = gear
    for frame in range(0, 61, 5):
        phase = frame * 6.0
        _, _, crosshead, middle_y, middle_z, rod_angle = _slider_state(phase)
        sine = math.sin(math.radians(phase))
        if abs(sine) < 1e-8:
            sine = 0.0
        downstroke = max(0.0, sine)
        upstroke = max(0.0, -sine)
        pinion_angle = math.degrees((initial_crosshead - crosshead) / PINION_RADIUS)
        rocker_angle = 7.0 * sine
        rocker_radians = math.radians(rocker_angle)

        cycle.keyframe(frame, crank_phase, rotationX=base_angle + phase, rotShortestDistanceX=False)
        cycle.keyframe(frame, piston, offsetY=crosshead - initial_crosshead)
        cycle.keyframe(
            frame,
            rod,
            offsetY=middle_y - initial_middle_y,
            offsetZ=middle_z - initial_middle_z,
            rotationX=rod_angle,
            rotShortestDistanceX=True,
        )
        cycle.keyframe(frame, pinion, rotationZ=pinion_angle, rotShortestDistanceZ=True)
        cycle.keyframe(frame, selector_drive, rotationZ=pinion_angle, rotShortestDistanceZ=True)
        cycle.keyframe(frame, rocker, rotationZ=rocker_angle, rotShortestDistanceZ=True)
        for side_sign, valve_rod in ((-1, left_rod), (1, right_rod)):
            cycle.keyframe(
                frame,
                valve_rod,
                offsetX=side_sign * ROCKER_ARM * (math.cos(rocker_radians) - 1.0),
                offsetY=side_sign * ROCKER_ARM * math.sin(rocker_radians),
            )

        for valve_name, opening in (
            ("upper-intake", downstroke),
            ("lower-output", downstroke),
            ("lower-intake", upstroke),
            ("upper-output", upstroke),
        ):
            flap, tappet = valves[valve_name]
            hinge_direction = 1 if valve_name in ("upper-intake", "lower-output") else -1
            tappet_direction = -1 if "upper" in valve_name else 1
            cycle.keyframe(frame, flap, rotationZ=hinge_direction * opening * 20.0, rotShortestDistanceZ=True)
            cycle.keyframe(frame, tappet, offsetY=tappet_direction * opening * .70)
    shape.add_animation(cycle.build())

def _pump_shape(candidate: str, *, mount_angle: float = 0, cutaway: bool = False, label: str) -> Shape:
    shape = _shape(f"lateral-motion-{candidate}-{label}")
    crank_phase = _add_crank(shape, through=False, base_angle=mount_angle)
    mount = _pivot(shape, "gw-pump-mount", (0, 0, 0), rotation=(mount_angle, 0, 0), group="pump")
    _add_pressure_body(shape, mount, cutaway=cutaway)
    _add_ports(shape, mount, cutaway=cutaway)
    _add_side_housings(shape, mount, cutaway=cutaway)
    valves = {name: _add_flap(shape, mount, name, position) for name, position in _valve_positions().items()}
    gear = _add_valve_gear(shape, mount)

    _, _, initial_crosshead, initial_middle_y, initial_middle_z, _ = _slider_state(0)
    piston = _add_piston(shape, mount, initial_crosshead)
    rod = _add_connecting_rod(shape, mount, initial_middle_y, initial_middle_z)
    _add_doubleacting_animation(shape, crank_phase, piston, rod, valves, gear, base_angle=mount_angle)
    return shape


def _valve_detail_shape(candidate: str) -> Shape:
    shape = _shape(f"lateral-motion-{candidate}-valve-detail")
    mount = _pivot(shape, "gw-valve-detail-mount", (0, 0, 0), group="pump")
    y = UPPER_VALVE_Y
    _hollow_x_section(
        shape,
        mount,
        "gw-valve-detail-housing",
        (-6.0, -3.8),
        y,
        half_y=1.25,
        half_z=2.2,
        wall=.45,
        group="pump-valve-housing",
        cutaway=True,
    )
    flap, tappet = _add_flap(shape, mount, "upper-intake", (-VALVE_SEAT_X, y))
    drive_rod = _pivot(shape, "gw-valve-detail-drive-rod-motion", (-VALVE_ROD_X, y + 1.0, 1.15), parent=mount, group="pump-valve-drive")
    shape.box("gw-valve-detail-drive-rod", (-.20, -3.0, -.20), (.20, 1.3, .20), texture="#gw-iron", parent=drive_rod, group="pump-valve-drive")
    shape.box("gw-valve-detail-drive-lug", (-.55, -.28, -.28), (.55, .16, .40), texture="#gw-bronze", parent=drive_rod, group="pump-valve-drive")

    motion = animate(
        shape,
        "Side-manifold rod pulls the internal flap through its rear lever",
        "valvegear",
        31,
        version=1,
        on_activity_stopped="EaseOut",
        on_animation_end="Repeat",
    )
    motion.keyframe(0, flap, rotationZ=0, rotShortestDistanceZ=True)
    motion.keyframe(0, tappet, offsetY=0)
    motion.keyframe(0, drive_rod, offsetY=0)
    motion.keyframe(15, flap, rotationZ=20, rotShortestDistanceZ=True)
    motion.keyframe(15, tappet, offsetY=-.70)
    motion.keyframe(15, drive_rod, offsetY=-.70)
    motion.keyframe(30, flap, rotationZ=0, rotShortestDistanceZ=True)
    motion.keyframe(30, tappet, offsetY=0)
    motion.keyframe(30, drive_rod, offsetY=0)
    shape.add_animation(motion.build())
    return shape


def _add_singleacting_top_head(
    shape: Shape,
    mount: ElementRef,
    *,
    half_x: float = SINGLE_CYLINDER_HALF_X,
) -> None:
    """Build a split roof around the rod well and two output-side air checks."""
    half_z = 3.8
    roof_bottom, roof_top = SINGLE_CYLINDER_TOP, SINGLE_HEAD_TOP
    valve_x0 = AIR_ROOF_CENTER_X - AIR_ROOF_COLLAR_HALF
    valve_x1 = AIR_ROOF_CENTER_X + AIR_ROOF_COLLAR_HALF

    # The pressure ceiling is deliberately assembled from separate plates.
    # The central square remains the rod well; two more exact cutouts on the
    # positive-X (process-output) half accept the air-check collars.
    roof_parts = (
        (
            "input-deck",
            (-half_x, roof_bottom, -half_z),
            (-SINGLE_GLAND_POCKET_HALF_X, roof_top, half_z),
        ),
        (
            "gland-front-bridge",
            (-SINGLE_GLAND_POCKET_HALF_X, roof_bottom, -half_z),
            (SINGLE_GLAND_POCKET_HALF_X, roof_top, -SINGLE_GLAND_POCKET_HALF_Z),
        ),
        (
            "gland-back-bridge",
            (-SINGLE_GLAND_POCKET_HALF_X, roof_bottom, SINGLE_GLAND_POCKET_HALF_Z),
            (SINGLE_GLAND_POCKET_HALF_X, roof_top, half_z),
        ),
        (
            "output-inner-web",
            (SINGLE_GLAND_POCKET_HALF_X, roof_bottom, -half_z),
            (valve_x0, roof_top, half_z),
        ),
        (
            "output-outer-web",
            (valve_x1, roof_bottom, -half_z),
            (half_x, roof_top, half_z),
        ),
        (
            "output-front-strip",
            (valve_x0, roof_bottom, -half_z),
            (valve_x1, roof_top, -AIR_ROOF_CENTER_Z - AIR_ROOF_COLLAR_HALF),
        ),
        (
            "output-center-strip",
            (valve_x0, roof_bottom, -AIR_ROOF_CENTER_Z + AIR_ROOF_COLLAR_HALF),
            (valve_x1, roof_top, AIR_ROOF_CENTER_Z - AIR_ROOF_COLLAR_HALF),
        ),
        (
            "output-back-strip",
            (valve_x0, roof_bottom, AIR_ROOF_CENTER_Z + AIR_ROOF_COLLAR_HALF),
            (valve_x1, roof_top, half_z),
        ),
    )
    for part, from_, to in roof_parts:
        shape.box(
            f"gw-cylinder-top-cap-roof-{part}",
            from_,
            to,
            texture="#gw-copper",
            parent=mount,
            group="pump-pressure-body",
        )

    plate_bottom = SINGLE_CYLINDER_TOP + .02
    plate_top = SINGLE_CYLINDER_TOP + .13
    plate_outer_x = SINGLE_GLAND_POCKET_HALF_X - .08
    plate_outer_z = SINGLE_GLAND_POCKET_HALF_Z - .10
    gland_inner = .50
    for part, from_, to in (
        ("front", (-plate_outer_x, plate_bottom, -plate_outer_z), (plate_outer_x, plate_top, -gland_inner)),
        ("back", (-plate_outer_x, plate_bottom, gland_inner), (plate_outer_x, plate_top, plate_outer_z)),
        ("left", (-plate_outer_x, plate_bottom, -gland_inner), (-gland_inner, plate_top, gland_inner)),
        ("right", (gland_inner, plate_bottom, -gland_inner), (plate_outer_x, plate_top, gland_inner)),
    ):
        shape.box(
            f"gw-piston-rod-gland-plate-{part}",
            from_,
            to,
            texture="#gw-bronze",
            parent=mount,
            group="pump-bearing",
        )

    # The square packing liner continues inside the head and stops above the
    # piston plate. Its 1x1 opening leaves .08 units around the .84-wide rod.
    for part, from_, to in (
        ("front", (-.62, SINGLE_CYLINDER_TOP - .35, -.62), (.62, plate_top, -gland_inner)),
        ("back", (-.62, SINGLE_CYLINDER_TOP - .35, gland_inner), (.62, plate_top, .62)),
        ("left", (-.62, SINGLE_CYLINDER_TOP - .35, -gland_inner), (-gland_inner, plate_top, gland_inner)),
        ("right", (gland_inner, SINGLE_CYLINDER_TOP - .35, -gland_inner), (.62, plate_top, gland_inner)),
    ):
        shape.box(
            f"gw-piston-rod-gland-liner-{part}",
            from_,
            to,
            texture="#gw-bronze",
            parent=mount,
            group="pump-bearing",
        )

    for index, (x, z) in enumerate(((-.66, -.85), (-.66, .85), (.66, -.85), (.66, .85))):
        shape.box(
            f"gw-piston-rod-gland-fastener-{index}",
            (x - .05, plate_top, z - .05),
            (x + .05, plate_top + .08, z + .05),
            texture="#gw-iron",
            parent=mount,
            group="pump-bearing",
        )


def _add_singleacting_output_wall_with_air_throats(
    shape: Shape,
    mount: ElementRef,
    *,
    half_x: float,
    lower_y: float,
) -> None:
    """Close the output wall; the paired air checks now pass through the roof."""
    shape.box(
        "gw-single-cylinder-output-wall-solid",
        (4.15, lower_y, -3.15),
        (half_x, SINGLE_CYLINDER_TOP, 3.15),
        texture="#gw-copper",
        parent=mount,
        group="pump-pressure-body",
    )

def _add_singleacting_window(
    shape: Shape,
    mount: ElementRef,
    *,
    prefix: str,
    z: float,
    outward_face: str,
    cutaway: bool,
    half_x: float = SINGLE_CYLINDER_HALF_X,
) -> None:
    """Glaze only the piston chamber; the process checks now live in the pipes."""
    if cutaway and outward_face == "north":
        return
    front = outward_face == "north"
    edge = -.22 if front else .22
    z0, z1 = (z + edge, z) if front else (z, z + edge)
    top = SINGLE_CYLINDER_TOP + .15
    bottom = SINGLE_BORE_BOTTOM

    for name, x_range in (
        ("left", (-half_x, -4.15)),
        ("right", (4.15, half_x)),
    ):
        shape.box(
            f"{prefix}-{name}-rail",
            (x_range[0], bottom, z0),
            (x_range[1], top, z1),
            texture="#gw-bronze",
            parent=mount,
            group="pump-window-frame",
        )
    for name, y_range in (("bottom", (bottom, bottom + .45)), ("top", (top - .45, top))):
        shape.box(
            f"{prefix}-{name}-rail",
            (-half_x, y_range[0], z0),
            (half_x, y_range[1], z1),
            texture="#gw-bronze",
            parent=mount,
            group="pump-window-frame",
        )
    shape.box(
        f"{prefix}-glass",
        (-4.15, bottom + .43, z - .05),
        (4.15, top - .43, z + .05),
        texture="#gw-glass",
        faces=(outward_face,),
        render_pass=1,
        parent=mount,
        group="pump-glass",
    )


def _add_singleacting_pressure_body(
    shape: Shape,
    mount: ElementRef,
    *,
    cutaway: bool,
    bottom_ports: bool = False,
    half_x: float = SINGLE_CYLINDER_HALF_X,
) -> None:
    """Build the compact glass chamber with either side or reservoir-floor ports."""
    half_z = 3.8
    top, bottom = SINGLE_CYLINDER_TOP, SINGLE_CYLINDER_BOTTOM
    pipe_inner_bottom = SINGLE_PIPE_CENTER_Y - 1.5
    pipe_inner_top = SINGLE_PIPE_CENTER_Y + 1.5
    pipe_inner_half_z = 1.5
    _add_singleacting_top_head(shape, mount, half_x=half_x)

    if bottom_ports:
        for part, from_, to in (
            ("front", (-half_x, bottom, -half_z), (half_x, SINGLE_BORE_BOTTOM, -1.5)),
            ("back", (-half_x, bottom, 1.5), (half_x, SINGLE_BORE_BOTTOM, half_z)),
            ("left-web", (-half_x, bottom, -1.5), (-4.0, SINGLE_BORE_BOTTOM, 1.5)),
            ("center-web", (-1.0, bottom, -1.5), (1.0, SINGLE_BORE_BOTTOM, 1.5)),
            ("right-web", (4.0, bottom, -1.5), (half_x, SINGLE_BORE_BOTTOM, 1.5)),
        ):
            if cutaway and part == "front":
                continue
            shape.box(
                f"gw-cylinder-bottom-cap-{part}",
                from_,
                to,
                texture="#gw-copper",
                parent=mount,
                group="pump-wet-end",
            )
        shape.box(
            "gw-single-cylinder-input-wall-solid",
            (-half_x, SINGLE_BORE_BOTTOM, -3.15),
            (-4.15, top, 3.15),
            texture="#gw-copper",
            parent=mount,
            group="pump-pressure-body",
        )
        _add_singleacting_output_wall_with_air_throats(
            shape,
            mount,
            half_x=half_x,
            lower_y=SINGLE_BORE_BOTTOM,
        )
    else:
        shape.box(
            "gw-cylinder-bottom-cap",
            (-half_x, bottom, -half_z),
            (half_x, SINGLE_BORE_BOTTOM, half_z),
            texture="#gw-copper",
            parent=mount,
            group="pump-wet-end",
        )
        for side, x_range in (
            ("input", (-half_x, -4.15)),
            ("output", (4.15, half_x)),
        ):
            wall_pieces = [
                ("beneath-pipe", (x_range[0], SINGLE_BORE_BOTTOM, -3.15), (x_range[1], pipe_inner_bottom, 3.15)),
                ("left-of-pipe", (x_range[0], pipe_inner_bottom, -3.15), (x_range[1], pipe_inner_top, -pipe_inner_half_z)),
                ("right-of-pipe", (x_range[0], pipe_inner_bottom, pipe_inner_half_z), (x_range[1], pipe_inner_top, 3.15)),
            ]
            if side == "input":
                wall_pieces.append(("above-pipe", (x_range[0], pipe_inner_top, -3.15), (x_range[1], top, 3.15)))
            for part, from_, to in wall_pieces:
                shape.box(
                    f"gw-single-cylinder-{side}-wall-{part}",
                    from_,
                    to,
                    texture="#gw-copper",
                    parent=mount,
                    group="pump-pressure-body",
                )
        _add_singleacting_output_wall_with_air_throats(
            shape,
            mount,
            half_x=half_x,
            lower_y=pipe_inner_top,
        )

    for side, x_range in (
        ("input", (-half_x, -4.15)),
        ("output", (4.15, half_x)),
    ):
        for depth, z_range in (("front", (-3.8, -3.15)), ("back", (3.15, 3.8))):
            if cutaway and depth == "front":
                continue
            shape.box(
                f"gw-cylinder-corner-{side}-{depth}",
                (x_range[0], bottom + .55, z_range[0]),
                (x_range[1], top + .2, z_range[1]),
                texture="#gw-copper",
                parent=mount,
                group="pump-pressure-body",
            )

    _add_singleacting_window(shape, mount, prefix="gw-cylinder-front", z=-half_z, outward_face="north", cutaway=cutaway, half_x=half_x)
    _add_singleacting_window(shape, mount, prefix="gw-cylinder-back", z=half_z, outward_face="south", cutaway=False, half_x=half_x)

    guide_outer = half_x - .05
    for side, x_range in (("left", (-guide_outer, -4.02)), ("right", (4.02, guide_outer))):
        shape.box(
            f"gw-crosshead-guide-{side}",
            (x_range[0], SINGLE_GUIDE_BOTTOM, -.62),
            (x_range[1], -1.85, .62),
            texture="#gw-iron",
            parent=mount,
            group="pump-guide",
        )
    for side, x_range in (("left", (-half_x - .05, -3.82)), ("right", (3.82, half_x + .05))):
        shape.box(
            f"gw-crosshead-guide-{side}-foot",
            (x_range[0], SINGLE_HEAD_TOP, -.66),
            (x_range[1], SINGLE_HEAD_TOP + .60, .66),
            texture="#gw-iron",
            parent=mount,
            group="pump-guide",
        )

def _add_singleacting_ports(shape: Shape, mount: ElementRef, *, cutaway: bool) -> None:
    """Run standard 4x4 process pipes directly into four-piece chamber walls."""
    half_x = SINGLE_CYLINDER_HALF_X
    for side, pipe_range, coupling_range in (
        ("input", (-8.0, -half_x), (-8.0, -7.35)),
        ("output", (half_x, 8.0), (7.35, 8.0)),
    ):
        _hollow_x_section(
            shape,
            mount,
            f"gw-pump-{side}-pipe",
            pipe_range,
            SINGLE_PIPE_CENTER_Y,
            half_y=2.0,
            half_z=2.0,
            wall=.5,
            group=f"pump-{side}",
            cutaway=cutaway,
        )
        _hollow_x_section(
            shape,
            mount,
            f"gw-pump-{side}-end-coupling",
            coupling_range,
            SINGLE_PIPE_CENTER_Y,
            half_y=2.5,
            half_z=2.5,
            wall=.5,
            group=f"pump-{side}",
            cutaway=False,
            texture="#gw-bronze" if side == "output" else "#gw-copper",
        )
        shadow_x = -8.09 if side == "input" else 8.0
        shape.box(
            f"gw-pump-{side}-shadow",
            (shadow_x, SINGLE_PIPE_CENTER_Y - 1.45, -1.45),
            (shadow_x + .09, SINGLE_PIPE_CENTER_Y + 1.45, 1.45),
            texture="#gw-shadow",
            faces=("west",) if side == "input" else ("east",),
            parent=mount,
            group=f"pump-{side}",
        )


def _add_singleacting_bottom_ports(shape: Shape, mount: ElementRef, *, cutaway: bool) -> None:
    """Drop from two reservoir-floor ports, turn once, and exit centered side faces."""
    for side, center_x, pipe_range, coupling_range, outer_direction in (
        ("input", -BOTTOM_RESERVOIR_PORT_X, (-8.0, -1.0), (-8.0, -7.35), -1),
        ("output", BOTTOM_RESERVOIR_PORT_X, (1.0, 8.0), (7.35, 8.0), 1),
    ):
        group = f"pump-{side}"
        _hollow_y_section(
            shape,
            mount,
            f"gw-pump-{side}-reservoir-riser",
            (BOTTOM_RISER_BOTTOM, BOTTOM_RISER_TOP),
            center_x,
            half_x=2.0,
            half_z=2.0,
            wall=.5,
            group=group,
            cutaway=cutaway,
        )
        for collar, y_range in (
            ("reservoir-collar", (-19.65, -19.0)),
            ("block-joint-band", (-24.40, -23.75)),
        ):
            _hollow_y_section(
                shape,
                mount,
                f"gw-pump-{side}-{collar}",
                y_range,
                center_x,
                half_x=2.5,
                half_z=2.5,
                wall=.5,
                group=group,
                cutaway=False,
            )

        x0, x1 = pipe_range
        shape.box(
            f"gw-pump-{side}-elbow-bottom",
            (x0, TALL_PIPE_CENTER_Y - 2.0, -2.0),
            (x1, TALL_PIPE_CENTER_Y - 1.5, 2.0),
            texture="#gw-copper",
            parent=mount,
            group=group,
        )
        top_range = (x0, center_x - 1.5) if outer_direction < 0 else (center_x + 1.5, x1)
        shape.box(
            f"gw-pump-{side}-elbow-top-outer",
            (top_range[0], TALL_PIPE_CENTER_Y + 1.5, -2.0),
            (top_range[1], TALL_PIPE_CENTER_Y + 2.0, 2.0),
            texture="#gw-copper",
            parent=mount,
            group=group,
        )
        for depth, z_range in (("front", (-2.0, -1.5)), ("back", (1.5, 2.0))):
            if cutaway and depth == "front":
                continue
            shape.box(
                f"gw-pump-{side}-elbow-{depth}",
                (x0, TALL_PIPE_CENTER_Y - 1.5, z_range[0]),
                (x1, TALL_PIPE_CENTER_Y + 1.5, z_range[1]),
                texture="#gw-copper",
                parent=mount,
                group=group,
            )

        _hollow_x_section(
            shape,
            mount,
            f"gw-pump-{side}-end-coupling",
            coupling_range,
            TALL_PIPE_CENTER_Y,
            half_y=2.5,
            half_z=2.5,
            wall=.5,
            group=group,
            cutaway=False,
            texture="#gw-bronze" if side == "output" else "#gw-copper",
        )
        shadow_x = -8.09 if side == "input" else 8.0
        shape.box(
            f"gw-pump-{side}-shadow",
            (shadow_x, TALL_PIPE_CENTER_Y - 1.45, -1.45),
            (shadow_x + .09, TALL_PIPE_CENTER_Y + 1.45, 1.45),
            texture="#gw-shadow",
            faces=("west",) if side == "input" else ("east",),
            parent=mount,
            group=group,
        )


def _add_compact_bottom_loop_ports(shape: Shape, mount: ElementRef, *, cutaway: bool) -> None:
    """Fold centered side connections down a shared tank wall and up through its floor."""
    for side, direction in (("input", -1), ("output", 1)):
        group = f"pump-{side}"
        riser_center_x = direction * BOTTOM_RESERVOIR_PORT_X
        if direction < 0:
            stub_range = (-8.0, -COMPACT_CYLINDER_HALF_X)
            stub_floor_range = (-8.0, -7.5)
            drop_outer_wall = (-8.0, -7.5)
            drop_face_range = (-7.5, -COMPACT_CYLINDER_HALF_X)
            drop_inner_wall = (-COMPACT_CYLINDER_HALF_X, -4.0)
            lower_range = (-7.5, -.5)
            top_ranges = ((-COMPACT_CYLINDER_HALF_X, -4.0), (-1.0, -.5))
            coupling_range = (-8.0, -7.35)
        else:
            stub_range = (COMPACT_CYLINDER_HALF_X, 8.0)
            stub_floor_range = (7.5, 8.0)
            drop_outer_wall = (7.5, 8.0)
            drop_face_range = (COMPACT_CYLINDER_HALF_X, 7.5)
            drop_inner_wall = (4.0, COMPACT_CYLINDER_HALF_X)
            lower_range = (.5, 7.5)
            top_ranges = ((.5, 1.0), (4.0, COMPACT_CYLINDER_HALF_X))
            coupling_range = (7.35, 8.0)

        stub_x0, stub_x1 = stub_range
        for part, from_, to in (
            (
                "top",
                (stub_x0, SINGLE_PIPE_CENTER_Y + 1.5, -2.0),
                (stub_x1, SINGLE_PIPE_CENTER_Y + 2.0, 2.0),
            ),
            (
                "bottom-outer",
                (stub_floor_range[0], SINGLE_PIPE_CENTER_Y - 2.0, -2.0),
                (stub_floor_range[1], SINGLE_PIPE_CENTER_Y - 1.5, 2.0),
            ),
            (
                "front",
                (stub_x0, SINGLE_PIPE_CENTER_Y - 1.5, -2.0),
                (stub_x1, SINGLE_PIPE_CENTER_Y + 1.5, -1.5),
            ),
            (
                "back",
                (stub_x0, SINGLE_PIPE_CENTER_Y - 1.5, 1.5),
                (stub_x1, SINGLE_PIPE_CENTER_Y + 1.5, 2.0),
            ),
        ):
            if cutaway and part == "front":
                continue
            shape.box(
                f"gw-pump-{side}-upper-stub-{part}",
                from_,
                to,
                texture="#gw-copper",
                parent=mount,
                group=group,
            )

        # The drop owns its outer/front/back walls. Beside the chamber, its
        # inner face is the pressure-vessel wall itself. A short inner wall is
        # retained only below the tank, where there is no vessel wall to share.
        shape.box(
            f"gw-pump-{side}-outer-drop-outer-wall",
            (drop_outer_wall[0], COMPACT_RISER_BOTTOM, -2.0),
            (drop_outer_wall[1], SINGLE_PIPE_CENTER_Y + 1.5, 2.0),
            texture="#gw-copper",
            parent=mount,
            group=group,
        )
        shape.box(
            f"gw-pump-{side}-outer-drop-inner-wall-lower",
            (drop_inner_wall[0], COMPACT_RISER_BOTTOM, -2.0),
            (drop_inner_wall[1], SINGLE_CYLINDER_BOTTOM, 2.0),
            texture="#gw-copper",
            parent=mount,
            group=group,
        )
        for depth, z_range in (("front", (-2.0, -1.5)), ("back", (1.5, 2.0))):
            if cutaway and depth == "front":
                continue
            shape.box(
                f"gw-pump-{side}-outer-drop-{depth}",
                (drop_face_range[0], COMPACT_RISER_BOTTOM, z_range[0]),
                (drop_face_range[1], SINGLE_PIPE_CENTER_Y + 1.5, z_range[1]),
                texture="#gw-copper",
                parent=mount,
                group=group,
            )

        lower_x0, lower_x1 = lower_range
        shape.box(
            f"gw-pump-{side}-lower-run-bottom",
            (lower_x0, COMPACT_LOWER_PIPE_CENTER_Y - 2.0, -2.0),
            (lower_x1, COMPACT_LOWER_PIPE_CENTER_Y - 1.5, 2.0),
            texture="#gw-copper",
            parent=mount,
            group=group,
        )
        for index, top_range in enumerate(top_ranges):
            shape.box(
                f"gw-pump-{side}-lower-run-top-{index}",
                (top_range[0], COMPACT_LOWER_PIPE_CENTER_Y + 1.5, -2.0),
                (top_range[1], COMPACT_LOWER_PIPE_CENTER_Y + 2.0, 2.0),
                texture="#gw-copper",
                parent=mount,
                group=group,
            )
        for depth, z_range in (("front", (-2.0, -1.5)), ("back", (1.5, 2.0))):
            if cutaway and depth == "front":
                continue
            shape.box(
                f"gw-pump-{side}-lower-run-{depth}",
                (lower_x0, COMPACT_LOWER_PIPE_CENTER_Y - 1.5, z_range[0]),
                (lower_x1, COMPACT_LOWER_PIPE_CENTER_Y + 1.5, z_range[1]),
                texture="#gw-copper",
                parent=mount,
                group=group,
            )

        # These walls continue through the split lower head to the bore floor,
        # leaving the full 3x3 passage open instead of capping the riser.
        _hollow_y_section(
            shape,
            mount,
            f"gw-pump-{side}-floor-riser",
            (COMPACT_RISER_BOTTOM, SINGLE_BORE_BOTTOM),
            riser_center_x,
            half_x=2.0,
            half_z=2.0,
            wall=.5,
            group=group,
            cutaway=cutaway,
        )
        _hollow_x_section(
            shape,
            mount,
            f"gw-pump-{side}-end-coupling",
            coupling_range,
            SINGLE_PIPE_CENTER_Y,
            half_y=2.5,
            half_z=2.5,
            wall=.5,
            group=group,
            cutaway=False,
            texture="#gw-bronze" if side == "output" else "#gw-copper",
        )
        shadow_x = -8.09 if side == "input" else 8.0
        shape.box(
            f"gw-pump-{side}-shadow",
            (shadow_x, SINGLE_PIPE_CENTER_Y - 1.45, -1.45),
            (shadow_x + .09, SINGLE_PIPE_CENTER_Y + 1.45, 1.45),
            texture="#gw-shadow",
            faces=("west",) if side == "input" else ("east",),
            parent=mount,
            group=group,
        )

def _add_xy_brace(
    shape: Shape,
    mount: ElementRef,
    name: str,
    start: tuple[float, float],
    end: tuple[float, float],
    z_range: tuple[float, float],
    *,
    width: float = .80,
    group: str = "pump-tall-frame",
) -> None:
    """Add one diagonal timber in an X/Y frame plane."""
    dx, dy = end[0] - start[0], end[1] - start[1]
    length = math.hypot(dx, dy)
    center_x = (start[0] + end[0]) / 2
    center_y = (start[1] + end[1]) / 2
    angle = -math.degrees(math.atan2(dx, dy))
    shape.box(
        name,
        (center_x - width / 2, center_y - length / 2, z_range[0]),
        (center_x + width / 2, center_y + length / 2, z_range[1]),
        texture="#gw-oak",
        rotation_origin=(center_x, center_y, (z_range[0] + z_range[1]) / 2),
        rotation=(0, 0, angle),
        parent=mount,
        group=group,
    )


def _add_tall_pump_frame(shape: Shape, mount: ElementRef) -> None:
    """Build a reinforced-oak service frame around the lower process gallery."""
    for side, x_range in (("left", (-8.0, -7.0)), ("right", (7.0, 8.0))):
        for depth, z_range in (("front", (-4.70, -3.70)), ("back", (3.70, 4.70))):
            shape.box(
                f"gw-tall-frame-{side}-{depth}-post",
                (x_range[0], TALL_PUMP_BOTTOM + .40, z_range[0]),
                (x_range[1], -8.70, z_range[1]),
                texture="#gw-oak",
                parent=mount,
                group="pump-tall-frame",
            )

    levels = (
        ("bottom", (TALL_PUMP_BOTTOM + .20, TALL_PUMP_BOTTOM + 1.20)),
        ("pipe-bed", (-35.0, -34.0)),
        ("block-joint", (-24.50, -23.50)),
        ("vessel-sill", (-20.30, -19.30)),
        ("top", (-9.80, -8.80)),
    )
    for level, y_range in levels:
        for depth, z_range in (("front", (-4.70, -3.70)), ("back", (3.70, 4.70))):
            shape.box(
                f"gw-tall-frame-{level}-{depth}-rail",
                (-8.0, y_range[0], z_range[0]),
                (8.0, y_range[1], z_range[1]),
                texture="#gw-oak",
                parent=mount,
                group="pump-tall-frame",
            )
        for side, x_range in (("left", (-8.0, -7.0)), ("right", (7.0, 8.0))):
            shape.box(
                f"gw-tall-frame-{level}-{side}-tie",
                (x_range[0], y_range[0], -3.70),
                (x_range[1], y_range[1], 3.70),
                texture="#gw-oak",
                parent=mount,
                group="pump-tall-frame",
            )

    # Full crossed bracing carries racking loads on the rear while the front
    # uses four knee triangles that leave the valve gallery unobstructed.
    for index, (start, end) in enumerate((
        ((-7.5, -39.0), (7.5, -24.0)),
        ((7.5, -39.0), (-7.5, -24.0)),
    )):
        _add_xy_brace(shape, mount, f"gw-tall-frame-back-cross-{index}", start, end, (3.78, 4.62), width=.90)

    front_knees = (
        ("lower-left", (-7.5, -39.0), (-3.8, -34.25)),
        ("lower-right", (7.5, -39.0), (3.8, -34.25)),
        ("upper-left", (-7.5, -24.0), (-3.8, -29.75)),
        ("upper-right", (7.5, -24.0), (3.8, -29.75)),
        ("vessel-left", (-7.5, -24.0), (-4.25, -19.45)),
        ("vessel-right", (7.5, -24.0), (4.25, -19.45)),
    )
    for name, start, end in front_knees:
        _add_xy_brace(shape, mount, f"gw-tall-frame-front-knee-{name}", start, end, (-4.62, -3.78), width=.90)

    for depth, z_range in (("front", (-4.86, -4.64)), ("back", (4.64, 4.86))):
        for name, center in (
            ("cross", (0.0, -31.5)),
            ("lower-left", (-3.8, -34.25)),
            ("lower-right", (3.8, -34.25)),
            ("upper-left", (-3.8, -29.75)),
            ("upper-right", (3.8, -29.75)),
        ):
            if depth == "back" and name != "cross":
                continue
            shape.box(
                f"gw-tall-frame-{depth}-{name}-joint-plate",
                (center[0] - .48, center[1] - .48, z_range[0]),
                (center[0] + .48, center[1] + .48, z_range[1]),
                texture="#gw-iron",
                parent=mount,
                group="pump-tall-frame-reinforcement",
            )

def _add_xz_brace(
    shape: Shape,
    mount: ElementRef,
    name: str,
    start: tuple[float, float],
    end: tuple[float, float],
    y_range: tuple[float, float],
    *,
    width: float = .80,
) -> None:
    """Add one diagonal timber in the horizontal cradle plane."""
    dx, dz = end[0] - start[0], end[1] - start[1]
    length = math.hypot(dx, dz)
    center_x = (start[0] + end[0]) / 2
    center_z = (start[1] + end[1]) / 2
    angle = math.degrees(math.atan2(dx, dz))
    shape.box(
        name,
        (center_x - width / 2, y_range[0], center_z - length / 2),
        (center_x + width / 2, y_range[1], center_z + length / 2),
        texture="#gw-oak",
        rotation_origin=(center_x, (y_range[0] + y_range[1]) / 2, center_z),
        rotation=(0, angle, 0),
        parent=mount,
        group="pump-downward-stand",
    )


def _add_compact_downward_stand(shape: Shape, mount: ElementRef) -> None:
    """Cradle a downward-facing pump on the block directly below it."""
    # Broad runners sit on the supporting block. They stop below the folded
    # process lines, while the four short legs live outside their Z envelope.
    for depth, z_range in (("front", (-6.60, -5.40)), ("back", (5.40, 6.60))):
        shape.box(
            f"gw-downward-stand-base-{depth}-runner",
            (-7.25, COMPACT_STAND_BOTTOM, z_range[0]),
            (7.25, COMPACT_STAND_BASE_TOP, z_range[1]),
            texture="#gw-oak",
            parent=mount,
            group="pump-downward-stand",
        )
    for side, x_range in (("left", (-6.60, -5.40)), ("right", (5.40, 6.60))):
        shape.box(
            f"gw-downward-stand-base-{side}-tie",
            (x_range[0], COMPACT_STAND_BOTTOM, -5.40),
            (x_range[1], COMPACT_STAND_BASE_TOP, 5.40),
            texture="#gw-oak",
            parent=mount,
            group="pump-downward-stand",
        )
        for depth, z_range in (("front", (-6.60, -5.40)), ("back", (5.40, 6.60))):
            shape.box(
                f"gw-downward-stand-{side}-{depth}-leg",
                (x_range[0], COMPACT_STAND_BASE_TOP, z_range[0]),
                (x_range[1], -19.25, z_range[1]),
                texture="#gw-oak",
                parent=mount,
                group="pump-downward-stand",
            )

    # The upper ring is a low vessel cradle, not a cage. Side members are
    # split around the descending process lines so no timber occupies a bore.
    for depth, z_range in (("front", (-4.35, -3.55)), ("back", (3.55, 4.35))):
        shape.box(
            f"gw-downward-stand-cradle-{depth}",
            (-5.25, COMPACT_STAND_CRADLE_BOTTOM, z_range[0]),
            (5.25, COMPACT_STAND_CRADLE_TOP, z_range[1]),
            texture="#gw-oak",
            parent=mount,
            group="pump-downward-stand",
        )
    for side, x_range in (("left", (-5.25, -4.45)), ("right", (4.45, 5.25))):
        for depth, z_range in (("front", (-3.55, -2.30)), ("back", (2.30, 3.55))):
            shape.box(
                f"gw-downward-stand-cradle-{side}-{depth}",
                (x_range[0], COMPACT_STAND_CRADLE_BOTTOM, z_range[0]),
                (x_range[1], COMPACT_STAND_CRADLE_TOP, z_range[1]),
                texture="#gw-oak",
                parent=mount,
                group="pump-downward-stand",
            )

    for index, (start, end) in enumerate((
        ((-6.0, -6.0), (-4.85, -3.95)),
        ((6.0, -6.0), (4.85, -3.95)),
        ((-6.0, 6.0), (-4.85, 3.95)),
        ((6.0, 6.0), (4.85, 3.95)),
    )):
        _add_xz_brace(
            shape,
            mount,
            f"gw-downward-stand-cradle-corner-strut-{index}",
            start,
            end,
            (COMPACT_STAND_CRADLE_BOTTOM, COMPACT_STAND_CRADLE_TOP),
            width=.90,
        )

    # Paired knees carry vertical load into both runners without covering the
    # glass or the central pipe folds. They are deliberately short and stout.
    for depth, z_range in (("front", (-6.28, -5.52)), ("back", (5.52, 6.28))):
        for side, start, end in (
            ("left", (-6.0, -23.10), (-4.85, -19.25)),
            ("right", (6.0, -23.10), (4.85, -19.25)),
        ):
            _add_xy_brace(
                shape,
                mount,
                f"gw-downward-stand-{depth}-knee-{side}",
                start,
                end,
                z_range,
                width=.92,
                group="pump-downward-stand",
            )

    for depth, z_range in (("front", (-6.50, -6.30)), ("back", (6.30, 6.50))):
        for side, center_x in (("left", -5.35), ("right", 5.35)):
            shape.box(
                f"gw-downward-stand-{depth}-{side}-joint-plate",
                (center_x - .48, -21.65, z_range[0]),
                (center_x + .48, -20.65, z_range[1]),
                texture="#gw-iron",
                parent=mount,
                group="pump-downward-stand-reinforcement",
            )

def _add_passive_check(shape: Shape, mount: ElementRef, name: str, *, seat_x: float, center_y: float, center_z: float = 0.0) -> ElementRef:
    """Add a full-passage swing check contained entirely inside one straight valve chest."""
    prefix = f"gw-{name}"
    outer, opening = 1.5, 1.10
    for part, y_range, z_range in (
        ("seat-top", (center_y + opening, center_y + outer), (center_z - outer, center_z + outer)),
        ("seat-bottom", (center_y - outer, center_y - opening), (center_z - outer, center_z + outer)),
        ("seat-front", (center_y - opening, center_y + opening), (center_z - outer, center_z - opening)),
        ("seat-back", (center_y - opening, center_y + opening), (center_z + opening, center_z + outer)),
    ):
        shape.box(f"{prefix}-{part}", (seat_x - .13, y_range[0], z_range[0]), (seat_x + .13, y_range[1], z_range[1]), texture="#gw-bronze", parent=mount, group="pump-passive-checks")
    hinge_y = center_y + opening
    flap = _pivot(shape, f"{prefix}-motion", (seat_x, hinge_y, center_z), parent=mount, group="pump-passive-checks")
    shape.box(f"{prefix}-flap", (-.16, -2.15, -opening + .02), (.16, -.10, opening - .02), texture="#gw-bronze", parent=flap, group="pump-passive-checks")
    shape.box(f"{prefix}-hinge-pin", (seat_x - .28, hinge_y - .16, center_z - 1.32), (seat_x + .28, hinge_y + .16, center_z + 1.32), texture="#gw-iron", parent=mount, group="pump-passive-checks")
    shape.box(f"{prefix}-travel-stop", (seat_x + .28, center_y - .52, center_z - 1.28), (seat_x + .55, center_y - .14, center_z + 1.28), texture="#gw-iron", parent=mount, group="pump-passive-checks")
    return flap


def _add_passive_poppet(shape: Shape, mount: ElementRef, name: str, *, seat_x: float, center_y: float, center_z: float = 0.0) -> ElementRef:
    """Add a full-passage lift disc whose guide is carried by the downstream stop spider."""
    prefix = f"gw-{name}"
    outer, opening = 1.5, 1.10
    for part, y_range, z_range in (
        ("seat-top", (center_y + opening, center_y + outer), (center_z - outer, center_z + outer)),
        ("seat-bottom", (center_y - outer, center_y - opening), (center_z - outer, center_z + outer)),
        ("seat-front", (center_y - opening, center_y + opening), (center_z - outer, center_z - opening)),
        ("seat-back", (center_y - opening, center_y + opening), (center_z + opening, center_z + outer)),
    ):
        shape.box(f"{prefix}-{part}", (seat_x - .13, y_range[0], z_range[0]), (seat_x + .13, y_range[1], z_range[1]), texture="#gw-bronze", parent=mount, group="pump-passive-checks")

    # The seat ring itself anchors the entrance end. A second iron spider here
    # crossed the moving disc, so only the downstream stop carries the guide.
    for support, x_range in (
        ("stop", (seat_x + .98, seat_x + 1.15)),
    ):
        shape.box(
            f"{prefix}-{support}-spider-y",
            (x_range[0], center_y - opening, center_z - .09),
            (x_range[1], center_y + opening, center_z + .09),
            texture="#gw-iron",
            parent=mount,
            group="pump-valve-guide",
        )
        shape.box(
            f"{prefix}-{support}-spider-z",
            (x_range[0], center_y - .09, center_z - opening),
            (x_range[1], center_y + .09, center_z + opening),
            texture="#gw-iron",
            parent=mount,
            group="pump-valve-guide",
        )
        shape.box(
            f"{prefix}-{support}-bushing",
            (x_range[0] - .03, center_y - .24, center_z - .24),
            (x_range[1] + .03, center_y + .24, center_z + .24),
            texture="#gw-bronze",
            parent=mount,
            group="pump-valve-guide",
        )

    for index, (y_offset, z_offset) in enumerate(((-1.38, -1.38), (-1.38, 1.20), (1.20, -1.38), (1.20, 1.20))):
        shape.box(
            f"{prefix}-cage-rail-{index}",
            (seat_x + .05, center_y + y_offset, center_z + z_offset),
            (seat_x + 1.08, center_y + y_offset + .18, center_z + z_offset + .18),
            texture="#gw-iron",
            parent=mount,
            group="pump-passive-checks",
        )
    for part, y_range, z_range in (
        ("top", (center_y + opening, center_y + outer), (center_z - outer, center_z + outer)),
        ("bottom", (center_y - outer, center_y - opening), (center_z - outer, center_z + outer)),
        ("front", (center_y - opening, center_y + opening), (center_z - outer, center_z - opening)),
        ("back", (center_y - opening, center_y + opening), (center_z + opening, center_z + outer)),
    ):
        shape.box(
            f"{prefix}-cage-stop-{part}",
            (seat_x + .98, y_range[0], z_range[0]),
            (seat_x + 1.15, y_range[1], z_range[1]),
            texture="#gw-iron",
            parent=mount,
            group="pump-passive-checks",
        )
    shape.box(
        f"{prefix}-guide-rod",
        (seat_x + .15, center_y - .13, center_z - .13),
        (seat_x + 1.08, center_y + .13, center_z + .13),
        texture="#gw-bronze",
        parent=mount,
        group="pump-valve-guide",
    )

    motion = _pivot(shape, f"{prefix}-motion", (seat_x + .08, center_y, center_z), parent=mount, group="pump-passive-checks")
    shape.box(f"{prefix}-disc", (-.13, -opening + .05, -opening + .05), (.13, opening - .05, opening - .05), texture="#gw-bronze", parent=motion, group="pump-passive-checks")
    shape.box(f"{prefix}-guide-sleeve", (-.15, -.19, -.19), (.28, .19, .19), texture="#gw-iron", parent=motion, group="pump-valve-guide")
    shape.box(
        f"{prefix}-glass-witness",
        (-.07, -.90, -1.29),
        (.23, .90, -1.01),
        texture="#gw-bronze",
        parent=motion,
        group="pump-valve-indicator",
    )
    return motion


def _add_vertical_passive_poppet(
    shape: Shape,
    mount: ElementRef,
    name: str,
    *,
    center_x: float,
    center_y: float,
    direction: int,
) -> ElementRef:
    """Add a compact floor-port check that opens vertically with the process flow."""
    if direction not in (-1, 1):
        raise ValueError("vertical check direction must be -1 or 1")
    prefix = f"gw-{name}"
    outer, opening = 1.5, 1.10
    for part, x_range, z_range in (
        ("seat-front", (center_x - outer, center_x + outer), (-outer, -opening)),
        ("seat-back", (center_x - outer, center_x + outer), (opening, outer)),
        ("seat-left", (center_x - outer, center_x - opening), (-opening, opening)),
        ("seat-right", (center_x + opening, center_x + outer), (-opening, opening)),
    ):
        shape.box(
            f"{prefix}-{part}",
            (x_range[0], center_y - .11, z_range[0]),
            (x_range[1], center_y + .11, z_range[1]),
            texture="#gw-bronze",
            parent=mount,
            group="pump-passive-checks",
        )

    stop_y = center_y + direction * .62
    support_y0, support_y1 = sorted((stop_y - .085, stop_y + .085))
    shape.box(
        f"{prefix}-stop-spider-x",
        (center_x - opening, support_y0, -.09),
        (center_x + opening, support_y1, .09),
        texture="#gw-iron",
        parent=mount,
        group="pump-valve-guide",
    )
    shape.box(
        f"{prefix}-stop-spider-z",
        (center_x - .09, support_y0, -opening),
        (center_x + .09, support_y1, opening),
        texture="#gw-iron",
        parent=mount,
        group="pump-valve-guide",
    )
    shape.box(
        f"{prefix}-stop-bushing",
        (center_x - .22, support_y0 - .03, -.22),
        (center_x + .22, support_y1 + .03, .22),
        texture="#gw-bronze",
        parent=mount,
        group="pump-valve-guide",
    )

    rail_y0, rail_y1 = sorted((center_y + direction * .05, stop_y))
    for index, (x_offset, z_offset) in enumerate(((-1.38, -1.38), (-1.38, 1.20), (1.20, -1.38), (1.20, 1.20))):
        shape.box(
            f"{prefix}-cage-rail-{index}",
            (center_x + x_offset, rail_y0, z_offset),
            (center_x + x_offset + .18, rail_y1, z_offset + .18),
            texture="#gw-iron",
            parent=mount,
            group="pump-passive-checks",
        )

    guide_y0, guide_y1 = sorted((center_y + direction * .13, stop_y))
    shape.box(
        f"{prefix}-guide-rod",
        (center_x - .13, guide_y0, -.13),
        (center_x + .13, guide_y1, .13),
        texture="#gw-bronze",
        parent=mount,
        group="pump-valve-guide",
    )

    motion = _pivot(
        shape,
        f"{prefix}-motion",
        (center_x, center_y + direction * .07, 0.0),
        parent=mount,
        group="pump-passive-checks",
    )
    shape.box(
        f"{prefix}-disc",
        (-opening + .05, -.13, -opening + .05),
        (opening - .05, .13, opening - .05),
        texture="#gw-bronze",
        parent=motion,
        group="pump-passive-checks",
    )
    shape.box(
        f"{prefix}-guide-sleeve",
        (-.19, -.19, -.19),
        (.19, .19, .19),
        texture="#gw-iron",
        parent=motion,
        group="pump-valve-guide",
    )
    shape.box(
        f"{prefix}-glass-witness",
        (-.82, -.07, -1.29),
        (.82, .12, -1.01),
        texture="#gw-bronze",
        parent=motion,
        group="pump-valve-indicator",
    )
    return motion

def _add_air_poppet(
    shape: Shape,
    mount: ElementRef,
    *,
    mode: str,
    center_z: float,
    direction: int,
    cutaway: bool,
) -> ElementRef:
    """Seat one compact vertical check directly inside a roof opening."""
    if direction not in (-1, 1):
        raise ValueError("roof air check direction must be -1 or 1")
    prefix = f"gw-breather-{mode}"
    center_x, seat_y = AIR_ROOF_CENTER_X, AIR_ROOF_SEAT_Y
    outer, opening = .66, AIR_ROOF_BORE_HALF

    for part, x_range, z_range in (
        ("seat-front", (center_x - outer, center_x + outer), (center_z - outer, center_z - opening)),
        ("seat-back", (center_x - outer, center_x + outer), (center_z + opening, center_z + outer)),
        ("seat-left", (center_x - outer, center_x - opening), (center_z - opening, center_z + opening)),
        ("seat-right", (center_x + opening, center_x + outer), (center_z - opening, center_z + opening)),
    ):
        if cutaway and center_z < 0 and part == "seat-front":
            continue
        shape.box(
            f"{prefix}-{part}",
            (x_range[0], seat_y - .08, z_range[0]),
            (x_range[1], seat_y + .08, z_range[1]),
            texture="#gw-bronze",
            parent=mount,
            group="pump-breather",
        )

    stop_y = seat_y + direction * .55
    support_y0, support_y1 = sorted((stop_y - .075, stop_y + .075))
    shape.box(
        f"{prefix}-stop-spider-x",
        (center_x - opening, support_y0, center_z - .07),
        (center_x + opening, support_y1, center_z + .07),
        texture="#gw-iron",
        parent=mount,
        group="pump-breather-guide",
    )
    shape.box(
        f"{prefix}-stop-spider-z",
        (center_x - .07, support_y0, center_z - opening),
        (center_x + .07, support_y1, center_z + opening),
        texture="#gw-iron",
        parent=mount,
        group="pump-breather-guide",
    )
    shape.box(
        f"{prefix}-stop-bushing",
        (center_x - .17, support_y0 - .02, center_z - .17),
        (center_x + .17, support_y1 + .02, center_z + .17),
        texture="#gw-bronze",
        parent=mount,
        group="pump-breather-guide",
    )

    rail_y0, rail_y1 = sorted((seat_y + direction * .04, stop_y))
    for index, (x_offset, z_offset) in enumerate(((-.60, -.60), (-.60, .46), (.46, -.60), (.46, .46))):
        if cutaway and center_z < 0 and z_offset < 0:
            continue
        shape.box(
            f"{prefix}-cage-rail-{index}",
            (center_x + x_offset, rail_y0, center_z + z_offset),
            (center_x + x_offset + .14, rail_y1, center_z + z_offset + .14),
            texture="#gw-iron",
            parent=mount,
            group="pump-breather",
        )

    guide_y0, guide_y1 = sorted((seat_y + direction * .10, stop_y))
    shape.box(
        f"{prefix}-guide-rod",
        (center_x - .08, guide_y0, center_z - .08),
        (center_x + .08, guide_y1, center_z + .08),
        texture="#gw-bronze",
        parent=mount,
        group="pump-breather-guide",
    )

    motion = _pivot(
        shape,
        f"{prefix}-check-motion",
        (center_x, seat_y + direction * .04, center_z),
        parent=mount,
        group="pump-breather",
    )
    shape.box(
        f"{prefix}-check-disc",
        (-opening + .04, -.10, -opening + .04),
        (opening - .04, .10, opening - .04),
        texture="#gw-bronze",
        parent=motion,
        group="pump-breather",
    )
    shape.box(
        f"{prefix}-guide-sleeve",
        (-.13, -.13, -.13),
        (.13, .17, .13),
        texture="#gw-iron",
        parent=motion,
        group="pump-breather-guide",
    )
    return motion


def _add_singleacting_breather(
    shape: Shape,
    mount: ElementRef,
    *,
    cutaway: bool,
    body_half_x: float,
) -> dict[str, ElementRef]:
    """Integrate paired vertical checks into the output half of the ceiling."""
    if AIR_ROOF_CENTER_X + AIR_ROOF_COLLAR_HALF > body_half_x:
        raise ValueError("roof air checks must remain inside the pressure-head width")

    valve_specs = (
        ("intake", AIR_ROOF_CENTER_Z, -1),
        ("exhaust", -AIR_ROOF_CENTER_Z, 1),
    )
    for mode, center_z, _direction in valve_specs:
        remove_front = cutaway and center_z < 0
        _hollow_y_section(
            shape,
            mount,
            f"gw-breather-{mode}-roof-collar",
            (SINGLE_CYLINDER_TOP - .12, SINGLE_HEAD_TOP + .20),
            AIR_ROOF_CENTER_X,
            half_x=AIR_ROOF_COLLAR_HALF,
            half_z=AIR_ROOF_COLLAR_HALF,
            wall=AIR_ROOF_COLLAR_WALL,
            group="pump-breather-head",
            cutaway=remove_front,
            center_z=center_z,
        )

        flange_outer, flange_inner = .86, AIR_ROOF_COLLAR_HALF
        for part, x_range, z_range in (
            ("front", (AIR_ROOF_CENTER_X - flange_outer, AIR_ROOF_CENTER_X + flange_outer), (center_z - flange_outer, center_z - flange_inner)),
            ("back", (AIR_ROOF_CENTER_X - flange_outer, AIR_ROOF_CENTER_X + flange_outer), (center_z + flange_inner, center_z + flange_outer)),
            ("left", (AIR_ROOF_CENTER_X - flange_outer, AIR_ROOF_CENTER_X - flange_inner), (center_z - flange_inner, center_z + flange_inner)),
            ("right", (AIR_ROOF_CENTER_X + flange_inner, AIR_ROOF_CENTER_X + flange_outer), (center_z - flange_inner, center_z + flange_inner)),
        ):
            if remove_front and part == "front":
                continue
            shape.box(
                f"gw-breather-{mode}-roof-flange-{part}",
                (x_range[0], SINGLE_HEAD_TOP, z_range[0]),
                (x_range[1], SINGLE_HEAD_TOP + .12, z_range[1]),
                texture="#gw-bronze",
                parent=mount,
                group="pump-breather-head",
            )

    return {
        mode: _add_air_poppet(
            shape,
            mount,
            mode=mode,
            center_z=center_z,
            direction=direction,
            cutaway=cutaway,
        )
        for mode, center_z, direction in valve_specs
    }

def _add_singleacting_piston(shape: Shape, mount: ElementRef, initial_crosshead_y: float) -> ElementRef:
    moving = _pivot(shape, "gw-piston-motion", (0, initial_crosshead_y, 0), parent=mount, group="pump-motion")
    for side, x_range in (("left", (-4.05, -1.45)), ("right", (1.45, 4.05))):
        shape.box(f"gw-crosshead-shoe-{side}", (x_range[0], -.55, -.55), (x_range[1], .55, .55), texture="#gw-iron", parent=moving, group="pump-motion")
    shape.box("gw-crosshead-front-bridge", (-1.45, -.55, -1.35), (1.45, .55, -1.05), texture="#gw-iron", parent=moving, group="pump-motion")
    shape.box("gw-crosshead-back-bridge", (-1.45, -.55, 1.05), (1.45, .55, 1.35), texture="#gw-iron", parent=moving, group="pump-motion")
    shape.box("gw-crosshead-pin", (-1.32, -.5, -.5), (1.32, .5, .5), texture="#gw-bronze", parent=moving, group="pump-bearing")
    shape.box("gw-piston-rod", (-.42, -7.75, -.42), (.42, -.62, .42), texture="#gw-bronze", parent=moving, group="pump-motion")
    shape.box("gw-piston-plate", (-3.85, -8.75, -3.05), (3.85, -7.75, 3.05), texture="#gw-bronze", parent=moving, group="pump-piston")
    shape.box("gw-piston-upper-seal", (-4.0, -7.92, -3.20), (4.0, -7.62, 3.20), texture="#gw-iron", parent=moving, group="pump-piston")
    shape.box("gw-piston-lower-seal", (-4.0, -8.78, -3.20), (4.0, -8.48, 3.20), texture="#gw-iron", parent=moving, group="pump-piston")
    return moving


def _add_exact_pipe_block(shape: Shape, prefix: str, *, center_x: float, center_y: float) -> None:
    """Reproduce one complete straight Gearwright pipe block from the approved 4x4 tube and 5x5 half-couplings."""
    def box(name: str, from_: tuple[float, float, float], to: tuple[float, float, float], *, group: str) -> None:
        shape.box(
            f"{prefix}-{name}",
            (center_x + from_[0], center_y + from_[1], from_[2]),
            (center_x + to[0], center_y + to[1], to[2]),
            texture="#gw-copper",
            group=group,
        )

    for y0 in (-2.0, 1.5):
        for z0 in (-2.0, 1.5):
            box(f"center-x-{y0:g}-{z0:g}", (-2, y0, z0), (2, y0 + .5, z0 + .5), group="pipe-fit-center")
    for x0 in (-2.0, 1.5):
        for z0 in (-2.0, 1.5):
            box(f"center-y-{x0:g}-{z0:g}", (x0, -1.5, z0), (x0 + .5, 1.5, z0 + .5), group="pipe-fit-center")
    for x0 in (-2.0, 1.5):
        for y0 in (-2.0, 1.5):
            box(f"center-z-{x0:g}-{y0:g}", (x0, y0, -1.5), (x0 + .5, y0 + .5, 1.5), group="pipe-fit-center")

    for face, from_, to in (
        ("cap-bottom", (-1.5, -2.0, -1.5), (1.5, -1.5, 1.5)),
        ("cap-top", (-1.5, 1.5, -1.5), (1.5, 2.0, 1.5)),
        ("cap-front", (-1.5, -1.5, -2.0), (1.5, 1.5, -1.5)),
        ("cap-back", (-1.5, -1.5, 1.5), (1.5, 1.5, 2.0)),
    ):
        box(face, from_, to, group="pipe-fit-caps")

    for side, x0, x1, coupling0, coupling1 in (
        ("left", -8.0, -2.0, -8.0, -7.35),
        ("right", 2.0, 8.0, 7.35, 8.0),
    ):
        for part, from_, to in (
            ("bottom", (x0, -2.0, -2.0), (x1, -1.5, 2.0)),
            ("top", (x0, 1.5, -2.0), (x1, 2.0, 2.0)),
            ("front", (x0, -1.5, -2.0), (x1, 1.5, -1.5)),
            ("back", (x0, -1.5, 1.5), (x1, 1.5, 2.0)),
        ):
            box(f"{side}-arm-{part}", from_, to, group="pipe-fit-arms")
        for part, from_, to in (
            ("bottom", (coupling0, -2.5, -2.5), (coupling1, -2.0, 2.5)),
            ("top", (coupling0, 2.0, -2.5), (coupling1, 2.5, 2.5)),
            ("front", (coupling0, -2.0, -2.5), (coupling1, 2.0, -2.0)),
            ("back", (coupling0, -2.0, 2.0), (coupling1, 2.0, 2.5)),
        ):
            box(f"{side}-half-coupling-{part}", from_, to, group="pipe-fit-couplings")


def _add_pipe_fit_blocks(shape: Shape, *, center_x: float, center_y: float) -> None:
    _add_exact_pipe_block(shape, "gw-fit-input-pipe-block", center_x=-center_x, center_y=center_y)
    _add_exact_pipe_block(shape, "gw-fit-output-pipe-block", center_x=center_x, center_y=center_y)


def _add_singleacting_animation(
    shape: Shape,
    crank_phase: ElementRef,
    piston: ElementRef,
    rod: ElementRef,
    wet_checks: dict[str, ElementRef],
    breathers: dict[str, ElementRef],
    *,
    base_angle: float,
    wet_mode: str,
) -> None:
    _, _, initial_crosshead, initial_middle_y, initial_middle_z, _ = _slider_state(
        0,
        radius=SINGLE_CRANK_THROW,
        rod_length=SINGLE_ROD_LENGTH,
    )
    cycle = animate(
        shape,
        "Single-acting wet end with in-pipe process checks and paired output-side air checks",
        "singleacting",
        61,
        version=1,
        on_activity_stopped="EaseOut",
        on_animation_end="Repeat",
    )
    for frame in range(0, 61, 5):
        phase = frame * 6.0
        _, _, crosshead, middle_y, middle_z, rod_angle = _slider_state(
            phase,
            radius=SINGLE_CRANK_THROW,
            rod_length=SINGLE_ROD_LENGTH,
        )
        sine = math.sin(math.radians(phase))
        if abs(sine) < 1e-8:
            sine = 0.0
        pressure_stroke = max(0.0, sine)
        suction_stroke = max(0.0, -sine)

        cycle.keyframe(frame, crank_phase, rotationX=base_angle + phase, rotShortestDistanceX=False)
        cycle.keyframe(frame, piston, offsetY=crosshead - initial_crosshead)
        cycle.keyframe(
            frame,
            rod,
            offsetY=middle_y - initial_middle_y,
            offsetZ=middle_z - initial_middle_z,
            rotationX=rod_angle,
            rotShortestDistanceX=True,
        )
        if wet_mode == "poppet":
            cycle.keyframe(frame, wet_checks["intake"], offsetX=SINGLE_CHECK_TRAVEL * suction_stroke)
            cycle.keyframe(frame, wet_checks["output"], offsetX=SINGLE_CHECK_TRAVEL * pressure_stroke)
        elif wet_mode == "vertical-poppet":
            cycle.keyframe(frame, wet_checks["intake"], offsetY=COMPACT_VERTICAL_CHECK_TRAVEL * suction_stroke)
            cycle.keyframe(frame, wet_checks["output"], offsetY=-COMPACT_VERTICAL_CHECK_TRAVEL * pressure_stroke)
        else:
            cycle.keyframe(frame, wet_checks["intake"], rotationZ=SINGLE_CHECK_ANGLE * suction_stroke, rotShortestDistanceZ=True)
            cycle.keyframe(frame, wet_checks["output"], rotationZ=SINGLE_CHECK_ANGLE * pressure_stroke, rotShortestDistanceZ=True)
        cycle.keyframe(frame, breathers["intake"], offsetY=-AIR_CHECK_TRAVEL * pressure_stroke)
        cycle.keyframe(frame, breathers["exhaust"], offsetY=AIR_CHECK_TRAVEL * suction_stroke)
    shape.add_animation(cycle.build())


def _singleacting_pump_shape(candidate: str, *, mount_angle: float = 0, cutaway: bool = False, label: str) -> Shape:
    shape = _shape(f"lateral-motion-{candidate}-{label}")
    crank_phase = _add_crank(shape, through=False, base_angle=mount_angle, throw=SINGLE_CRANK_THROW)
    mount = _pivot(shape, "gw-pump-mount", (0, 0, 0), rotation=(mount_angle, 0, 0), group="pump")
    tall_layout = candidate == "a10-two-block-bottom-pipes"
    compact_full_frame = candidate == "a11-one-block-full-frame"
    bottom_fed = tall_layout or compact_full_frame
    pressure_half_x = COMPACT_CYLINDER_HALF_X if compact_full_frame else SINGLE_CYLINDER_HALF_X
    _add_singleacting_pressure_body(
        shape,
        mount,
        cutaway=cutaway,
        bottom_ports=bottom_fed,
        half_x=pressure_half_x,
    )
    if tall_layout:
        _add_singleacting_bottom_ports(shape, mount, cutaway=cutaway)
        _add_tall_pump_frame(shape, mount)
    elif compact_full_frame:
        _add_compact_bottom_loop_ports(shape, mount, cutaway=cutaway)
        supported_downward_state = mount_angle % 360 == 0 and label in {"pump-bottom", "pump-cutaway", "pipe-fit"}
        if supported_downward_state:
            _add_compact_downward_stand(shape, mount)
    else:
        _add_singleacting_ports(shape, mount, cutaway=cutaway)


    if compact_full_frame:
        wet_mode = "vertical-poppet"
        wet_checks = {
            "intake": _add_vertical_passive_poppet(
                shape,
                mount,
                "wet-intake-check",
                center_x=-BOTTOM_RESERVOIR_PORT_X,
                center_y=COMPACT_INTAKE_CHECK_CENTER_Y,
                direction=1,
            ),
            "output": _add_vertical_passive_poppet(
                shape,
                mount,
                "wet-output-check",
                center_x=BOTTOM_RESERVOIR_PORT_X,
                center_y=COMPACT_OUTPUT_CHECK_CENTER_Y,
                direction=-1,
            ),
        }
    else:
        wet_mode = "poppet"
        wet_center_y = TALL_PIPE_CENTER_Y if tall_layout else SINGLE_PIPE_CENTER_Y
        wet_checks = {
            "intake": _add_passive_poppet(
                shape,
                mount,
                "wet-intake-check",
                seat_x=SINGLE_INTAKE_VALVE_SEAT_X,
                center_y=wet_center_y,
            ),
            "output": _add_passive_poppet(
                shape,
                mount,
                "wet-output-check",
                seat_x=SINGLE_OUTPUT_VALVE_SEAT_X,
                center_y=wet_center_y,
            ),
        }
    breathers = _add_singleacting_breather(shape, mount, cutaway=cutaway, body_half_x=pressure_half_x)

    _, _, initial_crosshead, initial_middle_y, initial_middle_z, _ = _slider_state(
        0,
        radius=SINGLE_CRANK_THROW,
        rod_length=SINGLE_ROD_LENGTH,
    )
    piston = _add_singleacting_piston(shape, mount, initial_crosshead)
    rod = _add_connecting_rod(shape, mount, initial_middle_y, initial_middle_z, rod_length=SINGLE_ROD_LENGTH)
    _add_singleacting_animation(shape, crank_phase, piston, rod, wet_checks, breathers, base_angle=mount_angle, wet_mode=wet_mode)
    return shape


def _passive_check_detail_shape(candidate: str) -> Shape:
    shape = _shape(f"lateral-motion-{candidate}-valve-detail")
    mount = _pivot(shape, "gw-valve-detail-mount", (0, 0, 0), group="pump")
    vertical = candidate == "a11-one-block-full-frame"
    if vertical:
        for side, center_x in (("input", -2.5), ("output", 2.5)):
            _hollow_y_section(
                shape,
                mount,
                f"gw-detail-wet-{side}-riser",
                (-4.0, 4.0),
                center_x,
                half_x=2.0,
                half_z=2.0,
                wall=.35,
                group="pump-wet-end",
                cutaway=True,
            )
        wet_mode = "vertical-poppet"
        wet = {
            "intake": _add_vertical_passive_poppet(
                shape,
                mount,
                "detail-wet-intake-check",
                center_x=-2.5,
                center_y=0.0,
                direction=1,
            ),
            "output": _add_vertical_passive_poppet(
                shape,
                mount,
                "detail-wet-output-check",
                center_x=2.5,
                center_y=0.0,
                direction=-1,
            ),
        }
    else:
        _hollow_x_section(
            shape,
            mount,
            "gw-detail-wet-gallery",
            (-6.0, 6.0),
            0.0,
            half_y=1.65,
            half_z=1.85,
            wall=.35,
            group="pump-wet-end",
            cutaway=True,
        )
        wet_mode = "poppet"
        wet = {
            "intake": _add_passive_poppet(shape, mount, "detail-wet-intake-check", seat_x=-2.25, center_y=0.0),
            "output": _add_passive_poppet(shape, mount, "detail-wet-output-check", seat_x=2.25, center_y=0.0),
        }

    motion = animate(
        shape,
        "Passive wet checks alternate between pressure and suction strokes",
        "passivechecks",
        41,
        version=1,
        on_activity_stopped="EaseOut",
        on_animation_end="Repeat",
    )
    for frame, pressure, suction in ((0, 0.0, 0.0), (10, 1.0, 0.0), (20, 0.0, 0.0), (30, 0.0, 1.0), (40, 0.0, 0.0)):
        if wet_mode == "vertical-poppet":
            motion.keyframe(frame, wet["intake"], offsetY=COMPACT_VERTICAL_CHECK_TRAVEL * suction)
            motion.keyframe(frame, wet["output"], offsetY=-COMPACT_VERTICAL_CHECK_TRAVEL * pressure)
        else:
            motion.keyframe(frame, wet["intake"], offsetX=SINGLE_CHECK_TRAVEL * suction)
            motion.keyframe(frame, wet["output"], offsetX=SINGLE_CHECK_TRAVEL * pressure)
    shape.add_animation(motion.build())
    return shape

def _pipe_fit_shape(candidate: str, pump_builder) -> Shape:
    shape = pump_builder(candidate, label="pipe-fit")
    if candidate == "a10-two-block-bottom-pipes":
        _add_pipe_fit_blocks(shape, center_x=16.0, center_y=TALL_PIPE_CENTER_Y)
    else:
        singleacting = candidate in ("a9-single-acting-caged-poppets", "a11-one-block-full-frame")
        center_y = SINGLE_PIPE_CENTER_Y if singleacting else PIPE_CENTER_Y
        center_x = 16.0 if singleacting else 19.5
        _add_pipe_fit_blocks(shape, center_x=center_x, center_y=center_y)
    return shape


def _candidate_shapes(candidate: str) -> OrderedDict[str, Shape]:
    singleacting = candidate in ("a9-single-acting-caged-poppets", "a10-two-block-bottom-pipes", "a11-one-block-full-frame")
    if singleacting:
        pump_builder = _singleacting_pump_shape
        detail = _passive_check_detail_shape(candidate)
    else:
        pump_builder = _pump_shape
        detail = _valve_detail_shape(candidate)
    crank_throw = SINGLE_CRANK_THROW if singleacting else 4.0
    return OrderedDict((
        ("one-axle-crank", _crank_state(through=False, throw=crank_throw)),
        ("through-crank", _crank_state(through=True, throw=crank_throw)),
        ("pump-bottom", pump_builder(candidate, label="pump-bottom")),
        ("pump-cutaway", pump_builder(candidate, cutaway=True, label="pump-cutaway")),
        ("pump-top-fit", pump_builder(candidate, mount_angle=180, label="pump-top-fit")),
        ("pump-side-fit", pump_builder(candidate, mount_angle=90, label="pump-side-fit")),
        ("pipe-fit", _pipe_fit_shape(candidate, pump_builder)),
        ("valve-detail", detail),
    ))

def build_review(root: Path) -> Path:
    root = root.resolve()
    review_root = (root / REVIEW_ROOT).resolve()
    managed, staging, backup = (review_root / name for name in (MANAGED_NAME, STAGING_NAME, BACKUP_NAME))
    if not all(_inside(root, path) for path in (review_root, managed, staging, backup)):
        raise ValueError("review output must remain inside the project")
    review_root.mkdir(parents=True, exist_ok=True)
    for transient in (staging, backup):
        if transient.exists():
            shutil.rmtree(transient)

    package = ModelPackage("lateral_motion_spectacle_pump_review")
    for candidate in DESCRIPTIONS:
        for stage, shape in _candidate_shapes(candidate).items():
            package.shape(shape, f"{REVIEW_ROOT.as_posix()}/{STAGING_NAME}/{candidate}/{stage}.shape.json")
    build_package(package, root, write=True)

    readme = [
        "# One-axle crank spectacle-pump alternatives",
        "",
        "Decision: A11 approved for runtime promotion. A7 remains the timed double-acting reference, A9 the unframed compact baseline, and A10 the two-block service-gallery alternative. The automatic large bellows remains shelved.",
        "",
        "Shared requirements",
        "",
        "- The one-axle-crank state has one connected shaft, one cantilever web, and one short retained journal. It has no output shaft and no far crank cheek.",
        "- The through-crank state shows the supported two-axle form only when both axial connections exist.",
        "- A7 retains its 4-unit throw, 8-unit rod, and 8-unit stroke. A9, A10, and A11 use a 3-unit throw, 6-unit rod, and 6-unit stroke so the piston reaches the lower edge of the unchanged visible bore. All three retain the nearly full-bore 8-unit piston seal.",
        "- The review contains no visible liquid sheets. The transparent cylinder shows piston travel without implying a preferred transported liquid.",
        "- A9 and A11 occupy one pump block and terminate their process pipes on the side faces at y=-16. A9 enters the chamber directly. Each A11 line turns into a flush shared-wall drop, runs inward at y=-21.25, and turns up through its own uncapped 3-by-3 reservoir-floor opening. A10 occupies two pump blocks from y=-8 to y=-40; its floor risers turn once and terminate on the lower block side faces at y=-32.",
        "- The pipe-fit stage adds complete neighboring 16-unit Gearwright pipe blocks centered at x=-16 and x=16. A9 and A11 align them at y=-16 and A10 at y=-32, so every 4-by-4 tube and 5-by-5 half-coupling meets the center of its block face exactly.",
        "",
        "Candidate behavior",
        "",
        "- A7 keeps the compact rear rack, twelve-tooth pinion, common selector shaft, lost-motion rocker, external-bore rods, and four actively timed clappers. Use doubleacting on pump states and valvegear on its detail state.",
        "- A9, A10, and A11 remove the complete timing train and both side valve compartments. A9 keeps chamber walls built from four pieces around the clear 3-by-3 side openings. A10 and A11 close those side walls and split the bottom head around two clear 3-by-3 reservoir-floor openings.",
        "- On the downstroke, the wet outlet check and atmospheric intake check open. On the upstroke, the wet inlet check and atmospheric exhaust check open. All four checks are pressure-operated; there is no rack, gear, rocker, tappet, or valve rod.",
        "- A9 uses horizontal lift discs in its centered side pipes. A10 places horizontal checks in the lower gallery after the bottom-fed risers turn outward. A11 places opposed vertical checks at the chamber floor: its slightly recessed inlet lifts into the chamber on suction without entering the piston envelope, while the outlet drops into its riser on pressure. Every seat anchors the entrance end and only the downstream stop spider supports the guide. The cutaway removes the front pipe, drop, and riser walls so each complete path can be inspected.",
        "- All three single-acting candidates integrate two half-scale vertical air checks into the output half of the pressure ceiling. Eight roof plates leave exact openings for the central rod well and both copper valve collars instead of hiding passages inside solid metal. The atmospheric intake remains almost flush above the roof and opens downward into a short internal cage on the downstroke; the exhaust uses a short exposed cage and opens upward on the upstroke. Neither has a weather cap. The previous side branches are removed and the output chamber wall is solid. The complete four-piece terminal rim of the main output coupling is bronze, repeating the same side cue without changing its bore, wall thickness, or block-face alignment. The recessed rod well keeps its flush bronze stuffing plate and close liner, and the guide feet remain clear of both roof valves through the full stroke. A10 adds one-unit oak corner posts, tied rails, a full rear X, front knee triangles, vessel-sill braces, and iron joint plates without enlarging the pressure vessel. A11 replaces the permanent cage with a low oak cradle: broad runners, four short legs, split pipe-clearing saddles, corner struts, paired knees, and local iron joint plates. It is present only in the supported downward-facing pump-bottom, cutaway, and pipe-fit states; the upward- and side-facing states are unframed.",
        "- Use singleacting at frames 0, 15, 30, and 45. The passivechecks detail isolates the wet outlet opening at frame 10 and wet inlet opening at frame 30.",
        "",
        "Candidates",
        "",
    ]
    readme.extend(f"- {candidate} - {description}" for candidate, description in DESCRIPTIONS.items())
    readme.extend([
        "",
        "Material choices",
        "",
        "- Oak forms the low-speed shaft, A10 full-height support frame, and A11 low downward-support cradle.",
        "- Iron carries the crank web, connecting rod, crosshead, guide rails, seals, and hinge pins. A7 alone uses iron for the compact timing train.",
        "- Bronze carries the crank journal, piston plate, guided valve discs and seats, bearing surfaces, A7 timing contacts, and the complete terminal rim on each single-acting output coupling.",
        "- Copper forms the cylinder, A9 four-piece process penetrations, A10/A11 split bottom heads, seamless hollow pipes, input couplings, and roof-valve collars because those parts need corrosion resistance rather than loaded teeth.",
        "- Guarded glass is confined to the central piston chamber and reaches the bottom head.",
        "",
        "Compare A10 and A11 from the front, back, both sides, top, and cutaway views. A10 tests readable bottom-fed plumbing and a two-block service gallery. A11 tests minimum height, three-bend side-to-floor pipes, opposed vertical floor checks, and a low cradle that transfers load into the block below without becoming a permanent cage. Compare pump-bottom with pump-top-fit and pump-side-fit to verify that the stand disappears outside the supported downward state. From the top and output-side views, inspect the two real ceiling cutouts, flush intake, raised exhaust cage, solid chamber wall, and fully bronze output coupling. Scrub singleacting frames 15, 30, and 45 to check the inward valve-to-piston, crosshead-to-roof, connecting-bearing-to-gland, piston, and wet-check clearances.",
        "",
    ])
    (staging / "README.md").write_text("\n".join(readme) + "\n", encoding="utf-8")
    ownership = OrderedDict((
        ("version", 1),
        ("purpose", "approval-only one-axle crank and spectacle-pump alternatives; never packaged"),
        ("source", "graphics/review/lateral_motion_system.py"),
        ("managedPath", f"{REVIEW_ROOT.as_posix()}/{MANAGED_NAME}"),
        ("logicalReferences", ["gearwright:block/inspection-glass", "gearwright:block/inspection-shadow"]),
        ("candidates", list(DESCRIPTIONS)),
        ("stages", list(STAGES)),
        ("animations", list(ANIMATIONS)),
        ("shelved", ["automatic-large-bellows"]),
        ("decision", OrderedDict((
            ("status", "approved"),
            ("baseApproved", "a10-two-block-bottom-pipes"),
            ("preferredDirection", "a11-one-block-full-frame"),
            ("candidate", "a11-one-block-full-frame"),
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
    parser = argparse.ArgumentParser(description="Build the one-axle crank and spectacle-pump review package.")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    args = parser.parse_args(argv)
    managed = build_review(args.root)
    print(f"Built {len(DESCRIPTIONS)} spectacle-pump candidates in {managed.relative_to(args.root.resolve()).as_posix()}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
