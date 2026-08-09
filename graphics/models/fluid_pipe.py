"""All static pipe sub-shapes and their review-state composition."""

from __future__ import annotations

from collections import OrderedDict
from copy import deepcopy
from typing import Mapping

from gearwright_graphics.compiler import compile_shape
from gearwright_graphics.model import ModelPackage, ReviewAssembly, ReviewScene, Shape, Vec3

COPPER = "#copper"
FACES = ("north", "east", "south", "west", "up", "down")
ROTATIONS = {
    "north": (0, 0, 0), "east": (0, -90, 0), "south": (0, 180, 0),
    "west": (0, 90, 0), "up": (90, 0, 0), "down": (-90, 0, 0),
}


def _textures(shape: Shape, copper: bool = True, glass: bool = False, liquid: bool = False) -> None:
    if copper:
        shape.texture("copper", "game:block/metal/sheet/copper1")
    if glass:
        shape.texture("glass", "gearwright:block/inspection-glass")
    if liquid:
        shape.texture("liquid", "game:block/liquid/waterportion")


def _frame(shape: Shape, prefix: str, start: float, end: float, *, group: str = "center") -> None:
    pieces = (
        (f"{prefix}-bottom", (6, 6, start), (10, 6.5, end)),
        (f"{prefix}-top", (6, 9.5, start), (10, 10, end)),
        (f"{prefix}-left", (6, 6.5, start), (6.5, 9.5, end)),
        (f"{prefix}-right", (9.5, 6.5, start), (10, 9.5, end)),
    )
    for name, from_, to in pieces:
        shape.box(name, from_, to, texture=COPPER, group=group)


def _center(shape: Shape) -> None:
    _textures(shape)
    for name, from_, to in (
        ("frame-x-bottom-north", (6, 6, 6), (10, 6.5, 6.5)),
        ("frame-x-bottom-south", (6, 6, 9.5), (10, 6.5, 10)),
        ("frame-x-top-north", (6, 9.5, 6), (10, 10, 6.5)),
        ("frame-x-top-south", (6, 9.5, 9.5), (10, 10, 10)),
        ("frame-y-west-north", (6, 6.5, 6), (6.5, 9.5, 6.5)),
        ("frame-y-east-north", (9.5, 6.5, 6), (10, 9.5, 6.5)),
        ("frame-y-west-south", (6, 6.5, 9.5), (6.5, 9.5, 10)),
        ("frame-y-east-south", (9.5, 6.5, 9.5), (10, 9.5, 10)),
        ("frame-z-bottom-west", (6, 6, 6.5), (6.5, 6.5, 9.5)),
        ("frame-z-bottom-east", (9.5, 6, 6.5), (10, 6.5, 9.5)),
        ("frame-z-top-west", (6, 9.5, 6.5), (6.5, 10, 9.5)),
        ("frame-z-top-east", (9.5, 9.5, 6.5), (10, 10, 9.5)),
    ):
        shape.box(name, from_, to, texture=COPPER, group="center")


def _arm(shape: Shape) -> None:
    _textures(shape)
    for name, from_, to in (
        ("tube-bottom", (6, 6, 0), (10, 6.5, 6)), ("tube-top", (6, 9.5, 0), (10, 10, 6)),
        ("tube-left", (6, 6.5, 0), (6.5, 9.5, 6)), ("tube-right", (9.5, 6.5, 0), (10, 9.5, 6)),
        ("half-coupling-bottom", (5.5, 5.5, 0), (10.5, 6, .65)), ("half-coupling-top", (5.5, 10, 0), (10.5, 10.5, .65)),
        ("half-coupling-left", (5.5, 6, 0), (6, 10, .65)), ("half-coupling-right", (10, 6, 0), (10.5, 10, .65)),
    ):
        shape.box(name, from_, to, texture=COPPER, group="attachment")


def _cap(shape: Shape) -> None:
    _textures(shape)
    shape.box("face-cap", (6.5, 6.5, 6), (9.5, 9.5, 6.5), texture=COPPER, group="attachment")


def _window(shape: Shape) -> None:
    _textures(shape, copper=False, glass=True)
    shape.box("glass", (6.5, 6.5, 6), (9.5, 9.5, 6.5), texture="#glass", faces=("north",), uv=(0, 0, 16, 16), render_pass=1, group="window")


def _slug(shape: Shape) -> None:
    _textures(shape, copper=False, liquid=True)
    shape.box("visible-liquid", (6.5, 6.5, 6.08), (9.5, 9.5, 6.12), texture="#liquid", faces=("north",), render_pass=1, group="liquid")


