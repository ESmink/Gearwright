"""Selected leather accumulator: full-height travel and fitted bellows sockets."""
from __future__ import annotations

import argparse
import json
from collections import OrderedDict
from dataclasses import replace
import math
from pathlib import Path
import shutil

import numpy as np

from gearwright_graphics.compiler import build_package, compile_shape, json_bytes
from gearwright_graphics.model import ElementRef, ModelPackage, Shape, Vec3, animate
from gearwright_graphics.geometry import _matrix
from graphics.review.pneumatic_receiver import TEXTURES as MACHINE_TEXTURES, smooth
from graphics.review.pneumatic_sender import SENDER_APPROVED_REFERENCE

REVIEW_ROOT = Path('generated/pneumatic-accumulator-review/current')
DESCRIPTIONS = OrderedDict((
    ('a-leather-bellows', 'A | Rising leather bag'),
))
SELECTED_REFERENCE = {
    'candidate': 'a-leather-bellows',
    'shape': (REVIEW_ROOT / 'a-leather-bellows/assembly.shape.json').as_posix(),
    'sha256': '10b716626d7c266689f4a6d5ff8dca84beac17befb37a2a18920dd25a03b01a4',
}
APPROVED_REFERENCE = {
    'candidate': 'a-leather-bellows',
    'shape': (REVIEW_ROOT / 'a-leather-bellows/assembly.shape.json').as_posix(),
    'sha256': '811b4e5a252d2d5194eb1270bdfb4786403f0fc5496b2e5785ffbf64f72f801b',
}
BRIEFS = {
    'a-leather-bellows': (
        'A larger leather reservoir lifts its metal top to the full block height.',
        'Fitted bellows sockets, compact collectors and a broad sliding reserve scale.',
        'A replaceable leather body carries the low-pressure air; brass clamps seal its ends. '
        'Iron guide rods resist binding under the compact weighted lid. Wood cannot replace the flexible wall.'),
}
STATES = ('assembly', 'mechanism', 'ports', 'connected-bellow')
TEXTURES = {key: MACHINE_TEXTURES[key] for key in ('brass', 'iron', 'glass', 'leather')}
TEXTURES['stone'] = ('game:block/stone/polishedrock/basalt', (32, 32))
TEXTURES['scale'] = MACHINE_TEXTURES['gear-brass']
PORTS = {
    'inlets': {'west': [0, 10.4, 8], 'north': [8, 10.4, 0], 'south': [8, 10.4, 16]},
    'outlet': {'east': [16, 8, 8]},
    'closed': ['up', 'down'], 'footprint': [1, 1, 1], 'unitsPerBlock': 16,
}
BAG_BOTTOM, BAG_LOW, BAG_HIGH = 3.4, 3., 11.75
BAG_PANELS, BAG_PANEL_LENGTH, BAG_RADIUS = 10, 1.30, 3.07
BAG_PANEL_WIDTH, LID_THICKNESS = 3.72, .85
NOZZLE_SIZE, NOZZLE_REACH, NOZZLE_HEIGHT = 1.5, 3., 10.4
SOCKET_BORE, SOCKET_DEPTH, COLLECTOR_BACK = 1.6, 3.15, 3.5


def charge(frame):
    """Fill, hold, drain, hold. A review parameter, not a calibrated pressure."""
    if frame <= 100:
        return smooth(frame / 100)
    if frame <= 140:
        return 1.
    return 1 - smooth((frame - 140) / 100)


def bag_node(index, fill):
    height = BAG_LOW + (BAG_HIGH - BAG_LOW) * fill
    rise = height / BAG_PANELS
    bulge = math.sqrt(BAG_PANEL_LENGTH ** 2 - rise ** 2)
    return BAG_RADIUS + (bulge if index % 2 else 0), BAG_BOTTOM + index * rise


