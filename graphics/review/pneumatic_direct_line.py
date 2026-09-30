"""Review-only pneumatic intake-to-receiver candidates; never emits runtime assets."""
from __future__ import annotations

import argparse
from collections import OrderedDict
from pathlib import Path
import math
import shutil

from gearwright_graphics.compiler import build_package, compile_shape, json_bytes
from gearwright_graphics.model import ModelPackage, Shape

REVIEW_ROOT = Path('generated/pneumatic-direct-line-review/current')
DESCRIPTIONS = OrderedDict((
    ('a-timber-lift', 'A | Timber lift: open oak posts, broad glass chamber and a bronze lift tray. The exposed vertical stroke explains loading.'),
    ('b-rotary-pocket', 'B | Rotary pocket: a rounded brass clock face and a quarter-turn bronze cradle. Tests compact rotary handling with a clear mainline.'),
    ('c-slide-lock', 'C | Slide lock: a copper sleeve, narrow sight windows and two brass shutters. Air lifts cargo after the lower lock opens; few exposed moving parts.'),
))
STATES = ('line', 'intake', 'straight-empty', 'straight-loaded', 'elbow',
          'sender-rest', 'sender-loading', 'sender-ready', 'receiver-ready')
TEXTURES = {
    'oak': ('game:block/wood/planks/oak1', (32, 32)),
    'copper': ('game:block/metal/sheet/copper1', (32, 32)),
    'brass': ('game:block/metal/sheet/brass1', (32, 32)),
    'bronze': ('game:block/metal/sheet/tinbronze1', (32, 32)),
    'temporal': ('game:item/resource/temporalgear', (16, 16)),
    'glass': ('gearwright:block/inspection-glass', (16, 16)),
}


class Canvas:
    def __init__(self, shape, prefix='', offset=(0, 0, 0)):
        self.shape, self.prefix, self.offset = shape, prefix, offset

    def box(self, name, start, end, texture='copper', group='housing', **kwargs):
        def point(value):
            return tuple(a + b for a, b in zip(value, self.offset))
        if 'rotation_origin' in kwargs:
            kwargs['rotation_origin'] = point(kwargs['rotation_origin'])
        return self.shape.box(self.prefix + name, point(start), point(end),
                              texture='#' + texture, group=group, **kwargs)

    def glass(self, name, start, end):
        axis = min(range(3), key=lambda i: end[i] - start[i])
        face = (('west', 'east'), ('down', 'up'), ('north', 'south'))[axis][int((start[axis] + end[axis]) / 2 > 8)]
        self.box(name, start, end, 'glass', 'glass', render_pass=1, uv=(0, 0, 16, 16), faces=(face,))


def ring_x(c, name, start, end, texture='copper', low=4.5, high=11.5, width=.7):
    for part, lo, hi in (
        ('bottom', (start, low, low), (end, low + width, high)),
        ('top', (start, high - width, low), (end, high, high)),
        ('front', (start, low + width, low), (end, high - width, low + width)),
        ('back', (start, low + width, high - width), (end, high - width, high)),
    ):
        c.box(name + '-' + part, lo, hi, texture, 'port')


def ring_y(c, name, start, end, texture='copper', low=4.5, high=11.5, width=.7):
    for part, lo, hi in (
        ('left', (low, start, low), (low + width, end, high)),
        ('right', (high - width, start, low), (high, end, high)),
        ('front', (low + width, start, low), (high - width, end, low + width)),
        ('back', (low + width, start, high - width), (high - width, end, high)),
    ):
        c.box(name + '-' + part, lo, hi, texture, 'port')


def arrow(c, x, y=8, z=4.42):
    # Two cuboid strokes make a readable arrow without introducing mesh geometry.
    for index, angle in enumerate((-42, 42)):
        c.box('arrow-' + str(index), (x - 1.35, y - .2, z), (x + .15, y + .2, z + .18),
              'brass', 'direction', rotation_origin=(x, y, z), rotation=(0, 0, angle))


def cargo(c, center=(8, 8, 8), size=2.6):
    half = size / 2
    c.box('cargo-example', tuple(v - half for v in center), tuple(v + half for v in center),
          'oak', 'cargo')


