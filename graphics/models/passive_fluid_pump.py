"""Passive gravity pump: body, attachments, moving mechanism, and review scenes."""

from __future__ import annotations

from gearwright_graphics.model import ModelPackage, ReviewAssembly, ReviewScene, Shape, Vec3, animate, model_creator_box_faces


def _body(shape: Shape) -> None:
    for key, location, size in (
        ("copper", "game:block/metal/sheet/copper1", (32, 32)),
        ("brass", "game:block/metal/ingot/brass", (32, 32)),
        ("glass", "gearwright:block/inspection-glass", (16, 16)),
        ("shadow", "gearwright:block/inspection-shadow", (4, 4)),
    ):
        shape.texture(key, location, size=size)
    items = (
        ("foot", (-6.5, 0, -6.5), (6.5, 1.35, 6.5), "#copper"), ("lower-body", (-5, 1.35, -5), (5, 4.25, 5), "#copper"), ("base-band", (-5.35, 4, -5.35), (5.35, 5.15, 5.35), "#brass"), ("flow-bed", (-4.5, 5.15, -4.5), (4.5, 6.25, 4.5), "#copper"), ("flow-cap", (-4.5, 9.75, -4.5), (4.5, 10.85, 4.5), "#copper"), ("channel-shadow-floor", (-3.65, 6.25, -4), (3.65, 6.45, 4), "#shadow"), ("channel-shadow-ceiling", (-3.65, 9.55, -4), (3.65, 9.75, 4), "#shadow"),
        ("west-window-frame-bottom", (-3.95, 5.9, -4.8), (3.95, 6.55, -4.1), "#brass"), ("west-window-frame-top", (-3.95, 9.45, -4.8), (3.95, 10.1, -4.1), "#brass"), ("west-window-frame-front", (-3.95, 6.55, -4.8), (-3.3, 9.45, -4.1), "#brass"), ("west-window-frame-back", (3.3, 6.55, -4.8), (3.95, 9.45, -4.1), "#brass"), ("east-window-frame-bottom", (-3.95, 5.9, 4.1), (3.95, 6.55, 4.8), "#brass"), ("east-window-frame-top", (-3.95, 9.45, 4.1), (3.95, 10.1, 4.8), "#brass"), ("east-window-frame-front", (-3.95, 6.55, 4.1), (-3.3, 9.45, 4.8), "#brass"), ("east-window-frame-back", (3.3, 6.55, 4.1), (3.95, 9.45, 4.8), "#brass"),
        ("deck-post-nw", (-4.8, 10.85, -4.8), (-3.7, 13.45, -3.7), "#copper"), ("deck-post-ne", (-4.8, 10.85, 3.7), (-3.7, 13.45, 4.8), "#copper"), ("deck-post-sw", (3.7, 10.85, -4.8), (4.8, 13.45, -3.7), "#copper"), ("deck-post-se", (3.7, 10.85, 3.7), (4.8, 13.45, 4.8), "#copper"), ("upper-header", (-5, 13.35, -5), (5, 14.3, 5), "#brass"), ("flat-deck", (-7, 14.3, -7), (7, 16, 7), "#copper"),
        ("gauge-neck", (-5, 10.35, 1.75), (-4, 11.05, 3.85), "#copper"), ("gauge-face", (-5.2, 10.35, .95), (-5.06, 13.35, 4.65), "#shadow"), ("gauge-frame-left", (-5.45, 10.05, .65), (-4.8, 13.65, 1.35), "#brass"), ("gauge-frame-right", (-5.45, 10.05, 4.25), (-4.8, 13.65, 4.95), "#brass"), ("gauge-frame-bottom", (-5.45, 10.05, 1.35), (-4.8, 10.75, 4.25), "#brass"), ("gauge-frame-top", (-5.45, 12.95, 1.35), (-4.8, 13.65, 4.25), "#brass"),
        ("gauge-mark-0", (-5.48, 11.395, 1.2611), (-5.28, 11.895, 1.4811), "#brass"), ("gauge-mark-1", (-5.48, 11.834, 1.6294), (-5.28, 12.334, 1.8494), "#brass"), ("gauge-mark-2", (-5.48, 12.1205, 2.1257), (-5.28, 12.6205, 2.3457), "#brass"), ("gauge-mark-3", (-5.48, 12.22, 2.69), (-5.28, 12.72, 2.91), "#brass"), ("gauge-mark-4", (-5.48, 12.1205, 3.2543), (-5.28, 12.6205, 3.4743), "#brass"), ("gauge-mark-5", (-5.48, 11.834, 3.7506), (-5.28, 12.334, 3.9706), "#brass"), ("gauge-mark-6", (-5.48, 11.395, 4.1189), (-5.28, 11.895, 4.3389), "#brass"), ("gauge-hub", (-5.66, 10.5, 2.48), (-5.42, 11.14, 3.12), "#brass"), ("sight-glass-front", (-4.62, 6.48, -3.42), (-4.47, 9.52, 3.42), "#glass"), ("sight-glass-back", (4.47, 6.48, -3.42), (4.62, 9.52, 3.42), "#glass"), ("pressure-guide-frame-left", (-4.78, 8.78, -1.05), (-4.3, 9.65, -.58), "#brass"), ("pressure-guide-frame-right", (-4.78, 8.78, .58), (-4.3, 9.65, 1.05), "#brass"), ("pressure-guide-cap", (-4.78, 9.3, -.58), (-4.3, 9.65, .58), "#copper"),
    )
    mark_rotations = {
        "gauge-mark-0": 60, "gauge-mark-1": 40, "gauge-mark-2": 20,
        "gauge-mark-4": -20, "gauge-mark-5": -40, "gauge-mark-6": -60,
    }
    tile_sizes = {"#copper": 8, "#brass": 8, "#glass": 4, "#shadow": 1}
    for name, from_, to, texture in items:
        rotation_x = mark_rotations.get(name, 0)
        rotation_origin = tuple((left + right) / 2 for left, right in zip(from_, to)) if rotation_x else None
        shape.box(
            name, from_, to,
            faces=model_creator_box_faces(texture, tile_sizes[texture]),
            rotation_origin=rotation_origin,
            rotation=(rotation_x, 0, 0),
            group="body-shell",
        )