class Rig:
    def __init__(self, candidate, state):
        self.candidate, self.state = candidate, state
        self.shape, self.targets = Shape(candidate + '-' + state), []
        for key, (location, size) in TEXTURES.items():
            self.shape.texture(key, location, size=size)

    def box(self, name, lo, hi, material='brass', group='fixed', **kw):
        return self.shape.box(name, lo, hi, texture='#' + material, group=group, **kw)

    def pane(self, name, lo, hi, faces, **kw):
        if self.state in ('assembly', 'connected-bellow'):
            uv = kw.pop('uv', (0, 0, 16, 16))
            self.box(name, lo, hi, 'glass', 'glass', faces=faces, render_pass=1, uv=uv, **kw)

    def pivot(self, name, fn, origin=(0, 0, 0)):
        # Keep an assembled static pose as well as the animated poses. The
        # reviewer initially selects None; animation-only placement would leave
        # folds and coil segments at the authoring origin until playback.
        initial = fn(0)
        offsets = ('offsetX', 'offsetY', 'offsetZ')
        rotations = ('rotationX', 'rotationY', 'rotationZ')
        first_offset = np.array([initial.get(key, 0) for key in offsets])
        first_rotation = _matrix(initial)[:3, :3]
        position = tuple(np.array(origin) + first_offset)
        rest = self.shape.box(name + '-rest', position, position, faces=(), pivot=True,
                              rotation_origin=position, rotation=tuple(initial.get(key, 0) for key in rotations),
                              group='moving')
        pivot = self.shape.pivot(name, (0, 0, 0), parent=rest, group='moving')
        def relative_pose(fill):
            pose = fn(fill)
            displacement = first_rotation.T @ (np.array([pose.get(key, 0) for key in offsets]) - first_offset)
            rotation = first_rotation.T @ _matrix(pose)[:3, :3]
            # Relative rotations stay away from the Euler singularity for all
            # leather folds. Shape transforms use Rz * Ry * Rx.
            angles = (math.atan2(rotation[2, 1], rotation[2, 2]),
                      math.asin(max(-1., min(1., -rotation[2, 0]))),
                      math.atan2(rotation[1, 0], rotation[0, 0]))
            return dict(zip(offsets + rotations, (*displacement, *(math.degrees(a) for a in angles))))
        self.targets.append((pivot, relative_pose))
        return pivot

    def finish(self):
        animation = animate(self.shape, 'Fill, hold and discharge', 'charge', 261,
                            on_animation_end='Repeat', on_activity_stopped='Stop')
        for frame in range(0, 261, 2):
            for target, fn in self.targets:
                values = dict(offsetX=0, offsetY=0, offsetZ=0, rotationX=0, rotationY=0, rotationZ=0)
                values.update(fn(charge(frame)))
                animation.keyframe(frame, target, **values)
        if self.targets:
            self.shape.add_animation(animation.build())
        return self.shape


def rectangular_tube(r, name, lo, hi, axis, thickness=.25, material='brass', group='port', **kw):
    transverse = [i for i in range(3) if i != axis]
    a, b = transverse
    for n, side in enumerate((a, a, b, b)):
        start, end = list(lo), list(hi)
        if n % 2:
            start[side] = end[side] - thickness
        else:
            end[side] = start[side] + thickness
        if side == b:
            start[a] += thickness
            end[a] -= thickness
        r.box(f'{name}-{n}', start, end, material, group, **kw)


