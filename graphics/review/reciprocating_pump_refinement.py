"""Reproduce approved pump A and its synchronized contact review scenes."""

from __future__ import annotations

import argparse
from collections import OrderedDict
from dataclasses import replace
import math
from pathlib import Path
import shutil

from gearwright_graphics.compiler import build_package, json_bytes
from gearwright_graphics.model import Animation, Keyframe, ModelPackage, Vec3
from graphics.review.lateral_motion_system import _singleacting_pump_shape, _slider_state
from graphics.review.pipe_wood_supports import _pipe
from graphics.models.parts.reciprocating_pump_wet_end import (
    UPPER_PISTON_BOTTOM, STROKE, CAPACITY, CLEARANCE, FLOOR, rebuild_wet_end,
)

REVIEW_ROOT = Path('generated/pump-refinement-review')
DESCRIPTIONS = OrderedDict((
    ('a-clean-folded-pipes', 'Approved A: exposed folded copper pipes and oak cradle. The descending pipes sit 0.9 units inboard, leaving the full terminal collars exposed underneath.'),
))
STATES = ('rest', 'middle', 'contact', 'bottom', 'cutaway', 'pipe-fit', 'blocked-output')


def volume(phase: float) -> float:
    crosshead = _slider_state(phase, radius=3, rod_length=6)[2]
    return CLEARANCE + CAPACITY * (crosshead + 9) / STROKE


def contact_phase(amount=2.0) -> float:
    lo, hi = 0.0, 180.0
    for _ in range(55):
        mid = (lo + hi) / 2
        if volume(mid) > amount:
            lo = mid
        else:
            hi = mid
    return (lo + hi) / 2


def sample(frame: float, *, blocked=False):
    phase = contact_phase() * min(1, frame / 90) if blocked else frame
    pin_y, pin_z, crosshead, middle_y, middle_z, rod_angle = _slider_state(
        phase, radius=3, rod_length=6)
    amount = 2.0 if blocked else min(2.0, volume(phase))
    surface = FLOOR + STROKE * amount / CAPACITY
    stopped = blocked and frame >= 90
    pressure = 0 < phase < 180 and not stopped
    suction = 180 < phase < 360 and not stopped
    wet = volume(phase) < 2.0 - 1e-8
    return dict(phase=phase, amount=amount, surface=surface,
                piston=UPPER_PISTON_BOTTOM + crosshead + 3,
                crosshead=crosshead, rod_y=middle_y, rod_z=middle_z, rod_angle=rod_angle,
                intake=.36 if suction and wet else 0,
                output=-.36 if pressure and wet and not blocked else 0,
                air_intake=-.22 * abs(math.sin(math.radians(phase))) if pressure else 0,
                air_exhaust=.22 * abs(math.sin(math.radians(phase))) if suction else 0)


def _pose(values):
    return OrderedDict((
        ('gw-crank-phase', dict(rotationX=values['phase'], rotShortestDistanceX=False)),
        ('gw-piston-motion', dict(offsetY=values['crosshead'] + 3)),
        ('gw-connecting-rod-motion', dict(offsetY=values['rod_y'], offsetZ=values['rod_z'], rotationX=values['rod_angle'])),
        ('gw-wet-intake-check-motion', dict(offsetY=values['intake'])),
        ('gw-wet-output-check-motion', dict(offsetY=values['output'])),
        ('gw-breather-intake-check-motion', dict(offsetY=values['air_intake'])),
        ('gw-breather-exhaust-check-motion', dict(offsetY=values['air_exhaust'])),
        ('gw-review-water-surface', dict(offsetY=values['surface'] - FLOOR)),
    ))


