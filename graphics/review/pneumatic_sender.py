"""Selected rack sender with a narrow inventory branch and approved receiver gear style."""
from __future__ import annotations

import argparse
from collections import OrderedDict
import math
from pathlib import Path
import shutil

from gearwright_graphics.compiler import build_package, compile_shape, json_bytes
from gearwright_graphics.model import ModelPackage, Shape, animate
from graphics.review.pneumatic_direct_line import Canvas, ring_x, ring_y
from graphics.review.pneumatic_receiver import (
    APPROVED_REFERENCE, TEXTURES, gear, axle, bearing, bar, smooth,
)

REVIEW_ROOT = Path('generated/pneumatic-sender-review/current')
DESCRIPTIONS = OrderedDict((
    ('a-rack-lift', 'A | Rack lift'),
))
BRIEFS = {
    'a-rack-lift': ('Iron pinion drives a brass rack and guided tray straight up.',
                    'The narrow glass branch follows the inventory collar around the full-size tray.',
                    'The gear train reverses to lower the empty tray.'),
}
SELECTED_REFERENCE = {
    'candidate': 'a-rack-lift',
    'shape': (REVIEW_ROOT / 'a-rack-lift/assembly.shape.json').as_posix(),
    'sha256': 'f3bfda1ba75e4c697c86f61e780a28ad35caa9f7234b48515cf48ab86dac08e3',
}
SENDER_APPROVED_REFERENCE = {
    'candidate': 'a-rack-lift',
    'shape': (REVIEW_ROOT / 'a-rack-lift/assembly.shape.json').as_posix(),
    'sha256': 'fee034d4eea28d6a4b379841e6d2b68a853f2a7316d9a43709d3a4e698a661f9',
}
STATES = ('assembly', 'mechanism', 'drive-detail', 'installed', 'pass-through')
GEAR_Z = 3.8
STROKE, DECK_Y, DECK_DEPTH = 6.2, .30, .28
GEAR_CENTERS = {'a-rack-lift': (3., 7.4)}
BRANCH_LOW, BRANCH_HIGH = 5.4, 10.6


def motion(candidate, frame):
    frame = min(360., max(0., frame))
    up, down = smooth(frame / 150), smooth((frame - 250) / 110)
    lift = STROKE * up * (1 - down)
    theta = math.degrees(lift / 2)
    launch = smooth((frame - 180) / 60)
    on_tray = frame <= 180
    return {'theta': theta, 'lift': lift, 'dx': 0., 'temporal': -theta * 10 / 8,
            'cargo': (8 if on_tray else 8 + 10 * launch,
                      DECK_Y + DECK_DEPTH + 1.2 + (lift if on_tray else STROKE), 8)}


class Rig:
    def __init__(self, candidate, state):
        self.candidate, self.state = candidate, state
        self.shape, self.targets = Shape(candidate + '-' + state), []
        for key, (location, size) in TEXTURES.items():
            self.shape.texture(key, location, size=size)

    def box(self, name, lo, hi, material='brass', group='fixed', **kw):
        return self.shape.box(name, lo, hi, texture='#' + material, group=group, **kw)

    def pane(self, name, lo, hi, face):
        if self.state not in ('mechanism', 'drive-detail'):
            self.box(name, lo, hi, 'glass', 'glass', faces=(face,), uv=(0, 0, 16, 16), render_pass=1)

    def pivot(self, name, origin, fn):
        ref = self.shape.pivot(name, origin, group='drive')
        self.targets.append((ref, fn))
        return ref

    def rotor(self, name, origin, fn):
        return self.pivot(name, origin, lambda p: dict(rotationZ=fn(p)))

    def finish(self):
        animation = animate(self.shape, 'Load, launch and return', 'send', 361,
                            on_animation_end='Hold', on_activity_stopped='Stop')
        for frame in range(0, 361, 3):
            pose = motion(self.candidate, frame if self.state != 'pass-through' else 0)
            for target, fn in self.targets:
                values = dict(offsetX=0, offsetY=0, offsetZ=0, rotationX=0, rotationY=0, rotationZ=0)
                values.update(fn(pose))
                animation.keyframe(frame, target, **values)
        self.shape.add_animation(animation.build())
        return self.shape


