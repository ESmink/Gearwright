"""Covered Jonas request-printer study; never writes runtime assets."""
from __future__ import annotations

import argparse
from collections import OrderedDict
import math
from pathlib import Path
import shutil

import numpy as np

from gearwright_graphics.assets import AssetResolver, permissive_json
from gearwright_graphics.compiler import build_package, compile_shape, json_bytes
from gearwright_graphics.geometry import triangles
from gearwright_graphics.model import ModelPackage
from graphics.models.pneumatic_transport import hardware
from graphics.models.pneumatic_smart_receiver import APPROVAL
from graphics.review.pneumatic_stock_printer import CANDIDATE, request_printer

REVIEW_ROOT = Path('generated/pneumatic-smart-receiver-review/current')
DESCRIPTIONS = OrderedDict(((CANDIDATE, 'D3 | Jonas request printer'),))
STATES = ('assembly', 'mechanism', 'controls', 'record', 'print-cutaway', 'installed')
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
REFERENCES = OrderedDict((
    ('oscillator', 'game:item/jonas/frames/oscillator01'),
    ('crafted-gearbox', 'game:item/jonas/frames/gearbox01'),
    ('eccentric-gearbox', 'game:item/jonas/frames/gearbox02'),
    ('difference-gears', 'game:item/jonas/frames/gears01'),
    ('planetary-gears', 'game:item/jonas/frames/gears02'),
    ('refraction-cylinder', 'game:item/jonas/parts/cylinder01'),
    ('parallel-vessel', 'game:item/jonas/parts/tank01'),
    ('return-base', 'game:block/machine/jonas/returnbasedeployed'),
    ('archive-generator', 'game:block/machine/jonas/gencore'),
    ('archive-pump', 'game:block/machine/jonas/pumphead1-complete'),
    ('archive-beam', 'game:block/machine/jonas/steamengine/newcbeam'),
    ('rift-ward', 'game:block/machine/riftward'),
    ('tuning-cylinder', 'game:block/machine/resonator-cylinder'),
    ('resonator', 'game:block/machine/resonator'),
    ('archive-resonator', 'game:entity/nonliving/libraryresonator'),
    ('wound-spring', 'game:item/jonas/frames/spring01'),
    ('dual-valence-emitter', 'game:item/jonas/parts/cylinder02'),
    ('vacuum-chamber', 'game:item/jonas/parts/tank02'),
))
BRIEFS = {
    CANDIDATE: {
        'tests': 'Develop the chosen scroll direction: quarter-turn orientation, one narrow paper strip, and a covered printer over the first quarter of the cassette.',
        'parts': 'Two short Z-axis spools, continuous paper bed, transverse print carriage, reciprocating stamp and an exposed feed wheel.',
        'loadPath': 'The existing D saddles carry a shallow tray. Four short bearing feet support the spools, while guide posts carry the print carriage on the hood.',
        'wear': 'Brass paper guides and print foot, steel journals and sliding guide rods. Ebony flanges support the light paper rolls.',
        'tier': 'Cupronickel cheeks and a protected teal pickup identify the salvaged Jonas printer. Iron remains in the narrow saddles and steel in the compact wear contacts.',
    },
}
RESEARCH = (
    ('Notched tally records', 'https://collection.sciencemuseumgroup.org.uk/objects/co60506/tally-sticks-medieval-exchequer-tallies',
     'Persistent notches distinguish a stored tally from an undifferentiated rotating shaft.'),
    ('Pianola rolls', 'https://collection.sciencemuseumgroup.org.uk/objects/co8034538',
     'Perforations arranged along a roll carry instructions; borrow the exposed reading bed and flanged reels.'),
    ('Manual counting aids', 'https://www.si.edu/spotlight/the-abacus-the-numeral-frame-and-counters/introduction',
     'Search summary inspected; full page unavailable. Separate lanes and physical markers make quantities legible.'),
    ('Mechanical counters', 'https://americanhistory.si.edu/collections/object-groups/counters',
     'Search summary inspected; full page unavailable. A register separates its stored reading from the input motion.'),
    ('Native tuning cylinder', 'https://wiki.vintagestory.at/Tuning_cylinder',
     'Installed model inspected: charcoal body, ebony end plates, distinct end-cap colour and axial socket.'),
)


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


