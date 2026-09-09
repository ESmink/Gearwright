"""Plank addon candidates; no runtime assets or structural simulation."""

from __future__ import annotations

import argparse
import shutil
from collections import OrderedDict
from pathlib import Path

from gearwright_graphics.compiler import build_package, json_bytes
from gearwright_graphics.model import FACES, Face, ModelPackage, Shape, UVRect
from graphics.models.fluid_pipe import ROTATIONS, build as build_pipe
from graphics.models.pipe_wood_support import compose_support

REVIEW_ROOT = Path("generated/pipe-wood-supports-review")
STATES = ("rest", "ground", "wall", "linked-run", "linked-north-south", "vertical", "junction", "six-way", "sprinkler", "top-continuation", "top-terminal", "barrel-connection",
          "insulated-rest", "insulated-linked-run", "insulated-vertical", "insulated-junction", "insulated-barrel-connection", "insulated-sprinkler")
DESCRIPTIONS = OrderedDict((
    ("c-braced-trestle", "Selected C: continuous frames, endpoint collars and one diagonal on each unconnected side and bottom. The second-plank insulated mode adds recessed narrow-board panels around the pipe openings to form a crate."),
))
PIPE_PARTS = build_pipe().outputs


def _pipe(shape: Shape, prefix: str, offset=(0, 0, 0), connections=("west", "east"), sprinkler=False, nozzles=()):
    """Compose the existing pipe source without changing its cuboids or UVs."""
    def part(suffix, name, rotation=(0, 0, 0)):
        source = PIPE_PARTS[f"assets/gearwright/shapes/block/{suffix}.json"]
        shape.textures.update(source.textures)
        parent = shape._element(
            prefix + name, offset, offset, pivot=True,
            rotation_origin=tuple(value + 8 for value in offset), rotation=rotation,
            group="pipe",
        )
        for element in source.elements:
            shape._element(
                prefix + name + "-" + element.name,
                element.from_.values(), element.to.values(), faces=element.faces,
                rotation_origin=element.rotation_origin.values() if element.rotation_origin else None,
                rotation=element.rotation.values(), render_pass=element.render_pass,
                shade=element.shade, gradient_shade=element.gradient_shade,
                parent=parent, group="pipe",
            )
    part("fluid-pipe-center", "center")
    for face in FACES:
        if face == "down" and sprinkler:
            continue
        if face in nozzles:
            # Intake is authored toward south rather than north.
            opposite = {"north": "south", "south": "north", "east": "west", "west": "east", "up": "down", "down": "up"}[face]
            part("fluid-pipe-intake", face, ROTATIONS[opposite])
        else:
            suffix = "arm" if face in connections else "window" if face == "north" else "cap"
            part("fluid-pipe-" + suffix, face, ROTATIONS[face])
    if sprinkler:
        from graphics.models.sprinkler import build
        source = build().outputs["assets/gearwright/shapes/block/sprinkler-body.json"]
        shape.textures.update(source.textures)
        for element in source.elements:
            shape._element(prefix + "sprinkler-" + element.name,
                           element.from_.values(), element.to.values(), faces=element.faces,
                           rotation_origin=element.rotation_origin.values() if element.rotation_origin else None,
                           rotation=element.rotation.values(), group="pipe")


