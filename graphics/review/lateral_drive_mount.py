"""Reproduce approved mount A with centered, shared journal seats."""
from __future__ import annotations

import argparse
from collections import OrderedDict
from dataclasses import replace
from pathlib import Path
import shutil

from gearwright_graphics.compiler import build_package, json_bytes
from gearwright_graphics.model import Animation, ElementRef, Keyframe, ModelPackage, Vec3
from graphics.models.parts.reciprocating_drive import centered_offset, rebuild_crank, rebuild_dry_adapter
from graphics.review.lateral_motion_system import _crank_state, _slider_state
from graphics.review.reciprocating_pump_refinement import candidate_shape as approved_pump

REVIEW_ROOT = Path('generated/lateral-drive-mount-review')
APPROVED = 'a-straight-bearing-lanes'
DESCRIPTIONS = OrderedDict(((APPROVED, 'Approved A: straight rods and guided crossheads. Seats regroup symmetrically around the journal center as devices are added or removed.'),))
MOUNTS = OrderedDict((('one-pump', (0,)), ('opposed-pumps', (0, 180)), ('adjacent-pumps', (0, 90)),
    ('three-pumps', (0, 180, 90)), ('four-pumps', (0, 180, 90, 270)), ('drive-detail', (0, 180, 90, 270))))
STATES = tuple(MOUNTS)
SEATS = tuple((angle, centered_offset(i, 4)) for i, angle in enumerate(MOUNTS['four-pumps']))

def seats_for(state):
    angles = MOUNTS[state]
    return tuple((angle, centered_offset(i, len(angles))) for i, angle in enumerate(angles))

def candidate_shape(kind, state, *, through=True):
    if kind not in DESCRIPTIONS or state not in STATES:
        raise ValueError('unknown mounting review candidate or state')
    shape = _crank_state(through=through, throw=3)
    rebuild_crank(shape, through=through)
    seats = seats_for(state)
    for angle, lane in seats:
        source = approved_pump('a-clean-folded-pipes', 'rest')
        rebuild_dry_adapter(source, lane=lane)
        prefix = f'device-{angle}-'
        for e in source.elements:
            if e.name.startswith('gw-crank-') or e.group == 'review-water':
                continue
            if angle != 0 and (e.group or '').startswith('pump-downward-stand'):
                continue
            if state == 'drive-detail' and not (e.pivot or e.name.startswith(('gw-connecting-rod-', 'gw-crosshead-'))):
                continue
            if e.name == 'gw-pump-mount':
                e = replace(e, rotation=Vec3(angle, 0, 0))
            shape._element(prefix + e.name, e.from_.values(), e.to.values(), faces=e.faces,
                rotation_origin=e.rotation_origin.values() if e.rotation_origin else None,
                rotation=e.rotation.values(), shade=e.shade, render_pass=e.render_pass,
                group=prefix + (e.group or 'body'), pivot=e.pivot,
                parent=ElementRef(shape.id, prefix + e.parent) if e.parent else None)
    keyframes = []
    for phase in range(361):
        poses = OrderedDict((('gw-crank-phase', {'rotationX': phase, 'rotShortestDistanceX': False}),))
        for angle, lane in seats:
            _, _, crosshead, middle_y, middle_z, rod_angle = _slider_state(phase - angle, radius=3, rod_length=6)
            prefix = f'device-{angle}-'
            poses[prefix + 'gw-piston-motion'] = {'offsetY': crosshead + 3}
            poses[prefix + 'gw-connecting-rod-motion'] = {'offsetY': middle_y, 'offsetZ': middle_z, 'rotationX': rod_angle}
        keyframes.append(Keyframe(phase, poses))
    shape.animations = [Animation('Centered shared journal', 'mounting', 361, tuple(keyframes), 1, 'Stop', 'Repeat')]
    return shape

