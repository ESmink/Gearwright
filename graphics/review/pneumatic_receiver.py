"""Selected receiver A1 with compact gearwork; one managed review package."""
from __future__ import annotations

import argparse
from collections import OrderedDict
from dataclasses import dataclass
import math
from pathlib import Path
import shutil

import numpy as np
from gearwright_graphics.compiler import build_package, compile_shape, json_bytes
from gearwright_graphics.model import ModelPackage, Shape, animate
from graphics.review.pneumatic_direct_line import Canvas, ring_x, ring_y

REVIEW_ROOT = Path('generated/pneumatic-receiver-review/current')
STATES = ('assembly', 'mechanism', 'cam-detail', 'installed', 'pass-through')
DESCRIPTIONS = OrderedDict((
    ('a1-return-cam', 'A1 | Compact weighted return cam'),
))
BRIEFS = {
    'a1-return-cam': ('Twin powered rollers; gravity closes the gate.',
        'Thick faceted gears, broad cross spokes and recessed bearings keep the drive close to the housing.',
        'The temporal gear sits diagonally above the iron gear. Both tooth meshes remain functional.'),
}
SELECTED_REFERENCE = {
    'candidate': 'a1-return-cam',
    'shape': (REVIEW_ROOT / 'a1-return-cam/assembly.shape.json').as_posix(),
    'sha256': 'a186b2cb28c6885fdfca72bcdd913335d9b02a19baa4bec1895376bdcb6a0dd0',
}
APPROVED_REFERENCE = {
    'candidate': 'a1-return-cam',
    'shape': (REVIEW_ROOT / 'a1-return-cam/assembly.shape.json').as_posix(),
    'sha256': 'be424a07b652259f211efecbb0ea8d122def1b7c05d561e16a84abb345d90bb0',
}
TEXTURES = {
    'brass': ('game:block/metal/sheet/brass1', (32, 32)),
    'iron': ('game:block/metal/sheet-plain/iron2', (32, 32)),
    'gear-iron': ('game:block/metal/plate/iron', (16, 16)),
    'gear-brass': ('game:block/metal/plate/brass', (16, 16)),
    'glass': ('gearwright:block/inspection-glass', (16, 16)),
    'temporal': ('game:item/resource/temporalgear', (16, 16)),
    'oak': ('game:block/wood/planks/oak1', (32, 32)),
    'leather': ('game:block/leather/plain', (32, 32)),
}
GATE = np.array((11., 5.2))
FOLLOWER_LINK = np.array((-.4, -.65))
ROLLER_R, FOLLOWER_R, CAM_CLEARANCE, RAIL_WIDTH = 1.6, .20, .045, .14
CAM_STEP = 3
CLOCKWORK_DEPTH = .4
# Thicken toward the outside; the rear faces stay fitted to the cam and housing.
GEAR_DEPTH = .8
GEAR_BACK = .088
GEAR_FRONT = GEAR_BACK - GEAR_DEPTH
GEAR_HUB_FRONT, GEAR_HUB_BACK = -.88, .12
VANILLA_REFERENCES = (
    ('metal-gear24', 'game:block/metal/mechanics/gear24', 'metal=game:block/metal/plate/iron'),
    ('temporal', 'game:item/gear-temporal', 'temporal=game:item/resource/temporalgear'),
    ('wood-spurgear16', 'game:block/wood/mechanics/spurgear16', 'wood=game:block/wood/planks/generic'),
)
FRONT_ROLLER_BEARING_Z = 4.9
FRONT_GATE_BEARING_Z = 4.86


@dataclass(frozen=True)
class Layout:
    y: float
    input_teeth: int = 8
    input_radius: float = 1.6
    output_teeth: int = 14
    output_radius: float = 2.8
    gear_z: float = 3.8
    cam_z: float = 4.15
    gate_z: float = 4.38

    @property
    def source(self):
        distance = self.input_radius + self.output_radius
        return (5.2 + distance / 2, self.y + distance * math.sqrt(3) / 2)


LAYOUTS = {
    'a1-return-cam': Layout(3.2),
}


def smooth(value):
    v = min(1., max(0., value))
    return v * v * (3 - 2 * v)


def rotation(angle):
    c, s = math.cos(math.radians(angle)), math.sin(math.radians(angle))
    return np.array(((c, -s), (s, c)))


def gate_angle(theta):
    if theta < 110:
        return -90 * smooth(theta / 110)
    if theta <= 235:
        return -90.
    return -90 * (1 - smooth((theta - 235) / 120))


def follower_position(candidate, theta):
    angle = gate_angle(theta)
    return GATE + rotation(angle) @ FOLLOWER_LINK