def tube(c, variant, *, loaded=False, elbow=False):
    if elbow:
        ring_x(c, 'input', 0, .8)
        ring_y(c, 'output', 15.2, 16, 'brass')
        # Square hollow elbow: no panel is left across either cargo opening.
        c.glass('elbow-bottom', (.8, 4.9, 5.2), (10.9, 5.1, 10.8))
        c.glass('elbow-outer', (10.9, 5.1, 5.2), (11.1, 15.2, 10.8))
        c.glass('elbow-inner-horizontal', (.8, 10.9, 5.2), (4.9, 11.1, 10.8))
        c.glass('elbow-inner-vertical', (4.9, 11.1, 5.2), (5.1, 15.2, 10.8))
        for side, z in (('front', 4.9), ('back', 10.9)):
            c.glass('elbow-' + side + '-horizontal', (.8, 5.1, z), (10.9, 10.9, z + .2))
            c.glass('elbow-' + side + '-vertical', (5.1, 10.9, z), (10.9, 15.2, z + .2))
        cargo(c)
        return
    ring_x(c, 'input', 0, .8)
    ring_x(c, 'output', 15.2, 16, 'brass')
    for name, start, end in (
        ('bottom', (.8, 4.9, 5.1), (15.2, 5.1, 10.9)),
        ('top', (.8, 10.9, 5.1), (15.2, 11.1, 10.9)),
        ('front', (.8, 4.9, 4.9), (15.2, 11.1, 5.1)),
        ('back', (.8, 4.9, 10.9), (15.2, 11.1, 11.1)),
    ):
        c.glass('tube-' + name, start, end)
    if variant == 'a-timber-lift':
        for z in (4.4, 11.0):
            c.box('lower-rail-' + str(z), (.8, 4.4, z), (15.2, 4.9, z + .6), 'oak')
    elif variant == 'b-rotary-pocket':
        ring_x(c, 'centre-band', 7.7, 8.3, 'brass', low=4.7, high=11.3, width=.35)
    else:
        for z in (4.6, 10.9):
            c.box('top-seam-' + str(z), (.8, 10.9, z), (15.2, 11.35, z + .45))
    arrow(c, 13.8)
    if loaded:
        cargo(c)


def gear(c, x, y, z, phase):
    # A new, deliberately simple cuboid composition using the inspected temporal
    # material; no unmodified game item model is copied into the project.
    for index in range(8):
        angle = index * 45 + phase * 90
        c.box('temporal-rim-' + str(index), (x - .85, y + 1.2, z), (x + .85, y + 1.9, z + .55),
              'temporal', 'clockwork', rotation_origin=(x, y, z), rotation=(0, 0, angle), glow=20)
        c.box('temporal-tooth-' + str(index), (x - .35, y + 1.9, z), (x + .35, y + 2.35, z + .55),
              'temporal', 'clockwork', rotation_origin=(x, y, z), rotation=(0, 0, angle), glow=20)
    for index in range(4):
        c.box('temporal-spoke-' + str(index), (x - .22, y, z + .1), (x + .22, y + 1.5, z + .45),
              'temporal', 'clockwork', rotation_origin=(x, y, z), rotation=(0, 0, index * 90 + phase * 90), glow=20)
    c.box('temporal-pin', (x - .4, y - .4, z - .18), (x + .4, y + .4, z + .75), 'bronze', 'clockwork')


def intake(c, variant):
    # One outlet. The five smaller recessed grilles are free-air faces, not tube ports.
    material = 'oak' if variant == 'a-timber-lift' else 'brass' if variant == 'b-rotary-pocket' else 'copper'
    c.box('plenum', (3, 3, 3), (12, 13, 13), material)
    ring_x(c, 'outlet-neck', 12, 15.2, 'copper', low=5, high=11, width=.45)
    ring_x(c, 'outlet', 15.2, 16, 'brass')
    for face in ('west', 'up', 'down', 'north', 'south'):
        for row in range(3):
            across = 5.2 + row * 2
            if face == 'west':
                lo, hi = (2.65, across, 5), (3, across + .65, 11)
            elif face in ('north', 'south'):
                z = 2.65 if face == 'north' else 13
                lo, hi = (4.5, across, z), (10.5, across + .65, z + .35)
            else:
                y = 2.65 if face == 'down' else 13
                lo, hi = (4.5, y, across), (10.5, y + .35, across + .65)
            c.box('air-grille-' + face + '-' + str(row), lo, hi, 'bronze', 'intake-grille')
    for y in (3.1, 12.3):
        c.box('plenum-strap-' + str(y), (2.85, y, 2.85), (12.15, y + .6, 3), 'copper')
    arrow(c, 13.8)