def candidate_shape(candidate, state='assembly'):
    if candidate not in DESCRIPTIONS or state not in STATES:
        raise ValueError('Unknown Smart Receiver candidate or state')
    shape = hardware('receiver', reinforced=False)
    for key, (location, size) in TEXTURES.items():
        shape.texture(key, location, size=size)
    c = Controls(shape)
    request_printer(c, tube_saddle)
    if state == 'mechanism':
        shape.elements = [e for e in shape.elements if e.render_pass != 1]
    elif state in ('controls', 'record', 'print-cutaway'):
        shape.elements = [e for e in shape.elements if e.group == 'stock-controls'
                          and (state == 'controls' or '-saddle-' not in e.name)
                          and (state != 'print-cutaway' or not e.name.startswith('stock-hood-'))]
        shape._refs = {name: ref for name, ref in shape._refs.items() if name.startswith('stock-')}
        shape.animations = [a for a in shape.animations if a.code == 'stock-check']
    elif state == 'installed':
        for name, lo, hi in (
            ('floor', (.3, -15.7, .3), (15.7, -15., 15.7)),
            ('front', (.3, -15., .3), (15.7, -3.5, 1.1)),
            ('back', (.3, -15., 14.9), (15.7, -3.5, 15.7)),
            ('left', (.3, -15., 1.1), (1.1, -3.5, 14.9)),
            ('right', (14.9, -15., 1.1), (15.7, -3.5, 14.9)),
            ('lid', (.3, -3.5, .3), (15.7, -2.45, 15.7)),
        ):
            shape.box('context-chest-' + name, lo, hi, texture='#oak', group='context')
    return shape