def _intake(shape: Shape) -> None:
    shape.texture("copper", "game:block/metal/sheet/copper1")
    shape.texture("brass", "game:block/metal/ingot/brass")
    shape.texture("shadow", "gearwright:block/inspection-shadow")
    for name, from_, to, texture in (
        ("intake-throat-bottom", (6.5, 6.5, 10.5), (9.5, 6.9, 13.5), "#copper"), ("intake-throat-top", (6.5, 9.1, 10.5), (9.5, 9.5, 13.5), "#copper"), ("intake-throat-left", (6.5, 6.9, 10.5), (6.9, 9.1, 13.5), "#copper"), ("intake-throat-right", (9.1, 6.9, 10.5), (9.5, 9.1, 13.5), "#copper"),
        ("intake-gland-bottom", (5.5, 5.5, 13.5), (10.5, 6.5, 14.5), "#brass"), ("intake-gland-top", (5.5, 9.5, 13.5), (10.5, 10.5, 14.5), "#brass"), ("intake-gland-left", (5.5, 6.5, 13.5), (6.5, 9.5, 14.5), "#brass"), ("intake-gland-right", (9.5, 6.5, 13.5), (10.5, 9.5, 14.5), "#brass"),
        ("intake-tube-bottom", (6.5, 6.5, 14.5), (9.5, 6.9, 18.5), "#copper"), ("intake-tube-top", (6.5, 9.1, 14.5), (9.5, 9.5, 18.5), "#copper"), ("intake-tube-left", (6.5, 6.9, 14.5), (6.9, 9.1, 18.5), "#copper"), ("intake-tube-right", (9.1, 6.9, 14.5), (9.5, 9.1, 18.5), "#copper"),
        ("intake-ferrule-bottom", (6, 6, 16), (10, 6.5, 17), "#brass"), ("intake-ferrule-top", (6, 9.5, 16), (10, 10, 17), "#brass"), ("intake-ferrule-left", (6, 6.5, 16), (6.5, 9.5, 17), "#brass"), ("intake-ferrule-right", (9.5, 6.5, 16), (10, 9.5, 17), "#brass"),
        ("intake-lip-bottom", (6.1, 6.1, 17.75), (9.9, 6.5, 18.5), "#brass"), ("intake-lip-top", (6.1, 9.5, 17.75), (9.9, 9.9, 18.5), "#brass"), ("intake-lip-left", (6.1, 6.5, 17.75), (6.5, 9.5, 18.5), "#brass"), ("intake-lip-right", (9.5, 6.5, 17.75), (9.9, 9.5, 18.5), "#brass"), ("intake-shadow", (6.9, 6.9, 18.2), (9.1, 9.1, 18.48), "#shadow"),
    ):
        shape.box(name, from_, to, texture=texture, group="intake")