def mechanism(c, variant, *, phase=0, receiving=False, loaded=True):
    if not 0 <= phase <= 1:
        raise ValueError('Transfer pose must be between zero and one')
    ring_x(c, 'input', 0, .8)
    ring_x(c, 'input-neck', .8, 4.1, low=5, high=11, width=.4)
    if not receiving:
        ring_x(c, 'output-neck', 11.9, 15.2, low=5, high=11, width=.4)
        ring_x(c, 'output', 15.2, 16, 'brass')
        arrow(c, 13.8)
    else:
        c.box('end-cap', (13, 4.5, 4.5), (13.7, 11.5, 11.5), 'copper')
    ring_y(c, 'inventory-collar', 0, .8, 'brass')
    # Narrow walls reach the chamber without plugging the inventory branch.
    ring_y(c, 'inventory-throat', .8, 2.8, low=5, high=11, width=.35)
    ring_y(c, 'chamber-floor', 2.75, 2.95, low=3.95, high=12.05, width=1.3)
    # Close both side walls around their square mainline opening. The necks meet
    # the opening edges, so glass cannot cap a sender's through-tube.
    for side, x in (('left', 3.95), ('right', 11.9)):
        c.glass(side + '-lower-pane', (x, 2.95, 4.1), (x + .15, 4.9, 11.9))
        c.glass(side + '-upper-pane', (x, 11.1, 4.1), (x + .15, 13, 11.9))
        c.glass(side + '-front-pane', (x, 4.9, 4.1), (x + .15, 11.1, 5))
        c.glass(side + '-back-pane', (x, 4.9, 11), (x + .15, 11.1, 11.9))
    if variant != 'a-timber-lift':
        c.glass('chamber-roof', (3.9, 12.85, 3.25), (12.1, 13, 12.9))
    if variant == 'a-timber-lift':
        for x in (3, 12):
            for z in (3, 12):
                c.box('oak-post-' + str(x) + '-' + str(z), (x, 1, z), (x + 1, 14, z + 1), 'oak')
        for y in (1, 13):
            for z in (3, 12):
                c.box('oak-crosspiece-' + str(y) + '-' + str(z), (4, y, z), (12, y + 1, z + 1), 'oak')
        c.glass('large-front-window', (4, 2, 3.95), (12, 13, 4.1))
        c.glass('large-back-window', (4, 2, 11.9), (12, 13, 12.05))
        c.glass('roof', (4, 12.85, 4.1), (12, 13, 11.9))
        gear(c, 6.3, 10.4, 2.65, phase)
        tray_y = 2.95 + phase * 3.5
        c.box('lift-tray', (6.3, tray_y, 6.3), (9.7, tray_y + .35, 9.7), 'bronze', 'moving')
        for x in (5.55, 10.05):
            c.box('lift-guide-' + str(x), (x, 2.2, 5.4), (x + .35, 10, 5.9), 'brass', 'moving-guide')
        c.box('lift-stem', (7.6, .85, 7.6), (8.4, tray_y, 8.4), 'bronze', 'moving')
        if loaded:
            cargo(c, (8, tray_y + 1.65, 8))
    elif variant == 'b-rotary-pocket':
        for x in (3.1, 12.1):
            for z in (3, 12):
                c.box('oak-cheek-' + str(x) + '-' + str(z), (x, 1, z), (x + .8, 13.5, z + 1), 'oak')
        # Eight shallow segments produce a readable clock plate with an open centre.
        for index in range(8):
            c.box('clock-rim-' + str(index), (6.1, 12, 2.3), (9.9, 13, 3.2), 'brass',
                  rotation_origin=(8, 8, 3), rotation=(0, 0, index * 45))
        c.glass('clock-window', (3.4, 3.4, 3.25), (12.6, 12.6, 3.4))
        c.glass('back-window', (3.8, 2, 12.3), (12.2, 13, 12.5))
        c.box('back-bearing-bridge', (3.9, 7.5, 12.5), (12.1, 8.5, 13.15), 'oak')
        gear(c, 8, 8, 1.35, phase)
        angle = -phase * math.pi / 2
        cx, cy = 8 + 2.8 * math.sin(angle), 8 - 2.8 * math.cos(angle)
        c.box('cradle-floor', (cx - 1.7, cy - 1.7, 6.3), (cx + 1.7, cy - 1.35, 9.7), 'bronze', 'moving')
        c.box('cradle-link', (7.7, 5.2, 11.2), (8.3, 8, 11.7), 'bronze', 'moving',
              rotation_origin=(8, 8, 11.4), rotation=(0, 0, -phase * 90))
        c.box('cradle-pin', (cx - .35, cy - .35, 9.7), (cx + .35, cy + .35, 11.7), 'bronze', 'moving')
        for z in (6, 9.7):
            c.box('cradle-cheek-' + str(z), (cx - 1.7, cy - 1.7, z), (cx + 1.7, cy + .9, z + .3), 'bronze', 'moving')
        if loaded:
            cargo(c, (cx, cy, 8))
    else:
        for x in (3.2, 12.1):
            for y0, y1 in ((1.1, 3.8), (11.2, 13.7)):
                c.box('copper-upright-' + str(x) + '-' + str(y0), (x, y0, 3), (x + .7, y1, 13), 'copper')
            for z in (3, 12):
                c.box('copper-side-post-' + str(x) + '-' + str(z), (x, 3.8, z), (x + .7, 11.2, z + 1), 'copper')
        for z in (3, 12.3):
            for y in (1.1, 12.7):
                c.box('sleeve-edge-' + str(y) + '-' + str(z), (3.9, y, z), (12.1, y + 1, z + .7), 'copper')
        c.glass('front-sight-window', (3.9, 2.1, 3.05), (12.1, 12.7, 3.25))
        c.glass('back-sight-window', (3.9, 2.1, 12.7), (12.1, 12.7, 12.9))
        c.box('clock-cover-top', (4.4, 10.1, 2.15), (11.6, 11, 2.7), 'brass')
        c.box('clock-cover-bottom', (4.4, 5, 2.15), (11.6, 5.9, 2.7), 'brass')
        gear(c, 8, 8, 1.45, phase)
        opening = min(1, phase * 4) * 2.8
        c.box('shutter-left', (5.25 - opening, 4.15, 5.25), (8 - opening, 4.5, 10.75), 'brass', 'moving')
        c.box('shutter-right', (8 + opening, 4.15, 5.25), (10.75 + opening, 4.5, 10.75), 'brass', 'moving')
        for z in (4.65, 10.85):
            c.box('shutter-rail-' + str(z), (2.3, 3.95, z), (13.7, 4.7, z + .5), 'bronze', 'moving-guide')
        if loaded:
            cargo(c, (8, 2.85 + max(0, (phase - .25) / .75) * 5.15, 8), size=2.2)