def glazing(r, z, face):
    lower_z = BRANCH_LOW if face == 'north' else BRANCH_HIGH - .15
    if face == 'north':
        # A fitted slot lets the carriage bridge reach the tray. Leather edge
        # strips illustrate replaceable guide lips; they are not a pressure seal.
        for name, x0, x1, y0, y1 in (
            ('top', .8, 15.2, 7.1, 10.9),
            ('left-main', .8, 8.85, 4.9, 7.1), ('right-main', 9.55, 15.2, 4.9, 7.1),
        ):
            r.pane(f'glass-{face}-{name}', (x0, y0, z), (x1, y1, z + .15), face)
        for name, x0, x1 in (('left', BRANCH_LOW + .15, 8.85), ('right', 9.55, BRANCH_HIGH - .15)):
            r.pane('glass-front-load-' + name, (x0, .3, lower_z), (x1, 4.9, lower_z + .15), face)
        for x in (8.77, 9.55):
            for label, bottom, top, plane in (('lower', .3, 4.9, lower_z), ('upper', 4.9, 7.1, z)):
                r.box(f'guide-lip-{label}-{x}', (x, bottom, plane - .03),
                      (x + .08, top, plane + .17), 'leather')
    else:
        r.pane('glass-back-main', (.8, 4.9, z), (15.2, 10.9, z + .15), face)
        r.pane('glass-back-load', (BRANCH_LOW + .15, .3, lower_z),
               (BRANCH_HIGH - .15, 4.9, lower_z + .15), face)


def housing(r):
    c = Canvas(r.shape)
    ring_x(c, 'host-input', 0, .8, 'brass')
    ring_x(c, 'host-output', 15.2, 16, 'brass')
    ring_y(c, 'inventory-mouth', 0, .3, 'brass', low=BRANCH_LOW, high=BRANCH_HIGH, width=.55)
    r.pane('host-top', (.8, 10.9, 5.05), (15.2, 11.1, 10.9), 'up')
    for name, lo, hi in (('input', .8, BRANCH_LOW + .15), ('output', BRANCH_HIGH - .15, 15.2)):
        r.pane('host-floor-' + name, (lo, 4.9, 5.05), (hi, 5.1, 10.9), 'down')
    # Shoulders close the tube floor around the narrower branch. The front
    # shoulder retains the same carriage-stem slot as the glazing.
    for name, lo, hi in (('left', BRANCH_LOW + .15, 8.85), ('right', 9.55, BRANCH_HIGH - .15)):
        r.pane('host-shoulder-front-' + name, (lo, 4.9, 5.05), (hi, 5.1, BRANCH_LOW + .15), 'down')
    r.pane('host-shoulder-back', (BRANCH_LOW + .15, 4.9, BRANCH_HIGH - .15),
           (BRANCH_HIGH - .15, 5.1, 10.9), 'down')
    glazing(r, 4.9, 'north')
    glazing(r, 10.9, 'south')
    for name, x, face in (('left', BRANCH_LOW, 'west'), ('right', BRANCH_HIGH - .15, 'east')):
        r.pane('loader-' + name, (x, .3, BRANCH_LOW), (x + .15, 4.9, BRANCH_HIGH), face)
    for z in (BRANCH_LOW - .35, BRANCH_HIGH):
        for x in (BRANCH_LOW - .35, BRANCH_HIGH):
            r.box('frame-post-' + str(x) + '-' + str(z), (x, .05, z), (x + .35, 5.1, z + .35))
        r.box('frame-foot-' + str(z), (BRANCH_LOW - .35, .05, z), (BRANCH_HIGH + .35, .3, z + .35))
    for x in (BRANCH_LOW - .35, BRANCH_HIGH):
        r.box('frame-depth-tie-' + str(x), (x, .05, BRANCH_LOW - .35), (x + .35, .3, BRANCH_HIGH + .35))
    # A small band above the loading chamber repeats the approved tube's brass
    # collar language without obscuring the loading stroke.
    r.box('host-crown-band', (7.7, 11.1, 4.7), (8.3, 11.3, 11.3))
    for x in (1.2, 14, 14.6):
        r.box('direction-mark-' + str(x), (x, 7.4, 4.68), (x + .25, 8.6, 4.84))


