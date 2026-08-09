"""Build the managed, review-only slingshot workflow fixture."""

from __future__ import annotations

import argparse
import shutil
from collections import OrderedDict
from pathlib import Path
from typing import Iterable

from gearwright_graphics.compiler import build_package, json_bytes
from gearwright_graphics.model import AnimationBuilder, ElementRef, ModelPackage, Shape, animate


REVIEW_ROOT = Path("generated/slingshot-review")
MANAGED_NAME = "current"
STAGING_NAME = ".current-build"
BACKUP_NAME = ".current-old"
WOOD = "gearwright:item/pottery-profile-tool"
BAND = "gearwright:block/inspection-shadow"
APPROVED_CANDIDATE = "model-c-tall-sapling-fork"

DESCRIPTIONS = OrderedDict((
    ("model-a-balanced-fork", "Moderately flared wooden fork with a segmented flat band; the neutral baseline for proportions and draw readability."),
    ("model-b-workshop-yoke", "Squared, braced workshop frame with a wider band; tests whether an engineered silhouette fits Gearwright better than a natural fork."),
    ("model-c-tall-sapling-fork", "Tall narrow fork with slim prongs and a compact grip; tests side-view clearance and whether the band remains readable around thin uprights."),
))


def _inside(root: Path, path: Path) -> bool:
    try:
        path.resolve().relative_to(root.resolve())
        return True
    except ValueError:
        return False


def _base(shape_id: str) -> Shape:
    shape = Shape(shape_id, 16, 16)
    shape.texture("wood", WOOD)
    shape.texture("band", BAND)
    return shape


def _bands(shape: Shape, *, y: float, left_anchor: float, right_anchor: float, width: float) -> dict[str, ElementRef]:
    center_left, center_right = 7.25, 8.75
    middle_left = (left_anchor + center_left) / 2
    middle_right = (right_anchor + center_right) / 2
    depth = (7.72, 8.28)
    middle_y = y + width / 2
    refs = {
        "left_outer": shape.box("band-left-outer", (left_anchor, y, depth[0]), (middle_left, y + width, depth[1]), texture="#band", rotation_origin=(left_anchor, middle_y, 8), group="band"),
        "left_inner": shape.box("band-left-inner", (middle_left, y, depth[0]), (center_left, y + width, depth[1]), texture="#band", rotation_origin=(middle_left, middle_y, 8), group="band"),
        "right_inner": shape.box("band-right-inner", (center_right, y, depth[0]), (middle_right, y + width, depth[1]), texture="#band", rotation_origin=(middle_right, middle_y, 8), group="band"),
        "right_outer": shape.box("band-right-outer", (middle_right, y, depth[0]), (right_anchor, y + width, depth[1]), texture="#band", rotation_origin=(right_anchor, middle_y, 8), group="band"),
        "pouch": shape.box("band-pouch", (center_left, y - .2, 7.45), (center_right, y + width + .2, 8.55), texture="#band", rotation_origin=(8, middle_y, 8), group="pouch"),
    }
    _add_animations(shape, refs)
    return refs


def _keyframes(builder: AnimationBuilder, frame: float, entries: Iterable[tuple[ElementRef, dict[str, float | bool]]]) -> None:
    for target, values in entries:
        builder.keyframe(frame, target, **values)


def _pose(refs: dict[str, ElementRef], amount: float) -> list[tuple[ElementRef, dict[str, float | bool]]]:
    return [
        (refs["left_outer"], {"rotationY": 18 * amount, "rotShortestDistanceY": True}),
        (refs["left_inner"], {"offsetZ": -1.25 * amount, "rotationY": 28 * amount, "rotShortestDistanceY": True}),
        (refs["right_inner"], {"offsetZ": -1.25 * amount, "rotationY": -28 * amount, "rotShortestDistanceY": True}),
        (refs["right_outer"], {"rotationY": -18 * amount, "rotShortestDistanceY": True}),
        (refs["pouch"], {"offsetY": -.3 * amount, "offsetZ": -3.2 * amount}),
    ]


