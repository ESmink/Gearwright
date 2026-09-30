"""Selected router A: one-door carriage and connection-aware air-bypass review."""
from __future__ import annotations

import argparse
from collections import OrderedDict
import math
from pathlib import Path
import shutil

import numpy as np

from gearwright_graphics.compiler import build_package, compile_shape, json_bytes
from gearwright_graphics.model import ModelPackage, Shape, animate
from graphics.review.pneumatic_receiver import TEXTURES, gear, smooth

REVIEW_ROOT = Path('generated/pneumatic-router-review/current')
SELECTED_BASIS = {
    'candidate': 'a-indexing-cassette',
    'shape': (REVIEW_ROOT / 'a-indexing-cassette/assembly.shape.json').as_posix(),
    'sha256': 'ad437e2ba2fb93954ab3efaa83f4eafd3fcf7641a8a8151aa968dc32ba213dbf',
}
APPROVED_REFERENCE = {
    'candidate': 'a-single-door',
    'shape': (REVIEW_ROOT / 'a-single-door/assembly.shape.json').as_posix(),
    'sha256': '342cfe661f3809fa430279807d21a74d933c81d73fc6d97f08c99355d9e5e4ca',
}
DESCRIPTIONS = OrderedDict((('a-single-door', 'A | Single-door carriage'),))
STATES = ('assembly', 'mechanism', 'drive', 'bypass', 'airflow', 'wall-mount',
          'two-connections', 'second-inlet', 'three-outputs')
BRIEFS = {'a-single-door': (
    'A reinforced carriage has one rack-driven lifting door, a fixed rear stop and high side retainers.',
    'Its docking shoe raises the tube shutter at the selected mouth; every other mouth stays shut.',
    'Two temporal gears total: the mounted index drive and a temporal/iron pinion driving the door rack.')}
HEADER_LOW, HEADER_HIGH, HEADER_Y = 1.35, 14.65, 2.425
DOOR_X, DOOR_Y, CARGO_OFFSET = 3.2, 6.55, 1.95
DOOR_STROKE, SHUTTER_STROKE, SHOE_PLAY = 5.22, 5.2, .02
GUIDE_ENGAGEMENT = 1 / 3
SHUTTER_GUIDE_TOP = 5.55 + SHUTTER_STROKE + (10.8 - 5.55) * GUIDE_ENGAGEMENT
DOOR_GUIDE_TOP = 6.55 + DOOR_STROKE + (9.65 - 6.55) * GUIDE_ENGAGEMENT


def port_configuration(state='assembly', *, connected_ports=None, inlet_pressures=None, output_ports=None):
    """Review inputs represent matched adjacent pneumatic pipes, never bare faces."""
    defaults = ((1, 2, 3, 4), {1: 80., 2: 35.}, (3, 4))
    if state == 'two-connections':
        defaults = ((1, 4), {1: 80.}, (4,))
    elif state == 'second-inlet':
        defaults = ((1, 2, 3, 4), {1: 35., 2: 80.}, (3, 4))
    elif state == 'three-outputs':
        defaults = ((1, 2, 3, 4), {1: 80.}, (2, 3, 4))
    connected = tuple(defaults[0] if connected_ports is None else connected_ports)
    inlets = dict(defaults[1] if inlet_pressures is None else inlet_pressures)
    outputs = tuple(defaults[2] if output_ports is None else output_ports)
    if (len(set(connected)) != len(connected) or not set(connected) <= {1, 2, 3, 4} or
            set(inlets) & set(outputs) or set(inlets) | set(outputs) != set(connected) or
            any(not math.isfinite(value) or value < 0 for value in inlets.values())):
        raise ValueError('Each connected port needs exactly one valid input or output role.')
    positive = [number for number in inlets if inlets[number] > 0]
    supply = max(positive, key=lambda number: (inlets[number], -number)) if positive else None
    return dict(connected=connected, inlets=inlets, outputs=outputs, supply=supply)


def door_lift(pose):
    return DOOR_STROKE * pose['door']


def port_shutter_lift(pose, number):
    aligned = (number == 1 and abs(pose['index']) < 1e-8) or (number == 4 and abs(pose['index'] + 90) < 1e-8)
    return max(0., door_lift(pose) - SHOE_PLAY) if aligned else 0.


def fixed_pivot(shape, name, origin, *, rotation=(0, 0, 0), parent=None):
    return shape.box(name, origin, origin, pivot=True, rotation_origin=origin, rotation=rotation, parent=parent)


def motion(candidate, frame):
    """Capture against the fixed rear stop; use the same door for entry and exit."""
    if candidate not in DESCRIPTIONS:
        raise ValueError(candidate)
    f = min(360., max(0., float(frame)))
    arrive, launch = smooth(f / 60), smooth((f - 210) / 50)
    index = -90 * smooth((f - 100) / 65) * (1 - smooth((f - 295) / 25))
    door = (smooth(f / 20) * (1 - smooth((f - 60) / 35)) +
            smooth((f - 170) / 35) * (1 - smooth((f - 260) / 30)))
    angle = math.radians(index)
    if f <= 60:
        cargo = (-1.6 + (9.6 + CARGO_OFFSET) * arrive, 8., 8.)
    elif f < 210:
        cargo = (8 + CARGO_OFFSET * math.cos(angle), 8., 8 - CARGO_OFFSET * math.sin(angle))
    else:
        cargo = (8., 8., 8 + CARGO_OFFSET - (9.6 + CARGO_OFFSET) * launch)
    return dict(theta=f, index=index, door=door, cargo=cargo, yaw=index if f < 210 else -90.)


