"""Review the existing large bellows against the finalized shaft crank.

The game model is read from the selected installation. No game geometry or
textures are copied into the repository or runtime assets by this workflow.
"""

from __future__ import annotations

import argparse
from collections import OrderedDict
from dataclasses import replace
import math
from pathlib import Path
import shutil

import numpy as np

from gearwright_graphics.assets import AssetResolver, permissive_json
from gearwright_graphics.compiler import build_package, compile_shape, json_bytes
from gearwright_graphics.model import Animation, ElementRef, Face, Keyframe, ModelPackage, Shape, UVRect, Vec3
from graphics.models.parts.reciprocating_drive import rebuild_crank, rebuild_dry_adapter
from graphics.review.lateral_motion_system import _crank_state, _pivot, _slider_state
from graphics.review.reciprocating_pump_refinement import candidate_shape as approved_pump

SOURCE = 'game:block/wood/mechanics/bellowslarge'
REVIEW_ROOT = Path('generated/large-bellows-review')
DESCRIPTIONS = OrderedDict((
    ('a-reinforced-wood', 'A: a reinforced wooden pitman and two broad oak rocker arms, '
     'with two slim lift links to the existing plate. This keeps the old bellows material language.'),
))
STATES = ('one-sided', 'through-shaft', 'shared-with-pump', 'top-through-shaft', 'both-bottom-preferred')
HINGE = (3.9095, 8.9055)
ANCHOR = (20.9, -2)
CRANK = (24, -8, 8)
ROCKER = (16.8, -2)
ROCKER_INPUT = 7.2
ROCKER_OUTPUT = 4.32
INPUT_LINK = 7
OUTPUT_LINK = 6.6


def states_for(candidate):
    return STATES


def reservoir_cycle(angles):
    """Steady demonstration: weighted upper chamber, check valve, open nozzle.

    Lower swept volume is proportional to the sine of the board opening.
    The upper board supplies a constant-pressure nozzle flow at this fixed
    demonstration speed. Storage integrates inflow minus outflow; no crank
    phase is prescribed for the upper board. This is not a runtime air solver.
    """
    volumes = [-math.sin(math.radians(angle)) for angle in angles]
    incoming = [max(0, volumes[i - 1] - volumes[i]) for i in range(1, 361)]
    outflow = sum(incoming) / 360
    stored = [0.0]
    for amount in incoming:
        stored.append(stored[-1] + amount - outflow)
    capacity = 2 * (max(volumes) - min(volumes))
    upper = [.35 + (amount - min(stored)) / capacity for amount in stored]
    intake = [volumes[(i - 1) % 360] < volumes[i % 360] for i in range(361)]
    return upper, intake, incoming, outflow


def add_chamber_pose(poses, targets, angle, upper, intake):
    compression = (angle + 12) / 10.5
    for name, value in (
        ('BottomPlatePIVOT', angle + 12),
        ('Fold1-PIVOTlower', 1.65 * compression), ('Fold2-PIVOT', 3.4 * compression),
        ('Fold3-PIVOT', 3.4 * compression), ('Fold4-PIVOT-top', 1.5 * upper),
        ('Fold5-PIVOT-top', 4 * upper), ('Fold6-PIVOT-top', 4 * upper),
        ('TopPlate-PIVOT', 2 * upper), ('FLAP', 25 if intake else 0),
    ):
        if name in targets:
            poses[name] = {'rotationZ': value}


def finish_animation(shape, keyframes):
    shape.add_animation(Animation('Shaft-driven large bellows', 'shaft-bellows', 361,
                                   tuple(keyframes), 1, 'Stop', 'Repeat'))
    # The reviewer initially selects no animation. Start with a complete
    # assembled pose, then express animated translations from that position.
    initial = keyframes[0].elements
    for index, element in enumerate(shape.elements):
        pose = initial.get(element.name, {})
        delta = Vec3(*(pose.get('offset' + axis, 0) for axis in 'XYZ'))
        shape.elements[index] = replace(element,
            from_=element.from_ + delta, to=element.to + delta,
            rotation_origin=element.rotation_origin + delta if element.rotation_origin else None,
            rotation=Vec3(*(pose.get('rotation' + axis, getattr(element.rotation, axis.lower()))
                            for axis in 'XYZ')))
    offsets = {name: {key: value for key, value in pose.items() if key.startswith('offset')}
               for name, pose in initial.items()}
    for frame in keyframes:
        for name, values in offsets.items():
            for key, value in values.items():
                frame.elements[name][key] -= value
    return shape