def cam_path(candidate):
    """Cam-space locus derived from the actual weighted follower motion."""
    center = np.array((10.8, LAYOUTS[candidate].y))
    points = np.array([rotation(-theta) @ (follower_position(candidate, theta) - center)
                       for theta in range(0, 360, CAM_STEP)])
    normals = []
    for i, point in enumerate(points):
        tangent = points[(i + 1) % len(points)] - points[i - 1]
        normal = np.array((-tangent[1], tangent[0]))
        normal /= np.linalg.norm(normal)
        if np.dot(normal, point) < 0:
            normal *= -1
        normals.append(normal)
    return points, np.array(normals)


def motion(candidate, frame):
    theta = min(360., max(0., frame))
    layout = LAYOUTS[candidate]
    arrival = min(1., max(0., (theta - 115) / 30))
    fall = min(1., max(0., (theta - 145) / 45))
    feed = max(0., math.radians(theta - 190) * ROLLER_R)
    return dict(theta=theta, gate=gate_angle(theta),
                motor=theta * layout.output_teeth / layout.input_teeth,
                cargo=(2 + 6 * arrival, 8 - (8 - layout.y) * fall - feed, 8))


class Rig:
    def __init__(self, candidate, state):
        self.candidate, self.state = candidate, state
        self.shape, self.targets = Shape(candidate + '-' + state), []
        for key, (location, size) in TEXTURES.items():
            self.shape.texture(key, location, size=size)

    def box(self, name, lo, hi, material='brass', group='fixed', **kw):
        if group in ('cam', 'cam-web', 'follower', 'return'):
            # The cam and follower remain thin plates behind the thick gears.
            lo, hi = (*lo[:2], lo[2] * CLOCKWORK_DEPTH), (*hi[:2], hi[2] * CLOCKWORK_DEPTH)
            if 'rotation_origin' in kw:
                origin = kw['rotation_origin']
                kw['rotation_origin'] = (*origin[:2], origin[2] * CLOCKWORK_DEPTH)
        return self.shape.box(name, lo, hi, texture='#' + material, group=group, **kw)

    def pane(self, name, lo, hi, face):
        if self.state not in ('mechanism', 'cam-detail'):
            self.box(name, lo, hi, 'glass', 'glass', faces=(face,), uv=(0, 0, 16, 16), render_pass=1)

    def pivot(self, name, origin, fn):
        ref = self.shape.pivot(name, origin, group='drive')
        self.targets.append((ref, fn))
        return ref

    def rotor(self, name, origin, fn):
        return self.pivot(name, origin, lambda p: dict(rotationZ=fn(p)))

    def finish(self):
        animation = animate(self.shape, 'Receive and reset gate', 'receive', 361,
                            on_animation_end='Hold', on_activity_stopped='Stop')
        for frame in range(0, 361, 3):
            pose = motion(self.candidate, frame if self.state != 'pass-through' else 0)
            for target, fn in self.targets:
                values = dict(offsetX=0, offsetY=0, offsetZ=0, rotationX=0, rotationY=0, rotationZ=0)
                values.update(fn(pose))
                animation.keyframe(frame, target, **values)
        self.shape.add_animation(animation.build())
        return self.shape


def bar(r, name, first, second, z, width, depth, material='brass', parent=None, group='fixed'):
    first, second = np.array(first), np.array(second)
    delta = second - first
    length = np.linalg.norm(delta)
    r.box(name, (first[0], first[1] - width / 2, z - depth / 2),
          (first[0] + length, first[1] + width / 2, z + depth / 2), material, group,
          parent=parent, rotation_origin=(*first, z),
          rotation=(0, 0, math.degrees(math.atan2(delta[1], delta[0]))))