class Rig:
    def __init__(self, candidate, state, **configuration):
        self.candidate, self.state = candidate, state
        self.ports = port_configuration(state, **configuration)
        self.shape, self.targets = Shape(candidate + '-' + state), []
        for key, (location, size) in TEXTURES.items():
            self.shape.texture(key, location, size=size)
        self.mount = None
        if state == 'wall-mount':
            self.mount = fixed_pivot(self.shape, 'wall-orientation', (8, 8, 8), rotation=(90, 0, 0))
            # All root geometry is authored in block coordinates. A translated
            # child restores that origin before the mounting rotation applies.
            self.mount = self.shape.pivot('wall-local-origin', (-8, -8, -8), parent=self.mount)

    def box(self, name, lo, hi, material='brass', group='fixed', **kwargs):
        if kwargs.get('parent') is None:
            kwargs['parent'] = self.mount
        return self.shape.box(name, lo, hi, texture='#' + material, group=group, **kwargs)

    def pane(self, name, lo, hi, face, parent=None):
        if self.state not in ('mechanism', 'drive'):
            self.box(name, lo, hi, 'glass', 'glass', parent=parent,
                     faces=(face,), uv=(0, 0, 16, 16), render_pass=1)

    def pivot(self, name, origin, fn, parent=None, rotation=(0, 0, 0)):
        ref = fixed_pivot(self.shape, name, origin, parent=parent or self.mount, rotation=rotation)
        self.targets.append((ref, fn))
        return ref

    def finish(self):
        if not self.targets:
            return self.shape
        animation = animate(self.shape, 'Receive, route and reset', 'route', 361,
                            on_animation_end='Hold', on_activity_stopped='Stop')
        for frame in range(0, 361, 2):
            pose = motion(self.candidate, frame)
            for ref, fn in self.targets:
                values = dict(offsetX=0, offsetY=0, offsetZ=0, rotationX=0, rotationY=0, rotationZ=0)
                values.update(fn(pose))
                animation.keyframe(frame, ref, **values)
        self.shape.add_animation(animation.build())
        return self.shape


class GearPlane:
    """Reuse the approved thick involute cuboids in a horizontal gear plane."""
    state = 'mechanism'

    def __init__(self, rig, parent=None):
        self.rig, self.parent = rig, parent

    def rotor(self, name, origin, fn):
        plane = fixed_pivot(self.rig.shape, name + '-plane', origin, rotation=(90, 0, 0),
                            parent=self.parent or self.rig.mount)
        return self.rig.pivot(name, (0, 0, 0), lambda p: dict(rotationZ=-fn(p)), parent=plane)

    def box(self, *args, **kwargs):
        return self.rig.box(*args, **kwargs)


def gear_pair(r, prefix, driven, source, radius, teeth, fn, parent=None):
    # Contact geometry is measured in the gear's local XY plane (world XZ).
    contact = math.degrees(math.atan2(driven[2] - source[2], driven[0] - source[0])) - 90
    phase = ((contact * 8 / 360 + (contact + 180) * teeth / 360 - .5) % 1) * 45
    plane = GearPlane(r, parent)
    gear(plane, prefix + '-iron', driven, radius, teeth, 'gear-iron', fn)
    gear(plane, prefix + '-temporal', source, 1.6, 8, 'temporal', lambda p: -fn(p) * teeth / 8, phase=phase)
    for label, center in (('iron', driven), ('temporal', source)):
        x, y, z = center
        axle_fn = fn if label == 'iron' else lambda p: -fn(p) * teeth / 8
        shaft = r.pivot(prefix + '-' + label + '-shaft', center,
                        lambda p, turn=axle_fn: dict(rotationY=turn(p)), parent=parent)
        r.box(prefix + '-' + label + '-axle', (-.2, -.7, -.2),
              (.2, .92, .2), 'iron', 'shaft', parent=shaft)
        for dx in (-.52, .3):
            r.box(prefix + '-' + label + '-bearing-' + str(dx),
                  (x + dx, y - .65, z - .52), (x + dx + .22, y - .12, z + .52), parent=parent)
        for dz in (-.52, .3):
            r.box(prefix + '-' + label + '-bearing-cap-' + str(dz),
                  (x - .3, y - .65, z + dz), (x + .3, y - .12, z + dz + .22), parent=parent)


class VerticalPlane:
    state = 'mechanism'

    def __init__(self, rig, parent):
        self.rig, self.parent = rig, parent

    def rotor(self, name, origin, fn):
        return self.rig.pivot(name, origin, lambda p: dict(rotationZ=fn(p)), parent=self.parent)

    def box(self, *args, **kwargs):
        return self.rig.box(*args, **kwargs)


def vertical_pair(r, prefix, driven, source, radius, teeth, fn, parent):
    contact = math.degrees(math.atan2(driven[1] - source[1], driven[0] - source[0])) - 90
    phase = ((contact * 8 / 360 + (contact + 180) * teeth / 360 - .5) % 1) * 45
    plane = VerticalPlane(r, parent)
    gear(plane, prefix + '-iron', driven, radius, teeth, 'gear-iron', fn)
    gear(plane, prefix + '-temporal', source, 1.6, 8, 'temporal', lambda p: -fn(p) * teeth / 8, phase=phase)
    for label, center in (('iron', driven), ('temporal', source)):
        x, y, z = center
        shaft = r.pivot(prefix + '-' + label + '-shaft', center,
                        lambda p, ratio=1 if label == 'iron' else -teeth / 8: dict(rotationZ=fn(p) * ratio),
                        parent=parent)
        r.box(prefix + '-' + label + '-axle', (-.16, -.16, -.85), (.16, .16, .8),
              'iron', 'shaft', parent=shaft)
        for dx in (-.48, .29):
            r.box(prefix + '-' + label + '-bearing-' + str(dx),
                  (x + dx, y - .48, z + .2), (x + dx + .19, y + .48, z + .7), parent=parent)
        for dy in (-.48, .29):
            r.box(prefix + '-' + label + '-bearing-cap-' + str(dy),
                  (x - .29, y + dy, z + .2), (x + .29, y + dy + .19, z + .7), parent=parent)
        # A fork carries the bearing without occupying its shaft aperture.
        for dx in (-.48, .29):
            r.box(prefix + '-' + label + '-bearing-post-' + str(dx),
                  (x + dx, 5.95, z + .42), (x + dx + .19, y - .48, z + .65), parent=parent)


def collar_y(r, name, x0, x1, y0, y1, z0, z1, parent, material='brass', thickness=.13):
    for side, lo, hi in (
        ('left', (x0, y0, z0), (x0 + thickness, y1, z1)),
        ('right', (x1 - thickness, y0, z0), (x1, y1, z1)),
        ('front', (x0 + thickness, y0, z0), (x1 - thickness, y1, z0 + thickness)),
        ('back', (x0 + thickness, y0, z1 - thickness), (x1 - thickness, y1, z1)),
    ):
        r.box(name + '-' + side, lo, hi, material, 'air', parent=parent)