def circle_joint(hinge, anchor, driver, length, *, branch=1):
    dx, dy = driver[0] - hinge[0], driver[1] - hinge[1]
    radius, distance = math.hypot(*anchor), math.hypot(dx, dy)
    cosine = (radius * radius + distance * distance - length ** 2) / (2 * radius * distance)
    if not -1 <= cosine <= 1:
        raise ValueError('the bellows linkage cannot reach its journal')
    angle = math.atan2(dy, dx) + branch * math.acos(cosine) - math.atan2(anchor[1], anchor[0])
    pin = (hinge[0] + anchor[0] * math.cos(angle) - anchor[1] * math.sin(angle),
           hinge[1] + anchor[0] * math.sin(angle) + anchor[1] * math.cos(angle))
    return pin, math.degrees(angle)


def rocker_state(phase):
    radians = math.radians(phase)
    journal = (CRANK[0] + 3 * math.sin(radians), CRANK[1] + 3 * math.cos(radians))
    rocker_pin, rocker_angle = circle_joint(ROCKER, (ROCKER_INPUT, 0), journal, INPUT_LINK)
    amount = ROCKER_OUTPUT / ROCKER_INPUT
    output = tuple(ROCKER[i] + (rocker_pin[i] - ROCKER[i]) * amount for i in range(2))
    plate_pin, plate_angle = circle_joint(HINGE, ANCHOR, output, OUTPUT_LINK)
    return journal, rocker_pin, output, plate_pin, rocker_angle, plate_angle


def read_source(game: Path) -> dict:
    return permissive_json(AssetResolver([game]).shape(SOURCE).read())


def import_bellows(document: dict, shape: Shape, *, parent=None, exclude=()) -> None:
    """Preserve the installed model's hierarchy, faces and texture references."""
    for key, location in document['textures'].items():
        shape.texture(key, location if ':' in location else 'game:' + location,
                      size=tuple(document.get('textureSizes', {}).get(key,
                          (document['textureWidth'], document['textureHeight']))))

    animated = {name for animation in document.get('animations', [])
                for frame in animation['keyframes'] for name in frame['elements']}

    def visit(element, parent):
        if element['name'] in exclude:
            return
        faces = OrderedDict((name, Face(
            face['texture'], enabled=face.get('enabled', True),
            uv=UVRect.of(face['uv']) if 'uv' in face else None,
            rotation=face.get('rotation', 0), glow=face.get('glow', 0),
            reflective_mode=face.get('reflectiveMode'), auto_uv=face.get('autoUv'),
        )) for name, face in element.get('faces', {}).items())
        # Keep static game rotations on an inner element. The review pipeline
        # replaces animated Euler values; a separate pivot makes them additive.
        offset = Vec3(0, 0, 0)
        name = element['name']
        if name in animated:
            offset = Vec3.of(element.get('rotationOrigin', element['from']))
            parent = _pivot(shape, name, offset.values(), parent=parent)
            name += '-base'
        origin = Vec3.of(element.get('rotationOrigin', element['from'])) - offset
        ref = shape._element(name, (Vec3.of(element['from']) - offset).values(),
            (Vec3.of(element['to']) - offset).values(),
            faces=faces, parent=parent, rotation_origin=origin.values(),
            rotation=tuple(element.get('rotation' + axis, 0) for axis in 'XYZ'),
            shade=element.get('shade'), gradient_shade=element.get('gradientShade'),
            render_pass=element.get('renderPass'), group='existing-bellows',
            pivot=any(end == start for start, end in zip(element['from'], element['to'])))
        # The authoring convenience method clears faces on ordinary pivots.
        # These game-authored planes have visible seam faces, so retain them.
        if shape.elements[-1].pivot and faces:
            shape.elements[-1] = replace(shape.elements[-1], faces=faces)
        for child in element.get('children', []):
            visit(child, ref)

    for element in document['elements']:
        visit(element, parent)