def drive(r):
    x, y = GEAR_CENTERS[r.candidate]
    radius, teeth = 2., 10
    source_x = x - 1.05
    source_y = y + math.sqrt((radius + 1.6) ** 2 - (source_x - x) ** 2)
    contact = math.degrees(math.atan2(y - source_y, x - source_x)) - 90
    phase = ((contact * 8 / 360 + (contact + 180) * teeth / 360 - .5) % 1) * 45
    if r.state != 'drive-detail':
        gear(r, 'iron-drive', (x, y, GEAR_Z), radius, teeth, 'gear-iron', lambda p: p['theta'])
        gear(r, 'temporal-drive', (source_x, source_y, GEAR_Z), 1.6, 8, 'temporal', lambda p: p['temporal'], phase=phase)
    axle(r, 'drive-shaft', x, y, 2.78, 4.75, lambda p: p['theta'])
    axle(r, 'input-shaft', source_x, source_y, 2.86, 4.75, lambda p: p['temporal'])
    bearing(r, 'drive-bearing', x, y, 4.4)
    bearing(r, 'input-bearing', source_x, source_y, 4.4)
    for offset in (-.78, .52):
        r.box('drive-support-spine-' + str(offset), (x + offset, .3, 4.47),
              (x + offset + .26, y + .60, 4.77))
    for offset in (-.55, .55):
        bar(r, 'input-support-' + str(offset), (x + offset, y + .6),
            (source_x + offset, source_y - .8), 4.62, .26, .30)
        bar(r, 'input-support-neck-' + str(offset), (source_x + offset, source_y - .8),
            (source_x + offset, source_y + .6), 4.62, .26, .30)
    r.box('drive-support-foot', (2.1, .3, 4.47), (BRANCH_LOW - .04, .6, 4.77))
    r.box('drive-foot-tie', (BRANCH_LOW - .32, .3, 4.47), (BRANCH_LOW - .04, .6, BRANCH_LOW))


def carriage(r):
    pivot = r.pivot('carriage', (0, 0, 0), lambda p: dict(offsetX=p['dx'], offsetY=p['lift']))
    r.box('tray-floor', (6.35, DECK_Y, 6.3), (9.65, DECK_Y + DECK_DEPTH, 9.7), parent=pivot)
    # Low side guides leave both mainline directions open for the air launch.
    for z in (6.3, 9.5):
        r.box('tray-lip-' + str(z), (6.35, .58, z), (9.65, .92, z + .2), 'iron', parent=pivot)
    r.box('tray-bridge', (9.03, .30, 2.74), (9.37, .56, 6.6), 'iron', parent=pivot)
    for x in (8.6, 9.65):
        r.box('guide-rail-' + str(x), (x, .3, 4.1), (x + .2, 7.2, 4.38), 'iron')
        r.box('guide-foot-' + str(x), (x, .15, 4.1), (x + .2, .35, BRANCH_LOW), 'iron')
    r.box('guide-header', (7.7, 7., 4.1), (9.85, 7.2, 4.38))
    r.box('guide-hanger', (7.7, 7., 4.1), (8., 11.3, 4.38))
    r.box('guide-crown-tie', (7.7, 11.1, 4.1), (8.3, 11.3, 4.9))
    # The crosshead runs between two rails with a small edge clearance.
    r.box('guide-crosshead', (8.85, .31, 4.08), (9.60, .60, 4.42), parent=pivot)
    return pivot