def build_review(root, game=None, *, render=False, quick=False, study=False):
    root = Path(root).resolve()
    managed = (root / REVIEW_ROOT).resolve()
    staging, backup = managed.with_name('.current-build'), managed.with_name('.current-old')
    for path in (managed, staging, backup):
        path.relative_to((root / 'generated').resolve())
    for path in (staging, backup):
        if path.exists():
            shutil.rmtree(path)
    package = ModelPackage('pneumatic_smart_receiver_review')
    for candidate in DESCRIPTIONS:
        for state in STATES:
            shape = candidate_shape(candidate, state)
            compile_shape(shape)
            package.shape(shape, f'{staging.relative_to(root).as_posix()}/{candidate}/{state}.shape.json')
    build_package(package, root, write=True)
    (staging / '.gearwright-review.json').write_bytes(json_bytes({
        'version': 1, 'source': 'graphics/review/pneumatic_smart_receiver.py',
        'geometrySource': 'graphics/review/pneumatic_stock_printer.py',
        'managedPath': REVIEW_ROOT.as_posix(), 'candidates': list(DESCRIPTIONS),
        'states': list(STATES), 'animations': ['receive', 'stock-check'],
        'decision': {'status': 'approved', 'runtimePromotion': True, 'approval': APPROVAL,
                     'retiredCandidates': ['a-roof-cassette', 'b-rear-tower', 'c-split-instruments',
                                           'd-differential-cradle', 'e-recessed-movement',
                                           'd1-tally-cylinder', 'd2-scroll-cassette'],
                     'revision': '2026-10-06: develop the scroll cassette into a narrow printer, turned 90 degrees, with covered writing and visible print/feed motion.'},
        'specification': 'PNEUMATIC-TUBES-DESIGN.md sections 3.6 and 8.2',
        'behavior': {'rows': 4, 'shortage': 'max(0, target - stored - reserved_not_dispatched - in_transit)',
                     'unsolicitedCargo': 'accept anything the attached inventory accepts',
                     'exportExcess': False, 'separateSignalWiring': False},
        'briefs': BRIEFS, 'styleReferences': dict(REFERENCES),
        'research': [{'title': title, 'url': url, 'application': note} for title, url, note in RESEARCH],
        'sources': [location for location, _ in TEXTURES.values()],
        'ports': {'input': 'west', 'output': 'east', 'inventory': 'down',
                  'hardwareBasis': 'approved pneumatic receiver with fitted inventory sleeve'},
        'limits': ['Frozen approval reference; runtime promotion includes the requested outward feed and thicker lid linkage.',
                   'Four request rows remain in the GUI; the world model prints one narrow record under its hood.',
                   'The short print cycle belongs at the start of gathering a request, not at every received item.',
                   'Review stamps are small item-like marks hidden under the cover, not live item names or quantities.',
                   'The scroll is visual only; no replaceable consumable or new inventory interaction is proposed.',
                   'Engine lighting, glass and authoritative playback remain integration checks.'],
    }))
    notes = ['# Smart Receiver / Jonas request printer', '',
        'On 2026-10-06 the maintainer chose the scroll direction for further development: '
        'turn it 90 degrees, narrow the paper, and cover the printing end with a housing '
        'and moving parts so the writing is mostly hidden. D3 is the resulting revision; '
        'it is approved for in-game use. The runtime model adds the requested thicker lid '
        'linkage, outward feed and tangent paper seams. This package preserves the approved reference.', '',
        '## Behavior', '',
        'Follow PNEUMATIC-TUBES-DESIGN.md sections 3.6 and 8.2: four sample/target rows, '
        'shortage accounting that counts stored, reserved and incoming items once, normal '
        'receiver insertion and no automatic export of surplus. The four rows belong in '
        'the menu. The world model does not need a keypad or pretend numeric readouts.', '',
        '## Jonas construction language', '',
        'The native tuning cylinder contributes its dark faceted body, broad ebony end '
        'plates and distinct rim. The oscillator and gearboxes contribute stepped '
        'cupronickel cheeks and small exposed contacts. The resonator contributes '
        'the relationship between a record, a reader and a protective housing. '
        'The return base and emitter keep teal confined to a protected pickup.', '',
        'One 2.8-unit paper strip runs along X, with both spool shafts along Z. '
        'The stepped printer hood covers one end of the '
        '11.2-unit cassette. In this approval reference, its visor hides the fresh record as it moves onto the '
        'covered take-up spool. Exact stock values stay in the four-row GUI contract.', '',
        '## Start-of-request motion', '',
        'The carriage traverses 1.95 model units on two top guides and stamps four '
        'positions with a 0.42-unit plunger stroke. Printing finishes before the paper '
        'advances 1.1 units; the spools and feed wheel turn by the corresponding '
        '82.93 degrees. The carriage then parks. This is a four-second illustrative '
        'cycle at 30 frames per second, triggered once when gathering a new request.', '',
        'The continuous blank paper bed is stationary geometry along its fixed path. '
        'Its printed marks move with the rotating spools, giving feed motion without '
        'stretching the paper or detaching it from either roll. The sample stamps '
        'only demonstrate the covered print effect. No paper consumable is added.', '',
        '## Research and interpretation', '',
        *[f'- [{title}]({url}): {note}' for title, url, note in RESEARCH], '',
        'The model is a fictional synthesis of these references, not a historical counting '
        'machine reconstruction. The native asset study includes the tuning cylinder, '
        'resonator, archive resonator, oscillator, gearboxes, vessels and larger Jonas machinery.', '',
        '## Candidates', '']
    for candidate, title in DESCRIPTIONS.items():
        notes += [f'- `{candidate}` - {BRIEFS[candidate]["tests"]}', '', title, '',
                  *[f'{label}: {BRIEFS[candidate][key]}' for label, key in
                    (('Parts', 'parts'), ('Load path', 'loadPath'), ('Wear', 'wear'), ('Material choice', 'tier'))], '']
    notes += ['## Review', '',
        'Assembly provides front and rear cameras. Controls '
        'isolates the mechanism and saddles; record isolates the cassette above the saddles. '
        'Print-cutaway removes the hood only for inspection. Mechanism removes glass; '
        'installed shows chest fit. Receive keeps the approved unloading cycle. '
        'Stock-check demonstrates printing, paper advance and carriage return.', '',
        'D3 is approved and promoted. These diagnostic renders do not verify engine lighting. '
        'The maintainer will review the installed runtime model in-game.', '']
    (staging / 'README.md').write_text('\n'.join(notes), encoding='utf-8')
    if study:
        study_references(root, game, staging)
    if render or quick:
        render_review(root, game, staging, quick=quick)
    for manifest in staging.rglob('manifest.json'):
        manifest.write_text(manifest.read_text(encoding='utf-8').replace(
            'pneumatic-smart-receiver-review/.current-build/', 'pneumatic-smart-receiver-review/current/'), encoding='utf-8')
    if managed.exists():
        managed.replace(backup)
    staging.replace(managed)
    if backup.exists():
        shutil.rmtree(backup)
    return managed


