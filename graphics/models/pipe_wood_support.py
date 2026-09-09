"""Chosen C support: continuous frames, single diagonal braces and optional crate lining."""

from __future__ import annotations

import math
from collections import OrderedDict

from gearwright_graphics.model import FACES, Face, ModelPackage, Shape, UVRect

ROTATIONS = {"north": (0, 0, 0), "east": (0, -90, 0), "south": (0, 180, 0),
             "west": (0, 90, 0), "up": (90, 0, 0), "down": (-90, 0, 0)}


def wood(shape, name, start, end, *, group="support", rotation=(0, 0, 0), origin=None):
    sizes = [end[i] - start[i] for i in range(3)]
    axis = max(range(3), key=lambda i: sizes[i])
    ends = (("east", "west"), ("up", "down"), ("north", "south"))[axis]
    faces = OrderedDict()
    for face in FACES:
        a, b = (0, 1) if face in ("north", "south") else (2, 1) if face in ("east", "west") else (0, 2)
        faces[face] = Face("#woodend" if face in ends else "#wood",
                           uv=UVRect(0, 0, sizes[a], sizes[b]),
                           rotation=90 if axis == b and face not in ends else 0)
    return shape.box(name, start, end, faces=faces, group=group,
                     rotation=rotation, rotation_origin=origin)


def beam(shape, name, start, end, *, width=1.2, group="support"):
    delta = [end[i] - start[i] for i in range(3)]
    length = math.sqrt(sum(value * value for value in delta))
    rotation = (0, -math.degrees(math.asin(delta[2] / length)),
                math.degrees(math.atan2(delta[1], delta[0])))
    return wood(shape, name, (start[0], start[1] - width / 2, start[2] - width / 2),
                (start[0] + length, start[1] + width / 2, start[2] + width / 2),
                origin=start, rotation=rotation, group=group)


def new_shape(name):
    shape = Shape("pipe-support-" + name, 16, 16)
    shape.texture("wood", "game:block/wood/plainoak")
    shape.texture("woodend", "game:block/wood/treetrunk/debarked/oak")
    return shape


def part(name: str) -> Shape:
    shape = new_shape(name)
    if name == "frame":
        # The twelve edges end exactly at the block boundary. Neighbor frames
        # share complete rectangular rims on all three axes, with no gap.
        for a in (0, 14.5):
            for b in (0, 14.5):
                wood(shape, f"upright-{a}-{b}", (a, 0, b), (a + 1.5, 16, b + 1.5))
                wood(shape, f"x-rail-{a}-{b}", (1.5, a, b), (14.5, a + 1.5, b + 1.5))
                wood(shape, f"z-rail-{a}-{b}", (a, b, 1.5), (a + 1.5, b + 1.5, 14.5))
    elif name in ("endpoint", "nozzle-endpoint"):
        inner = 4.5 if name == "nozzle-endpoint" else 5.5
        outer = inner - 1
        for suffix, start, end in (
            ("bottom", (outer, outer, 0), (16 - outer, inner, 1)),
            ("top", (outer, 16 - inner, 0), (16 - outer, 16 - outer, 1)),
            ("left", (outer, inner, 0), (inner, 16 - inner, 1)),
            ("right", (16 - inner, inner, 0), (16 - outer, 16 - inner, 1)),
        ):
            wood(shape, "collar-" + suffix, start, end, group="endpoint-collar")
        for x, joint_x in ((.75, outer + .5), (15.25, 15.5 - outer)):
            for y, joint_y in ((.75, outer + .5), (15.25, 15.5 - outer)):
                beam(shape, f"collar-knee-{x}-{y}", (x, y, .75), (joint_x, joint_y, .75),
                     group="endpoint-brace")
    elif name == "corners":
        # Keep the public shape code while replacing the four knees as requested.
        beam(shape, "diagonal-brace", (.75, .75, .75), (15.25, 15.25, .75),
             group="diagonal-brace")
    elif name in ("lining", "lining-port", "lining-nozzle", "lining-sprinkler"):
        shape.texture("lining", "gearwright:block/pipe-insulation")
        rectangles = [(1.5, 1.5, 14.5, 14.5)]
        if name != "lining":
            inner = 4 if name == "lining-sprinkler" else 4.5 if name == "lining-nozzle" else 6
            rectangles = [(1.5, 1.5, 14.5, inner), (1.5, 16 - inner, 14.5, 14.5),
                          (1.5, inner, inner, 16 - inner), (16 - inner, inner, 14.5, 16 - inner)]
        for i, (x1, y1, x2, y2) in enumerate(rectangles):
            faces = OrderedDict((face, Face("#lining", uv=UVRect(x1, 16 - y2, x2, 16 - y1))) for face in FACES)
            shape.box(f"lining-{i}", (x1, y1, 1.5), (x2, y2, 2), faces=faces, group="lining")
    elif name in ("top", "top-opening"):
        # Infill meets the frame's upper rim without overlapping its surfaces.
        # Together they form the same three sparse boards, now flush at the edges.
        for i, (z1, z2) in enumerate(((1.5, 3.5), (6.75, 9.25), (12.5, 14.5))):
            if i == 1 and name == "top-opening":
                continue
            wood(shape, f"deck-{i}", (1.5, 14.5, z1), (14.5, 16, z2), group="top-platform")
    elif name == "anchor":
        # Retain this emitted asset for compatibility. The expanded frame now
        # contacts neighbors directly and no longer needs these inset-frame feet.
        for x in (1.5, 13):
            for y in (1.5, 13):
                wood(shape, f"anchor-{x}-{y}", (x, y, 0), (x + 1.5, y + 1.5, 1.5), group="anchor")
    elif name == "seat":
        for x in (6, 8.5):
            wood(shape, f"saddle-{x}", (x, 4.5, .75), (x + 1.5, 6, 15.25), group="seat")
            for z in (.75, 15.25):
                beam(shape, f"seat-knee-{x}-{z}", (.75 if x == 6 else 15.25, .75, z),
                     (x + .75, 5.25, z), group="seat")
    else:
        raise ValueError(f"unknown support part: {name}")
    return shape