def gear(r, name, center, radius, teeth, material, fn, phase=0):
    if r.state == 'cam-detail':
        return  # Explicit cutaway: keep the shaft, cam and working follower exposed.
    pivot = r.rotor(name, center, fn)
    module = 2 * radius / teeth
    rim_radius = radius - module
    temporal = material == 'temporal'
    face_options = {'glow': 35} if temporal else {}
    # Vanilla gear24 supplies the broad rim/cross proportions. Four cuboid
    # steps per tooth retain a working shortened involute while reading as
    # the native model's block teeth, rather than many fine radial strips.
    pressure = math.radians(20)
    base = radius * math.cos(pressure)
    pitch_involute = math.tan(pressure) - pressure
    profile = []
    for radial in np.linspace(radius - 1.2 * module, radius + .75 * module, 5):
        alpha = math.acos(base / radial) if radial > base else 0
        half_angle = math.pi / (2 * teeth) - .018 / radius + pitch_involute - (math.tan(alpha) - alpha)
        profile.append((radial * math.cos(half_angle), radial * math.sin(half_angle)))
    for index in range(teeth):
        angle = phase + index * 360 / teeth
        width = 2 * rim_radius * math.sin(math.pi / teeth)
        rim_outer = rim_radius * math.cos(math.pi / teeth)
        r.box(name + '-rim-' + str(index), (-width / 2, rim_outer - (.35 if temporal else .55), GEAR_FRONT),
              (width / 2, rim_outer, GEAR_BACK), material, 'gear', parent=pivot,
              rotation_origin=(0, 0, 0), rotation=(0, 0, angle), **face_options)
        sections = [('tip' if i == len(profile) - 2 else f'layer-{i}', min(a[1], b[1]) * 1.94, a[0], b[0])
                    for i, (a, b) in enumerate(zip(profile, profile[1:]))]
        for part, w, bottom, top in sections:
            r.box(f'{name}-tooth-{index}-{part}', (-w / 2, bottom, GEAR_FRONT), (w / 2, top, GEAR_BACK),
                  material, 'gear', parent=pivot, rotation_origin=(0, 0, 0), rotation=(0, 0, angle), **face_options)
    if temporal:
        # The installed temporal item is a crossed-bar lattice with small face
        # pins. Rebuild that motif inside the eight working teeth, rather than
        # placing a decorative temporal item on a separate hidden input wheel.
        for axis in range(2):
            for offset in (-.44, .44):
                r.box(f'{name}-lattice-{axis}-{offset}', (offset - .16, -1.1, GEAR_FRONT),
                      (offset + .16, 1.1, GEAR_BACK), material, 'gear', parent=pivot,
                      rotation_origin=(0, 0, 0), rotation=(0, 0, axis * 90), **face_options)
        for x in (-.44, .44):
            for y in (-.44, .44):
                r.box(f'{name}-face-pin-{x}-{y}', (x - .07, y - .07, GEAR_FRONT - .10),
                      (x + .07, y + .07, GEAR_FRONT), material, 'gear', parent=pivot, **face_options)
    else:
        for index in range(4):
            r.box(name + '-spoke-' + str(index), (-.35, 0, GEAR_FRONT + .08),
                  (.35, rim_radius + .02, GEAR_BACK - .08), material, 'gear', parent=pivot,
                  rotation_origin=(0, 0, 0), rotation=(0, 0, 90 * index))
    r.box(name + '-hub', (-.43, -.43, GEAR_HUB_FRONT), (.43, .43, GEAR_HUB_BACK),
          material, 'gear', parent=pivot, **face_options)


def axle(r, name, x, y, lo, hi, fn):
    pivot = r.rotor(name, (x, y, 0), fn)
    r.box(name + '-iron-shaft', (-.21, -.21, lo), (.21, .21, hi), 'iron', 'shaft', parent=pivot)
    r.box(name + '-key', (.15, -.08, lo), (.28, .08, hi), 'iron', 'shaft', parent=pivot)


def bearing(r, name, x, y, z):
    for i, (lo, hi) in enumerate((((-.6, -.6), (-.33, .6)), ((.33, -.6), (.6, .6)),
                                  ((-.33, -.6), (.33, -.33)), ((-.33, .33), (.33, .6)))):
        r.box(name + '-' + str(i), (x + lo[0], y + lo[1], z - .28),
              (x + hi[0], y + hi[1], z + .28), group='bearing')
    for dx in (-.42, .42):
        r.box(name + '-bolt-' + str(dx), (x + dx - .075, y - .075, z - .35),
              (x + dx + .075, y + .075, z + .35), 'iron', 'bolt')


def roller(r, name, x, y, fn):
    rotor = r.rotor(name, (x, y, 8), fn)
    r.box(name + '-keyed-core', (-.38, -.38, -1.9), (.38, .38, 1.9), 'iron', 'roller', parent=rotor)
    for z in (-1.9, 1.72):
        for spoke in range(4):
            r.box(name + '-end-spoke-' + str(z) + '-' + str(spoke), (-.15, 0, z), (.15, 1.45, z + .18),
                  'iron', 'roller', parent=rotor, rotation_origin=(0, 0, 0), rotation=(0, 0, spoke * 90))
    for i in range(24):
        width, outer = 2 * ROLLER_R * math.sin(math.pi / 24), ROLLER_R * math.cos(math.pi / 24)
        r.box(name + '-leather-' + str(i), (-width / 2, 1.28, -1.7), (width / 2, outer, 1.7),
              'leather', 'roller', parent=rotor, rotation_origin=(0, 0, 0), rotation=(0, 0, i * 15))
        for z in (-1.9, 1.72):
            r.box(name + '-end-' + str(i) + '-' + str(z), (-width / 2, 1.25, z),
                  (width / 2, outer, z + .18), 'iron', 'roller', parent=rotor,
                  rotation_origin=(0, 0, 0), rotation=(0, 0, i * 15))
    axle(r, name + '-shaft', x, y, LAYOUTS[r.candidate].gear_z + GEAR_HUB_FRONT - .06, 11.65, fn)
    for z in (FRONT_ROLLER_BEARING_Z, 11.4):
        bearing(r, name + '-bearing-' + str(z), x, y, z)