def candidate_shape(candidate, state):
    if candidate not in DESCRIPTIONS or state not in STATES:
        raise ValueError('unknown pump review candidate or state')
    shape = _singleacting_pump_shape('a11-one-block-full-frame', label='pump-bottom')
    rebuild_wet_end(shape)
    shape.texture('gw-water', 'game:block/liquid/waterportion', size=(24, 24))
    # A thin surface is intentional: the reviewer cannot reproduce runtime
    # dynamic liquid quads. Static views include the complete water volume.
    shape.box('gw-review-water-surface', (-4.149, FLOOR - .004, -3.748),
              (4.149, FLOOR, 3.748), texture='#gw-water',
              faces=('up', 'down'), render_pass=2, parent=shape.ref('gw-pump-mount'),
              group='review-water', rotation_origin=(0, FLOOR, 0))
    shape.animations = [Animation('Two-litre supply: contact, discharge, refill', 'singleacting', 361,
        tuple(Keyframe(frame, _pose(sample(frame))) for frame in range(361)),
        1, 'Stop', 'Repeat'),
        Animation('Blocked outlet: approach water, stop at contact, hold', 'blocked-output', 181,
        tuple(Keyframe(frame, _pose(sample(frame, blocked=True))) for frame in range(181)),
        1, 'Stop', 'Hold')]
    if state == 'pipe-fit':
        _pipe(shape, 'review-input-', (-24, -24, -8), connections=('west', 'east'))
        _pipe(shape, 'review-output-', (8, -24, -8), connections=('west', 'east'))
    if state == 'cutaway':
        shape.elements = [replace(e, faces=OrderedDict((name, face) for name, face in e.faces.items()
                          if name != 'north')) if e.group == 'pump-wet-body' else e for e in shape.elements]
        shape.elements = [e for e in shape.elements if not e.name.startswith('gw-cylinder-front-')
                          and e.group != 'review-service-cover']
    if state in ('middle', 'contact', 'bottom'):
        phase = {'middle': 75, 'contact': contact_phase(), 'bottom': 180}[state]
        values = sample(phase)
        poses = _pose(values)
        # Bake ordinary offsets/rotations into the definition, never JSON.
        for i, e in enumerate(shape.elements):
            pose = poses.get(e.name)
            if pose is None:
                continue
            delta = Vec3(0, pose.get('offsetY', 0), pose.get('offsetZ', 0))
            shape.elements[i] = replace(e, from_=e.from_ + delta, to=e.to + delta,
                rotation_origin=e.rotation_origin + delta if e.rotation_origin else None,
                rotation=Vec3(pose.get('rotationX', e.rotation.x), e.rotation.y, e.rotation.z))
        shape.animations = []
        shape.box('gw-review-water-volume', (-4.149, FLOOR, -3.748),
                  (4.149, max(FLOOR + .001, values['surface'] - .005), 3.748), texture='#gw-water',
                  faces=('north', 'south'), render_pass=2, parent=shape.ref('gw-pump-mount'),
                  group='review-water')
    elif state == 'blocked-output':
        shape.animations = shape.animations[1:]
    return shape