PARTS = ("frame", "endpoint", "nozzle-endpoint", "corners", "top", "top-opening", "anchor", "seat",
         "lining", "lining-port", "lining-nozzle", "lining-sprinkler")


def build() -> ModelPackage:
    package = ModelPackage("pipe_wood_support")
    for name in PARTS:
        package.shape(part(name), f"assets/gearwright/shapes/block/pipe-support-{name}.json")
    return package


def append_part(target, source, prefix, *, offset=(0, 0, 0), face="north"):
    target.textures.update(source.textures)
    parent = target._element(prefix, offset, offset, pivot=True,
                             rotation_origin=tuple(v + 8 for v in offset), rotation=ROTATIONS[face])
    for element in source.elements:
        target._element(prefix + "-" + element.name, element.from_.values(), element.to.values(),
                        faces=element.faces, rotation_origin=element.rotation_origin.values() if element.rotation_origin else None,
                        rotation=element.rotation.values(), group=element.group, parent=parent)


def compose_support(shape, prefix="", offset=(0, 0, 0), *, connections=(), nozzles=(),
                    pipe_above=False, sprinkler=False, insulated=False):
    append_part(shape, part("frame"), prefix + "frame", offset=offset)
    for face in FACES:
        wide = face in nozzles or (sprinkler and face == "down")
        endpoint = wide or face in connections
        kind = "nozzle-endpoint" if wide else "endpoint" if endpoint else "corners"
        if endpoint or face != "up":
            append_part(shape, part(kind), prefix + face + "-" + kind, offset=offset, face=face)
        if insulated:
            lining = "lining-sprinkler" if sprinkler and face == "down" else "lining-nozzle" if wide else "lining-port" if endpoint else "lining"
            append_part(shape, part(lining), prefix + face + "-" + lining, offset=offset, face=face)
    if not insulated and not pipe_above:
        kind = "top-opening" if "up" in connections or "up" in nozzles else "top"
        append_part(shape, part(kind), prefix + kind, offset=offset)
    if not connections and not nozzles and not sprinkler:
        append_part(shape, part("seat"), prefix + "seat", offset=offset)