def _bearing(shape, prefix, root, y, inner, outer, *, depth=.58):
    for part, x0, y0, x1, y1 in (
        ('top', -outer, y + inner, outer, y + outer),
        ('bottom', -outer, y - outer, outer, y - inner),
        ('left', -outer, y - inner, -inner, y + inner),
        ('right', inner, y - inner, outer, y + inner),
    ):
        shape.box(prefix + '-' + part, (x0, y0, -depth), (x1, y1, depth),
                  texture='#gw-bronze', parent=root, group='bellows-bearing')


def _z_pin(shape, name, root, x, depth):
    # The same crossed-cuboid journal construction as the approved crank.
    # Its corners stay inside the 0.28-unit radius at every relative angle.
    short, long = .28 / math.sqrt(5), .56 / math.sqrt(5)
    shape.box(name, (x - long, -short, -depth), (x + long, short, depth),
              texture='#gw-bronze', parent=root)
    for side, y0, y1 in (('low', -long, -short), ('high', short, long)):
        shape.box(name + '-' + side, (x - short, y0, -depth), (x + short, y1, depth),
                  texture='#gw-bronze', parent=root)


def candidate_shape(document, candidate, state):
    if candidate not in DESCRIPTIONS or state not in STATES:
        raise ValueError('unknown bellows candidate or state')
    if state in ('top-through-shaft', 'both-bottom-preferred'):
        from graphics.review.large_bellows_top import attachment_scene
        return attachment_scene(document, top=state.startswith('top'), both=state.startswith('both'))
    crank = _crank_state(through=state != 'one-sided', throw=3)
    rebuild_crank(crank, through=state != 'one-sided')
    shape = Shape(candidate + '-' + state, 16, 16)
    for texture in crank.textures.values():
        shape.texture(texture.key, texture.location, size=texture.size)
    mount = _pivot(shape, 'gw-shaft-mount', CRANK, rotation=(0, 90, 0))
    for element in crank.elements:
        shape.elements.append(replace(element,
            parent=element.parent or mount.name))
        shape._refs[element.name] = ElementRef(shape.id, element.name)
    import_bellows(document, shape, exclude=('LINKAGE-1d',))
    lane = .7 if state == 'shared-with-pump' else 0
    rod = _pivot(shape, 'gw-bellows-rod', (0, 0, 8 - lane))
    half = INPUT_LINK / 2
    _bearing(shape, 'gw-bellows-big-end', rod, -half, 1.05, 1.3)
    _bearing(shape, 'gw-bellows-small-end', rod, half, .32, .59)
    wooden = candidate == 'a-reinforced-wood'
    width = .80 if wooden else .4
    shape.box('gw-bellows-pitman', (-width, -half + 1.3, -.48),
              (width, half - .59, .48), texture='#plainoak' if wooden else '#gw-iron',
              parent=rod, group='bellows-pitman')
    if wooden:
        for end, y in (('lower', -half + 1.3), ('upper', half - 1.3)):
            for side, z in (('front', -.56), ('back', .48)):
                shape.box(f'gw-bellows-{end}-strap-{side}', (-width, y, z),
                          (width, y + .72, z + .08), texture='#gw-iron', parent=rod)
    rocker = _pivot(shape, 'gw-bellows-rocker', (*ROCKER, 8))
    outer = 3.9 if wooden else 3.4
    height = .65 if wooden else .4
    for side, z0, z1 in (('front', -outer, -3.1), ('back', 3.1, outer)):
        shape.box('gw-bellows-rocker-arm-' + side, (-.5, -height, z0),
                  (ROCKER_INPUT + .5, height, z1),
                  texture='#plainoak' if wooden else '#gw-iron', parent=rocker)
        if wooden:
            for name, x in (('fulcrum', 0), ('output', ROCKER_OUTPUT), ('input', ROCKER_INPUT)):
                shape.box(f'gw-bellows-rocker-strap-{side}-{name}', (x - .4, -height - .06, z0 - .04),
                          (x + .4, height + .06, z0 + .04), texture='#gw-iron', parent=rocker)
    for name, x, depth in (('fulcrum', 0, 6.7), ('output', ROCKER_OUTPUT, 4), ('input', ROCKER_INPUT, 4)):
        _z_pin(shape, 'gw-bellows-rocker-pin-' + name, rocker, x, depth)
    for side, z0, z1 in (('front', 1, 1.6), ('back', 14.4, 15)):
        shape.box('gw-bellows-fulcrum-hanger-' + side, (16, -2.6, z0),
                  (17.6, 2, z1), texture='#gw-iron')
    plate_pin = _pivot(shape, 'gw-bellows-plate-pin', (0, 0, 8))
    _z_pin(shape, 'gw-bellows-pin', plate_pin, 0, 3.5)
    for side, z in (('front', -2.8), ('back', 2.8)):
        # Lift links pass either side of the central input pitman.
        link = _pivot(shape, 'gw-bellows-lift-' + side, (0, 0, 8 + z))
        for end, y in (('low', -OUTPUT_LINK / 2), ('high', OUTPUT_LINK / 2)):
            _bearing(shape, f'gw-bellows-lift-eye-{side}-{end}', link, y, .32, .52, depth=.22)
        shape.box('gw-bellows-lift-bar-' + side, (-.3, -OUTPUT_LINK / 2 + .52, -.2),
                  (.3, OUTPUT_LINK / 2 - .52, .2), texture='#gw-iron', parent=link)
        shape.box('gw-bellows-clevis-' + side, (-.65, -.55, z + .3),
                  (.65, 1.55, z + .6), texture='#gw-iron', parent=plate_pin)

    if state == 'shared-with-pump':
        pump = approved_pump('a-clean-folded-pipes', 'rest')
        rebuild_dry_adapter(pump, lane=-.7)
        for texture in pump.textures.values():
            if texture.key not in shape.textures:
                shape.texture(texture.key, texture.location, size=texture.size)
        for element in pump.elements:
            if element.name.startswith('gw-crank-') or element.group == 'review-water':
                continue
            if (element.group or '').startswith('pump-downward-stand'):
                continue
            shape.elements.append(replace(element, parent=element.parent or mount.name))
            shape._refs[element.name] = ElementRef(shape.id, element.name)

    targets = set(shape._refs)
    keyframes = []
    upper_cycle, intake_cycle, _, _ = reservoir_cycle([rocker_state(p)[-1] for p in range(361)])
    for phase in range(361):
        journal, input_pin, output_pin, pin, rocker_angle, bottom_angle = rocker_state(phase)
        rod_angle = math.degrees(math.atan2(journal[0] - input_pin[0], input_pin[1] - journal[1]))
        lift_angle = math.degrees(math.atan2(output_pin[0] - pin[0], pin[1] - output_pin[1]))
        poses = OrderedDict((
            ('gw-crank-phase', {'rotationX': phase, 'rotShortestDistanceX': False}),
            ('gw-bellows-rod', {'offsetX': (journal[0] + input_pin[0]) / 2,
                               'offsetY': (journal[1] + input_pin[1]) / 2, 'rotationZ': rod_angle}),
            ('gw-bellows-rocker', {'rotationZ': rocker_angle}),
            ('gw-bellows-plate-pin', {'offsetX': pin[0], 'offsetY': pin[1], 'rotationZ': bottom_angle}),
            ('BottomPlatePIVOT', {'rotationZ': bottom_angle + 12}),
        ))
        for side in ('front', 'back'):
            poses['gw-bellows-lift-' + side] = {'offsetX': (output_pin[0] + pin[0]) / 2,
                'offsetY': (output_pin[1] + pin[1]) / 2, 'rotationZ': lift_angle}
        add_chamber_pose(poses, targets, bottom_angle, upper_cycle[phase], intake_cycle[phase])
        if state == 'shared-with-pump':
            _, _, crosshead, middle_y, middle_z, rod_angle_pump = _slider_state(phase, radius=3, rod_length=6)
            poses['gw-piston-motion'] = {'offsetY': crosshead + 3}
            poses['gw-connecting-rod-motion'] = {'offsetY': middle_y, 'offsetZ': middle_z,
                                                 'rotationX': rod_angle_pump}
        keyframes.append(Keyframe(phase, poses))
    return finish_animation(shape, keyframes)