def housing(r):
    c, y = Canvas(r.shape), LAYOUTS[r.candidate].y
    ring_x(c, 'host-input', 0, .8, 'brass')
    ring_x(c, 'host-output', 15.2, 16, 'brass')
    for name, lo, hi, face in (
        ('top', (.8, 10.9, 5.1), (15.2, 11.1, 10.9), 'up'),
        # Recessed gate bearing occupies a deliberate port in the front pane.
        ('front-left', (.8, 4.9, 4.9), (10.35, 10.9, 5.1), 'north'),
        ('front-right', (11.65, 4.9, 4.9), (15.2, 10.9, 5.1), 'north'),
        ('front-above-gate', (10.35, 5.85, 4.9), (11.65, 10.9, 5.1), 'north'),
        ('back', (.8, 4.9, 10.9), (15.2, 10.9, 11.1), 'south'),
        ('floor-in', (.8, 4.9, 5.1), (5.98, 5.1, 10.9), 'down'),
        ('floor-out', (11.32, 4.9, 5.1), (15.2, 5.1, 10.9), 'down'),
    ):
        r.pane('host-glass-' + name, lo, hi, face)
    for z in (4.7, 11.1):
        r.box('host-band-side-' + str(z), (7.7, 5.1, z), (8.3, 10.9, z + .2))
    r.box('host-band-top', (7.7, 11.1, 4.7), (8.3, 11.3, 11.3))
    for x in (1.2, 14, 14.6):
        r.box('direction-mark-' + str(x), (x, 7.4, 4.68), (x + .25, 8.6, 4.84))
    ring_y(c, 'inventory-outlet', 0, .8, 'brass', low=5.4, high=10.6, width=.55)
    # Panes have apertures for the two roller shafts.
    for z, face in ((5.7, 'north'), (10.15, 'south')):
        for name, x0, x1, y0, y1 in (
            ('bottom', 3.3, 12.7, 1.35, y - .36), ('top', 3.3, 12.7, y + .36, 4.9),
            ('left', 3.3, 4.84, y - .36, y + .36), ('middle', 5.56, 10.44, y - .36, y + .36),
            ('right', 11.16, 12.7, y - .36, y + .36),
        ):
            r.pane('roller-glass-' + str(z) + '-' + name, (x0, y0, z), (x1, y1, z + .15), face)
    for x, face in ((3.15, 'west'), (12.7, 'east')):
        r.pane('roller-side-' + str(x), (x, 1.35, 5.85), (x + .15, 4.9, 10.15), face)
    for x0, x1 in ((3.3, 5.8), (10.2, 12.7)):
        r.box('roller-floor-' + str(x0), (x0, 1.1, 5.7), (x1, 1.35, 10.3))
    for z in (5.7, 10.15):
        r.pane('outlet-pane-' + str(z), (5.8, .8, z), (10.2, 1.35, z + .15), 'north' if z < 8 else 'south')
    for z in (4.1, 11.15):
        for x in (3.05, 13.35):
            r.box('frame-post-' + str(x) + '-' + str(z), (x, .35, z), (x + .35, 5.85, z + .5))
        support_z = z + .35 if z < 8 else z
        r.box('bearing-sill-' + str(z), (3.05, y - .80, support_z), (13.7, y - .55, support_z + .5))
        r.box('frame-foot-' + str(z), (3.05, .35, z), (13.7, .65, z + .5))
        r.box('gate-support-' + str(z), (10.45, 5.73, support_z), (13.7, 5.98, support_z + .5))
    for x in (3.05, 13.35):
        r.box('frame-depth-tie-' + str(x), (x, .35, 4.1), (x + .35, .65, 11.65))
        r.box('host-saddle-' + str(x), (x, 4.55, 4.1), (x + .35, 4.9, 11.65))