def candidate_shape(candidate: str, state: str) -> Shape:
    if candidate not in DESCRIPTIONS or state not in STATES:
        raise ValueError("unknown support candidate or state")
    shape = Shape(f"{candidate}-{state}", 16, 16)
    insulated = state.startswith("insulated-")
    state = state.removeprefix("insulated-")
    shape.texture("context", "game:block/stone/rock/granite1")
    if state.startswith("linked-"):
        along_x = state == "linked-run"
        directions = ("west", "east") if along_x else ("north", "south")
        for i in range(3):
            offset = (16 * (i - 1), 0, 0) if along_x else (0, 0, 16 * (i - 1))
            _pipe(shape, f"pipe-{i}-", offset, connections=directions)
            compose_support(shape, f"support-{i}-", offset, connections=directions, insulated=insulated)
        shape.box("context-anchor", (-16, -4, 0) if along_x else (0, -4, -16),
                  (0, 0, 16) if along_x else (16, 0, 0), texture="#context", group="context")
    elif state in ("vertical", "top-continuation"):
        _pipe(shape, "lower-", connections=("west", "up"))
        compose_support(shape, "lower-", connections=("west", "up"), pipe_above=True, insulated=insulated)
        _pipe(shape, "upper-", (0, 16, 0), connections=("down", "east"))
        compose_support(shape, "upper-", (0, 16, 0), connections=("down", "east"), insulated=insulated)
        shape.box("context-ground", (0, -4, 0), (16, 0, 16), texture="#context", group="context")
    else:
        connections = tuple(FACES) if state == "six-way" else ("west", "east", "north", "south") if state == "junction" else ("west", "up") if state == "top-terminal" else ("west", "east")
        nozzles = ("up",) if state == "barrel-connection" else ()
        _pipe(shape, "pipe-", connections=connections, sprinkler=state == "sprinkler", nozzles=nozzles)
        compose_support(shape, connections=connections, nozzles=nozzles, sprinkler=state == "sprinkler", insulated=insulated)
        if state == "ground":
            shape.box("context-ground", (0, -4, 0), (16, 0, 16), texture="#context", group="context")
        if state == "wall":
            shape.box("context-wall", (0, 0, 16), (16, 20, 20), texture="#context", group="context")
    return shape


def build_review(root: Path) -> Path:
    root = root.resolve()
    review = (root / REVIEW_ROOT).resolve()
    managed, staging, backup = (review / name for name in ("current", ".current-build", ".current-old"))
    for path in (review, managed, staging, backup):
        path.resolve().relative_to(root)
    review.mkdir(parents=True, exist_ok=True)
    for path in (staging, backup):
        if path.exists():
            shutil.rmtree(path)
    package = ModelPackage("pipe_wood_supports_review")
    for candidate in DESCRIPTIONS:
        for state in STATES:
            package.shape(candidate_shape(candidate, state),
                          f"{REVIEW_ROOT.as_posix()}/.current-build/{candidate}/{state}.shape.json")
    build_package(package, root, write=True)
    readme = [
        "# Wooden pipe supports — selected C", "",
        "The maintainer selected C and requested one shared endpoint rule for every pipe face.",
        "The requested wider frame reaches all six block boundaries. Adjacent frames share a complete rim with no gap, including stacked frames and pipes without a shared open port. The top infill meets this rim without coplanar overlaps.",
        "Every pipe or nozzle face has a collar at the block boundary, tied to the corner frame. Adjacent supported pipes meet collar-to-collar. Each unconnected side and bottom has one corner-to-corner diagonal, dividing the opening into two triangles.",
        "A second plank fits narrow oak-board panels behind the frame and braces. The insulated crate keeps pipe, nozzle and sprinkler openings clear; gameplay treats all six faces as a solid enclosure for rooms. Remove the lining before removing the support frame.",
        "Three open top slats remain on a terminal supported pipe. With an upward port or nozzle, remove the centre slat so the pipe can enter a barrel. With another pipe above, remove the complete platform while keeping the endpoint collar.",
        "The barrel-connection state uses the real upward nozzle; the top-terminal state uses a plain upward pipe. The barrel itself is omitted to expose the opening.",
        "The top is intended to support game block attachment whenever there is no pipe above; visible gaps do not change that rule.",
        "Material tier: wood, the cheapest structural tier. Broad collars carry the pipe endpoints, diagonal struts transfer their load into the frame, and corner knees resist racking. Splitting at the joints is the likely failure point. No wear surfaces require metal.",
        "Inspected logical textures: game:block/wood/plainoak, game:block/wood/treetrunk/debarked/oak, survival:textures/block/wood/planks/oak1.png for the stitched narrow-board lining, and game:block/stone/rock/granite1 for context. Existing pipe and nozzle materials come from their approved definitions.",
        "No span limits, collapse, or structural network simulation is introduced.", "",
        *(f"- `{candidate}` - {description}" for candidate, description in DESCRIPTIONS.items()), "",
    ]
    (staging / "README.md").write_text("\n".join(readme), encoding="utf-8")
    (staging / ".gearwright-review.json").write_bytes(json_bytes({
        "version": 1, "purpose": "wooden pipe addon visual approval",
        "source": "graphics/review/pipe_wood_supports.py",
        "managedPath": f"{REVIEW_ROOT.as_posix()}/current",
        "candidates": list(DESCRIPTIONS), "states": list(STATES), "animations": [],
        "intendedTopFace": {"solidWithoutPipeAbove": True, "terminalUpwardPortOmitsCenterSlat": True, "visualGapsAllowed": True},
        "decision": {"status": "approved", "candidate": "c-braced-trestle", "runtimePromotion": True,
                     "direction": "C with the requested continuous frame, single diagonal side/bottom braces, and second-plank insulated crate mode"},
    }))
    if managed.exists():
        managed.replace(backup)
    staging.replace(managed)
    if backup.exists():
        shutil.rmtree(backup)
    return managed


