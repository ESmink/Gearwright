"""Reproduce the approved upper frame and check it against the moving bellows."""
from __future__ import annotations

import argparse
from collections import OrderedDict
from pathlib import Path
import shutil

import numpy as np

from gearwright_graphics.compiler import build_package, compile_shape, json_bytes
from gearwright_graphics.model import ModelPackage
from graphics.models.parts.large_bellows_frame import FRAME_GROUP
from graphics.review.large_bellows import read_source
from graphics.review.large_bellows_top import attachment_scene
from graphics.review.lateral_drive_clearance import intersects, posed_boxes

REVIEW_ROOT = Path('generated/large-bellows-frame-review/current')
DESCRIPTIONS = OrderedDict((
    ('a-braced-uprights', 'Approved A with horizontal oak rails joining the front and rear posts at the top of the regular base.'),
))
STATES = ('rest',)


def frame_shape(document, candidate):
    if candidate not in DESCRIPTIONS:
        raise ValueError('Unknown upper frame candidate')
    return attachment_scene(document, top=True)


def audit(shape):
    compiled = compile_shape(shape)
    visible = {e.name for e in shape.elements if any(f.enabled for f in e.faces.values())}
    frame = {e.name for e in shape.elements if e.group == FRAME_GROUP}
    collisions = {}
    wood = [box for box in posed_boxes(compiled, 0) if box[0] in frame]
    for index, first in enumerate(wood):
        for second in wood[index + 1:]:
            # The original bearing seats lap onto the posts. New base rails
            # must meet the existing frame without introducing intersections.
            if '-base-top-rail-' in first[0] or '-base-top-rail-' in second[0]:
                if intersects(first, second): collisions[(first[0], second[0])] = 0
    for phase in range(0, 361, 5):
        boxes = [box for box in posed_boxes(compiled, phase) if box[0] in visible]
        for wood in (box for box in boxes if box[0] in frame):
            for other in (box for box in boxes if box[0] not in frame):
                # The bearing shaft deliberately enters its oak seats.
                if other[0].startswith('gw-bellows-rocker-pin-fulcrum'): continue
                if np.all(np.minimum(wood[6], other[6]) - np.maximum(wood[5], other[5]) > 1e-8) and intersects(wood, other):
                    collisions.setdefault((wood[0], other[0]), phase)
    return [{'first': a, 'second': b, 'phase': phase} for (a, b), phase in collisions.items()]


def build_review(root, document):
    root = Path(root).resolve()
    managed = (root / REVIEW_ROOT).resolve()
    staging = managed.with_name('.current-build')
    backup = managed.with_name('.current-old')
    for path in (managed, staging, backup): path.relative_to(root / 'generated')
    for path in (staging, backup):
        if path.exists(): shutil.rmtree(path)
    package = ModelPackage('large_bellows_frame_review')
    reports = {}
    for candidate in DESCRIPTIONS:
        for state in STATES:
            shape = frame_shape(document, candidate)
            package.shape(shape, f'{staging.relative_to(root).as_posix()}/{candidate}/{state}.shape.json')
            key = candidate + '/' + state
            reports[key] = {'sampleDegrees': 5, 'collisions': audit(shape)}
            print(key + ': ' + str(len(reports[key]['collisions'])) + ' intersecting pairs', flush=True)
    build_package(package, root, write=True)
    (staging / 'clearance.json').write_bytes(json_bytes(reports))
    (staging / '.gearwright-review.json').write_bytes(json_bytes({
        'version': 1, 'source': 'graphics/review/large_bellows_frame.py',
        'managedPath': REVIEW_ROOT.as_posix(), 'purpose': 'Extend the native bellows base into the upper drive support',
        'candidates': list(DESCRIPTIONS), 'states': list(STATES), 'animations': ['shaft-bellows'],
        'decision': {'status': 'approved', 'runtimePromotion': True,
                     'approvedCandidate': 'a-braced-uprights', 'approvedState': 'rest',
                     'approvedReference': (REVIEW_ROOT / 'a-braced-uprights/rest.shape.json').as_posix(),
                     'approval': 'Maintainer approved original A with base-top rails and requested lower intake motion during top drive.',
                     'pendingCandidates': []},
    }))
    (staging / 'README.md').write_text('\n'.join([
        '# Upper bellows frame', '', *[f'- {description}' for description in DESCRIPTIONS.values()], '',
        'A uses the existing stand footprint, two-unit oak plank width, half-unit thickness, '
        'grain direction and diagonal side bracing. Separate feet and the detached gantry are removed. '
        'The upper board, shaft position and linkage motion are unchanged.', '',
        'Rest restores the original A and adds two horizontal side rails between the native front '
        'and rear posts, flush with the top of the base. These rails give the upper extension a '
        'clear junction with the original stand. The original brace height, bearing seats and '
        'top crosspiece are retained. Rejected revisions are removed.', '',
        'Load path: the rocker bearing seats transfer force into the extended base posts and diagonal '
        'oak planks. Wood carries this low-speed air load; diagonal bracing limits sway. Existing bronze '
        'pins remain the wear surfaces. A thinner unbraced extension would be too flexible.', '',
        'Approved frame promoted to graphics/models/parts/large_bellows_frame.py. '
        'The lower chamber now breathes during top drive. The original game body is read from '
        'the selected installation and is never copied into tracked files.', '',
    ]), encoding='utf-8')
    if managed.exists(): managed.replace(backup)
    staging.replace(managed)
    if backup.exists(): shutil.rmtree(backup)
    if any(report['collisions'] for report in reports.values()):
        raise ValueError('Frame clearance failed; inspect the managed clearance.json')
    return managed


def render_review(root, game, managed):
    from gearwright_graphics.photoshoot import build_parser, run
    from PIL import Image, ImageDraw, ImageFont
    candidate = next(iter(DESCRIPTIONS))
    for state in STATES:
        args = ['--vintage-story', str(game), '--mod', str(root),
            '--model', str(managed / candidate / (state + '.shape.json')),
            '--strict-textures', '--views', 'front-right,front,back,right,left,top,bottom',
            '--lighting', 'flat', '--light-direction', '.6,.7,-1', '--size', '800x720',
            '--orthographic', '--orthographic-scale', '3.2', '--camera-target', '.95,1.02,.5',
            '--animation', 'shaft-bellows', '--frames', '0,90,180,270',
            '--output', str(managed / candidate / state / 'renders')]
        run(build_parser().parse_args(args), root)
        print('Rendered ' + state, flush=True)
    sheet = Image.new('RGB', (1600, 768), (20, 23, 28))
    draw = ImageDraw.Draw(sheet)
    for column, phase in enumerate((0, 180)):
        x = column * 800
        draw.text((x + 20, 12), f'APPROVED A | PHASE {phase}',
                  font=ImageFont.load_default(size=25), fill=(235, 224, 205))
        with Image.open(managed / candidate / 'rest' / f'renders/front-shaft-bellows-{phase}.png') as photo:
            sheet.paste(photo.convert('RGB'), (x, 48))
    sheet.save(managed / 'approved.png')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--vintage-story', type=Path, required=True)
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args()
    managed = build_review(args.root, read_source(args.vintage_story))
    if args.render: render_review(args.root, args.vintage_story, managed)


if __name__ == '__main__':
    main()
