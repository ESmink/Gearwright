"""Sender approval boundaries, actual motion, cargo clearance and working drives."""
from pathlib import Path
import hashlib
import json
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
from graphics.review.pneumatic_sender import (
    DESCRIPTIONS, STATES, REVIEW_ROOT, STROKE, DECK_Y, DECK_DEPTH,
    GEAR_CENTERS, build_review, candidate_shape, motion,
    SELECTED_REFERENCE, SENDER_APPROVED_REFERENCE,
)


def moving_names(compiled):
    animated = set(evaluate_animation(compiled['animations'][0], 0))
    result = set()
    def visit(elements, parent_moves=False):
        for element in elements:
            active = parent_moves or element['name'] in animated
            if active:
                result.add(element['name'])
            visit(element.get('children', []), active)
    visit(compiled['elements'])
    return result


def overlaps(first, second):
    if not first or not second:
        return []
    low, high = np.array([b[5] for b in second]), np.array([b[6] for b in second])
    result = []
    for a in first:
        for i in np.where(np.all(np.minimum(high, a[6]) > np.maximum(low, a[5]), axis=1))[0]:
            if intersects(a, second[i]):
                result.append((a[0], second[i][0]))
    return result


class PneumaticSenderReviewTests(unittest.TestCase):
    def test_selected_candidate_replaces_one_managed_path_without_runtime_writes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            sentinel = root / 'assets/gearwright/sentinel'
            sentinel.parent.mkdir(parents=True)
            sentinel.write_text('unchanged')
            first = build_review(root)
            (first / 'stale').write_text('stale')
            current = build_review(root)
            self.assertEqual((root / REVIEW_ROOT).resolve(), current.resolve())
            self.assertFalse((current / 'stale').exists())
            self.assertFalse(current.with_name('.current-build').exists())
            self.assertFalse(current.with_name('.current-old').exists())
            self.assertEqual('unchanged', sentinel.read_text())
            self.assertEqual(len(DESCRIPTIONS) * len(STATES), len(list(current.rglob('*.shape.json'))))
            manifest = json.loads((current / '.gearwright-review.json').read_text())
            self.assertEqual('approved', manifest['decision']['status'])
            self.assertEqual(['a-rack-lift'], manifest['candidates'])
            self.assertEqual(SELECTED_REFERENCE, manifest['decision']['basisReference'])
            self.assertTrue(manifest['decision']['runtimePromotion'])
            self.assertEqual(SENDER_APPROVED_REFERENCE, manifest['decision']['approvedReference'])
            self.assertEqual(64, len(manifest['styleReference']['sha256']))
            self.assertNotIn(str(root), (current / '.gearwright-review.json').read_text())

    def test_approved_narrow_sender_reference_is_preserved(self):
        compiled = compile_shape(candidate_shape('a-rack-lift'))
        self.assertEqual(SENDER_APPROVED_REFERENCE['sha256'], hashlib.sha256(json_bytes(compiled)).hexdigest())

    def test_cycle_lifts_launches_and_returns_without_cargo_teleport(self):
        for candidate in DESCRIPTIONS:
            self.assertAlmostEqual(0, motion(candidate, 0)['lift'])
            self.assertAlmostEqual(STROKE, motion(candidate, 150)['lift'])
            self.assertAlmostEqual(STROKE, motion(candidate, 250)['lift'])
            self.assertAlmostEqual(0, motion(candidate, 360)['lift'])
            self.assertGreater(motion(candidate, 360)['cargo'][0] - 1.2, 16)
            for frame in range(1, 361):
                current, previous = motion(candidate, frame), motion(candidate, frame - 1)
                self.assertLess(np.linalg.norm(np.array(current['cargo']) - previous['cargo']), .26)
                if frame <= 180:
                    self.assertAlmostEqual(current['cargo'][1] - 1.2, DECK_Y + DECK_DEPTH + current['lift'])
                if frame >= 240:
                    self.assertEqual(current['cargo'], motion(candidate, 240)['cargo'])

    def test_mainline_clear_when_parked_and_machine_stays_inside_one_block(self):
        center, half = np.array((.5, .5, .5)), np.array((.5, .075, .075))
        swept = ('mainline', '', center, np.eye(3), half, center - half, center + half)
        for candidate in DESCRIPTIONS:
            parked = compile_shape(candidate_shape(candidate, 'pass-through'))
            self.assertEqual([], overlaps([swept], posed_boxes(parked, 0)), candidate)
            compiled = compile_shape(candidate_shape(candidate))
            for frame in range(0, 361, 6):
                for box in posed_boxes(compiled, frame):
                    if box[0].startswith('cargo-'):
                        continue
                    self.assertTrue(np.all(box[5] >= -1e-7), (candidate, frame, box[0], box[5]))
                    self.assertTrue(np.all(box[6] <= 1 + 1e-7), (candidate, frame, box[0], box[6]))

    def test_cargo_clears_every_part_during_lift_launch_and_return(self):
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate))
            for frame in range(0, 361, 3):
                boxes = posed_boxes(compiled, frame)
                cargo = [b for b in boxes if b[0] == 'cargo-envelope']
                machine = [b for b in boxes if not b[0].startswith('cargo-')]
                self.assertEqual([], overlaps(cargo, machine), (candidate, frame))

    def test_temporal_teeth_drive_the_actual_lift_at_correct_ratio(self):
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate))
            elements = {e['name']: e for e in compiled['elements']}
            count = 10
            for name, teeth, material in (('temporal-drive', 8, '#temporal'), ('iron-drive', count, '#gear-iron')):
                tips = [e for e in elements[name]['children'] if e['name'].endswith('-tip')]
                self.assertEqual(teeth, len(tips))
                for tip in tips:
                    self.assertEqual({material}, {f['texture'] for f in tip['faces'].values()})
                    self.assertAlmostEqual(.8, tip['to'][2] - tip['from'][2])
            for frame in range(0, 361, 3):
                pose = evaluate_animation(compiled['animations'][0], frame)
                self.assertAlmostEqual(pose['temporal-drive']['rotationZ'] * 8, -pose['iron-drive']['rotationZ'] * count)
                theta = pose['iron-drive']['rotationZ']
                dy = pose['carriage']['offsetY']
                self.assertAlmostEqual(math.radians(theta) * 2, dy)

    def test_gears_and_rack_clear_their_independently_moving_mates(self):
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate, 'mechanism'))
            for frame in range(0, 361, 3):
                boxes = posed_boxes(compiled, frame)
                temporal = [b for b in boxes if b[0].startswith('temporal-drive-')]
                iron = [b for b in boxes if b[0].startswith('iron-drive-')]
                self.assertEqual([], overlaps(temporal, iron), (candidate, frame, 'gear mesh'))
                rack = [b for b in boxes if b[0].startswith(('rack-tooth-', 'rack-backbone'))]
                self.assertEqual([], overlaps(iron + temporal, rack), (candidate, frame, 'rack mesh'))

    def test_moving_parts_clear_fixed_glass_supports_and_bearing_apertures(self):
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate))
            moving = moving_names(compiled)
            for frame in range(0, 361, 3):
                boxes = posed_boxes(compiled, frame)
                fixed = [b for b in boxes if b[0] not in moving]
                moving_boxes = [b for b in boxes if b[0] in moving and not b[0].startswith('cargo-')]
                self.assertEqual([], overlaps(moving_boxes, fixed), (candidate, frame))

    def test_narrow_branch_matches_collar_without_shrinking_tray(self):
        boxes = {b[0]: b for b in posed_boxes(compile_shape(candidate_shape('a-rack-lift')), 0)}
        left, right, back = (boxes[name] for name in ('loader-left', 'loader-right', 'glass-back-load'))
        front = boxes['glass-front-load-left']
        self.assertAlmostEqual(5.2, (right[6][0] - left[5][0]) * 16)
        self.assertAlmostEqual(5.2, (back[6][2] - front[5][2]) * 16)
        self.assertAlmostEqual(left[5][0], boxes['inventory-mouth-left'][5][0])
        self.assertAlmostEqual(right[6][0], boxes['inventory-mouth-right'][6][0])
        tray = boxes['tray-floor']
        np.testing.assert_allclose((tray[6] - tray[5])[[0, 2]] * 16, (3.3, 3.4))
        self.assertAlmostEqual(.8, (tray[5][0] - left[6][0]) * 16)
        self.assertAlmostEqual(.8, (right[5][0] - tray[6][0]) * 16)
        self.assertAlmostEqual(.75, (tray[5][2] - front[6][2]) * 16)
        self.assertAlmostEqual(.75, (back[5][2] - tray[6][2]) * 16)


if __name__ == '__main__':
    unittest.main()