def flipper(r):
    layout = LAYOUTS[r.candidate]
    pivot = r.rotor('flipper', (*GATE, 8), lambda p: p['gate'])
    r.box('flipper-floor', (-5, -.10, -2.75), (.2, .10, 2.75), group='gate', parent=pivot)
    r.box('flipper-wear-edge', (-5, -.14, -2.75), (-4.75, .14, 2.75), 'iron', 'gate', parent=pivot)
    axle(r, 'gate', *GATE, layout.gate_z - .30 * CLOCKWORK_DEPTH, 11.65, lambda p: p['gate'])
    for z in (FRONT_GATE_BEARING_Z, 11.4):
        bearing(r, 'gate-bearing-' + str(z), *GATE, z)
    for z in (5.5, 10.1):
        r.box('gate-closed-stop-' + str(z), (6, 4.87, z), (6.35, 5.1, z + .4), 'iron', 'stop')


def cam(r):
    layout = LAYOUTS[r.candidate]
    pivot = r.rotor('cam', (10.8, layout.y, layout.cam_z), lambda p: p['theta'])
    points, normals = cam_path(r.candidate)
    distance = FOLLOWER_R + CAM_CLEARANCE + RAIL_WIDTH / 2
    rail = points - distance * normals
    for i in range(len(rail)):
        bar(r, f'cam-rail--1-{i}', rail[i], rail[(i + 1) % len(rail)], -.12,
            RAIL_WIDTH, .54, parent=pivot, group='cam')
    for i in range(0, len(points), 8):
        end = points[i] - distance * normals[i]
        bar(r, 'cam-back-web-' + str(i), (0, 0), end, -.34, .16, .20, parent=pivot, group='cam-web')
    r.box('cam-keyed-hub', (-.4, -.4, -.55), (.4, .4, .22), 'iron', 'cam-web', parent=pivot)
    arm = r.rotor('follower-arm', (*GATE, layout.gate_z), lambda p: p['gate'])
    bar(r, 'follower-lever', (0, 0), FOLLOWER_LINK, 0, .25, .25, 'iron', arm, 'follower')
    center = r.pivot('follower-center', (0, 0, layout.cam_z),
                     lambda p: dict(offsetX=float(follower_position(r.candidate, p['theta'])[0]),
                                    offsetY=float(follower_position(r.candidate, p['theta'])[1])))
    for i in range(12):
        outer, width = FOLLOWER_R * math.cos(math.pi / 12), 2 * FOLLOWER_R * math.sin(math.pi / 12)
        r.box('follower-roller-' + str(i), (-width / 2, .12, -.15), (width / 2, outer, .15), 'iron', 'follower',
              parent=center, rotation_origin=(0, 0, 0), rotation=(0, 0, i * 30))
    pin_end = layout.gate_z - layout.cam_z + .15 * CLOCKWORK_DEPTH
    r.box('follower-pin', (-.08, -.08, -.1 * CLOCKWORK_DEPTH), (.08, .08, pin_end),
          'iron', 'follower-pin', parent=center)
    weight = r.rotor('return-weight', (*GATE, layout.gate_z), lambda p: p['gate'])
    bar(r, 'weighted-arm', (0, 0), (-1.1, -.35), -.15, .22, .25, 'iron', weight, 'return')
    r.box('brass-return-weight', (-1.4, -.6, -.3), (-.8, -.1, 0), 'brass', 'return', parent=weight)


def drivetrain(r):
    layout = LAYOUTS[r.candidate]
    sx, sy = layout.source
    # The temporal teeth themselves mesh with the iron left-roller wheel.
    # That wheel drives the brass right-roller wheel in the same plane.
    left_phase = 180 / layout.output_teeth
    contact = math.degrees(math.atan2(layout.y - sy, 5.2 - sx)) - 90
    phase = ((contact * layout.input_teeth / 360
              + (contact + 180 - left_phase) * layout.output_teeth / 360 - .5) % 1) * 360 / layout.input_teeth
    gear(r, 'temporal-drive', (sx, sy, layout.gear_z), layout.input_radius, layout.input_teeth,
         'temporal', lambda p: p['motor'], phase=phase)
    axle(r, 'input', sx, sy, layout.gear_z + GEAR_HUB_FRONT - .06, 4.75, lambda p: p['motor'])
    bearing(r, 'input-bearing', sx, sy, 4.4)
    bar(r, 'upper-input-bracket', (3.25, 5.6), (sx - .4, sy + .4), 4.4, .38, .5)
    # Keep the output pair in phase, then index the angled temporal gear to it.
    gear(r, 'iron-left-feed', (5.2, layout.y, layout.gear_z), layout.output_radius,
         layout.output_teeth, 'gear-iron', lambda p: -p['theta'], phase=left_phase)
    gear(r, 'brass-feed', (10.8, layout.y, layout.gear_z), layout.output_radius, layout.output_teeth,
         'gear-brass', lambda p: p['theta'])
    roller(r, 'right-feed', 10.8, layout.y, lambda p: p['theta'])
    roller(r, 'left-feed', 5.2, layout.y, lambda p: -p['theta'])
    flipper(r)
    cam(r)