def port_shutter(r, number, root):
    """The docking shoe lifts a passive, spring-return guillotine shutter."""
    for z in (-3.5, 3.15):
        r.box(f'port-{number}-shutter-guide-{z}', (-5.78, 5.2, z),
              (-5.35, SHUTTER_GUIDE_TOP, z + .35), parent=root)
    r.box(f'port-{number}-shutter-sill', (-5.7, 5.2, -2.8), (-5.4, 5.55, 2.8), parent=root)
    shutter = r.pivot(f'port-{number}-shutter', (0, 0, 0),
                      lambda p, n=number: dict(offsetY=port_shutter_lift(p, n)), parent=root)
    for z in (-2.82, 2.56):
        r.box(f'port-{number}-shutter-side-{z}', (-5.54, 5.55, z), (-5.42, 10.8, z + .26),
              'iron', parent=shutter)
    for y in (5.55, 10.55):
        r.box(f'port-{number}-shutter-crossbar-{y}', (-5.54, y, -2.56), (-5.42, y + .25, 2.56),
              parent=shutter)
    if r.state not in ('mechanism', 'drive'):
        r.box(f'port-{number}-shutter-glass', (-5.525, 5.8, -2.56), (-5.435, 10.55, 2.56),
              'glass', 'glass', parent=shutter, faces=('west', 'east'), render_pass=1, uv=(0, 0, 16, 16))
    # Seal flange stands in front of the sliding panel, outside its travel plane.
    for z in (-2.82, 2.6):
        r.box(f'port-{number}-seal-frame-side-{z}', (-5.62, 5.55, z), (-5.58, 10.8, z + .22), parent=root)
        r.box(f'port-{number}-shutter-seal-side-{z}', (-5.58, 5.55, z), (-5.54, 10.8, z + .22),
              'leather', parent=root)
    for y in (5.55, 10.65):
        r.box(f'port-{number}-seal-frame-bar-{y}', (-5.62, y, -2.6), (-5.58, y + .15, 2.6), parent=root)
        r.box(f'port-{number}-shutter-seal-bar-{y}', (-5.58, y, -2.6), (-5.54, y + .15, 2.6),
              'leather', parent=root)
    r.box(f'port-{number}-shutter-lifting-tab', (-5.42, 6.55, -.3), (-4.95, 6.78, .3),
          'iron', parent=shutter)
    # Two enclosed spring cartridges pull the shutter shut in any mounting orientation.
    for sign in (-1, 1):
        z0, z1 = (-3.99, -3.65) if sign < 0 else (3.65, 3.99)
        xc, zc = -5.275, (z0 + z1) / 2
        r.box(f'port-{number}-return-mount-{sign}', (-5.5, .6, z0), (-5.05, .85, z1), parent=root)
        for side, lo, hi in (
            ('left', (-5.45, .85, z0), (-5.4, 6.45, z1)),
            ('right', (-5.15, .85, z0), (-5.1, 6.45, z1)),
            ('front', (-5.4, .85, z0), (-5.15, 6.45, z0 + .05)),
            ('back', (-5.4, .85, z1 - .05), (-5.15, 6.45, z1)),
        ):
            r.box(f'port-{number}-return-can-{sign}-{side}', lo, hi, parent=root)
        r.box(f'port-{number}-return-plunger-{sign}', (xc - .04, .9, zc - .04),
              (xc + .04, 6.55, zc + .04), 'iron', parent=shutter)
        r.box(f'port-{number}-return-yoke-root-{sign}', (-5.52, 6.55, min(sign * 2.7, sign * 2.88)),
              (-5.23, 6.78, max(sign * 2.7, sign * 2.88)), 'iron', parent=shutter)
        r.box(f'port-{number}-return-yoke-arm-{sign}', (-5.31, 6.55, min(sign * 2.7, zc)),
              (-5.23, 6.78, max(sign * 2.7, zc)), 'iron', parent=shutter)
        r.box(f'port-{number}-shutter-guide-slider-{sign}', (-5.35, 5.55, min(sign * 3.12, sign * 3.37)),
              (-5.3, 10.8, max(sign * 3.12, sign * 3.37)), 'iron', parent=shutter)


