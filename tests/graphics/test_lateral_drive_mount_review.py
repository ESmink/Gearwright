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
from graphics.review.lateral_drive_clearance import audit, posed_boxes
from graphics.review.lateral_drive_mount import DESCRIPTIONS, SEATS, STATES, build_review, candidate_shape, seats_for


class LateralDriveMountReviewTests(unittest.TestCase):
    def test_candidates_are_review_only_and_use_one_managed_directory(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            managed = build_review(root)
            self.assertEqual(root / 'generated/lateral-drive-mount-review/current', managed,
                'review candidates must use the one fixed generated/.../current directory')
            (managed / 'stale.txt').write_text('stale')
            self.assertEqual(managed, build_review(root),
                'rebuilding a review must replace the same managed directory')
            self.assertFalse((managed / 'stale.txt').exists(),
                'rebuilding a review must remove stale files from the managed directory')
            self.assertFalse((root / 'assets').exists(),
                'review candidates must never be emitted as runtime assets')
            self.assertFalse((managed.parent / '.current-build').exists(),
                'the temporary review staging directory must not remain after promotion')
            self.assertFalse((managed.parent / '.current-old').exists(),
                'the temporary review backup directory must not remain after promotion')
            manifest = json.loads((managed / '.gearwright-review.json').read_text())
            self.assertEqual('approved', manifest['decision']['status'])
            self.assertTrue(manifest['decision']['runtimePromotion'])
            for candidate in DESCRIPTIONS:
                self.assertEqual(set(STATES), {p.name.removesuffix('.shape.json')
                    for p in (managed / candidate).glob('*.shape.json')})

    def test_four_devices_and_their_own_rods_clear_each_other_through_a_full_turn(self):
        for candidate in DESCRIPTIONS:
            for state in STATES:
                if state == 'drive-detail':
                    continue
                self.assertEqual([], audit(candidate, state=state), (candidate, state))
                seats = [offset for _, offset in seats_for(state)]
                self.assertAlmostEqual(0, sum(seats))
            self.assertEqual([], audit(candidate, through=False), 'one-sided shaft with four devices')

    def test_actual_pipe_and_rotating_shaft_bounds_stay_in_their_own_blocks(self):
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate, 'four-pumps'))
            for phase in range(0, 361, 15):
                for name, owner, center, axes, half, low, high in posed_boxes(compiled, phase):
                    if owner == 'shaft':
                        self.assertGreaterEqual(low.min(), -.5 - 1e-8, (candidate, phase, name))
                        self.assertLessEqual(high.max(), .5 + 1e-8, (candidate, phase, name))
                    if 'gw-pump-wet-body-' not in name and '-end-coupling-' not in name:
                        continue
                    angle = math.radians(int(owner.split('-')[1]))
                    block_center = np.array((0, -math.cos(angle), -math.sin(angle)))
                    self.assertGreaterEqual((low - block_center).min(), -.5 - 1e-8, name)
                    self.assertLessEqual((high - block_center).max(), .5 + 1e-8, name)

    def test_big_ends_follow_one_journal_and_each_small_end_stays_on_its_crosshead(self):
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate, 'four-pumps'))
            for phase in range(0, 361, 15):
                boxes = {box[0]: box for box in posed_boxes(compiled, phase)}
                radians = math.radians(phase)
                for angle, seat in SEATS:
                    prefix = f'device-{angle}-'
                    top = boxes[prefix + 'gw-connecting-rod-crank-bearing-top'][2]
                    bottom = boxes[prefix + 'gw-connecting-rod-crank-bearing-bottom'][2]
                    np.testing.assert_allclose((top + bottom) / 2,
                        np.array((seat, 3 * math.cos(radians), 3 * math.sin(radians))) / 16,
                        atol=1e-9, err_msg=f'{candidate}: journal at {phase}')
                    small_top = boxes[prefix + 'gw-connecting-rod-crosshead-bearing-top'][2]
                    small_bottom = boxes[prefix + 'gw-connecting-rod-crosshead-bearing-bottom'][2]
                    small_center = (small_top + small_bottom) / 2
                    crosshead = boxes[prefix + 'gw-crosshead-pin'][2]
                    np.testing.assert_allclose(small_center[1:], crosshead[1:], atol=1e-9)
                    self.assertAlmostEqual(small_center[0] * 16, seat)


if __name__ == '__main__':
    unittest.main()
