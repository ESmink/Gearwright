"""Review-only opaque inventory fittings and receiver clearance revision."""
from __future__ import annotations

import argparse
from collections import OrderedDict
import json
from pathlib import Path
import shutil

import numpy as np
from gearwright_graphics.compiler import build_package, compile_shape, json_bytes
from gearwright_graphics.model import ModelPackage, Face, UVRect
from gearwright_graphics.assets import AssetResolver, permissive_json
from gearwright_graphics.geometry import triangles
from gearwright_graphics.animation import apply_animation
from graphics.models.pneumatic_transport import base_hardware, inventory_ports, repair_surfaces, PORT_APPROVALS, EXTENSION
from graphics.review.pneumatic_sender import candidate_shape as sender
from graphics.review.pneumatic_receiver import candidate_shape as receiver
from graphics.review.lateral_drive_clearance import posed_boxes, intersects

REVIEW_ROOT = Path('generated/pneumatic-inventory-ports-review/current')
DESCRIPTIONS = OrderedDict((
    ('a-brass-sleeve', 'A | Brass sleeve with a recessed dark bore'),
))
STATES = ('sender', 'receiver', 'terminal', 'sender-terminal', 'receiver-terminal', 'elbow-terminal')


def shape_for(candidate, state, *, repaired=True):
    if candidate not in DESCRIPTIONS or state not in STATES:
        raise ValueError('Unknown inventory fitting candidate/state')
    is_sender, is_receiver = state.startswith('sender'), state.startswith('receiver')
    shape = sender('a-rack-lift') if is_sender else receiver('a1-return-cam') if is_receiver else base_hardware('elbow' if state == 'elbow-terminal' else 'straight')
    shape.id = candidate + '-' + state
    inventory_ports(shape, state)
    return repair_surfaces(shape, state) if repaired else shape


def moving_fixed_audit(shape, step=3):
    compiled = compile_shape(shape)
    moving_targets = set(compiled['animations'][0]['keyframes'][0]['elements'])
    moving_names = set()
    def visit(elements, moving=False):
        for e in elements:
            active = moving or e['name'] in moving_targets
            if active:
                moving_names.add(e['name'])
            visit(e.get('children', []), active)
    visit(compiled['elements'])
    collisions = {}
    for frame in range(0, 361, step):
        boxes = posed_boxes(compiled, frame)
        occlusion = {e.name for e in shape.elements if e.group == 'occlusion'}
        fixed = [b for b in boxes if b[0] not in moving_names and b[0] not in occlusion]
        moving = [b for b in boxes if b[0] in moving_names and not b[0].startswith('cargo-')]
        lows, highs = np.array([b[5] for b in fixed]), np.array([b[6] for b in fixed])
        for a in moving:
            possible = np.where(np.all(np.minimum(highs, a[6]) > np.maximum(lows, a[5]) + 1e-8, axis=1))[0]
            for index in possible:
                b = fixed[index]
                pair = (a[0], b[0])
                if pair not in collisions and intersects(a, b):
                    collisions[pair] = frame
    return [dict(first=a, second=b, frame=f) for (a, b), f in collisions.items()]


def glass_surface_contacts(shape, frame=0):
    """Positive-area coplanar glass/metal faces, including fixed construction."""
    compiled = compile_shape(shape)
    if compiled.get('animations'):
        compiled = apply_animation(compiled, compiled['animations'][0]['code'], frame)
    surfaces = triangles(compiled)
    glass = [t for t in surfaces if t.render_pass == 1]
    solid = [t for t in surfaces if t.render_pass == 0]
    contacts = set()
    for pane in glass:
        normal = pane.normal
        axes = [i for i in range(3) if i != int(np.argmax(np.abs(normal)))]
        a = pane.vertices[:, axes]
        for part in solid:
            if abs(np.dot(normal, part.normal)) < 1 - 1e-8 or np.max(np.abs((part.vertices - pane.vertices[0]) @ normal)) > 1e-7:
                continue
            b = part.vertices[:, axes]
            separated = False
            for polygon in (a, b):
                for i in range(3):
                    edge = polygon[(i + 1) % 3] - polygon[i]
                    direction = np.array((-edge[1], edge[0]))
                    length = np.linalg.norm(direction)
                    if length < 1e-10:
                        continue
                    direction /= length
                    p, q = a @ direction, b @ direction
                    if min(p.max(), q.max()) - max(p.min(), q.min()) <= 1e-7:
                        separated = True
                        break
                if separated:
                    break
            if not separated:
                contacts.add((pane.name, part.name))
    return sorted(contacts)