def port(r, number, angle):
    root = fixed_pivot(r.shape, 'port-' + str(number), (8, 0, 8), rotation=(0, angle, 0), parent=r.mount)
    for end, x0, x1 in (('outer', -8, -7.2), ('inner', -6.1, -5.6)):
        for side, lo, hi in (
            ('bottom', (x0, 4.5, -3.5), (x1, 5.2, 3.5)),
            ('top', (x0, 10.8, -3.5), (x1, 11.5, 3.5)),
            ('front', (x0, 5.2, -3.5), (x1, 10.8, -2.8)),
            ('back', (x0, 5.2, 2.8), (x1, 10.8, 3.5)),
        ):
            r.box(f'port-{number}-{end}-{side}', lo, hi, parent=root, group='port')
    # Split the tube floor around an actual open takeoff, not a pipe capped by glass.
    for name, lo, hi, face in (
        ('floor-front', (-7.2, 5.2, -2.76), (-6.1, 5.35, -1.1), 'down'),
        ('floor-back', (-7.2, 5.2, 1.1), (-6.1, 5.35, 2.76), 'down'),
        ('front', (-7.2, 5.35, -2.76), (-6.1, 10.65, -2.61), 'north'),
        ('back', (-7.2, 5.35, 2.61), (-6.1, 10.65, 2.76), 'south'),
        ('roof', (-7.2, 10.65, -2.76), (-6.1, 10.8, 2.76), 'up'),
    ):
        r.pane(f'port-{number}-glass-{name}', lo, hi, face, root)
    selected = number == r.ports['supply'] or number in r.ports['outputs']
    # Full-height inspection neck connects the header interior to the tube bore.
    for label, y0, y1 in (('takeoff', 5.05, 5.55), ('union', 3.08, 3.42), ('valve', 4.0, 4.28)):
        collar_y(r, f'port-{number}-{label}', -7.2, -6.1, y0, y1, -1.1, 1.1, root)
    for z in (-1.1, .97):
        r.box(f'port-{number}-neck-side-{z}', (-7.08, 3.2, z), (-6.22, 5.05, z + .13),
              parent=root, group='air')
    for name, x0, x1, face in (('outer', -7.2, -7.08, 'west'), ('inner', -6.22, -6.1, 'east')):
        r.pane(f'port-{number}-neck-glass-{name}', (x0, 3.2, -.97), (x1, 5.05, .97), face, root)
    # A weaker input has a closed butterfly across its neck, with a leather seal.
    if selected:
        r.box(f'port-{number}-isolation-valve', (-6.71, 3.65, -.94), (-6.59, 4.65, .94),
              'iron', 'air', parent=root)
    else:
        r.box(f'port-{number}-isolation-valve', (-7.09, 4.09, -.98), (-6.21, 4.21, .98),
              'iron', 'air', parent=root)
        collar_y(r, f'port-{number}-closed-seal', -7.1, -6.2, 4.07, 4.23, -.99, .99,
                 root, 'leather', .07)
    r.box(f'port-{number}-valve-spindle', (-7.45, 4.08, -.1), (-6.65, 4.24, .1),
          'iron', 'air', parent=root)
    if selected:
        r.box(f'port-{number}-valve-handle', (-7.58, 3.85, -.13), (-7.42, 4.5, .13),
              'brass', 'air', parent=root)
    else:
        r.box(f'port-{number}-valve-handle', (-7.58, 4.08, -.45), (-7.42, 4.24, .45),
              'iron', 'air', parent=root)
    # Three scoop vanes expose the turn: incoming air bends down, outgoing air bends up/out.
    for i, z in enumerate((-.75, -.08, .59)):
        r.box(f'port-{number}-scoop-{i}', (-7.0, 5.58, z), (-6.3, 5.75, z + .16),
              'iron', 'air', parent=root, rotation_origin=(-6.65, 5.65, z + .08),
              rotation=(0, 0, -38 if number in r.ports['inlets'] else 38))
    # Small brass chevrons are fixed to the neck; they indicate the demonstrated port roles.
    direction = -1 if number in r.ports['inlets'] else 1
    for z, angle_z in ((-.28, 40 * direction), (.28, -40 * direction)):
        r.box(f'port-{number}-flow-chevron-{z}', (-7.24, 4.48, z - .3), (-7.18, 4.58, z + .3),
              'brass', 'air', parent=root, rotation_origin=(-7.21, 4.53, z),
              rotation=(angle_z, 0, 0))
    for z in (-3.5, 3.0):
        r.box(f'port-{number}-foot-{z}', (-5.7, .6, z), (-5.4, 4.2, z + .5), 'iron', parent=root)
        r.box(f'port-{number}-support-shoe-{z}', (-6.1, 4.2, z), (-5.4, 4.5, z + .5), parent=root)
        r.box(f'port-{number}-base-shoe-{z}', (-6.0, .6, z - .12), (-4.95, .85, z + .62), parent=root)
        bolt_z = -3.4 if z < 0 else 3.4
        r.box(f'port-{number}-base-bolt-{z}', (-5.6, .85, bolt_z - .08), (-5.4, .98, bolt_z + .08),
              'iron', parent=root)
    for tick in range(number):
        r.box(f'port-{number}-label-{tick}', (-7.95, 11.53, -1.1 + tick * .6),
              (-7.35, 11.72, -.85 + tick * .6), 'iron', 'port-label', parent=root)
    port_shutter(r, number, root)


def connected_pipe_context(r, number, angle):
    """Short neighbouring pipe pieces prove that connection frames attach to pipes."""
    root = fixed_pivot(r.shape, f'context-port-{number}', (8, 0, 8),
                       rotation=(0, angle, 0), parent=r.mount)
    for side, lo, hi in (
        ('floor', (-12, 4.5, -3.5), (-11.5, 5.2, 3.5)),
        ('roof', (-12, 10.8, -3.5), (-11.5, 11.5, 3.5)),
        ('front', (-12, 5.2, -3.5), (-11.5, 10.8, -2.8)),
        ('back', (-12, 5.2, 2.8), (-11.5, 10.8, 3.5)),
    ):
        r.box(f'context-port-{number}-collar-{side}', lo, hi, parent=root, group='context')
    for name, lo, hi, face in (
        ('floor', (-11.5, 5.2, -2.76), (-8, 5.35, 2.76), 'down'),
        ('front', (-11.5, 5.35, -2.76), (-8, 10.65, -2.61), 'north'),
        ('back', (-11.5, 5.35, 2.61), (-8, 10.65, 2.76), 'south'),
        ('roof', (-11.5, 10.65, -2.76), (-8, 10.8, 2.76), 'up'),
    ):
        r.pane(f'context-port-{number}-glass-{name}', lo, hi, face, root)


def foundation(r):
    for i, (lo, hi) in enumerate((
        ((1, .2, 1), (15, .6, 2.4)), ((1, .2, 13.6), (15, .6, 15)),
        ((1, .2, 2.4), (2.4, .6, 13.6)), ((13.6, .2, 2.4), (15, .6, 13.6)),
    )):
        r.box('base-oak-' + str(i), lo, hi, 'oak')
    for x in (1.0, 14.5):
        r.box('base-strap-' + str(x), (x, .61, 1), (x + .5, .85, 15))
    for z in (4.0, 11.3):
        r.box('base-crossbeam-' + str(z), (1, .2, z), (15, .6, z + .7), 'oak')
    r.box('base-index-beam', (7.3, .2, 1), (8.7, .6, 15), 'oak')
    r.box('base-table-beam', (1, .2, 7.3), (15, .6, 8.7), 'oak')


def housing(r):
    foundation(r)
    for x in (1.05, 14.2):
        for z in (1.05, 14.2):
            r.box(f'bypass-support-{x}-{z}', (x, .85, z), (x + .65, 1.65, z + .65), 'oak')
    # Hollow square-section brass plumbing, with inspection glazing on its upper face.
    # Open corner channels and a gap under every vertical takeoff connect all four branches.
    for number, angle in enumerate((0, 90, 180, 270), 1):
        root = fixed_pivot(r.shape, f'bypass-{number}', (8, 0, 8), rotation=(0, angle, 0), parent=r.mount)
        r.box(f'bypass-{number}-floor', (-7.4, 1.65, -7.4), (-5.9, 1.79, 5.9), group='air', parent=root)
        r.pane(f'bypass-{number}-glass-outer', (-7.4, 1.79, -7.4), (-7.26, 3.06, 7.26), 'west', root)
        r.box(f'bypass-{number}-outer-upper-edge', (-7.4, 3.06, -7.4), (-7.26, 3.2, 7.26), group='air', parent=root)
        r.box(f'bypass-{number}-inner-wall', (-6.04, 1.79, -5.9), (-5.9, 3.2, 5.9), group='air', parent=root)
        for label, z0, z1 in (('before', -7.26, -1.1), ('after', 1.1, 5.9)):
            r.pane(f'bypass-{number}-glass-{label}', (-7.26, 3.06, z0), (-6.04, 3.2, z1), 'up', root)
        # Narrow shoulders close the header roof around the narrower takeoff neck.
        for x0, x1 in ((-7.26, -7.2), (-6.1, -6.04)):
            r.box(f'bypass-{number}-tee-shoulder-{x0}', (x0, 3.06, -1.1), (x1, 3.2, 1.1), group='air', parent=root)
        for z in (-4.8, 4.5):
            for label, lo, hi in (
                ('outer', (-7.48, 1.59, z), (-7.4, 3.29, z + .3)),
                ('inner', (-5.9, 1.59, z), (-5.82, 3.29, z + .3)),
                ('top', (-7.4, 3.2, z), (-5.9, 3.29, z + .3)),
            ):
                r.box(f'bypass-{number}-union-{z}-{label}', lo, hi, 'iron', 'air', parent=root)
        if number in r.ports['connected']:
            port(r, number, angle)
            connected_pipe_context(r, number, angle)
        else:
            r.pane(f'bypass-{number}-glass-unused-tee', (-7.26, 3.06, -1.1),
                   (-6.04, 3.2, 1.1), 'up', root)


