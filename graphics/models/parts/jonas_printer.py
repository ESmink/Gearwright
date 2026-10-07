"""Approved D3 printer, with visible outward feed and tangent paper-roll joins."""
from __future__ import annotations

import math
import numpy as np

from gearwright_graphics.model import animate


CANDIDATE = 'd3-request-printer'
PAPER_WIDTH = 2.8
PAPER_TOP = 14.16
FEED_DISTANCE = 1.35
ROLL_RADIUS = .8
STAMP_FRAMES = (18, 30, 42, 54)


def timeline(frame, points):
    for (a, va), (b, vb) in zip(points, points[1:]):
        if frame <= b:
            t = max(0., min(1., (frame - a) / (b - a)))
            return va + (vb - va) * t * t * (3 - 2 * t)
    return points[-1][1]


def printer_motion(frame):
    travel = timeline(frame, ((0, 0), (20, 0), (24, .65), (32, .65),
        (36, 1.3), (44, 1.3), (48, 1.95), (104, 1.95), (120, 0)))
    press_points = [(0, 0)]
    for hit in STAMP_FRAMES:
        press_points += [(hit - 6, 0), (hit - 2, .42), (hit, .42), (hit + 2, 0)]
    press_points += [(120, 0)]
    return {
        'travel': travel,
        'press': timeline(frame, press_points),
        'feed': timeline(frame, ((0, 0), (60, 0), (100, FEED_DISTANCE), (120, FEED_DISTANCE))),
    }