def render_review(root: Path, game: Path, managed: Path) -> None:
    """Use the standard photoshoot with identical framing for each state."""
    from gearwright_graphics.photoshoot import build_parser, run
    from PIL import Image, ImageDraw, ImageFont

    for candidate in DESCRIPTIONS:
        for state in STATES:
            view_state = state.removeprefix("insulated-")
            target, scale = ((.5, .9, .5), 2.8) if view_state in ("vertical", "top-continuation") else ((.5, .4, .5), 3.4) if view_state.startswith("linked-") else ((.5, .4, .5), 1.9)
            args = build_parser().parse_args([
                "--vintage-story", str(game), "--mod", str(root),
                "--model", str(managed / candidate / f"{state}.shape.json"),
                "--strict-textures", "--views", "isometric,front,back,right,left,top,bottom",
                "--size", "480x480", "--orthographic", "--orthographic-scale", str(scale),
                "--camera-target", ",".join(str(v) for v in target),
                "--output", str(managed / candidate / f"fixed-render-{state}"),
            ])
            run(args, root)
            print(f"Rendered {candidate}/{state}", flush=True)
    states = (("rest", "isometric", "OPEN FRAME"), ("rest", "bottom", "BOTTOM BRACE"),
              ("insulated-rest", "isometric", "INSULATED PIPE"), ("insulated-linked-run", "isometric", "INSULATED RUN"),
              ("insulated-vertical", "isometric", "INSULATED STACK"), ("insulated-barrel-connection", "isometric", "BARREL CONNECTION"))
    sheet = Image.new("RGB", (1440, 2 * 524 + 70), (20, 23, 28))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=24)
    draw.text((18, 22), "C - DIAGONAL BRACES AND INSULATED CRATES", font=font, fill=(236, 224, 205))
    for i, (state, view, label) in enumerate(states):
        x, y = (i % 3) * 480, 70 + (i // 3) * 524
        with Image.open(managed / "c-braced-trestle" / f"fixed-render-{state}" / (view + ".png")) as photo:
            sheet.paste(photo.convert("RGB"), (x, y))
        draw.text((x + 18, y + 483), label, font=font, fill=(194, 202, 212))
    sheet.save(managed / "comparison.png")
    sheet.crop((0, 0, 1440, 594)).save(managed / "overview.png")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--render", action="store_true")
    parser.add_argument("--vintage-story", type=Path)
    args = parser.parse_args()
    managed = build_review(args.root)
    if args.render:
        from gearwright_graphics.review_model import discover_vintage_story
        game = args.vintage_story or discover_vintage_story()
        if game is None:
            parser.error("Vintage Story is required for strict-texture renders")
        render_review(args.root.resolve(), game, managed)
    print(managed.relative_to(args.root.resolve()).as_posix())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