def rack_lift(r, rotor, gate):
    vertical_pair(r, 'door-in', (-1.6, 8., -2.1), (1.2, 8., -2.1),
                  1.2, 6, lambda p: -math.degrees(door_lift(p) / 1.2), rotor)
    pitch_x, pitch = -2.8, math.pi * .4
    # The lower teeth formerly swept through the base temporal wheel. The
    # shortened rack still covers the pinion pitch point throughout its stroke.
    r.box('door-rack-backbone', (-3.6, 2.5, -2.812), (-3.28, 9.85, -2.012),
          'gear-brass', parent=gate)
    for tooth in range(-4, 2):
        cy = 8 + tooth * pitch
        for step in range(4):
            near, far = -.30 + .195 * step, -.30 + .195 * (step + 1)
            half = .4 * math.pi / 4 - .028 + near * math.tan(math.radians(20))
            r.box(f'door-rack-tooth-{tooth}-{step}', (pitch_x - far, cy - half, -2.812),
                  (pitch_x - near, cy + half, -2.012), 'gear-brass', parent=gate)
    r.box('door-rack-key', (-3.6, 6.55, -2.1), (-3.06, 6.77, -1.28), 'iron', parent=gate)
    for z in (-1.8, 1.55):
        r.box('door-guide-' + str(z), (-3.7, 6.3, z), (-3.52, DOOR_GUIDE_TOP, z + .25), 'iron', parent=rotor)
        r.box('door-guide-seat-' + str(z), (-3.75, 6.18, z), (-2.7, 6.3, z + .25), parent=rotor)
        r.box('door-crosshead-' + str(z), (-3.5, 6.55, z), (-3.34, 9.65, z + .25), parent=gate)


def cassette(r):
    source = 8 - 4.4 / math.sqrt(2)
    gear_pair(r, 'index', (8, 1.55, 8), (source, 1.55, source), 2.8, 14, lambda p: p['index'])
    # Open beds secure the two bearing legs without obstructing their rotating shafts.
    for z in (7.25, 8.35):
        r.box('index-drive-bed-' + str(z), (7.35, .6, z), (8.65, .9, z + .4))
    r.box('index-source-mount', (source - .7, .2, source - .7),
          (source + .7, .6, source + .7), 'oak')
    for dx in (-.6, .3):
        r.box('index-source-mount-leg-' + str(dx), (source + dx, .6, source - .6),
              (source + dx + .3, .9, source + .6), 'oak')
        r.box('index-source-mount-strap-' + str(dx), (source + dx, .605, source - .68),
              (source + dx + .3, .7, source + .68))
        r.box('index-source-mount-bolt-' + str(dx), (source + dx + .06, .7, source - .56),
              (source + dx + .24, .82, source - .38), 'iron')
    rotor = r.pivot('cassette', (8, 0, 8), lambda p: dict(rotationY=p['index']))
    r.box('index-shaft', (-.24, 2.4, -.24), (.24, 6.31, .24), 'iron', parent=rotor)
    r.box('carriage-floor', (-2.9, 6.3, -1.8), (3.0, 6.55, 1.8), 'oak', parent=rotor)
    for z in (-1.8, 1.55):
        r.box('carriage-skid-' + str(z), (-2.9, 6.55, z), (3, 6.73, z + .25), 'iron', parent=rotor)
        r.box('carriage-side-plate-' + str(z), (-2.9, 6.73, z), (3, 7.2, z + .25), parent=rotor)
        r.box('carriage-upper-rail-' + str(z), (-2.65, 9.48, z), (3.0, 9.72, z + .25), parent=rotor)
        for x in (-2.65, .05, 2.72):
            r.box(f'carriage-stanchion-{x}-{z}', (x, 6.73, z), (x + .2, 9.48, z + .25),
                  'iron', parent=rotor)
    for z in (-.92, .68):
        r.box('carriage-wear-strip-' + str(z), (-2.85, 6.55, z), (2.95, 6.68, z + .24),
              'leather', parent=rotor)
    # A fixed rear stop has no hinge or animation target.
    for z in (-1.55, 1.3):
        r.box('carriage-fixed-stop-stile-' + str(z), (3.16, 6.3, z), (3.4, 9.72, z + .25),
              'iron', parent=rotor)
    for y in (7.65, 9.42):
        r.box('carriage-fixed-stop-bar-' + str(y), (3.16, y, -1.3), (3.4, y + .25, 1.3), parent=rotor)
    r.box('carriage-fixed-stop-bumper', (3.151, 7.65, -1.28), (3.16, 8.02, 1.28),
          'leather', parent=rotor)
    gate = r.pivot('gate-in', (0, 0, 0), lambda p: dict(offsetY=door_lift(p)), parent=rotor)
    for z in (-1.48, 1.2):
        r.box('gate-in-stile-' + str(z), (-3.34, 6.67, z), (-3.06, 9.65, z + .28), 'iron', parent=gate)
    for y in (6.55, 8.05, 9.45):
        r.box('gate-in-crossbar-' + str(y), (-3.34, y, -1.2), (-3.06, y + .2, 1.2), parent=gate)
    # The nose travels under the stationary lifting tab, then pushes it up.
    r.box('gate-docking-arm', (-5.25, 6.3, -.24), (-3.2, 6.53, .24), 'iron', parent=gate)
    r.box('gate-docking-shoe', (-5.25, 6.3, -.27), (-4.97, 6.53, .27), 'iron', parent=gate)
    rack_lift(r, rotor, gate)
    # Cardinal supports keep the diagonal temporal-wheel sweep clear.
    for x, z in ((3., 8.), (8., 3.), (13., 8.), (8., 13.)):
        r.box(f'cassette-support-{x}-{z}', (x - .25, .6, z - .25), (x + .25, 4.05, z + .25), 'iron')
        r.box(f'cassette-pad-{x}-{z}', (x - .35, 4.05, z - .35), (x + .35, 4.28, z + .35), 'leather')
    for i in range(32):
        r.box('cassette-running-ring-' + str(i), (-.51, 4.3, 4.85), (.51, 4.6, 5.15), parent=rotor,
              rotation_origin=(0, 0, 0), rotation=(0, i * 11.25, 0))
    for z in (-1.8, 1.5):
        r.box('cassette-ring-bridge-' + str(z), (-4.78, 4.6, z), (4.78, 4.84, z + .3), 'iron', parent=rotor)
        for x in (-2.8, 2.5):
            r.box(f'cassette-floor-post-{x}-{z}', (x, 4.84, z), (x + .3, 6.3, z + .3), parent=rotor)