def build_review(root, document):
    root = Path(root).resolve()
    review = (root / REVIEW_ROOT).resolve()
    managed, staging, backup = (review / name for name in ('current', '.current-build', '.current-old'))
    for path in (review, managed, staging, backup):
        path.relative_to(root)
    review.mkdir(parents=True, exist_ok=True)
    for path in (staging, backup):
        if path.exists():
            shutil.rmtree(path)
    package = ModelPackage('large_bellows_review')
    for candidate in DESCRIPTIONS:
        for state in states_for(candidate):
            package.shape(candidate_shape(document, candidate, state),
                f'{REVIEW_ROOT.as_posix()}/.current-build/{candidate}/{state}.shape.json')
    build_package(package, root, write=True)
    (staging / '.gearwright-review.json').write_bytes(json_bytes({
        'version': 1, 'source': 'graphics/review/large_bellows.py',
        'purpose': 'Fit the existing large bellows to the finalized shaft crank',
        'managedPath': f'{REVIEW_ROOT.as_posix()}/current',
        'candidates': list(DESCRIPTIONS), 'states': list(STATES), 'animations': ['shaft-bellows'],
        'sources': [SOURCE, 'graphics/models/parts/reciprocating_drive.py'],
        'decision': {'status': 'approved', 'runtimePromotion': True,
                     'approvedCandidate': 'a-reinforced-wood',
                     'approval': 'Maintainer: A is good. One connection, bottom preferred; model adjustments for top and bottom authorized.',
                     'pendingCandidates': []},
    }))
    angles = [rocker_state(phase)[-1] for phase in range(361)]
    (staging / 'README.md').write_text('\n'.join([
        '# Large bellows on the shaft crank', '',
        *[f'- `{candidate}` - {description}' for candidate, description in DESCRIPTIONS.items()], '',
        'A is approved and implemented. One connection is selected, preferring an aligned lower shaft '
        'even when stopped. The upper arrangement drives the top board directly; its lower chamber '
        'follows suction upward and drops to refill on the return stroke. '
        'The both-shafts state shows only the lower linkage. Rejected rope and wraparound alternatives are removed.',
        'The body, nozzle, stand, leather and source UVs come from the installed game large bellows. '
        'The old hanging link is replaced. The crank is the exact approved three-unit-throw definition. '
        'Its axis crosses the bellows width, above or below the rear half.',
        f'In A, one turn closes a constant {INPUT_LINK}-unit pitman, a 10:6 rocker, and '
        f'two {OUTPUT_LINK}-unit lift links at every degree. '
        f'The lower plate swings from {min(angles):.2f} to {max(angles):.2f} degrees; '
        'the reduction keeps its leather folds within their original -12 to -1.5-degree travel. '
        'The upper chamber now integrates one-way inflow from the lower chamber minus a steady nozzle '
        'outflow at the demonstration speed. Its weighted board falls while the lower chamber refills. '
        'It is not rigidly connected to the shaft. This normalized reservoir model is a review demonstration, '
        'not a calibrated air-pressure or overspeed simulation.',
        'The shaft uses the current journal and 1.4-unit seat spacing. A\'s shared-with-pump state '
        'shows a current pump on the opposite face with centered +/-0.7 seats.',
        'A load path: reinforced oak carries the broad pitman and rocker; iron straps resist splitting at '
        'the end eyes and bronze carries repeated pin wear. Bare wood wears at the journal; '
        'copper is too soft for the bearing. The upper mounting adds an oak support and connects '
        'straight to the upper board with the same bearing construction. The upper chamber pumps '
        'through the existing intake path while its lower chamber breathes. The approved oak frame '
        'continues the regular base, including horizontal rails at their junction.',
        'Mechanical reference: Keir Memorial Museum, Artefacts Canada, accession KM.87.15.01: '
        'https://app.pch.gc.ca/application/artefacts_hum/detailler_detail.app?d=AASEKM.87.15.01&lang=fr&pID=0',
        'Review chamber seams, plate motion and link clearance over a complete turn. '
        'The deterministic renders are diagnostic; in-game validation follows approval.', '',
    ]), encoding='utf-8')
    if managed.exists():
        managed.replace(backup)
    staging.replace(managed)
    if backup.exists():
        shutil.rmtree(backup)
    return managed