def port_body(r, name, angle, outlet=False):
    """Author each connector facing west, then rotate about the block centre."""
    parent = r.shape.box(name + '-mount', (8, 0, 8), (8, 0, 8), faces=(), pivot=True,
                         rotation_origin=(8, 0, 8), rotation=(0, angle, 0), group='port')
    class Local:
        def box(self, label, lo, hi, material='brass', group='port', **kw):
            return r.box(name + '-' + label, (lo[0] - 8, lo[1], lo[2] - 8),
                         (hi[0] - 8, hi[1], hi[2] - 8), material, group, parent=parent, **kw)
    local = Local()
    if outlet:
        rectangular_tube(local, 'collar', (0, 4.5, 4.5), (.65, 11.5, 11.5), 0, .55)
        rectangular_tube(local, 'collar-band', (.65, 4.75, 4.75), (.85, 11.25, 11.25), 0, .45, 'iron')
        rectangular_tube(local, 'shoulder', (.85, 5., 5.), (1.1, 11., 11.), 0, 1.15)
        # Glass and short corner rails repeat the approved pneumatic tube finish.
        for y in (6.15, 9.6):
            for z in (6.15, 9.6):
                local.box(f'neck-rail-{y}-{z}', (1.1, y, z), (3.35, y + .25, z + .25))
        for label, lo, hi, face in (
            ('top', (1.1, 9.7, 6.4), (3.35, 9.85, 9.6), 'up'),
            ('front', (1.1, 6.4, 6.15), (3.35, 9.6, 6.3), 'north'),
            ('back', (1.1, 6.4, 9.7), (3.35, 9.6, 9.85), 'south'),
            ('floor', (1.1, 6.15, 6.4), (2.95, 6.3, 9.6), 'down'),
        ):
            if r.state != 'mechanism':
                local.box('neck-glass-' + label, lo, hi, 'glass', 'glass',
                          faces=(face,), uv=(0, 0, 16, 16), render_pass=1)
        local.box('collector-back', (3.35, 2.8, 6.15), (3.5, 9.85, 9.85))
        local.box('collector-front', (2.95, 2.8, 6.15), (3.1, 6.15, 9.85))
        for z in (6.15, 9.7):
            local.box('collector-side-' + str(z), (2.95, 2.8, z), (3.35, 9.85, z + .15))
        local.box('collector-roof', (3.1, 9.7, 6.3), (3.5, 9.85, 9.7))
    else:
        # The Automatic Bellow's native 1.5-square spout projects three units
        # into this block at y=10.4. The 1.6-square leather-lined bore receives
        # the whole spout, with 0.05 clearance per side and 0.15 beyond its tip.
        rectangular_tube(local, 'collar', (0, 9.05, 6.65), (.32, 11.75, 9.35), 0, .5)
        rectangular_tube(local, 'barrel', (.32, 9.275, 6.875), (SOCKET_DEPTH, 11.525, 9.125), 0, .275)
        rectangular_tube(local, 'lining', (0, 9.55, 7.15), (SOCKET_DEPTH, 11.25, 8.85), 0, .05, 'leather')
        for x in (.4, 2.75):
            rectangular_tube(local, 'band-' + str(x), (x, 9.2, 6.8), (x + .18, 11.6, 9.2), 0, .075, 'iron')
        for y in (9.18, 11.32):
            for z in (6.78, 8.92):
                local.box(f'socket-fastener-{y}-{z}', (.01, y, z), (.15, y + .3, z + .3), 'iron')
        local.box('collector-back', (3.38, 2.8, 6.875), (COLLECTOR_BACK, 11.525, 9.125))
        local.box('collector-front', (3.03, 2.8, 6.875), (3.15, 9.55, 9.125))
        for z in (6.875, 8.975):
            local.box('collector-side-' + str(z), (3.03, 2.8, z), (3.38, 11.525, z + .15))
        local.box('collector-roof', (3.15, 11.25, 7.025), (3.5, 11.525, 8.975))
        rectangular_tube(local, 'collector-foot', (2.88, 2.65, 6.7), (3.5, 3.1, 9.3), 1, .12)