def rack_lift(r, tray):
    # Module .4 matches the ten-tooth, radius-2 iron pinion. The rack pitch
    # line is x=5, tangent to its right side. Its teeth follow a 20-degree flank.
    r.box('rack-backbone', (5.48, .20, 3.088), (5.8, 9.05, 3.888), 'gear-brass', parent=tray)
    pitch = math.pi * .4
    first = 7.4 - 5 * pitch
    for tooth in range(7):
        cy = first + tooth * pitch
        for step in range(4):
            lo, hi = 4.70 + .195 * step, 4.70 + .195 * (step + 1)
            half = .4 * math.pi / 4 - .028 + (lo - 5) * math.tan(math.radians(20))
            r.box(f'rack-tooth-{tooth}-{step}', (lo, cy - half, 3.088), (hi, cy + half, 3.888),
                  'gear-brass', parent=tray)
    r.box('rack-carriage-link', (5.48, .3, 2.74), (9.25, .56, 3.02), 'iron', parent=tray)
    r.box('rack-root-key', (5.54, .3, 2.74), (5.75, .56, 3.30), 'iron', parent=tray)
    for y in (6.1, 8.7):
        r.box('rack-guide-' + str(y), (5.48, y, 4.0), (6.0, y + .45, 4.3))
        r.box('rack-guide-bracket-' + str(y), (3, y, 4.47), (6, y + .2, 4.77))
        r.box('rack-guide-seat-' + str(y), (5.82, y, 4.0), (6, y + .45, 4.77))


def inventory_context(r):
    for name, lo, hi in (
        ('floor', (.4, -15.7, .4), (15.6, -15, 15.6)),
        ('back', (.4, -15, 14.8), (15.6, -3.5, 15.6)),
        ('left', (.4, -15, .4), (1.2, -3.5, 14.8)),
        ('right', (14.8, -15, .4), (15.6, -3.5, 14.8)),
    ):
        r.box('context-' + name, lo, hi, 'oak', 'context')