def audit_review(document, managed):
    import copy
    from graphics.review.lateral_drive_clearance import intersects, posed_boxes
    from graphics.review.large_bellows_top import top_state
    # Verify the analytic attachment points against the installed body's real
    # hinge hierarchy, without storing game geometry in a checked-in fixture.
    marked = copy.deepcopy(document)
    def mark(elements):
        for element in elements:
            center = {'BottomPlatePIVOT': (20.9, -1, 7),
                      'TopPlate-PIVOT': (14.8095, 2.5, 7)}.get(element['name'])
            if center:
                element.setdefault('children', []).append({'name': element['name'] + '-anchor-test',
                    'from': [v - .01 for v in center], 'to': [v + .01 for v in center], 'faces': {}})
            mark(element.get('children', []))
    mark(marked['elements'])
    for top in (False, True):
        state = 'top-through-shaft' if top else 'through-shaft'
        compiled = compile_shape(candidate_shape(marked, 'a-reinforced-wood', state))
        marker = ('TopPlate-PIVOT' if top else 'BottomPlatePIVOT') + '-anchor-test'
        for phase in range(0, 361, 5):
            actual = next(box[2] for box in posed_boxes(compiled, phase) if box[0] == marker)
            pin = (top_state if top else rocker_state)(phase)[3]
            np.testing.assert_allclose(actual, np.array((*pin, 8)) / 16, atol=1e-8,
                err_msg=f'{state}: clevis detached from native board at phase {phase}')
    report = {}
    for candidate in DESCRIPTIONS:
        for state in states_for(candidate):
            compiled = compile_shape(candidate_shape(document, candidate, state))
            collisions = {}
            def visible_names(elements):
                return {element['name'] for element in elements
                        if any(face.get('enabled', True) for face in element.get('faces', {}).values())} | {
                    name for element in elements for name in visible_names(element.get('children', []))}
            visible = visible_names(compiled['elements'])
            for phase in range(0, 361, 5):
                boxes = [box for box in posed_boxes(compiled, phase) if box[0] in visible]
                # Pivots with no faces are transform helpers, not material.
                rods = [box for box in boxes if box[0].startswith('gw-bellows-')]
                drive = [box for box in boxes if box[0].startswith(('gw-crank-', 'gw-crosshead-',
                         'gw-connecting-rod-'))]
                for rod in rods:
                    for other in drive:
                        if np.all(np.minimum(rod[6], other[6]) - np.maximum(rod[5], other[5]) > 1e-8):
                            if intersects(rod, other):
                                collisions.setdefault((rod[0], other[0]), phase)
                linkage = [box for box in rods if box[0].startswith(('gw-bellows-big-end-',
                           'gw-bellows-small-end-', 'gw-bellows-pitman', 'gw-bellows-lift-'))]
                rocker = [box for box in rods if box[0].startswith('gw-bellows-rocker-')]
                for link in linkage:
                    for arm in rocker:
                        if np.all(np.minimum(link[6], arm[6]) - np.maximum(link[5], arm[5]) > 1e-8):
                            if intersects(link, arm):
                                collisions.setdefault((link[0], arm[0]), phase)
                if state == 'top-through-shaft':
                    moving = linkage + [box for box in rods if box[0].startswith((
                        'gw-bellows-rocker-arm-', 'gw-bellows-rocker-output-stretcher',
                        'gw-bellows-plate-stretcher', 'gw-bellows-rope-fiber-'))]
                    frame = [box for box in rods if box[0].startswith((
                        'gw-bellows-extension-',))]
                    body = [box for box in boxes if not box[0].startswith('gw-')]
                    for link in moving:
                        # The plate stretcher intentionally meets the lower board.
                        obstacles = frame if link[0] == 'gw-bellows-plate-stretcher' else frame + body
                        for other in obstacles:
                            if np.all(np.minimum(link[6], other[6]) - np.maximum(link[5], other[5]) > 1e-8):
                                if intersects(link, other):
                                    collisions.setdefault((link[0], other[0]), phase)
            report[candidate + '/' + state] = {
                'stepDegrees': 5, 'collisions': [
                    {'first': pair[0], 'second': pair[1], 'phase': phase}
                    for pair, phase in collisions.items()],
            }
    (managed / 'clearance.json').write_bytes(json_bytes(report))
    count = sum(len(entry['collisions']) for entry in report.values())
    print(f'Bellows drive, frame and chamber clearance: {count} intersecting part pairs', flush=True)
    if count:
        raise ValueError('bellows linkage clearance failed; see clearance.json')