def foundation(r):
    r.box('base-solid-stone', (.6, 0, .6), (15.4, 2.2, 15.4), 'stone', 'base')
    r.box('base-manifold-cover', (1., 2.2, 1.), (15., 2.8, 15.), 'brass', 'base')
    r.box('base-reservoir-seat', (4.0, 2.8, 4.0), (12., BAG_BOTTOM, 12.), 'brass', 'base')
    for x in (1.1, 14.1):
        for z in (1.1, 14.1):
            r.box(f'base-clamp-{x}-{z}', (x, .25, z), (x + .8, 2.8, z + .8), 'iron', 'base')
            r.box(f'base-bolt-{x}-{z}', (x + .15, 2.8, z + .15), (x + .65, 3., z + .65), 'brass', 'base')
    for name, angle, outlet in (('in-west', 0, False), ('in-north', -90, False),
                                ('in-south', 90, False), ('out-east', 180, True)):
        port_body(r, name, angle, outlet)


def height_scale(r, lid, lid_y):
    # This broad fourth guide also carries the scale. The reader is a hollow
    # sliding cuff attached to the lid, not a pointer floating beside a wire.
    r.box('reserve-scale-foot', (12.35, 2.8, 2.6), (13.5, 3.35, 3.55))
    r.box('reserve-scale', (12.6, 3.0, 2.85), (13.3, 15.8, 3.25), 'iron')
    for x in (12.6, 13.22):
        r.box('reserve-scale-edge-' + str(x), (x, 3.35, 2.81), (x + .08, 15.8, 2.85), 'scale')
    for tick in range(21):
        y = lid_y + .20 + (BAG_HIGH - BAG_LOW) * tick / 20
        major = tick % 5 == 0
        r.box('reserve-tick-' + str(tick), (12.73 if major else 12.96, y - .04, 2.78),
              (13.2, y + .04, 2.84), 'scale')
    r.box('reserve-full-mark', (12.75, 15.48, 2.78), (13.15, 15.68, 2.84), 'scale')
    rectangular_tube(r, 'reserve-empty-mark', (12.75, 6.03, 2.78), (13.15, 6.23, 2.84),
                     2, .05, 'scale', group='fixed')
    rectangular_tube(r, 'reserve-reader', (12.46, lid_y - .13, 2.64), (13.44, lid_y + .54, 3.46),
                     1, .12, 'scale', group='moving', parent=lid)
    r.box('reserve-reader-line', (12.47, lid_y + .16, 2.60), (13.43, lid_y + .24, 2.64),
          'iron', 'moving', parent=lid)
    r.box('reserve-reader-lid-ear', (12.15, lid_y + .1, 3.3), (12.58, lid_y + .36, 3.9),
          parent=lid, group='moving')
    r.box('reserve-head-stop', (12.42, 15.8, 2.68), (13.46, 16., 3.48))