def _inventory(shape: Shape) -> None:
    _center(shape)
    _textures(shape, glass=True)
    for name, from_, to in (
        ("east-bottom", (10, 6, 6), (16, 6.5, 10)), ("east-top", (10, 9.5, 6), (16, 10, 10)),
        ("east-north", (10, 6.5, 6), (16, 9.5, 6.5)), ("east-south", (10, 6.5, 9.5), (16, 9.5, 10)),
        ("east-coupling-bottom", (15.35, 5.5, 5.5), (16, 6, 10.5)), ("east-coupling-top", (15.35, 10, 5.5), (16, 10.5, 10.5)),
        ("east-coupling-north", (15.35, 6, 5.5), (16, 10, 6)), ("east-coupling-south", (15.35, 6, 10), (16, 10, 10.5)),
    ):
        shape.box(name, from_, to, texture=COPPER, group="attachment")
    for name, from_, to in (
        ("south-bottom", (6, 6, 10), (10, 6.5, 16)), ("south-top", (6, 9.5, 10), (10, 10, 16)),
        ("south-left", (6, 6.5, 10), (6.5, 9.5, 16)), ("south-right", (9.5, 6.5, 10), (10, 9.5, 16)),
        ("south-coupling-bottom", (5.5, 5.5, 15.35), (10.5, 6, 16)), ("south-coupling-top", (5.5, 10, 15.35), (10.5, 10.5, 16)),
        ("south-coupling-left", (5.5, 6, 15.35), (6, 10, 16)), ("south-coupling-right", (10, 6, 15.35), (10.5, 10, 16)),
    ):
        shape.box(name, from_, to, texture=COPPER, group="attachment")
    shape.box("front-glass", (6.5, 6.5, 6), (9.5, 9.5, 6.5), texture="#glass", faces=("north",), uv=(0, 0, 16, 16), render_pass=1, group="window")
    for name, from_, to in (("west-cap", (6, 6.5, 6.5), (6.5, 9.5, 9.5)), ("bottom-cap", (6.5, 6, 6.5), (9.5, 6.5, 9.5)), ("top-cap", (6.5, 9.5, 6.5), (9.5, 10, 9.5))):
        shape.box(name, from_, to, texture=COPPER, group="attachment")


def _new(shape_id: str, builder, width: int = 16, height: int = 16) -> Shape:
    shape = Shape(shape_id, width, height)
    builder(shape)
    return shape


def build() -> ModelPackage:
    package = ModelPackage("fluid_pipe")
    package.shape(_new("fluid-pipe-center", _center), "assets/gearwright/shapes/block/fluid-pipe-center.json")
    package.shape(_new("fluid-pipe-arm", _arm), "assets/gearwright/shapes/block/fluid-pipe-arm.json")
    package.shape(_new("fluid-pipe-cap", _cap), "assets/gearwright/shapes/block/fluid-pipe-cap.json")
    package.shape(_new("fluid-pipe-window", _window), "assets/gearwright/shapes/block/fluid-pipe-window.json")
    package.shape(_new("fluid-slug", _slug), "assets/gearwright/shapes/block/fluid-slug.json")
    package.shape(_new("fluid-pipe-inventory", _inventory), "assets/gearwright/shapes/block/fluid-pipe-inventory.json")
    assemblies = tuple(ReviewAssembly(face, (f"{face}-*",), Vec3(*ROTATIONS[face]), 1) for face in FACES)
    package.add_scene(ReviewScene("active-window-sprinkler", ({"asset": "gearwright:block/fluid-pipe-center", "state": {"north": "connection", "east": "window", "down": "sprinkler"}, "pressure": 100, "role": "target"},), ("front-right", "top"), assemblies))
    package.add_scene(ReviewScene("normal", ({"asset": "gearwright:block/fluid-pipe-center", "state": {}, "role": "target"},), ("front-right",), assemblies))
    package.add_scene(ReviewScene("exploded", ({"asset": "gearwright:block/fluid-pipe-center", "state": {"north": "connection", "east": "window", "down": "sprinkler"}, "pressure": 100, "role": "target"},), ("isometric",), assemblies))
    return package


def compose_pipe_shape(state: Mapping[str, str], pressure: float = 0) -> dict:
    """Compose the dynamic preview from the same named sub-shapes as runtime."""
    package = build()
    base = deepcopy(compile_shape(package.outputs["assets/gearwright/shapes/block/fluid-pipe-center.json"]))
    output_by_suffix = {path.rsplit("/", 1)[-1].replace(".json", ""): compile_shape(shape) for path, shape in package.outputs.items()}
    import importlib.util
    sprinkler_path = __file__.replace("fluid_pipe.py", "sprinkler.py")
    spec = importlib.util.spec_from_file_location("gearwright_sprinkler_preview", sprinkler_path)
    if spec is not None and spec.loader is not None:
        sprinkler_module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(sprinkler_module)
        sprinkler_package = sprinkler_module.build()
        for path, shape in sprinkler_package.outputs.items():
            output_by_suffix[path.rsplit("/", 1)[-1].replace(".json", "")] = compile_shape(shape)
    for face in FACES:
        attachment = state.get(face, "empty")
        if attachment == "connection":
            part = output_by_suffix["fluid-pipe-arm"]
        elif attachment == "window":
            part = output_by_suffix["fluid-pipe-window"]
        elif attachment == "sprinkler":
            part = output_by_suffix["sprinkler-body"] if "sprinkler-body" in output_by_suffix else None
        else:
            part = output_by_suffix["fluid-pipe-cap"]
        if part is not None:
            base.setdefault("textures", {}).update(deepcopy(part.get("textures", {})))
            base.setdefault("elements", []).append({"name": f"{face}-{attachment}", "from": [0, 0, 0], "to": [0, 0, 0], "rotationOrigin": [8, 8, 8], "children": deepcopy(part.get("elements", []))})
        if attachment == "window" and pressure > 0:
            part = output_by_suffix["fluid-slug"]
            base.setdefault("textures", {}).update(deepcopy(part.get("textures", {})))
            base.setdefault("elements", []).append({"name": f"{face}-visible-liquid", "from": [0, 0, 0], "to": [0, 0, 0], "rotationOrigin": [8, 8, 8], "children": deepcopy(part.get("elements", []))})
    return base
