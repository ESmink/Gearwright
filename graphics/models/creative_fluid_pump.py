"""Creative test pump."""

from gearwright_graphics.model import ModelPackage, Shape


def build() -> ModelPackage:
    package = ModelPackage("creative_fluid_pump")
    shape = Shape("creative-fluid-pump")
    shape.texture("copper", "game:block/metal/sheet/copper1")
    shape.texture("brass", "game:block/metal/ingot/brass")
    shape.texture("glass", "gearwright:block/inspection-glass")
    for name, from_, to, texture, kwargs in (
        ("base", (1, 0, 1), (15, 2, 15), "#copper", {}), ("body", (2, 2, 2), (14, 13, 14), "#copper", {}), ("top", (1.5, 13, 1.5), (14.5, 15, 14.5), "#brass", {}),
        ("dial-rim", (5, 6, 1.2), (11, 12, 2.2), "#brass", {}), ("dial-face", (5.7, 6.7, 1), (10.3, 11.3, 1.25), "#glass", {"faces": ("north",), "render_pass": 1}),
        ("dial-needle", (7.7, 7.3, .7), (8.3, 10.7, 1.1), "#brass", {"rotation_origin": (8, 8.9, .9), "rotation": (0, 0, 28)}),
        ("port-left", (0, 6, 6), (3, 10, 10), "#brass", {}), ("port-left-half-coupling", (0, 5.5, 5.5), (.65, 10.5, 10.5), "#copper", {}),
        ("port-right", (13, 6, 6), (16, 10, 10), "#brass", {}), ("port-right-half-coupling", (15.35, 5.5, 5.5), (16, 10.5, 10.5), "#copper", {}),
    ):
        shape.box(name, from_, to, texture=texture, group="pump", **kwargs)
    package.shape(shape, "assets/gearwright/shapes/block/creative-fluid-pump.json")
    return package