def study_references(root, game, managed):
    from gearwright_graphics.photoshoot import build_parser, run
    from PIL import Image, ImageDraw, ImageFont
    from io import BytesIO
    resolver = AssetResolver((game, root))
    for name, logical in REFERENCES.items():
        document = permissive_json(resolver.shape(logical).read())
        points = np.concatenate([t.vertices for t in triangles(document)])
        low, high = points.min(axis=0), points.max(axis=0)
        target = (low + high) / 2
        scale = max(.4, float(np.linalg.norm(high - low)) * 1.08)
        args = ['--vintage-story', str(game), '--mod', str(root), '--model', logical,
                '--strict-textures', '--views', 'isometric,front,top', '--size', '480x480',
                '--orthographic', '--orthographic-scale', str(scale), '--lighting', 'studio',
                '--light-direction=.6,.8,-1',
                '--camera-target=' + ','.join(str(float(v)) for v in target),
                '--output', str(managed / 'study' / name)]
        for key, location in document.get('textures', {}).items():
            if not location.startswith('#'):
                args += ['--texture', key + '=' + (location if ':' in location else 'game:' + location)]
        run(build_parser().parse_args(args), root)
    board = Image.new('RGB', (1920, math.ceil(len(REFERENCES) / 4) * 550), (20, 23, 28))
    draw = ImageDraw.Draw(board)
    font = ImageFont.load_default(size=21)
    for n, name in enumerate(REFERENCES):
        x, y = n % 4 * 480, n // 4 * 550
        draw.text((x + 16, y + 15), name.upper(), fill=(236, 227, 210), font=font)
        with Image.open(managed / 'study' / name / 'isometric.png') as photo:
            board.paste(photo.convert('RGB'), (x, y + 50))
    board.save(managed / 'jonas-study.png')
    palette = Image.new('RGB', (320 * len(TEXTURES), 390), (20, 23, 28))
    draw = ImageDraw.Draw(palette)
    for i, (key, (logical, _)) in enumerate(TEXTURES.items()):
        with Image.open(BytesIO(resolver.texture(logical).read())) as material:
            palette.paste(material.convert('RGB').resize((288, 288), Image.Resampling.NEAREST), (i * 320 + 16, 70))
        draw.text((i * 320 + 16, 20), key, fill=(236, 227, 210), font=font)
    palette.save(managed / 'materials.png')
    print('Jonas source study rendered.', flush=True)


