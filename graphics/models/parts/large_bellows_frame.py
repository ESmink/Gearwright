"""Approved A: continue the native stand with braced posts and base-top rails."""
from collections import OrderedDict
import math

from gearwright_graphics.model import Face, UVRect

FRAME_GROUP = 'bellows-upper-frame'


def timber(shape, name, a, b, *, width=2, depth=.5, grain=0):
    """Oak side-frame plank, with lengthwise grain at native pixel density."""
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    angle = math.degrees(math.atan2(dy, dx))
    offset = 0
    while offset < length - 1e-8:
        u = (grain + offset) % 16
        part = min(length - offset, 16 - u)
        start = (a[0] + dx * offset / length, a[1] + dy * offset / length, a[2])
        faces = OrderedDict((face, Face('#plainoak', uv=UVRect(*uv))) for face, uv in (
            ('north', (u, 2, u + part, 2 + width)),
            ('south', (u, 11.5, u + part, 11.5 + width)),
            ('up', (u, 14, u + part, 14 + depth)),
            ('down', (u, 14.5, u + part, 14.5 + depth)),
            ('east', (8, 8, 8 + depth, 8 + width)),
            ('west', (8, 8, 8 + depth, 8 + width)),
        ))
        shape.box(f'gw-bellows-extension-{name}-{offset:g}',
            (start[0], start[1] - width / 2, start[2] - depth / 2),
            (start[0] + part, start[1] + width / 2, start[2] + depth / 2),
            faces=faces, rotation_origin=start, rotation=(0, 0, angle), group=FRAME_GROUP)
        offset += part


def upper_frame(shape):
    for side, z, brace_z in (('north', .75, .25), ('south', 15.25, 15.75)):
        # Continue the native post's outer face; relieve the inside alongside
        # the moving leather, returning to full thickness above the chamber.
        relief_z = z + (-.075 if side == 'north' else .075)
        timber(shape, side + '-rear-post-low', (15, 11.5, relief_z),
               (15, 20, relief_z), depth=.35, grain=11.5)
        timber(shape, side + '-rear-post-high', (15, 20, z), (15, 32, z), grain=20)
        timber(shape, side + '-bearing-seat', (14, 30, z), (18.3, 30, z))
        timber(shape, side + '-raking-brace', (6, 10.5, brace_z), (15, 25.5, brace_z))
        timber(shape, side + '-base-top-rail', (7, 10.5, relief_z),
               (14, 10.5, relief_z), depth=.35)
    shape.box('gw-bellows-extension-top-tie', (14, 32, .5), (16, 32.5, 15.5),
              texture='#plainoak', face_rotations={'up': 90, 'down': 90}, group=FRAME_GROUP)