def build_review(root: Path):
    root = root.resolve()
    review = (root / REVIEW_ROOT).resolve()
    managed, staging, backup = (review / name for name in ('current', '.current-build', '.current-old'))
    for path in (review, managed, staging, backup):
        path.resolve().relative_to(root)
    review.mkdir(parents=True, exist_ok=True)
    for path in (staging, backup):
        if path.exists():
            shutil.rmtree(path)
    package = ModelPackage('pump_refinement_review')
    for candidate in DESCRIPTIONS:
        for state in STATES:
            package.shape(candidate_shape(candidate, state),
                          f'{REVIEW_ROOT.as_posix()}/.current-build/{candidate}/{state}.shape.json')
    build_package(package, root, write=True)
    (staging / 'README.md').write_text('\n'.join([
        '# Approved pump A', '',
        *(f'- `{candidate}` - {description}' for candidate, description in DESCRIPTIONS.items()), '',
        'The approved model preserves the one-block pump, six-unit stroke, three-unit crank throw, six-unit connecting rod, glass chamber, roof air checks, side connection centers and low oak cradle.',
        f'The internal floor rises 0.495 model units to {FLOOR:.3f}. This gives 0.075 units of clearance at bottom dead center, matching 0.05 L of dead volume and 4 L of swept volume. The piston and journal geometry stay unchanged.',
        'Water height is floor + 1.5 model units per stored litre, clamped only at piston contact. A fixed amount stays at one height while the piston moves through air. No capacity or saved amount is reinterpreted.',
        f'The illustrative 2 L blocked case contacts at {contact_phase():.3f} degrees. Scrub blocked-output frames 0..90 to approach; frames 90..180 hold the piston, rod, journal and water together with all checks seated.',
        'The singleacting animation uses a two-litre limited supply. It holds the water level until contact, discharges with downward piston travel, and refills on the upstroke. Wet checks open only while transferring. Animated previews show the liquid surface; static middle/contact/bottom views also show the water sides.',
        'These animations are proposed presentation behavior, not recordings of the live hydraulic solver. Existing runtime regressions pass capped-line stops and open-line discharge, but the reported in-game stop still needs reproduction. Engine playback, rotated mounting and real multiplayer packets must be verified before completion.',
        'Existing pipe wall defects: independently built stub/drop walls overlap at the upper bend; lower runs intersect floor-riser walls; the inlet shadow pane projects outside its block. The replacement constructs a single exterior shell around connected empty passages and omits buried faces, including at bends.',
        'Material/load path: oak carries the vessel; small iron crank/rod/guide sections carry concentrated cyclic bending; bronze bearings and check seats take sliding wear; copper forms the stationary wet body. Wood cannot seal the wet chamber, and copper is too soft for the bearings and loaded crank pins.',
        'Inspected existing textures: game:block/metal/sheet/copper1, game:block/metal/sheet/tinbronze1, game:block/metal/sheet-plain/iron2, game:block/wood/debarked/oak, game:block/liquid/waterportion, gearwright:block/inspection-glass and gearwright:block/inspection-shadow. No new texture or game atlas is copied.',
        'Approved on 2026-09-11: A with slightly narrower pipes so the thick terminal collars remain exposed. Reference: generated/pump-refinement-review/current/a-clean-folded-pipes. Runtime copper geometry is owned by graphics/models/parts/reciprocating_pump_wet_end.py; this generator emits review files only.', '',
    ]), encoding='utf-8')
    (staging / '.gearwright-review.json').write_bytes(json_bytes({
        'version': 1, 'purpose': 'pump pipe clearance and synchronized water-contact review',
        'source': 'graphics/review/reciprocating_pump_refinement.py',
        'managedPath': f'{REVIEW_ROOT.as_posix()}/current',
        'candidates': list(DESCRIPTIONS), 'states': list(STATES),
        'animations': ['singleacting', 'blocked-output'],
        'decision': {'status': 'approved', 'candidate': 'a-clean-folded-pipes', 'runtimePromotion': True,
                     'reference': 'generated/pump-refinement-review/current/a-clean-folded-pipes',
                     'revision': 'Descending pipes 0.9 units inboard of the original sides, exposing the thick end collars.'},
        'contactDegreesForTwoLitres': contact_phase(),
    }))
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
        for state in ('middle', 'contact', 'bottom', 'cutaway', 'pipe-fit'):
            args = ['--vintage-story', str(game), '--mod', str(root), '--model', str(managed / candidate / f'{state}.shape.json'),
                    '--strict-textures', '--views', 'isometric,front,back,right,left,top,bottom',
                    '--light-direction', '.6,.7,-1',
                    '--size', '560x560', '--orthographic', '--orthographic-scale', '2.4' if state != 'pipe-fit' else '3.7',
                    '--camera-target', '0,-0.63,0', '--output', str(managed / candidate / f'fixed-render-{state}')]
            if state in ('cutaway', 'pipe-fit'):
                args += ['--animation', 'singleacting', '--frames', '75']
            run(build_parser().parse_args(args), root)
            print(f'Rendered {candidate}/{state}', flush=True)
    sheet = Image.new('RGB', (560, 1250), (20, 23, 28))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=24)
    small = ImageFont.load_default(size=19)
    for col, candidate in enumerate(DESCRIPTIONS):
        x = col * 560
        label = 'A - EXPOSED PIPE COLLARS'
        draw.text((x + 18, 16), label, font=font, fill=(236, 224, 205))
        for row, state in enumerate(('middle', 'contact')):
            with Image.open(managed / candidate / f'fixed-render-{state}' / 'isometric.png') as photo:
                sheet.paste(photo.convert('RGB'), (x, 60 + row * 590))
            draw.text((x + 18, 618 + row * 590), '2 L - piston above water' if row == 0 else '2 L - piston at water contact',
                      font=small, fill=(194, 202, 212))
    sheet.save(managed / 'comparison.png')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--vintage-story', type=Path)
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args()
    if args.render and args.vintage_story is None:
        parser.error('--render requires --vintage-story')
    managed = build_review(args.root)
    if args.render:
        render_review(args.root, args.vintage_story, managed)
    print(managed.relative_to(args.root.resolve()).as_posix())


if __name__ == '__main__':
    main()
