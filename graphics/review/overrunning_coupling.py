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

DESCRIPTIONS = OrderedDict((
    (
        "model-a2-three-pawl-spider",
        "The selected three-pawl oak spider, revised with taller bearing crowns, Vanilla-proportioned crossed axles, and square end beams side-bolted into its shortened foundations.",
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
    reach: float,
    width: float,
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
    shape.box(
        f"{name}-hook-body",
        (x_range[0], -reach, -width / 2),
        (x_range[1], .15, width / 2),
        texture="#bronze",
        rotation_origin=(0, 0, 0),
        rotation=(mount_angle, 0, 0),
        parent=pivot,
        group="pawl",
    )
    shape.box(
        f"{name}-tooth-hook",
        (x_range[0] - .15, -reach - .5, -width * .72),
        (x_range[1] + .15, -reach + .1, width * .72),
        texture="#bronze",
        rotation_origin=(0, 0, 0),
        rotation=(mount_angle, 0, 0),
        parent=pivot,
        group="pawl",
    )
    shape.box(
        f"{name}-counterweight-heel",
        (x_range[0] - .25, -.15, -width * .72),
        (x_range[1] + .25, .72, width * .72),
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


def _animations(
    shape: Shape,
    input_pivot: ElementRef,
    output_pivot: ElementRef,
    pawls: tuple[tuple[ElementRef, float], ...],
) -> None:
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
            lift = 14 * (cycle / .7 if cycle < .7 else (1 - cycle) / .3)
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
                reach=1.25,
                width=.92,
            ),
            pawl_phases[index],
        )
        for index, angle in enumerate(pawl_angles)
    )
    _animations(shape, input_pivot, output_pivot, pawls)
    return shape


def _three_pawl_spider() -> Shape:
    return _compact_variant(
        "overrunning-coupling-three-pawl-spider",
        pawl_angles=(0, 120, 240),
        pawl_phases=(0, 1 / 3, 2 / 3),
    )
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

    shapes = OrderedDict((("model-a2-three-pawl-spider", _three_pawl_spider()),))
    if approved_candidate is not None and approved_candidate not in shapes:
        raise ValueError(f"unknown approved candidate: {approved_candidate}")
    selected = shapes if approved_candidate is None else OrderedDict(((approved_candidate, shapes[approved_candidate]),))

    package = ModelPackage("overrunning_coupling_review")
    for candidate, shape in selected.items():
        package.shape(shape, f"{REVIEW_ROOT.as_posix()}/{STAGING_NAME}/{candidate}/rest.shape.json")
    build_package(package, root, write=True)

    readme = [
        "# Overrunning coupling review",
        "",
        "Review-only candidates for an automatic transmission that couples only while its designated input shaft is faster in the allowed direction.",
        "The output may overrun a slow, stopped, or reversed input without back-driving it. The two shafts remain separate Vanilla mechanical networks.",
        "",
        "All working teeth, pawls, pivots, bearing caps, and structural bands use tin bronze. Copper is too soft for repeated tooth impact; iron is not required.",
        "Oak carries the separate floor pedestals, Vanilla-proportioned crossed shafts, hubs, ratchet wheel, and open pawl carrier. One square beam closes each end of the shortened foundation feet. Two bronze heads on each outward beam face mark the lag bolts driven lengthwise into the two foundations.",
        "",
        f"Decision: `{'open' if approved_candidate is None else approved_candidate + ' approved'}`.",
        "A2 is the selected direction; these revised support proportions and connections remain open for confirmation.",
        "",
        "Each candidate includes `drive` and `overrun` animations. In `drive`, both rotors turn together. In the 72-frame `overrun`, the output turns 360 degrees while the input turns 120 degrees; every pawl orbits, climbs each 30-degree tooth pitch, and snaps down from a 14-degree lift.",
        "The review animation is a deterministic clearance check. The in-game renderer will instead derive both rotor angles and all three staggered pawl lifts from the two live mechanical-network angles, so acceleration, deceleration, engagement, and overrun follow the actual shafts rather than a canned playback speed.",
        "Inspect the front, back, both sides, top, and oblique views, then scrub the overrun animation for hook clearance and a continuous load path from output hub to pawl pin.",
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
        ("animations", ["drive", "overrun"]),
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