def leather_bag(r):
    for side in range(8):
        angle = side * math.pi / 4
        for panel in range(BAG_PANELS):
            def pose(fill, index=panel, theta=angle):
                radius, y = bag_node(index, fill)
                next_radius, next_y = bag_node(index + 1, fill)
                return dict(offsetX=8 + radius * math.cos(theta), offsetY=y,
                            offsetZ=8 + radius * math.sin(theta), rotationY=90 - math.degrees(theta),
                            rotationX=math.degrees(math.atan2(next_radius - radius, next_y - y)))
            pivot = r.pivot(f'bag-fold-{side}-{panel}', pose)
            if r.state == 'mechanism':
                continue
            half = BAG_PANEL_WIDTH / 2
            r.box(f'bag-leather-{side}-{panel}', (-half, -.04, -.06), (half, BAG_PANEL_LENGTH + .04, .06),
                  'leather', 'moving', parent=pivot)
            # Short stitch runs follow each rigid fold without stretching its UVs.
            if side in (0, 2, 4, 6):
                for y in (.5, .82, 1.1):
                    r.box(f'bag-stitch-{side}-{panel}-{y}', (half - .2, y, .061), (half - .12, y + .10, .085),
                          'brass', 'moving', parent=pivot)
    lid = r.pivot('bag-lid', lambda q: dict(offsetY=(BAG_HIGH - BAG_LOW) * q))
    lid_y = BAG_BOTTOM + BAG_LOW
    r.box('bag-lid-plate', (3.65, lid_y, 3.65), (12.35, lid_y + .45, 12.35), parent=lid, group='moving')
    r.box('bag-lid-weight', (4.7, lid_y + .45, 4.7), (11.3, lid_y + LID_THICKNESS, 11.3),
          'iron', 'moving', parent=lid)
    for x in (3.05, 12.95):
        for z in (3.05, 12.95):
            if x > 8 and z < 8:
                continue  # The broad measuring guide occupies this corner.
            r.box(f'bag-guide-{x}-{z}', (x - .12, 2.8, z - .12), (x + .12, 15.8, z + .12), 'iron')
            r.box(f'bag-guide-foot-{x}-{z}', (x - .35, 2.8, z - .35), (x + .35, 3.25, z + .35))
            r.box(f'bag-guide-stop-{x}-{z}', (x - .3, 15.8, z - .3), (x + .3, 16., z + .3))
            # A hollow sleeve surrounds each post; a diagonal tab ties it to the lid.
            rectangular_tube(r, f'bag-sleeve-{x}-{z}', (x - .28, lid_y, z - .28),
                             (x + .28, lid_y + .5, z + .28), 1, .12, group='moving', parent=lid)
            inner_x, inner_z = (3.9 if x < 8 else 12.1), (3.9 if z < 8 else 12.1)
            outer_x, outer_z = x + (.22 if x < 8 else -.22), z + (.22 if z < 8 else -.22)
            r.box(f'bag-lid-ear-{x}-{z}', (min(outer_x, inner_x), lid_y + .12, min(outer_z, inner_z)),
                  (max(outer_x, inner_x), lid_y + .38, max(outer_z, inner_z)), parent=lid, group='moving')
    height_scale(r, lid, lid_y)


def bellow_reference(r, document=None):
    if document is None:
        # Portable interface fixture of the four inspected native nozzle walls.
        # Human renders use the actual installed body and approved attachment.
        rectangular_tube(r, 'context-nozzle', (0, 9.65, 7.25), (3, 11.15, 8.75),
                         0, .5, 'iron', group='context')
        return
    from graphics.review.large_bellows import candidate_shape as automatic_bellow
    reference = automatic_bellow(document, 'a-reinforced-wood', 'one-sided')
    for texture in reference.textures.values():
        r.shape.texture('context-' + texture.key, texture.location, size=texture.size)
    location = (-8, 0, 8)
    mount = r.shape.box('context-bellow-mount', location, location, faces=(), pivot=True,
                        rotation_origin=location, rotation=(0, 180, 0), group='context')
    for element in reference.elements:
        delta = Vec3(0, 0, 0) if element.parent else Vec3(8, 0, 8)
        copied = replace(element, name='context-' + element.name,
                         parent='context-' + element.parent if element.parent else mount.name,
                         from_=element.from_ - delta, to=element.to - delta,
                         rotation_origin=element.rotation_origin - delta if element.rotation_origin else None,
                         faces=OrderedDict((face, replace(value, texture='#context-' + value.texture.lstrip('#')))
                                           for face, value in element.faces.items()), group='context')
        r.shape.elements.append(copied)
        r.shape._refs[copied.name] = ElementRef(r.shape.id, copied.name)


def candidate_shape(candidate, state='assembly', *, bellow_document=None):
    if candidate not in DESCRIPTIONS or state not in STATES:
        raise ValueError('Unknown accumulator candidate or state')
    r = Rig(candidate, state)
    foundation(r)
    if state != 'ports':
        leather_bag(r)
    if state == 'connected-bellow':
        bellow_reference(r, bellow_document)
    return r.finish()