def render_review(root, game, managed):
    from gearwright_graphics.photoshoot import build_parser, run
    from PIL import Image, ImageDraw, ImageFont
    views = ('through-shaft', 'top-through-shaft', 'both-bottom-preferred')
    candidate = 'a-reinforced-wood'
    for state in views:
        args = ['--vintage-story', str(game), '--mod', str(root),
                '--model', str(managed / candidate / (state + '.shape.json')),
                '--strict-textures', '--views', 'front-right,front,back,right,left,top,bottom',
                '--light-direction', '.6,.7,-1', '--size', '720x620', '--orthographic',
                '--orthographic-scale', '4.1', '--camera-target', '.95,.55,.5',
                '--animation', 'shaft-bellows', '--frames', '0,90,180,270',
                '--output', str(managed / candidate / state / 'renders')]
        run(build_parser().parse_args(args), root)
        print(f'Rendered {state}', flush=True)
    sheet = Image.new('RGB', (720 * len(views), 1340), (20, 23, 28))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=25)
    for column, state in enumerate(views):
        for row, phase in enumerate((0, 180)):
            x, y = column * 720, row * 670
            draw.text((x + 18, y + 12),
                      f'{("BOTTOM", "TOP", "BOTH | BOTTOM SELECTED")[column]} | {phase} DEG',
                      font=font, fill=(236, 224, 205))
            with Image.open(managed / candidate / state / f'renders/front-right-shaft-bellows-{phase}.png') as photo:
                sheet.paste(photo.convert('RGB'), (x, y + 50))
    sheet.save(managed / 'comparison.png')