def airflow_paths(configuration=None):
    configuration = configuration or port_configuration()
    source = configuration['supply']
    if source is None:
        return ()
    a, b, y = HEADER_LOW + .23, HEADER_HIGH - .23, HEADER_Y
    ring = [(a, 8.3), (a, b), (8.3, b), (b, b), (b, 7.7), (b, a), (7.7, a), (a, a)]
    mouths = {1: (0, 8.3), 2: (8.3, 16), 3: (16, 7.7), 4: (7.7, 0)}
    start_index = (source - 1) * 2
    x, z = ring[start_index]
    mx, mz = mouths[source]
    shared = [(mx, 6.05, mz), (x, 5.9, z), (x, y, z)]
    paths = []
    for number in configuration['outputs']:
        target = (number - 1) * 2
        clockwise = (target - start_index) % 8
        direction = 1 if clockwise <= 4 else -1
        count = clockwise if direction > 0 else 8 - clockwise
        route = [ring[(start_index + direction * step) % 8] for step in range(1, count + 1)]
        x, z = ring[target]
        mx, mz = mouths[number]
        paths.append(shared + [(xx, y, zz) for xx, zz in route] + [(x, 5.9, z), (mx, 6.05, mz)])
    return tuple(paths)


def path_point(points, phase):
    points = np.array(points, dtype=float)
    lengths = np.linalg.norm(np.diff(points, axis=0), axis=1)
    distance = (phase % 1) * sum(lengths)
    for start, end, length in zip(points, points[1:], lengths):
        if distance <= length:
            return start + (end - start) * distance / length
        distance -= length
    return points[-1]


def air_markers(r):
    for path_index, points in enumerate(airflow_paths(r.ports)):
        for bead in range(9):
            ref = r.pivot(f'air-marker-{path_index}-{bead}', (0, 0, 0),
                          lambda p, route=points, phase=bead / 9: dict(zip(
                              ('offsetX', 'offsetY', 'offsetZ'), path_point(route, p['theta'] / 360 + phase))))
            r.box(f'air-marker-{path_index}-{bead}-bead', (-.13, -.13, -.13), (.13, .13, .13),
                  'temporal', 'air-overlay', parent=ref, glow=100)


def candidate_shape(candidate, state='assembly', **configuration):
    if candidate not in DESCRIPTIONS or state not in STATES:
        raise ValueError((candidate, state))
    r = Rig(candidate, state, **configuration)
    if state == 'drive':
        foundation(r)
    else:
        housing(r)
    if state not in ('bypass', 'airflow', 'second-inlet', 'three-outputs'):
        cassette(r)
    if state in ('airflow', 'second-inlet', 'three-outputs'):
        air_markers(r)
    if state not in ('drive', 'bypass', 'airflow', 'second-inlet', 'three-outputs'):
        cargo = r.pivot('representative-cargo', (0, 0, 0), lambda p: dict(
            offsetX=p['cargo'][0], offsetY=p['cargo'][1], offsetZ=p['cargo'][2], rotationY=p['yaw']))
        r.box('cargo-envelope', (-1.2, -1.2, -1.2), (1.2, 1.2, 1.2), 'oak', 'cargo', parent=cargo)
        r.box('cargo-band', (-1.201, -1.201, -.18), (1.201, 1.201, .18), 'leather', 'cargo', parent=cargo)
    return r.finish()