def _add_animations(shape: Shape, refs: dict[str, ElementRef]) -> None:
    draw = animate(shape, "Steady draw", "draw-steady", 30, version=1, on_animation_end="Hold")
    for frame, amount in ((0, 0.0), (15, .5), (29, 1.0)):
        _keyframes(draw, frame, _pose(refs, amount))
    shape.add_animation(draw.build())

    release = animate(shape, "Release snap", "release-snap", 20, version=1, on_animation_end="Hold")
    for frame, amount in ((0, 1.0), (12, 1.0), (16, .35), (19, 0.0)):
        _keyframes(release, frame, _pose(refs, amount))
    shape.add_animation(release.build())

    idle = animate(shape, "Idle stability check", "idle-check", 48, version=1, on_animation_end="Repeat")
    for frame, sway in ((0, 0.0), (12, 1.0), (24, 0.0), (36, -1.0), (47, 0.0)):
        _keyframes(idle, frame, [
            (refs["left_inner"], {"offsetZ": -.16 * sway, "rotationY": 2.5 * sway, "rotShortestDistanceY": True}),
            (refs["right_inner"], {"offsetZ": -.16 * sway, "rotationY": -2.5 * sway, "rotShortestDistanceY": True}),
            (refs["pouch"], {"offsetY": .12 * sway, "offsetZ": -.32 * sway, "rotationZ": 1.5 * sway, "rotShortestDistanceZ": True}),
        ])
    shape.add_animation(idle.build())


def _balanced_fork() -> Shape:
    shape = _base("slingshot-workflow-balanced-fork")
    shape.box("grip", (6.45, 1, 7), (9.55, 9, 9), texture="#wood", group="frame")
    shape.box("shoulder", (5.1, 7.8, 7), (10.9, 10.2, 9), texture="#wood", group="frame")
    shape.box("prong-left", (4.35, 8.8, 7), (6.25, 14.6, 9), texture="#wood", rotation_origin=(5.3, 9, 8), rotation=(0, 0, 5), group="frame")
    shape.box("prong-right", (9.75, 8.8, 7), (11.65, 14.6, 9), texture="#wood", rotation_origin=(10.7, 9, 8), rotation=(0, 0, -5), group="frame")
    _bands(shape, y=13.5, left_anchor=5.75, right_anchor=10.25, width=.72)
    return shape


def _workshop_yoke() -> Shape:
    shape = _base("slingshot-workflow-workshop-yoke")
    shape.box("grip", (6.2, .8, 6.8), (9.8, 8.2, 9.2), texture="#wood", group="frame")
    shape.box("brace-low", (4.5, 7.2, 6.8), (11.5, 9.2, 9.2), texture="#wood", group="frame")
    shape.box("upright-left", (4.3, 8.4, 6.8), (6.1, 14.8, 9.2), texture="#wood", group="frame")
    shape.box("upright-right", (9.9, 8.4, 6.8), (11.7, 14.8, 9.2), texture="#wood", group="frame")
    shape.box("brace-left", (5.7, 8.5, 7.2), (7.2, 10.0, 8.8), texture="#wood", rotation_origin=(5.7, 8.5, 8), rotation=(0, 0, -25), group="frame")
    shape.box("brace-right", (8.8, 8.5, 7.2), (10.3, 10.0, 8.8), texture="#wood", rotation_origin=(10.3, 8.5, 8), rotation=(0, 0, 25), group="frame")
    _bands(shape, y=13.45, left_anchor=5.55, right_anchor=10.45, width=.95)
    return shape