def build_review(root):
    root = Path(root).resolve()
    review = (root / REVIEW_ROOT).resolve()
    managed, staging, backup = (review / name for name in ('current', '.current-build', '.current-old'))
    for path in (review, managed, staging, backup):
        path.relative_to(root)
    review.mkdir(parents=True, exist_ok=True)
    for path in (staging, backup):
        if path.exists():
            shutil.rmtree(path)
    package = ModelPackage('lateral_drive_mount_review')
    for candidate in DESCRIPTIONS:
        for state in STATES:
            package.shape(candidate_shape(candidate, state), f'{REVIEW_ROOT.as_posix()}/.current-build/{candidate}/{state}.shape.json')
    build_package(package, root, write=True)
    (staging / '.gearwright-review.json').write_bytes(json_bytes({
        'version': 1, 'source': 'graphics/review/lateral_drive_mount.py',
        'purpose': 'Approved centered radial-device attachment seats and axle clearance',
        'managedPath': f'{REVIEW_ROOT.as_posix()}/current', 'candidates': list(DESCRIPTIONS),
        'states': list(STATES), 'animations': ['mounting'],
        'decision': {'status': 'approved', 'runtimePromotion': True, 'candidate': APPROVED,
            'reference': f'{REVIEW_ROOT.as_posix()}/current/{APPROVED}',
            'revision': 'Center the attached rods as closely as their bearing clearance permits.'},
    }))
    (staging / 'README.md').write_text('\n'.join([
        '# Approved centered reciprocating drive', '', DESCRIPTIONS[APPROVED], '',
        'One device sits at zero. Two use -0.7/+0.7 model units; three use -1.4/0/+1.4; four use -2.1/-0.7/+0.7/+2.1. Canonical physical face order determines seats. Reversing pipe direction mirrors the local offset without moving the physical attachment. Regrouping occurs only when attachment topology changes; the server publishes every seat position with shaft motion and fluid state.',
        'The three-unit throw, six-unit connecting rod and six-unit stroke remain unchanged. The wet end remains approved pump A. A small saddle below the crosshead lets a centered rod pass above the piston stem. Single and through shaft models use the same journal envelope.',
        'Runtime geometry: graphics/models/parts/reciprocating_drive.py. Runtime interface: IReciprocatingDriveDevice. The drive sums each device load once; the mechanical integrator steps all devices from one shaft angle. Stored orientations, amounts, phases and asset identifiers remain compatible. Seats are derived and never saved.',
        'Load path: oak carries the low-speed axle, compact iron webs and rods carry cyclic bending, and bronze pins carry bearing wear. Wood cannot replace these small loaded sections; copper is too soft for repeated bearing contact. The next higher metal tier is unnecessary.',
        'Existing inspected textures: game:block/wood/debarked/oak, game:block/metal/sheet-plain/iron2, game:block/metal/sheet/tinbronze1, game:block/metal/sheet/copper1, gearwright:block/inspection-glass and gearwright:block/inspection-shadow.',
        'The review shows geometry and kinematics with seated wet checks. Runtime hydraulic tests cover flow, blocked outputs and multiplayer playback.', '',
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
    candidate = APPROVED
    for state in STATES:
        args = ['--vintage-story', str(game), '--mod', str(root), '--model', str(managed / candidate / f'{state}.shape.json'),
                '--strict-textures', '--views', 'isometric,front,back,right,left,top,bottom',
                '--light-direction', '.6,.7,-1', '--size', '620x620', '--orthographic',
                '--orthographic-scale', '2' if state == 'drive-detail' else '4.6',
                '--camera-target', '0,0,0', '--animation', 'mounting',
                '--frames', '0,45,90,180,270' if state == 'drive-detail' else '45',
                '--output', str(managed / candidate / f'render-{state}')]
        run(build_parser().parse_args(args), root)
        print(f'Rendered {candidate}/{state}', flush=True)
    sheet = Image.new('RGB', (1240, 1350), (20, 23, 28))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=23)
    for i, (state, label) in enumerate((('one-pump', 'ONE: CENTERED'), ('opposed-pumps', 'TWO: +/-0.7'),
                                      ('three-pumps', 'THREE: -1.4 / 0 / +1.4'), ('four-pumps', 'FOUR: +/-0.7 AND +/-2.1'))):
        x, y = i % 2 * 620, i // 2 * 675
        draw.text((x + 18, y + 15), label, font=font, fill=(236, 224, 205))
        with Image.open(managed / candidate / f'render-{state}/isometric-mounting-45.png') as photo:
            sheet.paste(photo.convert('RGB'), (x, y + 50))
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