def request_printer(c, saddle):
    """One narrow X-feed strip; spool axes are Z, 90 degrees from D2."""
    for name, x in (('left', 2.55), ('right', 12.85)):
        saddle(c, name + '-saddle', x, x + .6)
    c.box('printer-floor', (2.45, 12.08, 5.65), (13.55, 12.43, 10.35), 'jonas-shield')
    for name, z in (('front', 5.65), ('rear', 10.0)):
        c.box(name + '-base-rim', (2.4, 12.31, z), (13.6, 12.72, z + .35), 'jonas-plain')
        for x in (3.15, 12.4):
            c.box(name + '-mount-' + str(x), (x, 12.08, z - .12), (x + .45, 12.32, z + .47), 'brass')
    for name, x in (('supply', 3.8), ('takeup', 12.2)):
        for end, z in (('front', 6.02), ('rear', 9.66)):
            c.box(name + '-' + end + '-bearing-foot', (x - .62, 12.43, z),
                  (x + .62, 12.76, z + .34), 'jonas-cupro')
            c.box(name + '-' + end + '-bearing', (x - .43, 12.76, z),
                  (x + .43, 13.98, z + .34), 'jonas-plain')
        c.box(name + '-spindle', (x - .12, 13.24, 5.91), (x + .12, 13.48, 10.1), 'jonas-steel')
        pivot = c.shape.pivot('stock-' + name + '-roll', (x, 13.36, 8), group='stock-controls')
        paper = c.shape.pivot('stock-' + name + '-paper-roll', (x, 13.36, 8), group='stock-controls')
        # Paper has a fixed tangent surface. Flanges and witness marks turn;
        # the web never jumps as a faceted reel rotates under it.
        for i in range(4):
            half = ROLL_RADIUS * math.tan(math.pi / 8)
            c.box(f'{name}-paper-{i}', (-half, -ROLL_RADIUS, -PAPER_WIDTH / 2),
                  (half, ROLL_RADIUS, PAPER_WIDTH / 2), 'scroll-paper', parent=paper,
                  rotation_origin=(0, 0, 0), rotation=(0, 0, i * 45),
                  face_uvs=paper_uvs(x - half, x + half, 6.6, 9.4))
        for end, z in (('front', -1.58), ('rear', 1.42)):
            for i in range(4):
                c.box(f'{name}-{end}-flange-{i}', (-.35, -.85, z), (.35, .85, z + .16),
                      'cylinder-end', parent=pivot, rotation_origin=(0, 0, 0), rotation=(0, 0, i * 45))
            c.box(name + '-' + end + '-witness', (-.10, .37, z - .008),
                  (.10, .72, z + .17), 'jonas-plain', parent=pivot)
    # Continuous material follows the same fixed path as the two rotating rolls.
    # The moving ink below shows advance without stretching or detaching the web.
    half = ROLL_RADIUS * math.tan(math.pi / 8)
    c.box('paper-web', (3.8 + half, PAPER_TOP - .06, 6.6), (12.2 - half, PAPER_TOP, 9.4),
          'scroll-paper', face_uvs=paper_uvs(3.8 + half, 12.2 - half, 6.6, 9.4))
    c.box('takeup-paper-bridge', (11.57, PAPER_TOP + .02, 6.32),
          (11.99, PAPER_TOP + .23, 9.68), 'jonas-plain')
    for name, z in (('front', 6.34), ('rear', 9.42)):
        c.box(name + '-paper-guide', (5.7, 13.99, z), (11.2, 14.34, z + .24), 'brass')
    c.box('paper-platen', (4.85, 13.82, 6.56), (5.65, 14.09, 9.44), 'jonas-steel')

    # A covered printing end occupies roughly the first quarter of the cassette.
    for name, z in (('front', 5.88), ('rear', 9.9)):
        c.box('hood-' + name + '-cheek', (2.57, 12.72, z), (5.90, 14.98, z + .22))
        c.box('hood-' + name + '-lip', (2.45, 14.74, z - .10), (5.99, 15.08, z + .32), 'jonas-plain')
    c.box('hood-end', (2.43, 12.72, 6.1), (2.69, 14.86, 9.9), 'jonas-cupro')
    c.box('hood-roof-back', (2.69, 14.86, 6.1), (4.68, 15.16, 9.9), 'jonas-shield')
    c.box('hood-roof-front', (5.69, 14.86, 6.1), (5.99, 15.16, 9.9), 'jonas-shield')
    c.box('hood-mouth-visor', (5.89, 14.41, 6.1), (6.09, 14.87, 9.9), 'jonas-cupro')
    # A dark baffle encloses the print slot while allowing the narrow stem through.
    for name, x0, x1 in (('left', 4.68, 5.07), ('right', 5.43, 5.69)):
        c.box('hood-slot-baffle-' + name, (x0, 14.87, 6.1), (x1, 15.0, 9.9), 'jonas-shield')
    for name, z in (('front', 6.0), ('rear', 9.76)):
        for x in (4.56, 5.65):
            c.box('guide-post-' + name + str(x), (x, 14.98, z), (x + .22, 15.83, z + .24))
    for name, x in (('left', 4.61), ('right', 5.70)):
        c.box('carriage-guide-' + name, (x, 15.48, 6.05), (x + .12, 15.60, 9.96), 'jonas-steel')
    carriage = c.shape.pivot('stock-print-carriage', (5.25, 15.56, 7.0), group='stock-controls')
    for name, a, b in (('left', -.47, -.15), ('right', .15, .47)):
        c.box('carriage-bridge-' + name, (a, .10, -.26), (b, .27, .26), 'jonas-plain', parent=carriage)
    for name, x in (('left', -.72), ('right', .47)):
        c.box('carriage-shoe-' + name, (x, -.04, -.27), (x + .25, .14, .27), 'jonas-plain', parent=carriage)
    # Plunger is animated on its own fixed origin, with the same transverse travel.
    press = c.shape.pivot('stock-print-plunger', (5.25, 14.97, 7.0), group='stock-controls')
    c.box('plunger-stem', (-.10, -.13, -.09), (.10, .77, .09), 'jonas-steel', parent=press)
    c.box('plunger-cap', (-.12, .72, -.15), (.12, .85, .15), 'jonas-plain', parent=press)
    c.box('print-foot', (-.24, -.39, -.23), (.24, -.13, .23), 'brass', parent=press)
    c.box('print-type', (-.21, -.39, -.19), (.21, -.36, .19), 'cylinder-record', parent=press)
    # Illustrative item stamps are confined under the cover, not simulated text.
    for row, z in enumerate((7.0, 7.65, 8.3, 8.95)):
        ink = c.shape.pivot('stock-print-ink-' + str(row), (5.25, PAPER_TOP - .175, z), group='stock-controls')
        c.box('item-stamp-' + str(row), (-.16, 0, -.17), (.16, .012, .17), 'cylinder-record', parent=ink)
        c.box('item-stamp-detail-' + str(row), (-.23, 0, -.06), (.23, .012, .06), 'cylinder-record', parent=ink)
    # Four previous order rows travel with the next advance. The far row
    # passes under the take-up guide before disappearing onto the roll.
    for age in range(4):
        history = c.shape.pivot('stock-print-history-' + str(age),
            (5.25 + FEED_DISTANCE * (age + 1), PAPER_TOP - .175, 8), group='stock-controls')
        for row, z in enumerate((-1., -.35, .3, .95)):
            c.box(f'history-{age}-stamp-{row}', (-.16, 0, z - .17), (.16, .012, z + .17), 'cylinder-record', parent=history)
            c.box(f'history-{age}-detail-{row}', (-.23, 0, z - .06), (.23, .012, z + .06), 'cylinder-record', parent=history)
    # A compact exposed feed wheel turns only while paper advances.
    drive = c.shape.pivot('stock-feed-wheel', (3.8, 13.36, 5.52), group='stock-controls')
    c.ring('feed-wheel-rim', 'z', -.14, .14, (0, 0), .65, .14, 'jonas-steel', parent=drive)
    c.box('feed-wheel-spoke', (-.55, -.10, -.11), (.55, .10, .11), 'jonas-steel', parent=drive)
    c.box('feed-wheel-hub', (-.22, -.22, -.19), (.22, .22, .19), 'jonas-plain', parent=drive)
    for i in range(8):
        c.box('feed-wheel-tooth-' + str(i), (-.14, .55, -.13), (.14, .77, .13),
              'jonas-steel', parent=drive, rotation_origin=(0, 0, 0), rotation=(0, 0, i * 45))
    c.box('feed-wheel-shaft', (3.69, 13.25, 5.48), (3.91, 13.47, 6.12), 'jonas-steel')
    c.box('pickup-case', (2.87, 15.16, 7.15), (3.87, 15.65, 8.85), 'jonas-cupro')
    c.box('pickup-slot', (2.94, 15.35, 7.08), (3.8, 15.54, 7.16), 'jonas-flux', glow=18)
    c.box('pickup-crown', (2.78, 15.65, 7.06), (3.96, 15.84, 8.94), 'jonas-plain')

    animation = animate(c.shape, 'Print the request then advance the scroll', 'stock-check', 121,
                        on_animation_end='Hold', on_activity_stopped='Stop')
    for frame in range(0, 121, 2):
        motion = printer_motion(frame)
        angle = -math.degrees(motion['feed'] / ROLL_RADIUS)
        animation.keyframe(frame, carriage, offsetZ=motion['travel'])
        animation.keyframe(frame, press, offsetZ=motion['travel'], offsetY=-motion['press'])
        for name in ('stock-takeup-roll', 'stock-supply-roll', 'stock-feed-wheel'):
            animation.keyframe(frame, c.shape.ref(name), rotationZ=angle)
        for row, hit in enumerate(STAMP_FRAMES):
            animation.keyframe(frame, c.shape.ref('stock-print-ink-' + str(row)), offsetX=motion['feed'],
                               offsetY=.18 if frame >= hit else 0)
        for age in range(4):
            animation.keyframe(frame, c.shape.ref('stock-print-history-' + str(age)), offsetX=motion['feed'])
    c.shape.add_animation(animation.build())



