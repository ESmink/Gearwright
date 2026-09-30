"""Receiver review boundaries, actual animated cargo paths and load transmission."""
from pathlib import Path
import json
import hashlib
import math
import sys
import tempfile
import unittest

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'tools/graphics'))
from gearwright_graphics.compiler import compile_shape, json_bytes
from gearwright_graphics.animation import evaluate_animation
from graphics.review.lateral_drive_clearance import intersects, posed_boxes
from graphics.review.pneumatic_receiver import (
    DESCRIPTIONS, STATES, REVIEW_ROOT, GATE, FOLLOWER_LINK, LAYOUTS, ROLLER_R, FOLLOWER_R,
    CAM_STEP, cam_path, follower_position, rotation, build_review, candidate_shape, motion,
    APPROVED_REFERENCE,
)


class PneumaticReceiverReviewTests(unittest.TestCase):
    def test_managed_replacement_never_touches_runtime_assets(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            sentinel = root / 'assets/gearwright/sentinel'
            sentinel.parent.mkdir(parents=True)
            sentinel.write_text('runtime unchanged')
            first = build_review(root)
            (first / 'stale').write_text('stale')
            current = build_review(root)
            self.assertEqual(current.resolve(), (root / REVIEW_ROOT).resolve())
            self.assertFalse((current / 'stale').exists())
            self.assertFalse(current.with_name('.current-build').exists())
            self.assertFalse(current.with_name('.current-old').exists())
            self.assertEqual('runtime unchanged', sentinel.read_text())
            manifest = json.loads((current / '.gearwright-review.json').read_text())
            self.assertEqual('approved', manifest['decision']['status'])
            self.assertEqual(['a1-return-cam'], manifest['candidates'])
            self.assertEqual('a1-return-cam', manifest['decision']['selectedCandidate'])
            self.assertEqual(64, len(manifest['decision']['basisReference']['sha256']))
            self.assertTrue(manifest['decision']['runtimePromotion'])
            self.assertEqual(APPROVED_REFERENCE, manifest['decision']['approvedReference'])
            self.assertEqual(['receive'], manifest['animations'])
            self.assertEqual(len(DESCRIPTIONS) * len(STATES), len(list(current.rglob('*.shape.json'))))
            self.assertNotIn(str(root), (current / '.gearwright-review.json').read_text())

    def test_approved_receiver_geometry_is_preserved(self):
        encoded = json_bytes(compile_shape(candidate_shape('a1-return-cam')))
        self.assertEqual(APPROVED_REFERENCE['sha256'], hashlib.sha256(encoded).hexdigest())

    def test_cargo_clears_all_solid_and_glass_parts(self):
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate))
            for frame in range(0, 361, 3):
                boxes = posed_boxes(compiled, frame)
                cargo = next(b for b in boxes if b[0] == 'cargo-envelope')
                for box in boxes:
                    if box[0].startswith('cargo-'):
                        continue
                    if np.all(np.minimum(cargo[6], box[6]) > np.maximum(cargo[5], box[5])):
                        self.assertFalse(intersects(cargo, box), (candidate, frame, box[0]))

    def test_parked_mainlines_remain_clear(self):
        center, half = np.array((.5, .5, .5)), np.array((.5, .075, .075))
        swept = ('mainline', '', center, np.eye(3), half, center - half, center + half)
        for candidate in DESCRIPTIONS:
            for box in posed_boxes(compile_shape(candidate_shape(candidate, 'pass-through')), 0):
                self.assertFalse(intersects(swept, box), (candidate, box[0]))

    def test_moving_machine_fits_declared_host_and_attachment(self):
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate))
            low, high = np.zeros(3), np.ones(3)
            for frame in range(0, 361, 3):
                for box in posed_boxes(compiled, frame):
                    if box[0].startswith('cargo-'):
                        continue  # The parcel intentionally crosses into the inventory.
                    self.assertTrue(np.all(box[5] >= low - 1e-7), (candidate, frame, box[0], box[5]))
                    self.assertTrue(np.all(box[6] <= high + 1e-7), (candidate, frame, box[0], box[6]))

    def test_actual_gear_centres_ratios_and_rigid_follower(self):
        for candidate, layout in LAYOUTS.items():
            self.assertAlmostEqual(np.linalg.norm(np.array(layout.source) - (5.2, layout.y)),
                                   layout.input_radius + layout.output_radius)
            self.assertAlmostEqual(10.8 - 5.2, 2 * layout.output_radius)
            self.assertAlmostEqual(layout.input_radius / layout.input_teeth,
                                   layout.output_radius / layout.output_teeth)
            for frame in range(361):
                pose = motion(candidate, frame)
                self.assertAlmostEqual(pose['motor'] * layout.input_teeth - pose['theta'] * layout.output_teeth, 0)
                self.assertAlmostEqual(np.linalg.norm(follower_position(candidate, frame) - GATE), np.linalg.norm(FOLLOWER_LINK))

    def test_temporal_teeth_drive_both_rollers_in_one_working_plane(self):
        for candidate, layout in LAYOUTS.items():
            compiled = compile_shape(candidate_shape(candidate))
            elements = {element['name']: element for element in compiled['elements']}
            self.assertNotIn('iron-input', elements)
            gears = [('temporal-drive', 'temporal', layout.input_teeth),
                     ('iron-left-feed', 'gear-iron', layout.output_teeth),
                     ('brass-feed', 'gear-brass', layout.output_teeth)]
            for name, material, teeth in gears:
                element = elements[name]
                self.assertAlmostEqual(element['from'][2], layout.gear_z)
                tips = [child for child in element['children'] if child['name'].endswith('-tip')]
                self.assertEqual(teeth, len(tips))
                for tip in tips:
                    self.assertEqual({'#' + material}, {face['texture'] for face in tip['faces'].values()})
            for frame in (30, 90, 210, 360):
                poses = evaluate_animation(compiled['animations'][0], frame)
                angle = lambda name: poses[name]['rotationZ']
                self.assertGreater(angle('temporal-drive'), 0)
                self.assertAlmostEqual(angle('temporal-drive') * layout.input_teeth,
                                       -angle('iron-left-feed') * layout.output_teeth)
                self.assertAlmostEqual(angle('left-feed'), angle('iron-left-feed'))
                self.assertAlmostEqual(angle('right-feed'), angle('brass-feed'))
                self.assertAlmostEqual(angle('left-feed'), -angle('right-feed'))
                self.assertAlmostEqual(angle('cam'), angle('right-feed'))
                # At the nip, the left roller's right edge and the right roller's
                # left edge both travel down. This is true before cargo arrives.
                self.assertLess(angle('left-feed'), 0)
                self.assertGreater(angle('right-feed'), 0)

    def test_cam_reconstructs_follower_and_leaves_running_clearance(self):
        for candidate, layout in LAYOUTS.items():
            points, normals = cam_path(candidate)
            for i, frame in enumerate(range(0, 360, CAM_STEP)):
                reconstructed = rotation(frame) @ points[i] + (10.8, layout.y)
                np.testing.assert_allclose(reconstructed, follower_position(candidate, frame), atol=1e-9)
            compiled = compile_shape(candidate_shape(candidate, 'mechanism'))
            for frame in np.arange(0, 360, 4.5):
                boxes = posed_boxes(compiled, frame)
                rails = [b for b in boxes if b[0].startswith('cam-rail-')]
                followers = [b for b in boxes if b[0].startswith('follower-roller-')]
                for follower in followers:
                    for rail in rails:
                        if np.all(np.minimum(follower[6], rail[6]) > np.maximum(follower[5], rail[5])):
                            self.assertFalse(intersects(follower, rail), (candidate, frame, rail[0], follower[0]))

    def test_cycle_delivers_then_resets_gate_without_teleporting_cargo(self):
        for candidate, layout in LAYOUTS.items():
            self.assertEqual(0, motion(candidate, 0)['gate'])
            self.assertEqual(-90, motion(candidate, 150)['gate'])
            self.assertEqual(0, motion(candidate, 360)['gate'])
            self.assertLess(motion(candidate, 360)['cargo'][1] + 1.2, 0)
            for frame in range(191, 361):
                previous, current = motion(candidate, frame - 1), motion(candidate, frame)
                self.assertAlmostEqual(previous['cargo'][1] - current['cargo'][1], math.pi / 180 * ROLLER_R)
            if candidate == 'a1-return-cam':
                for angle in np.linspace(-90, 0, 91):
                    # The weight stays left of the shaft, so gravity always closes it.
                    self.assertLess((rotation(angle) @ (-1.1, -.35))[0], 0)

    def test_external_cam_force_opens_the_gate_without_a_dead_centre(self):
        points, normals = cam_path('a1-return-cam')
        for i, frame in enumerate(range(0, 360, CAM_STEP)):
            force = rotation(frame) @ normals[i]
            lever = follower_position('a1-return-cam', frame) - GATE
            torque = lever[0] * force[1] - lever[1] * force[0]
            self.assertLess(torque, -.05, (frame, torque))

    def test_tooth_meshes_and_keyed_shafts_clear_mating_parts(self):
        for candidate, layout in LAYOUTS.items():
            compiled = compile_shape(candidate_shape(candidate, 'mechanism'))
            pairs = [('temporal-drive-', 'iron-left-feed-'), ('iron-left-feed-', 'brass-feed-'),
                     ('temporal-drive-', 'brass-feed-')]
            for frame in range(0, 361, 3):
                boxes = posed_boxes(compiled, frame)
                for first, second in pairs:
                    left = [b for b in boxes if b[0].startswith(first)]
                    right = [b for b in boxes if b[0].startswith(second)]
                    lo, hi = np.array([b[5] for b in right]), np.array([b[6] for b in right])
                    for a in left:
                        for index in np.where(np.all(np.minimum(hi, a[6]) > np.maximum(lo, a[5]), axis=1))[0]:
                            self.assertFalse(intersects(a, right[index]), (candidate, frame, a[0], right[index][0]))
                for name in ('input', 'right-feed', 'left-feed', 'gate'):
                    shaft_name = name if name in ('input', 'gate') else name + '-shaft'
                    shafts = [b for b in boxes if b[0] in (shaft_name + '-iron-shaft', shaft_name + '-key')]
                    bearings = [b for b in boxes if b[0].startswith(name + '-bearing-')]
                    for shaft in shafts:
                        for bearing in bearings:
                            self.assertFalse(intersects(shaft, bearing), (candidate, frame, shaft[0], bearing[0]))

    def test_compact_layers_keep_independent_moving_parts_clear(self):
        candidate = 'a1-return-cam'
        layout = LAYOUTS[candidate]
        self.assertLessEqual(layout.gate_z - layout.gear_z, (3.8 - 1.1) / 2)
        # The third wheel is close to the brass wheel but must not make a
        # second contact that would lock this three-wheel train.
        tip_gap = np.linalg.norm(np.array(layout.source) - (10.8, layout.y)) - 1.9 - 3.1
        self.assertGreaterEqual(tip_gap, .1)
        self.assertLess(tip_gap, .12)
        compiled = compile_shape(candidate_shape(candidate, 'mechanism'))
        for frame in range(0, 361, 3):
            boxes = posed_boxes(compiled, frame)
            drive = [b for b in boxes if b[0].startswith(('temporal-drive-', 'iron-left-feed-', 'brass-feed-'))]
            cam = [b for b in boxes if b[0].startswith('cam-')]
            lever = [b for b in boxes if b[0] in ('follower-lever', 'weighted-arm', 'brass-return-weight')]
            bearings = [b for b in boxes if '-bearing-' in b[0]]
            for first, second in ((drive, cam), (cam, lever), (lever, bearings)):
                lows, highs = np.array([b[5] for b in second]), np.array([b[6] for b in second])
                for a in first:
                    for index in np.where(np.all(np.minimum(highs, a[6]) > np.maximum(lows, a[5]), axis=1))[0]:
                        b = second[index]
                        self.assertFalse(intersects(a, b), (frame, a[0], b[0]))
            if frame == 0:
                rim_back = max(b[6][2] for b in drive if '-rim-' in b[0]) * 16
                cam_front = min(b[5][2] for b in cam if b[0].startswith('cam-back-web-')) * 16
                self.assertAlmostEqual(.086, cam_front - rim_back)
                cam_back = max(b[6][2] for b in cam if b[0].startswith('cam-rail-')) * 16
                weight_front = next(b[5][2] for b in lever if b[0] == 'brass-return-weight') * 16
                self.assertAlmostEqual(.05, weight_front - cam_back)
                arm_back = next(b[6][2] for b in lever if b[0] == 'follower-lever') * 16
                gate_bearings = [b for b in bearings if b[0].startswith('gate-bearing-')]
                self.assertAlmostEqual(.08, min(b[5][2] for b in gate_bearings) * 16 - arm_back)

    def test_marked_shaft_gaps_close_without_intersecting_front_supports(self):
        compiled = compile_shape(candidate_shape('a1-return-cam'))
        moving_pivots = set(evaluate_animation(compiled['animations'][0], 0))
        moving_names = set()

        def visit(elements, moving=False):
            for element in elements:
                active = moving or element['name'] in moving_pivots
                if active:
                    moving_names.add(element['name'])
                visit(element.get('children', []), active)

        visit(compiled['elements'])
        for frame in range(0, 361, 3):
            boxes = posed_boxes(compiled, frame)
            fixed = [b for b in boxes if b[0] not in moving_names and b[5][2] * 16 < 5.3]
            moving = [b for b in boxes if b[0] in moving_names and not b[0].startswith('cargo-')]
            lows, highs = np.array([b[5] for b in fixed]), np.array([b[6] for b in fixed])
            for a in moving:
                for index in np.where(np.all(np.minimum(highs, a[6]) > np.maximum(lows, a[5]), axis=1))[0]:
                    self.assertFalse(intersects(a, fixed[index]), (frame, a[0], fixed[index][0]))
            if frame == 0:
                temporal_back = next(b[6][2] for b in boxes if b[0] == 'temporal-drive-hub') * 16
                input_front = min(b[5][2] for b in fixed if b[0].startswith('input-bearing-')) * 16
                self.assertAlmostEqual(.13, input_front - temporal_back)
                cam_back = max(b[6][2] for b in moving if b[0].startswith('cam-')) * 16
                # The lower cam now enters the frame depth, eliminating the
                # exposed stand-off highlighted by the maintainer.
                self.assertGreater(cam_back, 4.1)
                self.assertLess(cam_back, 4.45)

    def test_thick_gears_keep_full_depth_teeth_and_keyed_shaft_support(self):
        compiled = compile_shape(candidate_shape('a1-return-cam'))
        boxes = posed_boxes(compiled, 0)
        for name, shaft_name in (('temporal-drive', 'input'), ('iron-left-feed', 'left-feed-shaft'),
                                 ('brass-feed', 'right-feed-shaft')):
            wheel = [b for b in boxes if b[0].startswith(name + '-')]
            rim = [b for b in wheel if '-rim-' in b[0]]
            teeth = [b for b in wheel if '-tooth-' in b[0]]
            for part in rim + teeth:
                self.assertGreaterEqual((part[6][2] - part[5][2]) * 16, .79)
            self.assertAlmostEqual(min(b[5][2] for b in rim), min(b[5][2] for b in teeth))
            self.assertAlmostEqual(max(b[6][2] for b in rim), max(b[6][2] for b in teeth))
            hub = next(b for b in wheel if b[0] == name + '-hub')
            shaft = next(b for b in boxes if b[0] == shaft_name + '-iron-shaft')
            self.assertLess(shaft[5][2], hub[5][2])
            self.assertGreater(shaft[6][2], hub[6][2])


if __name__ == '__main__':
    unittest.main()