def build_review(root, game=None):
    root = Path(root).resolve()
    managed = (root / REVIEW_ROOT).resolve()
    staging, backup = managed.with_name('.current-build'), managed.with_name('.current-old')
    for path in (managed, staging, backup):
        path.relative_to((root / 'generated').resolve())
    for path in (staging, backup):
        if path.exists():
            shutil.rmtree(path)
    package = ModelPackage('pneumatic_accumulator_review')
    document = None
    if game is not None:
        from graphics.review.large_bellows import read_source
        document = read_source(game)
    for candidate in DESCRIPTIONS:
        for state in STATES:
            shape = candidate_shape(candidate, state, bellow_document=document)
            compile_shape(shape)
            package.shape(shape, f'{staging.relative_to(root).as_posix()}/{candidate}/{state}.shape.json')
    build_package(package, root, write=True)
    (staging / '.gearwright-review.json').write_bytes(json_bytes({
        'version': 1, 'source': 'graphics/review/pneumatic_accumulator.py', 'managedPath': REVIEW_ROOT.as_posix(),
        'candidates': list(DESCRIPTIONS), 'states': list(STATES), 'animations': ['charge'],
        'decision': {'status': 'approved', 'runtimePromotion': True,
                     'approvedReference': APPROVED_REFERENCE,
                     'basisReference': SELECTED_REFERENCE,
                     'requestedRevision': 'Full-block lid rise, wider bag, better scale and fitted Automatic Bellow sockets',
                     'retiredDirections': ['b-spring-piston', 'c-weighted-vane']},
        'displayName': 'Pneumatic Accumulator', 'ports': PORTS,
        'senderReference': SENDER_APPROVED_REFERENCE,
        'sources': sorted(set(location for location, _ in TEXTURES.values())),
        'motion': {'frames': 261, 'fill': [0, 100], 'fullHold': [100, 140], 'drain': [140, 240],
                   'emptyHold': [240, 260], 'bagLidTravel': BAG_HIGH - BAG_LOW,
                   'maximumHeight': BAG_BOTTOM + BAG_HIGH + LID_THICKNESS},
        'automaticBellowFit': {'source': 'game:block/wood/mechanics/bellowslarge',
                               'outletParts': ['nozzle2', 'nozzle3', 'nozzle4', 'nozzle5'],
                               'outerSize': NOZZLE_SIZE, 'height': NOZZLE_HEIGHT, 'projection': NOZZLE_REACH,
                               'socketBore': SOCKET_BORE, 'socketDepth': SOCKET_DEPTH,
                               'sideClearance': .05, 'tipClearance': .15},
        'limits': ['Review-only geometry and qualitative charge animation; no runtime registration or save changes.',
                   'Three horizontal inlets supersede the old five-face visual concept. Runtime adapter still has legacy face semantics.',
                   'Air reserve, pressure and usable output require a runtime relationship; charge is not kPa or litres.',
                   'Working seals, check valves, relief, spring tuning and engine lighting remain integration work.'],
    }))
    notes = ['# Pneumatic Accumulator A: fitted leather reservoir', '',
             'The maintainer selected A and requested a taller, larger reservoir, a clearer scale and '
             'connections fitted to the Automatic Bellow. Only revised A remains. Its selected basis '
             'hash is recorded. The maintainer approved this full-height revision for runtime implementation.', '',
             '## What changed', '',
             'Five pairs of leather folds lift the brass lid and iron load through 8.75 model units. '
             'The top reaches exactly y=16 when full. The solid plinth is lower and the bag is wider; '
             'the closest folded leather passes beside compact fixed collectors instead of broad grilles.', '',
             'Three recessed brass sockets receive the actual Automatic Bellow spout. Its exterior is '
             '1.5 by 1.5 units, centred at y=10.4, and projects 3 units into this block. Each leather-lined '
             'socket has a 1.6-square clear bore, 0.05 clearance per side and 0.15 clearance past the tip. '
             'Iron retaining bands and small corner fasteners frame the fitted connection. The larger '
             'pneumatic outlet remains centred at y=8 and uses the approved 7-unit collar with a glass neck.', '',
             'The broad fourth guide carries 21 contrasting scale marks, longer quarter marks, an empty '
             'outline and a filled top mark. A hollow cuff tied to the lid slides around it; its dark '
             'index line crosses the scale without intersecting the engraved marks.', '',
             '## Review', '',
             'Play charge: 0 empty, 50 half, 100 full; hold to 140, drain to 240, hold empty to 260. '
             'The opening pose is fully assembled even with animation set to None. Mechanism removes '
             'the leather and outlet glass, ports isolates the manifold, and connected-bellow shows '
             'the installed native body with the approved automatic drive attachment. The bellow '
             'reference stays stationary so the connection can be inspected.', '',
             'The human package reads the game body from the selected installation. Portable tests use '
             'a four-wall nozzle fixture of the measured interface. No native geometry or atlas is copied '
             'into tracked runtime assets. Retained sources are the inspected game leather, metal plates, '
             'basalt, and project inspection glass.', '',
             '## Remaining integration', '',
             'This is passive reserve storage with three horizontal air sockets and one tube outlet. '
             'Charge remains a qualitative reserve/available-power cue, not calibrated pressure. The '
             'existing intake adapter still uses its five-face legacy behavior and capped delivery; '
             'the selected design needs compatible face validation, live pressure/power feedback, '
             'working seals, check valves and relief. Public identifiers and saved state are unchanged.', '']
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
    for candidate in DESCRIPTIONS:
        for state in STATES:
            connected = state == 'connected-bellow'
            args = ['--vintage-story', str(game), '--mod', str(root),
                    '--model', str(managed / candidate / (state + '.shape.json')),
                    '--strict-textures', '--lighting', 'flat', '--light-direction', '.6,.7,-1',
                    '--size', '640x640', '--orthographic', '--orthographic-scale', '4.0' if connected else '1.65',
                    '--camera-target=' + ('-.5,.2,.5' if connected else '.5,.49,.5'),
                    '--views', 'isometric,front,back,left,right,top,bottom',
                    '--output', str(managed / candidate / state / 'renders')]
            if state != 'ports':
                args += ['--animation', 'charge', '--frames', '50']
            run(build_parser().parse_args(args), root)
            if state == 'assembly':
                args[args.index('--views') + 1] = 'isometric,front'
                args[args.index('--frames') + 1] = '0,100,190,260'
                run(build_parser().parse_args(args), root)
            elif connected:
                args[args.index('--views') + 1] = 'isometric,front,top'
                args[args.index('--orthographic-scale') + 1] = '1.0'
                args[next(i for i, value in enumerate(args) if value.startswith('--camera-target='))] = '--camera-target=.05,.65,.5'
                args[args.index('--output') + 1] = str(managed / candidate / state / 'connection-renders')
                run(build_parser().parse_args(args), root)
        print('Rendered ' + candidate, flush=True)
    bg, ink, accent = (20, 23, 28), (236, 227, 210), (114, 204, 185)
    font, small = ImageFont.load_default(size=26), ImageFont.load_default(size=19)
    board = Image.new('RGB', (1920, 980), bg)
    draw = ImageDraw.Draw(board)
    draw.text((24, 18), 'ACCUMULATOR A / FULL-HEIGHT LEATHER RESERVOIR', fill=ink, font=font)
    draw.text((24, 58), 'Larger bag. Supported scale. Three fitted Automatic Bellow sockets. One glass tube outlet.', fill=ink, font=small)
    for candidate, title in DESCRIPTIONS.items():
        shots = (('EMPTY', 'assembly/renders/isometric-charge-0.png'),
                 ('FULL / TOP AT BLOCK HEIGHT', 'assembly/renders/isometric-charge-100.png'),
                 ('AUTOMATIC BELLOW / FIT', 'connected-bellow/connection-renders/isometric-charge-50.png'))
        for column, (label, path) in enumerate(shots):
            x = column * 640
            draw.text((x + 20, 116), label, fill=accent, font=font)
            with Image.open(managed / candidate / path) as photo:
                board.paste(photo.convert('RGB'), (x, 162))
        for n, text in enumerate((
            'Lid travel 8.75 units; metal top reaches y=16. Wider folds close the space beside the fixed collectors.',
            'Air socket: 1.6-square leather-lined bore around the native 1.5-square nozzle, with room for its full 3-unit reach.',
            'The broad scale is also a guide. Its sliding cuff, dark index and quarter marks follow the actual lid height.',
        )):
            draw.text((24, 830 + n * 27), text, fill=ink, font=small)
        detail = Image.new('RGB', (1920, 1480), bg)
        dd = ImageDraw.Draw(detail)
        dd.text((24, 18), title + ' / RESERVE MOTION', fill=accent, font=font)
        for n, (view, frame, label) in enumerate((('isometric', 0, 'EMPTY'), ('isometric', 50, 'HALF'),
                                                 ('isometric', 100, 'FULL'), ('front', 0, 'EMPTY / FRONT'),
                                                 ('front', 50, 'HALF / FRONT'), ('front', 100, 'FULL / FRONT'))):
            xx, yy = n % 3 * 640, 65 + n // 3 * 700
            dd.text((xx + 20, yy), label, fill=ink, font=small)
            with Image.open(managed / candidate / 'assembly/renders' / f'{view}-charge-{frame}.png') as photo:
                detail.paste(photo.convert('RGB'), (xx, yy + 32))
        detail.save(managed / (candidate + '.png'))
        audit = Image.new('RGB', (1280, 780), bg)
        ad = ImageDraw.Draw(audit)
        ad.text((18, 12), title + ' / ORTHOGONAL REVIEW', fill=accent, font=font)
        views = [(view, 'assembly') for view in ('isometric', 'front', 'back', 'left', 'right', 'top', 'bottom')]
        views.append(('isometric', 'mechanism'))
        for n, (view, state) in enumerate(views):
            xx, yy = n % 4 * 320, 55 + n // 4 * 360
            ad.text((xx + 12, yy), view.upper() if state == 'assembly' else 'MECHANISM', fill=ink, font=small)
            with Image.open(managed / candidate / state / 'renders' / f'{view}-charge-50.png') as photo:
                audit.paste(photo.convert('RGB').resize((320, 320), Image.Resampling.NEAREST), (xx, yy + 28))
        audit.save(managed / (candidate + '-views.png'))
    draw.text((24, 942), 'Selected A, revised for review. Play charge to fill, hold and discharge. Runtime integration remains pending.', fill=ink, font=small)
    board.save(managed / 'comparison.png')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--vintage-story', type=Path, required=True)
    parser.add_argument('--render', action='store_true')
    parser.add_argument('--inspect-bellow', action='store_true')
    args = parser.parse_args()
    if args.inspect_bellow:
        from graphics.review.large_bellows import read_source, import_bellows
        from gearwright_graphics.geometry import triangles
        source = Shape('automatic-bellow-reference')
        import_bellows(read_source(args.vintage_story), source)
        groups = {}
        for triangle in triangles(compile_shape(source)):
            if triangle.name.lower().startswith('nozzle'):
                groups.setdefault(triangle.name, []).extend(triangle.vertices * 16)
        print(json.dumps({name: {'low': np.min(points, axis=0).round(5).tolist(),
                                 'high': np.max(points, axis=0).round(5).tolist()}
                          for name, points in groups.items()}, indent=2))
        return
    managed = build_review(args.root, args.vintage_story)
    if args.render:
        render_review(args.root, args.vintage_story, managed)
    print('Accumulator review ready: ' + REVIEW_ROOT.as_posix())


if __name__ == '__main__':
    main()