def candidate_shape(candidate, state='assembly'):
    if candidate not in DESCRIPTIONS or state not in STATES:
        raise ValueError('Unknown sender candidate or state')
    r = Rig(candidate, state)
    housing(r)
    drive(r)
    tray = carriage(r)
    rack_lift(r, tray)
    if state == 'installed':
        inventory_context(r)
    if state != 'pass-through':
        cargo = r.pivot('representative-cargo', (0, 0, 0), lambda p: dict(zip(('offsetX', 'offsetY', 'offsetZ'), p['cargo'])))
        r.box('cargo-envelope', (-1.2, -1.2, -1.2), (1.2, 1.2, 1.2), 'oak', 'cargo', parent=cargo)
        r.box('cargo-band', (-1.201, -1.201, -.14), (1.201, 1.201, .14), 'brass', 'cargo', parent=cargo)
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
    package = ModelPackage('pneumatic_sender_review')
    for candidate in DESCRIPTIONS:
        for state in STATES:
            shape = candidate_shape(candidate, state)
            compile_shape(shape)
            package.shape(shape, f'{staging.relative_to(root).as_posix()}/{candidate}/{state}.shape.json')
    build_package(package, root, write=True)
    (staging / '.gearwright-review.json').write_bytes(json_bytes({
        'version': 1, 'source': 'graphics/review/pneumatic_sender.py', 'managedPath': REVIEW_ROOT.as_posix(),
        'candidates': list(DESCRIPTIONS), 'states': list(STATES), 'animations': ['send'],
        'decision': {'status': 'approved', 'runtimePromotion': True, 'selectedCandidate': 'a-rack-lift',
                     'basisReference': SELECTED_REFERENCE, 'approvedReference': SENDER_APPROVED_REFERENCE,
                     'retiredDirections': ['b-crank-yoke', 'c-parallel-arms']},
        'branch': {'previousOuterWidth': 9.5, 'outerWidth': BRANCH_HIGH - BRANCH_LOW,
                   'outerDepth': BRANCH_HIGH - BRANCH_LOW, 'trayWidth': 3.3, 'trayDepth': 3.4,
                   'minimumTraySideClearance': .8, 'minimumTrayDepthClearance': .75, 'unitsPerBlock': 16},
        'styleReference': APPROVED_REFERENCE,
        'sources': [location for location, _ in TEXTURES.values()],
        'ports': {'input': 'west', 'output': 'east', 'inventory': 'down', 'footprint': [1, 1, 1]},
        'cycle': {'frames': 361, 'lift': [0, 150], 'hold': [150, 250], 'airLaunch': [180, 240],
                  'emptyReturn': [250, 360], 'stroke': STROKE, 'cargoSize': 2.4},
        'limits': ['Inventory handoff at branch mouth precedes this mechanical loading cycle.',
                   'Air launch follows receiver orders; the sender does not create push deliveries.',
                   'Dwell holding, reversal control, branch sealing and irregular cargo require runtime engineering.',
                   'The upward loader assumes gravity down; alternate mounts and chest access are not approved.'],
    }))
    notes = ['# Sender A: rack lift with narrow inventory branch', '',
             'The maintainer approved A with the narrower input branch. Only the approved A remains. '
             'The exact approved assembly reference is recorded in the manifest and protected by a hash test. '
             'This review generator does not promote runtime assets.', '']
    for candidate, title in DESCRIPTIONS.items():
        notes.extend([f'- `{candidate}` - {title}. ' + ' '.join(BRIEFS[candidate]), ''])
    notes.extend([
        '## Loading and branch fit', '',
        'The lower glass branch is 5.2 units wide and deep, following the inventory collar. Its '
        'former width was 9.5 units. The frame contracts around it, and glass shoulders join it '
        'to the unchanged through-tube. The 3.3-by-3.4 tray retains 0.8 units of side clearance '
        'and 0.75 units of front/back clearance. Both carriage guides remain supported; the '
        'stem passes through a fitted slot in the front pane and shoulder.', '',
        'A 2.4-unit cargo representation starts on the tray at the inventory mouth, after the native '
        'inventory-to-sender handoff. It rises 6.2 model units, waits at mainline height, and is launched '
        'along the mainline by transport air. The tray then returns empty. All machine cuboids fit one block. '
        'The parked mechanism clears a 2.4-unit through-line cargo envelope. The review uses downward '
        'inventory attachment and horizontal mainline; other orientations are not implied.', '',
        'Eight temporal teeth directly mesh with the ten-tooth iron driving wheel and its brass rack. '
        'The gear centres, complete tooth geometry, tray size, rack and 6.2-unit stroke remain intact. '
        'The wheels reuse the approved receiver cuboids, '
        '0.8-unit wheel depth and inspected native plate/temporal textures. Glass and brass carry the '
        'housing; iron carries compact shafts, guiding rails and loaded pins. These wear surfaces '
        'need harder, smaller sections than copper or wood can provide. No brass teeth mesh together.', '',
        '## Review controls and limits', '',
        'Scrub send: 0 branch handoff, 75 mid-lift, 150 raised, 210 air launch, 300 empty return, '
        '360 parked. The animation holds at the end, with the parcel entirely beyond the outlet. '
        'Mechanism hides glass; drive-detail also hides the two wheels to expose the conversion '
        'to the linear lift. Installed adds a schematic inventory cutaway. In the isometric views '
        'the downstream outlet is on the left side of the image.', '',
        'This comparison establishes geometry and visible load paths. It does not yet implement '
        'inventory extraction, receiver orders, load-holding at the dwell, air-valve timing, reversal '
        'control, pressure seals, jam handling or variable cargo. A uses a fitted stem slot in the '
        'front glazing. Chest lid/access and engine rendering need review '
        'when integrating the selected sender. Cargo ownership changes remain server/persistence '
        'operations and never occur because an animation frame was reached.', '',
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
    for candidate in DESCRIPTIONS:
        for state in STATES:
            shots = [('isometric,front,back,left,right,top,bottom', '75')]
            if state == 'assembly':
                shots += [('isometric', '0,150,210,300,360')]
            elif state == 'installed':
                shots = [('isometric', '75')]
            elif state == 'pass-through':
                shots = [('front,top', '0')]
            elif state == 'drive-detail':
                shots = [('front,isometric', '0,75,150,300')]
            for views, frames in shots:
                installed = state == 'installed'
                args = ['--vintage-story', str(game), '--mod', str(root),
                        '--model', str(managed / candidate / (state + '.shape.json')),
                        '--strict-textures', '--views', views, '--animation', 'send', '--frames', frames,
                        '--lighting', 'flat', '--light-direction', '.6,.7,-1', '--size', '640x640',
                        '--orthographic', '--orthographic-scale', '2.4' if installed else '1.6',
                        '--camera-target', '.5,0,.5' if installed else '.5,.45,.5',
                        '--output', str(managed / candidate / state / 'renders')]
                run(build_parser().parse_args(args), root)
        print('Rendered ' + candidate, flush=True)
    bg, ink, accent = (20, 23, 28), (236, 227, 210), (114, 204, 185)
    font, small = ImageFont.load_default(size=24), ImageFont.load_default(size=18)
    board = Image.new('RGB', (1920, 980), bg)
    draw = ImageDraw.Draw(board)
    draw.text((24, 20), 'SENDER A / NARROW INVENTORY BRANCH', fill=ink, font=font)
    draw.text((24, 58), 'Selected rack lift. Direct temporal tooth drive. Glass + brass + iron.', fill=ink, font=small)
    for candidate, title in DESCRIPTIONS.items():
        for i, (label, view) in enumerate((('ASSEMBLY', 'isometric'), ('FRONT / BRANCH WIDTH', 'front'),
                                          ('BOTTOM / INVENTORY MOUTH', 'bottom'))):
            x = i * 640
            draw.text((x + 20, 108), label, fill=accent, font=font)
            with Image.open(managed / candidate / 'assembly/renders' / f'{view}-send-75.png') as photo:
                board.paste(photo.convert('RGB'), (x, 150))
        for n, line in enumerate((
            'Lower glass branch: 9.5 > 5.2 units wide, with the frame pulled in to match.',
            'Full-size tray, 6.2-unit stroke and thick 8:10 gearing retained. Side clearance 0.8 units; depth clearance 0.75.',
            'Glass shoulders join the narrow branch to the existing through-line. Stem slot and supported guides stay clear.',
        )):
            draw.text((24, 810 + n * 32), line, fill=ink, font=small)
        detail = Image.new('RGB', (1920, 1480), bg)
        dd = ImageDraw.Draw(detail)
        dd.text((24, 18), title + ' / COMPLETE LOAD AND RETURN', fill=accent, font=font)
        for n, (frame, label) in enumerate(((0, 'AT INVENTORY MOUTH'), (75, 'LIFT'), (150, 'RAISED / DWELL'),
                                          (210, 'AIR LAUNCH'), (300, 'EMPTY RETURN'), (360, 'PARKED / CARGO AWAY'))):
            xx, yy = n % 3 * 640, 70 + n // 3 * 700
            dd.text((xx + 20, yy), label, fill=ink, font=small)
            with Image.open(managed / candidate / 'assembly/renders' / f'isometric-send-{frame}.png') as photo:
                detail.paste(photo.convert('RGB'), (xx, yy + 32))
        detail.save(managed / (candidate + '.png'))
    draw.text((24, 938), 'Cargo handoff begins at the branch mouth. Dwell control, pressure sealing and inventory integration remain to engineer.', fill=ink, font=small)
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
    print('Sender review ready: ' + REVIEW_ROOT.as_posix())


if __name__ == '__main__':
    main()
