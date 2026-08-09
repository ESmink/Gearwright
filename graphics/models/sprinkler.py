"""Installed late-game steel sprinkler shapes (legacy asset code retained)."""

from gearwright_graphics.model import ModelPackage, Shape


def _body(shape: Shape, item: bool = False) -> None:
    shape.texture("steel", "game:block/metal/sheet/steel1")
    if item:
        shape.box("threaded-inlet", (6, 12, 6), (10, 16, 10), texture="#steel", group="sprinkler-body")
    values = (
        ("inlet-collar", (5.25 if item else 5.5, 11 if item else 5.25, 5.25 if item else 5.5), (10.75 if item else 10.5, 12.5 if item else 6.25, 10.75 if item else 10.5)),
        ("spindle", (7.4 if item else 7.5, 4 if item else 1.5, 7.4 if item else 7.5), (8.6 if item else 8.5, 12 if item else 6, 8.6 if item else 8.5)),
        ("left-yoke", (4 if item else 4.75, 4.5 if item else 1.8, 6.5 if item else 6.75), (5.5 if item else 6.1, 11.5 if item else 6, 9.5 if item else 9.25)),
        ("right-yoke", (10.5 if item else 9.9, 4.5 if item else 1.8, 6.5 if item else 6.75), (12 if item else 11.25, 11.5 if item else 6, 9.5 if item else 9.25)),
        ("distributor", (5.25 if item else 5.75, 3.25 if item else 1.2, 5.25 if item else 5.75), (10.75 if item else 10.25, 4.5 if item else 1.9, 10.75 if item else 10.25)),
    )
    for name, from_, to in values:
        rotation = (0, 0, 0)
        rotation_origin = None
        if name == "left-yoke":
            rotation = (0, 0, -18)
            rotation_origin = (4.75, 8, 8) if item else (5.4, 3.9, 8)
        elif name == "right-yoke":
            rotation = (0, 0, 18)
            rotation_origin = (11.25, 8, 8) if item else (10.6, 3.9, 8)
        shape.box(
            name, from_, to,
            texture="#steel",
            rotation_origin=rotation_origin,
            rotation=rotation,
            group="sprinkler-body",
        )
    if item:
        shape.box("rotor-ns", (7.5, 2.5, 2), (8.5, 3.4, 14), texture="#brass", group="rotor")
        shape.box("rotor-ew", (2, 2.5, 7.5), (14, 3.4, 8.5), texture="#brass", group="rotor")
    shape.box(
        "rotor-pin",
        (7.2, 2, 7.2) if item else (7.4, .72, 7.4),
        (8.8, 4, 8.8) if item else (8.6, 1.35, 8.6),
        texture="#steel",
        group="sprinkler-body",
    )


def _rotor(shape: Shape) -> None:
    shape.texture("steel", "game:block/metal/sheet/steel1")
    for name, from_, to in (
        ("rotor-hub", (7.2, .08, 7.2), (8.8, .65, 8.8)), ("arm-north", (7.65, .16, 3.65), (8.35, .57, 7.2)), ("arm-south", (7.65, .16, 8.8), (8.35, .57, 12.35)), ("arm-west", (3.65, .16, 7.65), (7.2, .57, 8.35)), ("arm-east", (8.8, .16, 7.65), (12.35, .57, 8.35)),
    ):
        shape.box(name, from_, to, texture="#steel", group="rotor")
    for name, from_, to, origin in (
        ("nozzle-north", (6.9, .03, 2.6), (9.1, .75, 3.65), (8, .39, 3.15)), ("nozzle-south", (6.9, .03, 12.35), (9.1, .75, 13.4), (8, .39, 12.85)), ("nozzle-west", (2.6, .03, 6.9), (3.65, .75, 9.1), (3.15, .39, 8)), ("nozzle-east", (12.35, .03, 6.9), (13.4, .75, 9.1), (12.85, .39, 8)),
    ):
        shape.box(name, from_, to, texture="#steel", rotation_origin=origin, rotation=(0, -12, 0), group="rotor")


def build() -> ModelPackage:
    package = ModelPackage("sprinkler")
    body = Shape("sprinkler-body")
    _body(body)
    package.shape(body, "assets/gearwright/shapes/block/sprinkler-body.json")
    rotor = Shape("sprinkler-rotor")
    _rotor(rotor)
    package.shape(rotor, "assets/gearwright/shapes/block/sprinkler-rotor.json")
    item = Shape("sprinkler-brass")
    _body(item, item=True)
    package.shape(item, "assets/gearwright/shapes/item/sprinkler-brass.json")
    return package