def render_review(root, game, managed, *, quick=False):
    from gearwright_graphics.photoshoot import build_parser, run
    from PIL import Image, ImageDraw, ImageFont
    candidate = CANDIDATE
    for state in (('assembly', 'record', 'print-cutaway') if quick else STATES):
        installed = state == 'installed'
        close = state in ('record', 'print-cutaway')
        args = ['--vintage-story', str(game), '--mod', str(root),
                '--model', str(managed / candidate / (state + '.shape.json')),
                '--strict-textures', '--views', 'isometric' if quick else 'isometric,front,left,right,top,bottom',
                '--lighting', 'studio', '--light-direction', '.6,.8,-1', '--size', '800x800',
                '--orthographic', '--orthographic-scale', '2.45' if installed else ('1.05' if close else '1.65'),
                '--camera-target=' + ('.5,-.02,.5' if installed else ('.5,.87,.49' if close else '.5,.42,.5')),
                '--output', str(managed / candidate / state / 'renders')]
        run(build_parser().parse_args(args), root)
        rear_args = list(args)
        rear_args[rear_args.index('--views') + 1] = 'back'
        rear_args[rear_args.index('--light-direction') + 1] = '.6,.8,1'
        run(build_parser().parse_args(rear_args), root)
        rear_args += ['--camera-position', '2.8,2.0,3.2']
        rear_args[rear_args.index('--output') + 1] = str(managed / candidate / state / 'rear-oblique')
        run(build_parser().parse_args(rear_args), root)
        if not quick and state in ('assembly', 'record', 'print-cutaway'):
            args[args.index('--views') + 1] = 'isometric'
            if state == 'assembly':
                code, frames = 'receive', '0,150,285,360'
            else:
                code = 'stock-check'
                frames = ','.join(str(f) for f in range(0, 121, 6)) if state == 'record' else '0,18,54,100,120'
            run(build_parser().parse_args(args + ['--animation', code, '--frames', frames]), root)
        print('Rendered ' + state, flush=True)
    bg, ink, accent = (20, 23, 28), (236, 227, 210), (134, 203, 185)
    font, small = ImageFont.load_default(size=29), ImageFont.load_default(size=21)
    for filename, heading, panels in (
        ('comparison.png', 'D3 / JONAS REQUEST PRINTER', (
            ('DRIVE SIDE', 'assembly/renders/isometric.png'),
            ('REAR SIDE', 'assembly/rear-oblique/back.png'))),
        ('record-comparison.png', 'NARROW SCROLL / COVERED PRINT HEAD', (
            ('COVERED PRINTER', 'record/renders/isometric.png'),
            ('INSIDE THE HOUSING', 'print-cutaway/renders/' +
             ('isometric.png' if quick else 'isometric-stock-check-54.png')))),
    ):
        board = Image.new('RGB', (1600, 1000), bg)
        draw = ImageDraw.Draw(board)
        draw.text((26, 20), heading, fill=ink, font=font)
        for n, (label, path) in enumerate(panels):
            x = n * 800
            draw.text((x + 26, 82), label, fill=accent, font=small)
            with Image.open(managed / candidate / path) as photo:
                board.paste(photo.convert('RGB'), (x, 120))
        draw.text((26, 951), 'Paper feeds along the tube. The short start-of-request cycle prints, advances, then parks.', fill=ink, font=small)
        board.save(managed / filename)
    if not quick:
        frames = []
        for frame in range(0, 121, 6):
            with Image.open(managed / candidate / 'record/renders' / f'isometric-stock-check-{frame}.png') as photo:
                frames.append(photo.convert('RGB').resize((640, 640), Image.Resampling.NEAREST))
        frames[0].save(managed / 'request-print.gif', save_all=True, append_images=frames[1:],
                       duration=[200] * 20 + [1000], loop=0, disposal=2)
        board = Image.new('RGB', (1600, 510), bg)
        draw = ImageDraw.Draw(board)
        for n, (frame, label) in enumerate(((0, 'READY'), (18, 'PRINT'), (54, 'REQUEST RECORDED'), (100, 'FEED UNDER COVER'))):
            draw.text((n * 400 + 18, 18), label, fill=accent, font=small)
            with Image.open(managed / candidate / 'print-cutaway/renders' / f'isometric-stock-check-{frame}.png') as photo:
                board.paste(photo.convert('RGB').resize((400, 400), Image.Resampling.NEAREST), (n * 400, 68))
        board.save(managed / 'print-cycle.png')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--vintage-story', type=Path, required=True)
    parser.add_argument('--render', action='store_true')
    parser.add_argument('--quick', action='store_true')
    parser.add_argument('--study', action='store_true')
    args = parser.parse_args()
    build_review(args.root, args.vintage_story, render=args.render, quick=args.quick, study=args.study)
    print('Smart Receiver review ready: ' + REVIEW_ROOT.as_posix())


if __name__ == '__main__':
    main()
