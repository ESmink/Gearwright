"""Approved straight-rod mount A, with centered seats and a clear stem saddle.

Approval: generated/lateral-drive-mount-review/current/a-straight-bearing-lanes.
The maintainer requested centering by attachment count when approving runtime
promotion. Geometry is shared by the runtime shapes and the review assembly.
"""

from dataclasses import replace
import math

from gearwright_graphics.model import ElementRef, Vec3
from graphics.review.lateral_motion_system import _shaft_segment

SEAT_PITCH = 1.4


def centered_offset(index, count):
    if not 1 <= count <= 4 or not 0 <= index < count:
        raise ValueError('a journal has one to four centered seats')
    return (index - (count - 1) / 2) * SEAT_PITCH


def _pin(shape, name, parent, x0, x1, y, radius, group):
    short = radius / math.sqrt(5)
    long = 2 * short
    shape.box(name, (x0, y - short, -long), (x1, y + short, long),
              texture='#gw-bronze', parent=parent, group=group)
    for side, y0, y1 in (('low', y - long, y - short), ('high', y + short, y + long)):
        shape.box(name + '-' + side, (x0, y0, -short), (x1, y1, short),
                  texture='#gw-bronze', parent=parent, group=group,
                  faces=('north', 'south', 'west', 'east', 'down' if side == 'low' else 'up'))


def rebuild_crank(shape, *, through):
    root = shape.ref('gw-crank-phase')
    shape.elements = [e for e in shape.elements if e.name == root.name]
    shape._refs = {e.name: ElementRef(shape.id, e.name) for e in shape.elements}
    _shaft_segment(shape, 'gw-crank-input-shaft', -8, -3.8, parent=root)
    if through:
        _shaft_segment(shape, 'gw-crank-output-shaft', 3.8, 8, parent=root)
    webs = (('input', -3.8, -3), ('output', 3, 3.8)) if through else (('single', -3.8, -3),)
    for side, x0, x1 in webs:
        shape.box(f'gw-crank-{side}-web', (x0, -1.35, -.8), (x1, 3.65, .8),
                  texture='#gw-iron', parent=root, group='crank-load-path')
    _pin(shape, 'gw-crank-bronze-journal', root, -3, 3, 3, .96, 'crank-journal')
    if not through:
        shape.box('gw-crank-journal-retainer', (2.88, 2.2, -1.1), (3.18, 3.8, 1.1),
                  texture='#gw-iron', parent=root, group='crank-journal')


def rebuild_dry_adapter(shape, *, lane=0):
    for i, e in enumerate(shape.elements):
        if e.name in ('gw-crosshead-guide-left', 'gw-crosshead-guide-right'):
            shape.elements[i] = replace(e, to=Vec3(e.to.x, -2.6, e.to.z))
        elif e.name == 'gw-connecting-rod-spine':
            shape.elements[i] = replace(e, from_=Vec3(e.from_.x, -2.65, e.from_.z))
    shape.elements = [e for e in shape.elements
                     if not ('-crosshead-bearing-' in e.name or
                         (e.name.startswith('gw-crosshead-') and not e.name.startswith('gw-crosshead-guide-')))]
    shape._refs = {e.name: ElementRef(shape.id, e.name) for e in shape.elements}
    rod = shape.ref('gw-connecting-rod-motion')
    for part, y0, y1, z0, z1 in (
        ('top', .3, .4, -.4, .4), ('bottom', -.4, -.3, -.4, .4),
        ('front', -.3, .3, -.4, -.3), ('back', -.3, .3, .3, .4)):
        shape.box(f'gw-connecting-rod-crosshead-bearing-{part}', (-.25, y0 - 3, z0),
                  (.25, y1 - 3, z1), texture='#gw-bronze', parent=rod, group='pump-bearing')
    moving = shape.ref('gw-piston-motion')
    for side, x0, x1 in (('left', -4.05, -3.25), ('right', 3.25, 4.05)):
        shape.box(f'gw-crosshead-shoe-{side}', (x0, -.55, -.55), (x1, .55, .55),
                  texture='#gw-iron', parent=moving, group='pump-motion')
    for side, z0, z1 in (('front', -1.35, -1.1), ('back', 1.1, 1.35)):
        shape.box(f'gw-crosshead-{side}-bridge', (-3.25, -.55, z0), (3.25, .55, z1),
                  texture='#gw-iron', parent=moving, group='pump-motion')
        shape.box(f'gw-crosshead-stem-{side}', (-.42, -.64, z0), (.42, .3, z1),
                  texture='#gw-iron', parent=moving, group='pump-motion')
    shape.box('gw-crosshead-stem-saddle', (-.42, -.64, -1.35), (.42, -.59, 1.35),
              texture='#gw-iron', parent=moving, group='pump-motion')
    for side, x0, x1 in (('left', -3.5, -3.25), ('right', 3.25, 3.5)):
        shape.box(f'gw-crosshead-{side}-cheek', (x0, -.55, -1.35), (x1, .55, 1.35),
                  texture='#gw-iron', parent=moving, group='pump-motion')
    _pin(shape, 'gw-crosshead-pin', moving, -3.25, 3.25, 0, .28, 'pump-bearing')
    if lane:
        delta = Vec3(lane, 0, 0)
        shape.elements = [replace(e, from_=e.from_ + delta, to=e.to + delta)
                          if e.parent == rod.name else e for e in shape.elements]