def build_review(root):
    root = Path(root).resolve()
    managed = (root / REVIEW_ROOT).resolve()
    staging, backup = managed.with_name('.current-build'), managed.with_name('.current-old')
    for path in (managed, staging, backup):
        path.relative_to((root / 'generated').resolve())
    for path in (staging, backup):
        if path.exists():
            shutil.rmtree(path)
    package = ModelPackage('pneumatic_router_review')
    for candidate in DESCRIPTIONS:
        for state in STATES:
            shape = candidate_shape(candidate, state)
            compile_shape(shape)
            package.shape(shape, f'{staging.relative_to(root).as_posix()}/{candidate}/{state}.shape.json')
    build_package(package, root, write=True)
    (staging / '.gearwright-review.json').write_bytes(json_bytes({
        'version': 1, 'source': 'graphics/review/pneumatic_router.py', 'managedPath': REVIEW_ROOT.as_posix(),
        'candidates': list(DESCRIPTIONS), 'states': list(STATES), 'animations': ['route'],
        'decision': {'status': 'approved', 'runtimePromotion': True, 'selectedFamily': 'A',
                     'selectedCandidate': 'a-single-door', 'selectedBasis': SELECTED_BASIS,
                     'approvedReference': APPROVED_REFERENCE,
                     'retiredCandidates': ['b-cross-slide', 'c-swing-fork', 'a1-hinge-drives', 'a2-door-clock']},
        'sources': [location for location, _ in TEXTURES.values()],
        'ports': {'mountingFace': 'down', 'faces': ['west', 'south', 'east', 'north'],
                  'labels': [1, 2, 3, 4], 'boreCenter': 8, 'outerCollar': 7, 'footprint': [1, 1, 1],
                  'visibility': 'matching connected pneumatic pipes only; preview context excluded from footprint'},
        'airBypass': {'path': 'open tube-floor takeoff, glass neck, hollow inspected brass header, outlet necks',
                      'supply': 'strongest valid connected inlet only; close all weaker inlet butterflies',
                      'outputs': 'all configured connected outlets', 'cargoInsidePressureTube': False,
                      'previewCases': {state: port_configuration(state) for state in
                                       ('airflow', 'second-inlet', 'three-outputs', 'two-connections')}},
        'cycle': {'frames': 361, 'arrival': [0, 60], 'closeDoor': [60, 95], 'route': [100, 165],
                  'openDoorForExit': [170, 205], 'launch': [210, 260], 'closeDoorForReset': [260, 290],
                  'emptyReset': [295, 360], 'cargoSize': 2.4, 'example': 'west to north',
                  'doorCount': 1, 'fixedRearStop': True, 'indexDegrees': -90,
                  'doorStroke': DOOR_STROKE, 'tubeShutterStroke': SHUTTER_STROKE,
                  'shutterDrive': 'carriage lifting shoe; passive spring return; no port gears',
                  'guideEngagementAtFullLift': GUIDE_ENGAGEMENT,
                  'tubeGuideHeight': SHUTTER_GUIDE_TOP, 'doorGuideHeight': DOOR_GUIDE_TOP},
        'limits': ['Review-only A refinement; runtime routing and dynamic neighbour visibility are pending.',
                   'Preview connection data stands for matched neighbouring pneumatic pipes.',
                   'Air strength values illustrate selection; they are not pressure units or flow simulation.',
                   'Animation frames never own or commit cargo.'],
    }))
    notes = ['# Router A: one-door carriage', '',
             'A is selected. The revised carriage has one sliding door and a fixed rear stop. '
             'A temporal/iron pinion lifts its brass rack; the second door and shared-clock alternative are retired. '
             'The original selected A reference remains in the manifest.', '',
             '## Carriage and cycle', '',
             'A reinforced oak floor has iron wear rails, leather contact strips, high open side retainers and '
             'a fixed padded rear stop. The parcel enters through the one geared door and stops at the rear. '
             'That door closes before the table turns 90 degrees toward the north outlet, opens for '
             'delivery, closes for the empty reset, and stays shut while idle. There are two temporal gears total: '
             '8:14 table indexing and 8:6 door-rack drive. The door travels 5.22 units. Its nose shoe passes under the docked pipe shutter tab, takes up 0.02 units of play and lifts the shutter 5.2 units. Each tube mouth has a sealed glass guillotine shutter and two spring-return cartridges. All other mouths stay shut; no port has its own gear. The active mouth closes before indexing.', '',
             '## Connected pipes, shutters and air selection', '',
             'Connection frames and their supports exist only for the connected pneumatic pipes supplied to the '
             'review definition. Short neighbouring pipe sections make these connections explicit. Two-connections '
             'shows only ports 1 and 4; the other faces have no collar, feet or takeoff. Unused header tees are capped '
             'by inspection glass. Runtime must derive this list from matching adjacent pneumatic pipes.', '',
             'Airflow shows inlet 1 at relative strength 80, inlet 2 at 35, and outlets 3 and 4. Only inlet 1 supplies '
             'the header; inlet 2 has a closed butterfly, leather seal and horizontal shut handle. Second-inlet reverses '
             'the strengths and the selected supply. Three-outputs demonstrates all three outlet branches supplied '
             'by inlet 1. Cyan markers trace only the selected inlet and every output.', '',
             '## Mounts and materials', '',
             'Tube frames sit on iron posts and brass shoes carried by the base crossbeams. They no longer stand '
             'on the air duct. The lower temporal wheel has an oak mount tied into a crossbeam, two raised legs '
             'beneath the brass bearing, and reinforcing straps. The opening between these legs leaves its shaft free '
             'to rotate. The index bearing uses an open bed on its own base beam. Four cardinal table supports '
             'stand on crossed base beams, outside the lower gear sweep. The door rack begins at y=2.5 above '
             'that sweep and retains all teeth needed through its full stroke.', '',
             'Tube shutter guides now end at y=12.5 and carriage guides at about y=12.8, instead of y=16 and '
             'y=15.2. Full-length slider shoes retain one third of their length against the guides at maximum '
             'lift. Spring cartridges move outside the guide shoes onto dedicated base feet.', '',
             'Oak carries broad slow loads; brass reinforces joints, forms air ducts, bushings and light retainers. '
             'Iron carries compact loaded shafts, hinges and wear rails; wood or copper would wear at those sections. '
             'Leather provides replaceable contact pads and seals. Glass exposes the neck and header air passages. '
             'Temporal gears mesh with iron, preserving the accepted thick native-style tooth geometry.', '',
             '## Review states', '',
             'Assembly includes neighbouring pipes and inspection glazing; mechanism removes glazing; drive isolates '
             'the carriage and mounted gears. Bypass removes cargo handling. Airflow, second-inlet and three-outputs '
             'explain the selected supply. Two-connections demonstrates hidden unused frames. Wall-mount rotates '
             'the complete model and its pipe context. Context pipes are excluded from the one-block machine bounds.', '',
             '## Runtime work', '',
             'Docking interlocks, actual neighbour queries, configurable roles, filters, jams and save-compatible parcel '
             'reservations remain runtime work. Photoshoots and the Python reviewer are diagnostic; Vintage Story must '
             'confirm final UVs, transparency and playback.', '']
    (staging / 'README.md').write_text('\n'.join(notes), encoding='utf-8')
    if managed.exists():
        managed.replace(backup)
    staging.replace(managed)
    if backup.exists():
        shutil.rmtree(backup)
    return managed