def inventory_context(r):
    def box(name, lo, hi):
        r.box('context-' + name, (lo[0], lo[1] - 16, lo[2]), (hi[0], hi[1] - 16, hi[2]), 'oak', 'context')
    box('floor', (.4, .3, .4), (15.6, 1, 15.6))
    box('back', (.4, 1, 14.8), (15.6, 12.5, 15.6))
    for x in (.4, 14.8):
        box('side-' + str(x), (x, 1, .4), (x + .8, 12.5, 14.8))
    box('front-cutaway', (1.2, 1, .4), (14.8, 3, 1.2))


def candidate_shape(candidate, state='assembly'):
    if candidate not in DESCRIPTIONS or state not in STATES:
        raise ValueError('Unknown flipper candidate or state')
    r = Rig(candidate, state)
    housing(r)
    drivetrain(r)
    if state == 'installed':
        inventory_context(r)
    if state != 'pass-through':
        parcel = r.pivot('representative-cargo', (0, 0, 0), lambda p: dict(zip(('offsetX', 'offsetY', 'offsetZ'), p['cargo'])))
        r.box('cargo-envelope', (-1.2,) * 3, (1.2,) * 3, 'oak', 'cargo', parent=parcel)
        r.box('cargo-band', (-1.201, -1.201, -.14), (1.201, 1.201, .14), 'brass', 'cargo', parent=parcel)
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
    package = ModelPackage('pneumatic_receiver_review')
    for candidate in DESCRIPTIONS:
        for state in STATES:
            shape = candidate_shape(candidate, state)
            compile_shape(shape)
            package.shape(shape, f'{staging.relative_to(root).as_posix()}/{candidate}/{state}.shape.json')
    build_package(package, root, write=True)
    (staging / '.gearwright-review.json').write_bytes(json_bytes({
        'version': 1, 'source': 'graphics/review/pneumatic_receiver.py', 'managedPath': REVIEW_ROOT.as_posix(),
        'candidates': list(DESCRIPTIONS), 'states': list(STATES), 'animations': ['receive'],
        'decision': {'status': 'approved', 'runtimePromotion': True, 'selectedCandidate': 'a1-return-cam',
                     'approvedReference': APPROVED_REFERENCE,
                     'basisReference': SELECTED_REFERENCE,
                     'requestedRevision': 'Thicker vanilla-style gears with the close housing fit retained',
                     'retiredDirections': ['b-drop-ram', 'c-swing-fork', 'a2-captive-twin', 'a3-rack-sector']},
        'sources': [location for location, _ in TEXTURES.values()],
        'styleReferences': [{'shape': model, 'textureOverride': texture} for _, model, texture in VANILLA_REFERENCES],
        'gearStyle': {'rimAndToothDepth': GEAR_DEPTH, 'hubDepth': GEAR_HUB_BACK - GEAR_HUB_FRONT,
                      'metalRimWidth': .55, 'metalSpokeWidth': .7, 'toothSteps': 4,
                      'temporalBody': 'crossed-bar lattice with face pins and native glow'},
        'ports': {'input': 'west', 'output': 'east', 'inventory': 'down', 'footprint': [1, 1, 1]},
        'cycle': {'frames': 361, 'gateRise': [0, 110], 'openDwell': [110, 235], 'gateReturn': [235, 355],
                  'camSampleDegrees': CAM_STEP, 'rollerRadius': ROLLER_R},
        'drive': {'path': ['temporal-drive', 'iron-left-feed', 'brass-feed'],
                  'teeth': [8, 14, 14], 'poweredRollers': ['left-feed', 'right-feed'],
                  'meshesInSamePlane': True},
        'spacing': {'previousPlanes': [2.54, 3.28, 3.84], 'currentPlanes': [3.8, 4.15, 4.38],
                    'clockworkDepthScale': CLOCKWORK_DEPTH,
                    'gearToCamFaceClearance': .086, 'camToWeightFaceClearance': .05,
                    'followerToBearingFaceClearance': .08, 'temporalBearingGap': .13,
                    'previousTemporalBearingGap': 1.21, 'camRecessBeyondFrameFace': .138,
                    'frontRollerBearingZ': FRONT_ROLLER_BEARING_Z,
                    'frontGateBearingZ': FRONT_GATE_BEARING_Z, 'unitsPerBlock': 16},
    }))
    notes = ['# Receiver A1: compact weighted return cam', '',
        'The maintainer approved this A1 with thick vanilla-style gears and minimum space against the housing. '
        'Only the revised A1 remains here. B tube proportions remain the piping reference.', '']
    for candidate, title in DESCRIPTIONS.items():
        notes.extend([f'- `{candidate}` - {title}. ' + ' '.join(BRIEFS[candidate]), ''])
    notes.extend([
        '## Drive and cycle', '',
        'A1 fits one block and discharges down. The eight teeth of the temporal gear mesh '
        'directly with a 14-tooth iron gear keyed to the left roller. That iron gear meshes with a '
        '14-tooth brass gear keyed to the right roller and cam. All three gears share one working '
        'plane. Both rollers turn inward at equal speed throughout the cycle. The temporal gear '
        'turns 630 degrees for one 360-degree cam revolution. Bearings are supported by the frame; '
        'the cam sits behind the tooth meshes.', '',
        'The gear, cam and follower pivots sit at 3.80, 4.15 and 4.38 model units. Thickening the '
        'gears toward the outside leaves their rear mounting faces and housing clearances in place. '
        'The temporal hub-to-bearing gap is 0.13 units, reduced from 1.21. The lower cam extends '
        '0.138 units inside the frame face. Front roller bearings are recessed to 4.90 and the gate '
        'bearing to 4.86, with supported sills and a fitted gate-bearing port in the glass.', '',
        'Gear rims and teeth are 0.80 units thick, up from 0.176; their hubs are 1.00 unit deep. '
        'Cam rails remain 0.216 and the follower lever 0.10. Independent '
        'faces retain 0.086 units between gear rims and cam webs, 0.05 between the cam and return '
        'weight, and 0.08 between the follower and gate-bearing bolts. Shaft lengths follow the '
        'actual wheel depths; no sleeve fills the removed gaps. Tube floor edges also clear the '
        'flipper nose and keyed gate shaft throughout motion.', '',
        'The temporal axle remains 30 degrees '
        'toward the centre around the iron gear; its teeth are re-indexed for that mesh. It leaves '
        'more than 0.10 units between its tip envelope and the brass gear, which it must not touch. '
        'Meshing centre distances, roller spacing, the cam profile and weighted return remain intact.', '',
        'The cam profile is generated from the physical follower locus in cam coordinates, at three-degree '
        'intervals. A 0.2-unit follower and explicit running clearance determine the rail offsets. This '
        'replaces the old unexplained clutch. A full revolution opens, holds and returns the gate.', '',
        'Use assembly for glass, mechanism for an exposed view, cam-detail to hide the drive wheels '
        'and expose the cam/follower, installed for an existing-inventory '
        'cutaway, and pass-through for an empty parked line. Scrub receive: 0 parked, 120 open, 150 divert, '
        '210 feed, 285 return, 360 gate reset. The animation holds after delivery; it never teleports '
        'cargo back to the inlet.', '',
        '## Materials and limits', '',
        'The installed game models game:block/metal/mechanics/gear24, game:item/gear-temporal and '
        'game:block/wood/mechanics/spurgear16 were inspected and rendered as references. Metal gear24 '
        'supplies the thick faceted rim, broad cross spokes, square projecting hub and block tooth cues. '
        'The temporal item supplies the crossed-bar lattice, small face pins and glow of 35. '
        'The wooden spur gear confirms the broad cuboid construction; this receiver retains metal gears. '
        'Native iron and brass plate textures replace the sheet textures on the wheels. These are new '
        'cuboid definitions fitted to the existing 8:14:14 train, not copied game shapes. Four stepped '
        'cuboids per tooth retain the working profile and clearance. Native reference renders are '
        'under vanilla-reference when this package is rendered.', '',
        'Glass and brass dominate. Iron carries compact shafts, tooth mates, roller cores and follower '
        'contact. Inspected leather sleeves provide a softer traction surface. Each brass toothed member '
        'has an iron mate. Copper is too soft for these wear contacts; bulk timber would hide this mechanism.', '',
        'The 2.4-unit representative cargo fits the nominal nip. Leather facets are inscribed inside '
        'the contact envelope. The fixed nip is a geometry study: irregular real-item silhouettes, '
        'adjustable preload, traction/pressure balance, sealing, jams and native glass rendering still '
        'need runtime engineering. Cargo ownership remains a server/persistence concern, never an '
        'animation event. See PNEUMATIC-TUBES-DESIGN.md for the current design and acceptance gate.', '',
        'The selected basis and exact approved shape hash are recorded in the manifest. The approved '
        'visual reference is retained while the sender is designed. Runtime integration remains a separate '
        'step; this review generator never emits a block, recipe, inventory or schema.', '',
    ])
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
    for slug, model, texture in VANILLA_REFERENCES:
        args = ['--vintage-story', str(game), '--mod', str(root), '--model', model,
                '--texture', texture, '--strict-textures', '--views', 'isometric,front,top',
                '--size', '640x640', '--output', str(managed / 'vanilla-reference' / slug)]
        run(build_parser().parse_args(args), root)
    for candidate in DESCRIPTIONS:
        for state in STATES:
            installed = state == 'installed'
            cam_detail = state == 'cam-detail'
            if installed:
                shots = [('isometric,front', '210,360')]
            elif state == 'pass-through':
                shots = [('front,top', '0')]
            elif cam_detail:
                shots = [('front,right,isometric', '210'), ('front', '0,120,285,360')]
            else:
                shots = [('isometric,front,back,left,right,top,bottom', '210')]
                if state == 'assembly':
                    shots.append(('isometric', '0,120,150,285,360'))
            for views, frames in shots:
                args = ['--vintage-story', str(game), '--mod', str(root),
                        '--model', str(managed / candidate / (state + '.shape.json')),
                        '--strict-textures', '--views', views, '--animation', 'receive', '--frames', frames,
                        '--lighting', 'flat', '--light-direction', '.6,.7,-1', '--size', '640x640',
                        '--orthographic', '--orthographic-scale', '2.4' if installed else '1.0' if cam_detail else '1.6',
                        '--camera-target', '.5,0,.5' if installed else '.66,.24,.18' if cam_detail else '.5,.35,.5',
                        '--output', str(managed / candidate / state / 'renders')]
                run(build_parser().parse_args(args), root)
        closeup = ['--vintage-story', str(game), '--mod', str(root),
                   '--model', str(managed / candidate / 'assembly.shape.json'),
                   '--strict-textures', '--views', 'right', '--animation', 'receive', '--frames', '210',
                   '--lighting', 'flat', '--light-direction', '.6,.7,-1', '--size', '640x640',
                   '--orthographic', '--orthographic-scale', '.68', '--camera-target', '.5,.325,.245',
                   '--output', str(managed / candidate / 'mount-detail/renders')]
        run(build_parser().parse_args(closeup), root)
        print('Rendered ' + candidate, flush=True)
    bg, ink, accent = (20, 23, 28), (236, 227, 210), (114, 204, 185)
    font, small = ImageFont.load_default(size=24), ImageFont.load_default(size=18)
    board = Image.new('RGB', (1920, 940), bg)
    draw = ImageDraw.Draw(board)
    draw.text((24, 18), 'RECEIVER A1 / COMPACT WEIGHTED RETURN CAM', fill=ink, font=font)
    draw.text((24, 55), 'Temporal teeth > iron left roller > brass right roller + cam. Both rollers powered.', fill=ink, font=small)
    for candidate, title in DESCRIPTIONS.items():
        panels = [('ASSEMBLY', 'assembly', 'isometric'), ('SIDE / HOUSING FIT', 'mount-detail', 'right'),
                  ('CAM / DRIVE WHEELS HIDDEN', 'cam-detail', 'front')]
        for i, (label, state, view) in enumerate(panels):
            x = i * 640
            draw.text((x + 20, 110), label, fill=accent, font=font)
            with Image.open(managed / candidate / state / 'renders' / f'{view}-receive-210.png') as photo:
                board.paste(photo.convert('RGB'), (x, 150))
        for y, text in enumerate((
            'Upper shaft gap: 1.21 > 0.13 units. Lower cam recessed 0.138 units inside the frame face.',
            'Gear rims and teeth: 0.80 units thick (was 0.176). Native plate textures and broad cross spokes.',
            'Small running gaps retained between moving layers. Both powered rollers and weighted return retained.',
        )):
            draw.text((24, 805 + 33 * y), text, fill=ink, font=small)
        detail = Image.new('RGB', (1920, 1480), bg)
        dd = ImageDraw.Draw(detail)
        dd.text((24, 18), title + ' / COMPLETE GATE CYCLE', fill=accent, font=font)
        for n, (frame, label) in enumerate(((0, 'PARKED'), (120, 'GATE OPEN'), (150, 'DIVERT'),
                                          (210, 'FEED'), (285, 'RETURN'), (360, 'GATE RESET / DELIVERED'))):
            xx, yy = n % 3 * 640, 70 + n // 3 * 700
            dd.text((xx + 20, yy), label, fill=ink, font=small)
            with Image.open(managed / candidate / 'assembly/renders' / f'isometric-receive-{frame}.png') as photo:
                detail.paste(photo.convert('RGB'), (xx, yy + 32))
        detail.save(managed / (candidate + '.png'))
    board.save(managed / 'comparison.png')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--vintage-story', type=Path, required=True)
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args()
    managed = build_review(args.root)
    if args.render:
        render_review(args.root, args.vintage_story, managed)
    print('Receiver A review ready: ' + REVIEW_ROOT.as_posix())


if __name__ == '__main__':
    main()