TEXTURES = {
    'jonas-cupro': ('game:block/metal/sheet/cupronickel1', (16, 16)),
    'jonas-plain': ('game:block/metal/sheet-plain/cupronickel1', (16, 16)),
    'jonas-steel': ('game:block/metal/sheet-plain/steel1', (16, 16)),
    'jonas-shield': ('game:block/metal/tarnished/lead', (16, 16)),
    'jonas-flux': ('game:block/machine/statictranslocator/rustyglow', (16, 16)),
    'cylinder-record': ('game:block/coal/charcoal', (16, 16)),
    'cylinder-end': ('game:block/wood/debarked/ebony', (16, 16)),
    'scroll-paper': ('game:block/cloth/paper/blank-small', (32, 32)),
}


class Controls:
    def __init__(self, shape):
        self.shape = shape

    def box(self, name, lo, hi, material='jonas-cupro', **kwargs):
        # Native density, with quieter sheet-metal crops for small components.
        if 'uv' not in kwargs and 'face_uvs' not in kwargs:
            dx, dy, dz = np.asarray(hi) - np.asarray(lo)
            u, v = (4., 4.) if material.startswith('jonas-') else (0., 0.)
            kwargs['face_uvs'] = {face: (u, v, u + w, v + h) for face, w, h in (
                ('north', dx, dy), ('south', dx, dy), ('west', dz, dy),
                ('east', dz, dy), ('up', dx, dz), ('down', dx, dz))}
        return self.shape.box('stock-' + name, lo, hi, texture='#' + material,
                              group='stock-controls', **kwargs)

    def ring(self, name, axis, start, end, center, radius, thickness, material,
             *, parent=None, omit=(), segments=8):
        width = 2 * radius * math.tan(math.pi / segments)
        for i in range(segments):
            if i in omit:
                continue
            if axis == 'x':
                y, z = center
                lo, hi = (start, y + radius - thickness, z - width / 2), (end, y + radius, z + width / 2)
                origin, rotation = (0, y, z), (i * 360 / segments, 0, 0)
            else:
                x, y = center
                lo, hi = (x - width / 2, y + radius - thickness, start), (x + width / 2, y + radius, end)
                origin, rotation = (x, y, 0), (0, 0, i * 360 / segments)
            self.box(f'{name}-facet-{i}', lo, hi, material, parent=parent,
                     rotation_origin=origin, rotation=rotation)

def tube_saddle(c, name, x0, x1):
    for side, lo, hi in (
        ('top', (x0, 11.45, 4.15), (x1, 11.8, 11.85)),
        ('bottom', (x0, 4.2, 4.15), (x1, 4.55, 11.85)),
        ('front', (x0, 4.55, 4.15), (x1, 11.45, 4.5)),
        ('back', (x0, 4.55, 11.5), (x1, 11.45, 11.85)),
    ):
        c.box(name + '-' + side, lo, hi, 'iron')
    c.box(name + '-shoe', (x0 - .10, 11.8, 5.0), (x1 + .1, 12.08, 11.25), 'brass')
    for z in (4.25, 11.35):
        c.box(name + '-frame-contact-' + str(z), (x0, 4.45, z), (x1 + .48, 4.75, z + .4), 'brass')




def paper_uvs(x0, x1, z0, z1):
    """Align the top grain across both tangent seams at native pixel density."""
    return {face: (x0, z0, x1, z1) for face in ('up', 'down', 'north', 'south', 'east', 'west')}