def inspect_source(document: dict) -> None:
    def visit(element, offset=Vec3(0, 0, 0), depth=0):
        start = offset + Vec3.of(element['from'])
        end = offset + Vec3.of(element['to'])
        origin = offset + Vec3.of(element.get('rotationOrigin', element['from']))
        if depth < 3 or any(word in element['name'].lower() for word in ('plate', 'linkage', 'pivot', 'stand')):
            print(f"{'  ' * depth}{element['name']}: {start.values()} .. {end.values()}, pivot {origin.values()}")
        for child in element.get('children', []):
            visit(child, start, depth + 1)
    for element in document['elements']:
        visit(element)
    for animation in document.get('animations', []):
        print(animation['code'], 'frames', animation['quantityframes'])
        for frame in animation['keyframes']:
            print(frame['frame'], {name: values for name, values in frame['elements'].items()
                                  if name in ('BottomPlatePIVOT', 'LINKAGE-1d', 'TopPlate-PIVOT')})


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--vintage-story', type=Path, required=True)
    parser.add_argument('--inspect', action='store_true')
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args()
    document = read_source(args.vintage_story)
    if args.inspect:
        inspect_source(document)
        angles = [rocker_state(phase)[-1] for phase in range(361)]
        print('Reduced plate travel:', min(angles), max(angles))
        return
    managed = build_review(args.root, document)
    audit_review(document, managed)
    if args.render:
        render_review(args.root, args.vintage_story, managed)
    print(managed.relative_to(args.root.resolve()).as_posix())


if __name__ == '__main__':
    main()