def _tall_sapling_fork() -> Shape:
    shape = _base("slingshot-workflow-tall-sapling-fork")
    shape.box("grip", (6.85, .5, 7.15), (9.15, 8.8, 8.85), texture="#wood", group="frame")
    shape.box("shoulder", (5.4, 7.8, 7.15), (10.6, 9.7, 8.85), texture="#wood", group="frame")
    shape.box("prong-left", (4.75, 8.5, 7.15), (6.25, 15.8, 8.85), texture="#wood", rotation_origin=(5.5, 8.7, 8), rotation=(0, 0, 3), group="frame")
    shape.box("prong-right", (9.75, 8.5, 7.15), (11.25, 15.8, 8.85), texture="#wood", rotation_origin=(10.5, 8.7, 8), rotation=(0, 0, -3), group="frame")
    shape.box("grip-cap", (6.45, .2, 6.8), (9.55, 1.4, 9.2), texture="#wood", group="frame")
    _bands(shape, y=14.55, left_anchor=5.7, right_anchor=10.3, width=.56)
    return shape


def build_review(root: Path, *, approved_candidate: str | None = APPROVED_CANDIDATE) -> Path:
    root = root.resolve()
    review_root = (root / REVIEW_ROOT).resolve()
    managed, staging, backup = (review_root / name for name in (MANAGED_NAME, STAGING_NAME, BACKUP_NAME))
    if not all(_inside(root, path) for path in (review_root, managed, staging, backup)):
        raise ValueError("review output must remain inside the project")
    review_root.mkdir(parents=True, exist_ok=True)
    for transient in (staging, backup):
        if transient.exists():
            shutil.rmtree(transient)

    package = ModelPackage("slingshot_workflow_review")
    shapes = OrderedDict((
        ("model-a-balanced-fork", _balanced_fork()),
        ("model-b-workshop-yoke", _workshop_yoke()),
        ("model-c-tall-sapling-fork", _tall_sapling_fork()),
    ))
    if approved_candidate is not None and approved_candidate not in shapes:
        raise ValueError(f"unknown approved candidate: {approved_candidate}")
    selected = shapes if approved_candidate is None else OrderedDict(((approved_candidate, shapes[approved_candidate]),))
    for candidate, shape in selected.items():
        package.shape(shape, f"{REVIEW_ROOT.as_posix()}/{STAGING_NAME}/{candidate}/rest.shape.json")
    build_package(package, root, write=True)

    readme = ["# Slingshot workflow review", "", "Review-only test fixture. Nothing in this folder is packaged with Gearwright.", ""]
    if approved_candidate is None:
        readme.extend(["Decision: open.", "", "Each model contains `draw-steady`, `release-snap`, and `idle-check` animations.", ""])
    else:
        readme.extend([
            f"Decision: `{approved_candidate}` approved by the maintainer.",
            "The approval closes this workflow test and does not authorize a runtime slingshot asset.",
            "",
        ])
    readme.extend(f"- `{candidate}` - {DESCRIPTIONS[candidate]}" for candidate in selected)
    (staging / "README.md").write_text("\n".join([*readme, ""]), encoding="utf-8")
    ownership = OrderedDict((
        ("version", 1),
        ("purpose", "review-only workflow test; never packaged"),
        ("source", "graphics/review/slingshot_workflow.py"),
        ("managedPath", f"{REVIEW_ROOT.as_posix()}/{MANAGED_NAME}"),
        ("candidates", list(selected)),
        ("animations", ["draw-steady", "release-snap", "idle-check"]),
        ("decision", OrderedDict((
            ("status", "open" if approved_candidate is None else "approved"),
            ("candidate", approved_candidate),
            ("runtimePromotion", False),
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
    parser = argparse.ArgumentParser(description="Build the managed review-only slingshot workflow fixture.")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--open", action="store_true", help="Restart the three-candidate workflow test instead of reproducing its approved result.")
    args = parser.parse_args(argv)
    approved = None if args.open else APPROVED_CANDIDATE
    managed = build_review(args.root, approved_candidate=approved)
    count = 3 if args.open else 1
    print(f"Built {count} review-only slingshot candidate(s) in {managed.relative_to(args.root.resolve()).as_posix()}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