def _outlet(shape: Shape) -> None:
    shape.texture("copper", "game:block/metal/sheet/copper1")
    for name, from_, to in (
        ("outlet-bottom", (6, 6, 0), (10, 6.5, 4)), ("outlet-top", (6, 9.5, 0), (10, 10, 4)), ("outlet-left", (6, 6.5, 0), (6.5, 9.5, 4)), ("outlet-right", (9.5, 6.5, 0), (10, 9.5, 4)), ("outlet-half-coupling-bottom", (5.5, 5.5, 0), (10.5, 6, .65)), ("outlet-half-coupling-top", (5.5, 10, 0), (10.5, 10.5, .65)), ("outlet-half-coupling-left", (5.5, 6, 0), (6, 10, .65)), ("outlet-half-coupling-right", (10, 6, 0), (10.5, 10, .65)),
    ):
        shape.box(name, from_, to, texture="#copper", group="outlet")


def _liquid(shape: Shape) -> None:
    shape.texture("liquid", "game:block/liquid/waterportion", size=(24, 24))
    faces = model_creator_box_faces("#liquid", 6)
    shape.box("flow-liquid-front", (-4.42, 6.72, -3.3), (-4.37, 9.28, 3.3), faces=faces, group="liquid")
    shape.box("flow-liquid-back", (4.37, 6.72, -3.3), (4.42, 9.28, 3.3), faces=faces, group="liquid")


def _mechanism(shape: Shape) -> None:
    shape.texture("copper", "game:block/metal/sheet/copper1", size=(32, 32))
    shape.texture("brass", "game:block/metal/ingot/brass", size=(32, 32))
    plunger = shape.pivot("b_pressure-plunger", (-4.19, 7.15, 0), group="gauge-mechanism")
    shape.box("pressure-plunger", (-.15, 0, -.78), (.15, .97, .78), faces=model_creator_box_faces("#brass", 8), parent=plunger, group="gauge-mechanism")
    shape.box("pressure-plunger-rod", (-.15, .97, -.2), (.15, 2.15, .2), faces=model_creator_box_faces("#copper", 8), parent=plunger, group="gauge-mechanism")
    gauge = shape.pivot("b_gauge-needle", (-5.54, 10.82, 2.8), group="gauge-mechanism")
    shape.box("gauge-needle", (-.08, -.04, -.16), (.07, 1.83, .16), faces=model_creator_box_faces("#brass", 8), parent=gauge, group="gauge-mechanism")
    pressure = animate(
        shape, "pressure", "pressure", 30,
        version=1, on_activity_stopped="EaseOut", on_animation_end="Hold",
    )
    shortest = {"rotShortestDistanceX": True, "rotShortestDistanceY": True, "rotShortestDistanceZ": True}
    pressure.keyframe(0, plunger, **shortest, offsetX=0, offsetY=0, offsetZ=0)
    pressure.keyframe(0, gauge, **shortest, rotationX=-52, rotationY=0, rotationZ=0)
    pressure.keyframe(29, plunger, **shortest, offsetX=0, offsetY=.85, offsetZ=0)
    pressure.keyframe(29, gauge, **shortest, rotationX=52, rotationY=0, rotationZ=0)
    shape.add_animation(pressure.build())