def chest_context(c):
    # Deliberately schematic existing inventory, not a proposed new storage block.
    c.box('existing-chest-body', (1, 0, 1), (15, 13.7, 15), 'oak', 'context')
    c.box('existing-chest-lid', (.8, 13.8, .8), (15.2, 16, 15.2), 'oak', 'context')
    for x in (2.5, 12.5):
        c.box('existing-chest-band-' + str(x), (x, 0, .7), (x + .6, 16, 1), 'bronze', 'context')
    c.box('existing-chest-latch', (7, 11.5, .35), (9, 14, .8), 'bronze', 'context')


def candidate_shape(candidate, state, *, phase=None):
    if candidate not in DESCRIPTIONS or state not in STATES:
        raise ValueError('Unknown pneumatic candidate or review state')
    shape = Shape(candidate + '-' + state, 16, 16)
    for key, (location, size) in TEXTURES.items():
        shape.texture(key, location, size=size)
    c = Canvas(shape)
    if state == 'line':
        intake(Canvas(shape, 'intake-', (0, 16, 0)), candidate)
        mechanism(Canvas(shape, 'sender-', (16, 16, 0)), candidate, phase=0, loaded=False)
        tube(Canvas(shape, 'tube1-', (32, 16, 0)), candidate, loaded=True)
        tube(Canvas(shape, 'tube2-', (48, 16, 0)), candidate)
        mechanism(Canvas(shape, 'receiver-', (64, 16, 0)), candidate, phase=0, receiving=True, loaded=False)
        chest_context(Canvas(shape, 'source-', (16, 0, 0)))
        chest_context(Canvas(shape, 'destination-', (64, 0, 0)))
    elif state == 'intake':
        intake(c, candidate)
    elif state.startswith('straight') or state == 'elbow':
        tube(c, candidate, loaded=state == 'straight-loaded', elbow=state == 'elbow')
    else:
        pose = {'sender-rest': 0, 'sender-loading': .5, 'sender-ready': 1, 'receiver-ready': 1}[state]
        mechanism(c, candidate, phase=pose if phase is None else phase, receiving=state.startswith('receiver'),
                  loaded=state != 'sender-rest')
    return shape