def render_review(root, game, managed):
    from gearwright_graphics.photoshoot import build_parser, run
    from PIL import Image, ImageDraw, ImageFont
    candidate = next(iter(DESCRIPTIONS))
    for state in STATES:
        views = 'isometric,front,back,left,right,top,bottom' if state in ('assembly', 'mechanism') else 'isometric'
        shots = [(views, '140')]
        if state == 'assembly':
            shots += [('isometric', '0,60,206,235,360')]
        for views, frames in shots:
            args = [
                '--vintage-story', str(game), '--mod', str(root),
                '--model', str(managed / candidate / (state + '.shape.json')),
                '--strict-textures', '--views', views,
                '--lighting', 'flat', '--light-direction', '.6,.9,-1', '--size', '600x600',
                '--orthographic', '--orthographic-scale', '1.5' if state == 'drive' else '2.05',
                '--camera-target', '.5,.4,.5',
                '--output', str(managed / candidate / state / 'renders')]
            if state != 'bypass':
                args += ['--animation', 'route', '--frames', frames]
            run(build_parser().parse_args(args), root)
    print('Rendered ' + candidate, flush=True)
    ink, accent, bg = (235, 228, 210), (112, 210, 190), (20, 23, 28)
    font, small = ImageFont.load_default(size=24), ImageFont.load_default(size=17)

    def photo(state, frame=140):
        filename = 'isometric.png' if state == 'bypass' else f'isometric-route-{frame}.png'
        return Image.open(managed / candidate / state / 'renders' / filename).convert('RGB')

    board = Image.new('RGB', (1200, 1390), bg)
    draw = ImageDraw.Draw(board)
    draw.text((24, 18), 'ROUTER A / SINGLE-DOOR CARRIAGE', fill=accent, font=font)
    draw.text((24, 55), 'One powered door opens the docked shutter. Short guides keep 1/3 overlap. Rack and supports clear the lower wheel.', fill=ink, font=small)
    panels = [('assembly', 'SHORT GUIDES / REINFORCED CARRIAGE'), ('drive', 'CLEAR RACK / REMOUNTED TABLE SUPPORTS'),
              ('two-connections', 'ONLY PORTS 1 AND 4 HAVE CONNECTED PIPES'), ('airflow', 'STRONG INLET 1 / WEAK INLET 2 ISOLATED')]
    for i, (state, label) in enumerate(panels):
        x, y = i % 2 * 600, 96 + i // 2 * 630
        draw.text((x + 18, y), label, fill=ink, font=small)
        board.paste(photo(state), (x, y + 26))
    board.save(managed / 'comparison.png')

    air_board = Image.new('RGB', (1800, 1170), bg)
    ad = ImageDraw.Draw(air_board)
    ad.text((24, 18), 'AIR BYPASS / STRONGEST CONNECTED INPUT AND EVERY OUTPUT', fill=accent, font=font)
    mouths = {1: (0, 8.3), 2: (8.3, 16), 3: (16, 7.7), 4: (7.7, 0)}

    def flow_diagram(x, configuration):
        def point(px, pz):
            return x + 180 + px * 15, 820 + pz * 15

        def arrow(start, end):
            dx, dy = end[0] - start[0], end[1] - start[1]
            length = math.hypot(dx, dy)
            if length < 1:
                return
            dx, dy = dx / length, dy / length
            tip = ((start[0] + end[0]) / 2, (start[1] + end[1]) / 2)
            ad.polygon((tip, (tip[0] - 10 * dx + 5 * dy, tip[1] - 10 * dy - 5 * dx),
                        (tip[0] - 10 * dx - 5 * dy, tip[1] - 10 * dy + 5 * dx)), fill=accent)

        ad.text((x + 300, 742), 'TOP-DOWN AIR PATH', fill=ink, font=small, anchor='mm')
        ad.rectangle((*point(HEADER_LOW, HEADER_LOW), *point(HEADER_HIGH, HEADER_HIGH)),
                     outline=(64, 76, 82), width=9)
        for number in configuration['connected']:
            px, pz = mouths[number]
            inner = (HEADER_LOW, pz) if number == 1 else (HEADER_HIGH, pz) if number == 3 else (
                px, HEADER_HIGH if number == 2 else HEADER_LOW)
            ad.line((point(px, pz), point(*inner)), fill=(64, 76, 82), width=9)
        for path in airflow_paths(configuration):
            points = [point(px, pz) for px, _, pz in path]
            ad.line(points, fill=accent, width=4)
            for start, end in zip(points, points[1:]):
                arrow(start, end)
        positions = {1: (x + 18, 938, 'lm'), 2: (x + 300, 1100, 'mm'),
                     3: (x + 444, 938, 'lm'), 4: (x + 300, 780, 'mm')}
        for number in configuration['connected']:
            lx, ly, anchor = positions[number]
            if number in configuration['outputs']:
                label, color = f'OUT {number}', accent
            else:
                strength = configuration['inlets'][number]
                selected = number == configuration['supply']
                label = f'IN {number}: {strength:g}' + ('' if selected else ' / SHUT')
                color = accent if selected else (218, 161, 101)
                if not selected:
                    px, pz = mouths[number]
                    cx, cy = point(px, pz)
                    ad.line((cx - 7, cy - 7, cx + 7, cy + 7), fill=color, width=4)
                    ad.line((cx - 7, cy + 7, cx + 7, cy - 7), fill=color, width=4)
            ad.text((lx, ly), label, fill=color, font=small, anchor=anchor)

    for i, (state, label) in enumerate((
        ('airflow', 'INPUT 1: 80 > INPUT 2: 35 / OUTPUTS 3 + 4'),
        ('second-inlet', 'INPUT 2: 80 > INPUT 1: 35 / OUTPUTS 3 + 4'),
        ('three-outputs', 'INPUT 1: 80 / OUTPUTS 2 + 3 + 4'))):
        x = i * 600
        ad.text((x + 18, 80), label, fill=ink, font=small)
        air_board.paste(photo(state), (x, 110))
        flow_diagram(x, port_configuration(state))
    ad.text((24, 1140), 'Cyan arrows show the bypass selection. Amber crosses show shut weaker inputs. Strengths are relative review values.', fill=ink, font=small)
    air_board.save(managed / 'airflow.png')
    detail = Image.new('RGB', (1800, 1320), bg)
    dd = ImageDraw.Draw(detail)
    dd.text((20, 15), 'A / ONE DOOR: RECEIVE, TURN, RELEASE, RESET', fill=accent, font=font)
    for n, (frame, label) in enumerate(((0, 'ARRIVAL'), (60, 'AT FIXED REAR STOP'), (140, 'TURNING'),
                                      (206, 'SAME DOOR OPEN FOR EXIT'), (235, 'AIR LAUNCH'), (360, 'EMPTY RESET'))):
        x, y = n % 3 * 600, 58 + n // 3 * 630
        dd.text((x + 18, y), label, fill=ink, font=small)
        detail.paste(photo('assembly', frame), (x, y + 26))
    detail.save(managed / (candidate + '.png'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--vintage-story', type=Path, required=True)
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args()
    managed = build_review(args.root)
    if args.render:
        render_review(args.root, args.vintage_story, managed)
    print('Router review ready: ' + REVIEW_ROOT.as_posix())


if __name__ == '__main__':
    main()
