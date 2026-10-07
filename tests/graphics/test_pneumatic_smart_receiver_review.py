"""Review isolation, receiver preservation and Jonas assembly connection safety."""
from __future__ import annotations

import json
import math
from pathlib import Path
import sys
import tempfile
import unittest

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'tools/graphics'))

from gearwright_graphics.compiler import compile_shape
from gearwright_graphics.geometry import triangles
from graphics.models.pneumatic_transport import hardware
from graphics.review.lateral_drive_clearance import intersects, posed_boxes
from graphics.review.pneumatic_smart_receiver import (
    DESCRIPTIONS, REVIEW_ROOT, STATES, build_review, candidate_shape,
)
from graphics.review.pneumatic_stock_printer import (
    CANDIDATE, FEED_DISTANCE, PAPER_TOP, PAPER_WIDTH, ROLL_RADIUS,
    STAMP_FRAMES, printer_motion,
)


class SmartReceiverReviewTests(unittest.TestCase):
    def test_review_replaces_one_directory_without_writing_runtime_assets(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            managed = build_review(root)
            self.assertEqual((root / REVIEW_ROOT).resolve(), managed.resolve())
            (managed / 'stale.txt').write_text('stale')
            build_review(root)
            self.assertFalse((managed / 'stale.txt').exists())
            self.assertFalse((root / 'assets').exists())
            self.assertFalse((managed.parent / '.current-build').exists())
            self.assertFalse((managed.parent / '.current-old').exists())
            manifest = json.loads((managed / '.gearwright-review.json').read_text())
            self.assertEqual('approved', manifest['decision']['status'])
            self.assertTrue(manifest['decision']['runtimePromotion'])
            self.assertEqual(4, manifest['behavior']['rows'])
            for candidate in DESCRIPTIONS:
                self.assertEqual(set(STATES), {p.name.removesuffix('.shape.json')
                    for p in (managed / candidate).glob('*.shape.json')})

    def test_accepted_receiver_and_full_receive_animation_are_unchanged(self):
        accepted = compile_shape(hardware('receiver', reinforced=False))
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate))
            original_elements = [e for e in compiled['elements'] if not e['name'].startswith('stock-')]
            self.assertEqual(accepted['elements'], original_elements, candidate)
            self.assertEqual(accepted['animations'][0], compiled['animations'][0], candidate)
            for key, value in accepted['textures'].items():
                self.assertEqual(value, compiled['textures'][key], (candidate, key))

    def test_new_assembly_fits_block_and_keeps_mainline_and_inventory_bores_clear(self):
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate, 'controls'))
            for face in triangles(compiled):
                points = face.vertices * 16
                self.assertGreaterEqual(float(points.min()), -1e-8, (candidate, face.name))
                self.assertLessEqual(float(points.max()), 16 + 1e-8, (candidate, face.name))
                low, high = points.min(axis=0), points.max(axis=0)
                # Cargo mainline: the seven-unit outer collar encloses this
                # 5.4-square bore through the block, centred at y=z=8.
                if high[1] > 5.3 and low[1] < 10.7:
                    self.assertTrue(high[2] <= 5.3 or low[2] >= 10.7,
                                    (candidate, face.name, 'mainline bore'))
                if low[1] < 1.3:
                    overlap = np.minimum(high[[0, 2]], 10.06) - np.maximum(low[[0, 2]], 5.94)
                    self.assertFalse(np.all(overlap > 1e-8), (candidate, face.name, 'inventory bore'))

    def test_added_mounts_clear_the_working_receiver_through_its_cycle(self):
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate))
            animated_roots = {name for frame in compiled['animations'][0]['keyframes']
                              for name in frame['elements']}
            moving = set()
            def visit(elements, active=False):
                for element in elements:
                    is_moving = active or element['name'] in animated_roots
                    if is_moving:
                        moving.add(element['name'])
                    visit(element.get('children', []), is_moving)
            visit(compiled['elements'])
            for frame in range(0, 361, 12):
                boxes = posed_boxes(compiled, frame)
                additions = [b for b in boxes if b[0].startswith('stock-')]
                lows = np.asarray([b[5] for b in additions])
                highs = np.asarray([b[6] for b in additions])
                for box in boxes:
                    if box[0] not in moving:
                        continue
                    overlaps = np.where(np.all(np.minimum(highs, box[6]) -
                        np.maximum(lows, box[5]) > 1e-8, axis=1))[0]
                    for index in overlaps:
                        self.assertFalse(intersects(box, additions[index]),
                            (candidate, frame, box[0], additions[index][0]))

    def test_printing_finishes_before_feed_and_carriage_return(self):
        for frame in range(121):
            motion = printer_motion(frame)
            if motion['press'] > 0:
                self.assertEqual(0, motion['feed'], frame)
            if 60 < frame < 100:
                self.assertEqual(0, motion['press'], frame)
                self.assertAlmostEqual(1.95, motion['travel'])
        self.assertEqual(0, printer_motion(120)['travel'])
        self.assertAlmostEqual(FEED_DISTANCE, printer_motion(120)['feed'])
        compiled = compile_shape(candidate_shape(CANDIDATE, 'controls'))
        for hit in STAMP_FRAMES:
            boxes = {b[0]: b for b in posed_boxes(compiled, hit)}
            self.assertAlmostEqual(PAPER_TOP, boxes['stock-print-type'][5][1] * 16)
        for frame in range(0, 121, 2):
            boxes = {b[0]: b for b in posed_boxes(compiled, frame)}
            self.assertGreaterEqual(boxes['stock-print-type'][5][1] * 16, PAPER_TOP - 1e-8)

    def test_rotating_spools_and_print_head_clear_housing_through_cycle(self):
        compiled = compile_shape(candidate_shape(CANDIDATE, 'controls'))
        for frame in range(0, 121, 2):
            boxes = posed_boxes(compiled, frame)
            moving = [b for b in boxes if any(part in b[0] for part in
                ('-flange-', 'stock-takeup-paper-', 'stock-supply-paper-',
                 'stock-plunger-', 'stock-print-foot', 'stock-print-type'))]
            fixed = [b for b in boxes if b[0].startswith('stock-hood-')
                     or b[0] == 'stock-printer-floor']
            for a in moving:
                self.assertTrue(np.all(a[5] >= -1e-8) and np.all(a[6] <= 1 + 1e-8), (frame, a[0]))
                for b in fixed:
                    if np.all(np.minimum(a[6], b[6]) - np.maximum(a[5], b[5]) > 1e-8):
                        self.assertFalse(intersects(a, b), (frame, a[0], b[0]))

    def test_narrow_strip_turns_the_spool_axes_and_keeps_ink_under_cover(self):
        compiled = compile_shape(candidate_shape(CANDIDATE, 'controls'))
        web = next(e for e in compiled['elements'] if e['name'] == 'stock-paper-web')
        self.assertAlmostEqual(PAPER_WIDTH, web['to'][2] - web['from'][2])
        self.assertLessEqual(PAPER_WIDTH, 3)
        self.assertGreater(web['to'][0] - web['from'][0], 2 * PAPER_WIDTH)
        rest_ink = [face for face in triangles(compiled) if face.name.startswith('stock-item-stamp-')]
        self.assertTrue(rest_ink)
        self.assertLess(max(float(face.vertices[:, 1].max()) * 16 for face in rest_ink), PAPER_TOP)
        for frame in range(0, 121, 2):
            boxes = {b[0]: b for b in posed_boxes(compiled, frame)}
            for row in range(4):
                ink = boxes['stock-item-stamp-' + str(row)]
                self.assertGreaterEqual(ink[5][0] * 16, 2.69)
                self.assertLessEqual(ink[6][0] * 16, 5.99)
                self.assertGreaterEqual(ink[5][2] * 16, web['from'][2])
                self.assertLessEqual(ink[6][2] * 16, web['to'][2])
                if frame >= STAMP_FRAMES[row]:
                    self.assertGreater(ink[5][1] * 16, PAPER_TOP)
                else:
                    self.assertLess(ink[6][1] * 16, PAPER_TOP)
        keys = compiled['animations'][0]['keyframes'][-1]['elements']
        for roll in ('stock-takeup-roll', 'stock-supply-roll'):
            self.assertAlmostEqual(math.degrees(FEED_DISTANCE / ROLL_RADIUS), keys[roll]['rotationZ'])
            self.assertEqual(0, keys[roll].get('rotationX', 0))

if __name__ == '__main__':
    unittest.main()