def add_chest(shape, game, prefix, offset):
    source = permissive_json(AssetResolver([game]).shape('game:block/wood/chest/normal').read())
    for key, location in source['textures'].items():
        shape.texture(prefix + key, 'game:' + location, size=(source['textureWidth'], source['textureHeight']))
    anchor = shape.pivot(prefix + 'placement', offset, group='context')
    def visit(elements, parent, path):
        for i, e in enumerate(elements):
            name = path + str(i) + '-' + e['name']
            faces = OrderedDict((f, Face('#' + prefix + data['texture'].lstrip('#'),
                enabled=data.get('enabled', True), uv=UVRect.of(data['uv']), rotation=data.get('rotation', 0)))
                for f, data in e.get('faces', {}).items())
            pivot = any(a == b for a, b in zip(e['from'], e['to']))
            ref = shape._element(name, e['from'], e['to'], parent=parent, faces=faces, pivot=pivot,
                rotation_origin=e.get('rotationOrigin'), rotation=tuple(e.get('rotation' + a, 0) for a in ('X', 'Y', 'Z')), group='context')
            visit(e.get('children', []), ref, name + '-')
    visit(source['elements'], anchor, prefix)


def build_review(root, game=None):
    root = Path(root).resolve()
    managed = (root / REVIEW_ROOT).resolve()
    staging, backup = managed.with_name('.current-build'), managed.with_name('.current-old')
    for path in (managed, staging, backup):
        path.relative_to((root / 'generated').resolve())
    for path in (staging, backup):
        if path.exists():
            shutil.rmtree(path)
    package = ModelPackage('pneumatic_inventory_ports_review')
    for candidate in DESCRIPTIONS:
        for state in STATES:
            package.shape(shape_for(candidate, state), f'{staging.relative_to(root).as_posix()}/{candidate}/{state}.shape.json')
            if game:
                installed = shape_for(candidate, state)
                if state.startswith(('sender', 'receiver')):
                    add_chest(installed, game, 'branch-chest-', (0, -16, 0))
                if state == 'terminal' or state.endswith('-terminal'):
                    add_chest(installed, game, 'outlet-chest-', (0, 16, 0) if state == 'elbow-terminal' else (16, 0, 0))
                installed.id += '-installed'
                package.shape(installed, f'{staging.relative_to(root).as_posix()}/{candidate}/{state}-installed.shape.json')
    build_package(package, root, write=True)
    (staging / '.gearwright-review.json').write_bytes(json_bytes({
        'version': 1, 'source': 'graphics/review/pneumatic_inventory_ports.py', 'managedPath': REVIEW_ROOT.as_posix(),
        'candidates': list(DESCRIPTIONS), 'states': list(STATES) + ([s + '-installed' for s in STATES] if game else []), 'animations': ['send', 'receive'],
        'decision': {'status': 'approved', 'selected': 'a-brass-sleeve', 'runtimePromotion': True,
                     'approvedShapeSha256': PORT_APPROVALS,
                     'repair': 'Requested glass-plane separation and continuous outlet lip; approved mechanism unchanged.'},
        'extension': EXTENSION, 'unitsPerBlock': 16,
        'briefs': dict(DESCRIPTIONS),
        'materials': {'brass': 'Low-impact sheet fittings; iron remains on shafts and wear parts.'},
    }))
    (staging / 'README.md').write_text('# Opaque inventory fittings\n\n' + '\n'.join(DESCRIPTIONS.values()) +
        '\n\nThe approved sleeve retains the working gear trains. Opaque branches hide the sender handoff. '
        'Spigots extend 2.25 model units past the block boundary; recessed dark bores conceal inventory surfaces. '
        'A terminal fitting can coexist with either side mechanism. A is approved and promoted through the runtime model compiler.\n', encoding='utf-8')
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
        for state, frames, views in (('sender', '0,45,90,150', 'front,isometric,top'),
                ('receiver', '0,150,210', 'front,back,isometric'),
                ('terminal', '0', 'front,back,left,right,top,isometric'),
                ('terminal-installed', '0', 'isometric'), ('elbow-terminal-installed', '0', 'isometric'), ('sender-terminal-installed', '0,90', 'isometric'),
                ('receiver-installed', '210', 'isometric')):
            installed = state.endswith('-installed')
            args = ['--vintage-story', str(game), '--mod', str(root), '--model', str(managed / candidate / (state + '.shape.json')),
                '--strict-textures', '--views', views, '--size', '480x480', '--orthographic',
                '--orthographic-scale', '2.6' if installed else '1.65', '--camera-target',
                '.8,.15,.5' if installed else '.5,.45,.5', '--lighting', 'flat', '--light-direction', '.6,.7,-1',
                '--output', str(managed / candidate / state / 'renders')]
            if state.startswith(('sender', 'receiver')):
                args += ['--animation', 'send' if state.startswith('sender') else 'receive', '--frames', frames]
            run(build_parser().parse_args(args), root)
        print('Rendered ' + candidate, flush=True)
    board = Image.new('RGB', (1440, 80 + 520 * len(DESCRIPTIONS)), (20, 23, 28))
    draw = ImageDraw.Draw(board)
    font = ImageFont.load_default(size=23)
    small = ImageFont.load_default(size=18)
    draw.text((24, 18), 'OPAQUE INVENTORY CONNECTIONS / CLIPPING REVISION', fill=(236, 227, 210), font=font)
    for row, (candidate, title) in enumerate(DESCRIPTIONS.items()):
        y = 70 + row * 520
        draw.text((24, y), title, fill=(114, 204, 185), font=font)
        for col, (state, filename, label) in enumerate((('sender', 'isometric-send-0.png', 'Sender / hidden initial cargo'),
                ('receiver', 'isometric-receive-210.png', 'Receiver / opaque discharge'),
                ('sender-terminal-installed', 'isometric-send-90.png', 'Branch + main outlet into chests'))):
            with Image.open(managed / candidate / state / 'renders' / filename) as photo:
                board.paste(photo.convert('RGB'), (col * 480, y + 34))
            draw.text((col * 480 + 16, y + 490), label, fill=(236, 227, 210), font=small)
    board.save(managed / 'comparison.png')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--audit', action='store_true')
    parser.add_argument('--vintage-story', type=Path)
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args()
    managed = build_review(args.root, args.vintage_story)
    if args.audit:
        report = {'baseline': moving_fixed_audit(receiver('a1-return-cam')),
                  'revised': moving_fixed_audit(shape_for('a-brass-sleeve', 'receiver')),
                  'glassBefore': {s: glass_surface_contacts(shape_for('a-brass-sleeve', s, repaired=False)) for s in ('sender', 'receiver', 'terminal')},
                  'glassAfter': {s: glass_surface_contacts(shape_for('a-brass-sleeve', s)) for s in ('sender', 'receiver', 'terminal')}}
        (managed / 'clearance-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        for name, collisions in report.items():
            print(name, collisions)
    print(REVIEW_ROOT.as_posix())
    if args.render:
        if not args.vintage_story:
            parser.error('--render requires --vintage-story')
        render_review(args.root.resolve(), args.vintage_story, managed)


if __name__ == '__main__':
    main()