def build() -> ModelPackage:
    package = ModelPackage("passive_fluid_pump")
    body = Shape("passive-fluid-pump", 32, 32)
    _body(body)
    package.shape(body, "assets/gearwright/shapes/block/passive-fluid-pump.json")
    intake = Shape("passive-fluid-pump-intake")
    _intake(intake)
    package.shape(intake, "assets/gearwright/shapes/block/passive-fluid-pump-intake.json")
    outlet = Shape("passive-fluid-pump-outlet")
    _outlet(outlet)
    package.shape(outlet, "assets/gearwright/shapes/block/passive-fluid-pump-outlet.json")
    liquid = Shape("passive-fluid-pump-liquid", 24, 24)
    _liquid(liquid)
    package.shape(liquid, "assets/gearwright/shapes/block/passive-fluid-pump-liquid.json")
    mechanism = Shape("passive-fluid-pump-mechanism", 32, 32)
    _mechanism(mechanism)
    package.shape(mechanism, "assets/gearwright/shapes/block/passive-fluid-pump-mechanism.json")
    groups = (
        ReviewAssembly("body-shell", ("foot", "lower-body", "base-band", "flow-bed", "flow-cap", "channel-*", "deck-*", "upper-header", "flat-deck"), Vec3(0, 0, 0)),
        ReviewAssembly("intake", ("intake-*",), Vec3(0, 0, 1)), ReviewAssembly("outlet", ("outlet-*",), Vec3(0, 0, -1)),
        ReviewAssembly("gauge-mechanism", ("b_*", "pressure-*", "gauge-*"), Vec3(-1, 0, 0)), ReviewAssembly("sight-glass", ("sight-glass-*",), Vec3(0, 0, 1)), ReviewAssembly("liquid", ("flow-liquid-*",), Vec3(0, 0, .8)),
    )
    package.add_scene(ReviewScene("side-tank-active", ({"asset": "gearwright:block/passive-fluid-pump", "role": "target"}, {"asset": "gearwright:block/passive-fluid-pump-intake", "placement": [0, 0, 0], "role": "intake"}, {"asset": "gearwright:block/passive-fluid-pump-outlet", "role": "outlet"}, {"asset": "gearwright:block/passive-fluid-pump-liquid", "role": "liquid"}, {"asset": "gearwright:block/passive-fluid-pump-mechanism", "animation": "pressure", "frame": 29, "role": "mechanism"}), ("front-right", "top"), groups))
    package.add_scene(ReviewScene("top-tank-active", ({"asset": "gearwright:block/passive-fluid-pump", "role": "target"}, {"asset": "gearwright:block/passive-fluid-pump-intake", "placement": [0, 1, 0], "rotation": [-90, 0, 0], "role": "intake"}, {"asset": "gearwright:block/passive-fluid-pump-outlet", "role": "outlet"}, {"asset": "gearwright:block/passive-fluid-pump-liquid", "role": "liquid"}, {"asset": "gearwright:block/passive-fluid-pump-mechanism", "animation": "pressure", "frame": 29, "role": "mechanism"}), ("front-right", "top"), groups))
    package.add_scene(ReviewScene("exploded", ({"asset": "gearwright:block/passive-fluid-pump", "role": "target"}, {"asset": "gearwright:block/passive-fluid-pump-intake", "role": "intake"}, {"asset": "gearwright:block/passive-fluid-pump-outlet", "role": "outlet"}, {"asset": "gearwright:block/passive-fluid-pump-liquid", "role": "liquid"}, {"asset": "gearwright:block/passive-fluid-pump-mechanism", "animation": "pressure", "frame": 14.5, "role": "mechanism"}), ("isometric",), groups))
    return package
