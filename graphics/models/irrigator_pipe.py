"""Approved Bronze Irrigator Pipe runtime shapes.

The geometry promotes review candidate irrigation-a2-three-direction-holes:
snug B2 ceiling yokes, rounded collarless G endpoints, and one drilled outlet
on the underside and each side. The side outlets are one model pixel lower
than the approved review package, as requested for runtime.
"""

from gearwright_graphics.model import ModelPackage, Shape


def _textures(shape: Shape, *, wood: bool = False) -> None:
    shape.texture("bronze", "game:block/metal/sheet/tinbronze1")
    shape.texture("shadow", "gearwright:block/inspection-shadow")
    if wood:
        # Runtime tesselation overrides this alias from the chosen plank. Oak
        # remains a complete default even when no custom source is available.
        shape.texture("wood", "game:block/wood/planks/oak1")


def _tube(shape: Shape) -> None:
    _textures(shape)
    for name, start, end in (
        ("tube-bottom", (6.25, 6, 0), (9.75, 6.6, 16)),
        ("tube-top", (6.25, 9.4, 0), (9.75, 10, 16)),
        ("tube-west", (6.25, 6.6, 0), (6.85, 9.4, 16)),
        ("tube-east", (9.15, 6.6, 0), (9.75, 9.4, 16)),
    ):
        shape.box(name, start, end, texture="#bronze", group="pipe")

    shape.box(
        "irrigation-hole-down",
        (7.55, 5.96, 7.55),
        (8.45, 5.99, 8.45),
        texture="#shadow",
        faces=("down",),
        group="outlets",
    )
    # These two approved side outlets are one model pixel lower than review.
    shape.box(
        "irrigation-hole-west",
        (6.22, 6.2, 7.55),
        (6.25, 7.1, 8.45),
        texture="#shadow",
        faces=("west",),
        group="outlets",
    )
    shape.box(
        "irrigation-hole-east",
        (9.75, 6.2, 7.55),
        (9.78, 7.1, 8.45),
        texture="#shadow",
        faces=("east",),
        group="outlets",
    )


def _connection(shape: Shape) -> None:
    _textures(shape)
    for name, start, end in (
        ("ring-bottom", (5.7, 5.45, 0), (10.3, 6, .65)),
        ("ring-top", (5.7, 10, 0), (10.3, 10.55, .65)),
        ("ring-west", (5.7, 6, 0), (6.25, 10, .65)),
        ("ring-east", (9.75, 6, 0), (10.3, 10, .65)),
    ):
        shape.box(name, start, end, texture="#bronze", group="connection")


def _endpoint(shape: Shape) -> None:
    _textures(shape)
    # Each stepped-octagon layer is five disjoint cuboids. Shared internal
    # faces are omitted, the two layers have a narrow depth gap, and the back
    # layer stops before the pipe body. No endpoint surfaces are coplanar.
    _octagonal_layer(
        shape, "cap-back", -.12, -.02,
        x_outer=(6.25, 9.75), x_inner=(6.5, 9.5),
        y_outer=(6, 10), y_inner=(6.3, 9.7),
    )
    _octagonal_layer(
        shape, "cap-front", -.28, -.14,
        x_outer=(6.55, 9.45), x_inner=(6.8, 9.2),
        y_outer=(6.2, 9.8), y_inner=(6.55, 9.45),
    )


def _octagonal_layer(
    shape: Shape,
    prefix: str,
    z0: float,
    z1: float,
    *,
    x_outer: tuple[float, float],
    x_inner: tuple[float, float],
    y_outer: tuple[float, float],
    y_inner: tuple[float, float],
) -> None:
    pieces = (
        ("bottom", (x_inner[0], y_outer[0], z0), (x_inner[1], y_inner[0], z1), ("north", "east", "south", "west", "down")),
        ("middle", (x_inner[0], y_inner[0], z0), (x_inner[1], y_inner[1], z1), ("north", "south")),
        ("top", (x_inner[0], y_inner[1], z0), (x_inner[1], y_outer[1], z1), ("north", "east", "south", "west", "up")),
        ("left", (x_outer[0], y_inner[0], z0), (x_inner[0], y_inner[1], z1), ("north", "south", "west", "up", "down")),
        ("right", (x_inner[1], y_inner[0], z0), (x_outer[1], y_inner[1], z1), ("north", "east", "south", "up", "down")),
    )
    for name, start, end, faces in pieces:
        shape.box(
            f"{prefix}-{name}", start, end,
            texture="#bronze", faces=faces, group="endpoint",
        )


def _support(shape: Shape) -> None:
    _textures(shape, wood=True)
    z0, z1 = 2.35, 3.85
    for name, start, end in (
        ("ceiling-beam", (4.25, 14.2, z0), (11.75, 16, z1)),
        ("west-drop", (5, 6, z0), (6.25, 14.2, z1)),
        ("east-drop", (9.75, 6, z0), (11, 14.2, z1)),
        ("bottom-cradle", (5, 5, z0), (11, 6.2, z1)),
    ):
        shape.box(name, start, end, texture="#wood", group="support")


def _inventory(shape: Shape) -> None:
    _tube(shape)
    _textures(shape, wood=True)
    for suffix, z0, z1 in (("north", 2.35, 3.85), ("south", 12.15, 13.65)):
        for name, start, end in (
            ("ceiling-beam", (4.25, 14.2, z0), (11.75, 16, z1)),
            ("west-drop", (5, 6, z0), (6.25, 14.2, z1)),
            ("east-drop", (9.75, 6, z0), (11, 14.2, z1)),
            ("bottom-cradle", (5, 5, z0), (11, 6.2, z1)),
        ):
            shape.box(f"{suffix}-{name}", start, end, texture="#wood", group="support")

    # Inventory view is a standalone pipe, so both rounded endpoints show.
    for suffix, back_depth, front_depth in (
        ("north", (-.12, -.02), (-.28, -.14)),
        ("south", (16.02, 16.12), (16.14, 16.28)),
    ):
        _octagonal_layer(
            shape, f"{suffix}-cap-back", *back_depth,
            x_outer=(6.25, 9.75), x_inner=(6.5, 9.5),
            y_outer=(6, 10), y_inner=(6.3, 9.7),
        )
        _octagonal_layer(
            shape, f"{suffix}-cap-front", *front_depth,
            x_outer=(6.55, 9.45), x_inner=(6.8, 9.2),
            y_outer=(6.2, 9.8), y_inner=(6.55, 9.45),
        )


def _shape(shape_id: str, builder) -> Shape:
    shape = Shape(shape_id, 16, 16)
    builder(shape)
    return shape


def build() -> ModelPackage:
    package = ModelPackage("irrigator_pipe")
    package.shape(_shape("irrigator-pipe-body", _tube), "assets/gearwright/shapes/block/irrigator-pipe-body.json")
    package.shape(_shape("irrigator-pipe-connection", _connection), "assets/gearwright/shapes/block/irrigator-pipe-connection.json")
    package.shape(_shape("irrigator-pipe-endpoint", _endpoint), "assets/gearwright/shapes/block/irrigator-pipe-endpoint.json")
    package.shape(_shape("irrigator-pipe-support", _support), "assets/gearwright/shapes/block/irrigator-pipe-support.json")
    package.shape(_shape("irrigator-pipe-inventory", _inventory), "assets/gearwright/shapes/block/irrigator-pipe-inventory.json")
    return package