def build_review(root):
    root = Path(root).resolve()
    managed = (root / REVIEW_ROOT).resolve()
    staging, backup = managed.with_name('.current-build'), managed.with_name('.current-old')
    generated = (root / 'generated').resolve()
    for path in (managed, staging, backup):
        path.relative_to(generated)
    for path in (staging, backup):
        if path.exists():
            shutil.rmtree(path)
    package = ModelPackage('pneumatic_direct_line_review')
    counts = {}
    for candidate in DESCRIPTIONS:
        for state in STATES:
            shape = candidate_shape(candidate, state)
            compile_shape(shape)
            package.shape(shape, f'{staging.relative_to(root).as_posix()}/{candidate}/{state}.shape.json')
            counts[candidate + '/' + state] = len(shape.elements)
    build_package(package, root, write=True)
    (staging / '.gearwright-review.json').write_bytes(json_bytes({
        'version': 1, 'source': 'graphics/review/pneumatic_direct_line.py',
        'managedPath': REVIEW_ROOT.as_posix(), 'purpose': 'Choose the first no-junction pneumatic transport model family',
        'candidates': list(DESCRIPTIONS), 'states': list(STATES), 'animations': [],
        'sources': [location for location, _ in TEXTURES.values()],
        'elementCounts': counts,
        'decision': {'status': 'pending', 'runtimePromotion': False, 'pendingCandidates': list(DESCRIPTIONS)},
    }))
    (staging / 'README.md').write_text('\n'.join([
        '# Pneumatic direct line - model candidates', '',
        'Review only. No model, recipe or block registration is promoted by this builder.', '',
        *DESCRIPTIONS.values(), '',
        'Shared geometry: 16 units per block; tube centre at (8,8,8); a 6-unit glass body, '
        '7-unit collars and a clear cargo opening. Input is left, output right, inventory branch down. '
        'Sender mainlines continue; end receivers have no right-hand tube port. Copper inlet and '
        'brass outlet collars plus physical chevrons show direction. All active blocks include a fixed temporal gear.', '',
        'The plenum has one tube outlet and five free-air receiving grilles. '
        'Bellows are existing equipment, located to the left of the intake; they are not redesigned here. '
        'The two wooden boxes in the line view stand for existing inventory blocks. The single oak cube '
        'illustrates cargo; it is not a new item or craftable carrier. A live renderer will use the actual item mesh.', '',
        'A load path: oak posts carry the chamber; bronze tray/guide contact takes wear. Unreinforced '
        'wood sliding against the tray would abrade. The 3.5-unit lift is shown at rest, half travel and full travel.',
        'B load path: oak cheeks carry a brass clock plate; a bronze cradle turns 90 degrees about '
        'the tube centre. Copper is too soft for the cradle bearing. The gear is integral, not an insertable upgrade.',
        'C load path: a folded copper enclosure carries light air loads; bronze rails support the '
        'brass shutters. Copper sliding faces would wear. Shutters retract 2.8 units each before air raises the cargo.', '',
        'Receiver-ready shows the catch pose; delivery reverses that loading motion into the branch. '
        'Poses are mechanism studies, not a running inventory simulation. No timing, item ownership or '
        'crash-recovery behaviour is implied by these stills.', '',
        'Inspected source materials: oak plank grain, copper/brass/tin-bronze sheet, temporal-gear '
        'texture and Gearwright inspection glass. Pixel density is retained through texture-size declarations. '
        'The temporal gear is a new small cuboid composition; no game atlas or game model is copied into tracked files.', '',
        'Choose A, B or C, or identify pieces to combine. Runtime promotion requires explicit approval. '
        'See PNEUMATIC-TUBES-DESIGN.md for the executable slice, transfer boundaries and acceptance tests.', '',
    ]), encoding='utf-8')
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
            line = state == 'line'
            args = ['--vintage-story', str(game), '--mod', str(root),
                    '--model', str(managed / candidate / (state + '.shape.json')),
                    '--strict-textures', '--views', 'front,isometric,back,top' if line else 'isometric,front,back,right,left,top,bottom',
                    '--lighting', 'flat', '--light-direction', '.6,.7,-1',
                    '--size', '1200x560' if line else '560x560', '--orthographic',
                    '--orthographic-scale', '2.8' if line else '1.8',
                    '--camera-target', '2.5,1,.5' if line else '.5,.5,.5',
                    '--output', str(managed / candidate / state / 'renders')]
            run(build_parser().parse_args(args), root)
        print('Rendered ' + candidate, flush=True)
    font = ImageFont.load_default(size=23)
    small = ImageFont.load_default(size=17)
    bg, ink = (20, 23, 28), (236, 227, 210)
    sheet = Image.new('RGB', (1680, 1760), bg)
    draw = ImageDraw.Draw(sheet)
    draw.text((24, 16), 'PNEUMATIC TRANSPORT / NO JUNCTION / REVIEW CANDIDATES', fill=ink, font=font)
    draw.text((24, 52), 'FLOW <   End receiver < two glass tubes < sender < air intake. Source chest on the right; destination on the left.', fill=ink, font=small)
    for index, (candidate, description) in enumerate(DESCRIPTIONS.items()):
        y = 90 + index * 550
        draw.text((24, y), description.split(':')[0], fill=ink, font=font)
        with Image.open(managed / candidate / 'line/renders/front.png') as photo:
            sheet.paste(photo.convert('RGB').resize((1080, 504)), (0, y + 38))
        with Image.open(managed / candidate / 'sender-ready/renders/isometric.png') as photo:
            sheet.paste(photo.convert('RGB').resize((480, 480)), (1200, y + 45))
        draw.text((1220, y + 24), 'SENDER / LOADED', fill=ink, font=small)
    sheet.save(managed / 'comparison.png')
    for candidate, description in DESCRIPTIONS.items():
        board = Image.new('RGB', (1680, 1290), bg)
        draw = ImageDraw.Draw(board)
        draw.text((24, 16), description.split(':')[0], fill=ink, font=font)
        items = [('intake', 'AIR INTAKE'), ('straight-loaded', 'GLASS TUBE / CARGO'), ('elbow', 'ELBOW'),
                 ('sender-rest', 'SENDER / REST'), ('sender-loading', 'SENDER / HALF STROKE'), ('receiver-ready', 'END RECEIVER / CATCH')]
        for index, (state, label) in enumerate(items):
            x, y = (index % 3) * 560, 65 + (index // 3) * 600
            draw.text((x + 20, y), label, fill=ink, font=small)
            with Image.open(managed / candidate / state / 'renders/isometric.png') as photo:
                board.paste(photo.convert('RGB'), (x, y + 30))
        board.save(managed / (candidate + '.png'))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--vintage-story', type=Path, required=True)
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args()
    managed = build_review(args.root)
    if args.render:
        render_review(args.root, args.vintage_story, managed)
    print('Pneumatic review ready: ' + REVIEW_ROOT.as_posix())


if __name__ == '__main__':
    main()
