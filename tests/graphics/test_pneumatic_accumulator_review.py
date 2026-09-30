"""Selected accumulator: native nozzle fit, full travel, scale and compact clearance."""
from pathlib import Path
import copy
import json
import math
import os
import sys
import tempfile
import unittest

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'tools/graphics'))
from gearwright_graphics.compiler import compile_shape
from gearwright_graphics.animation import evaluate_animation
from graphics.review.lateral_drive_clearance import posed_boxes
from tests.graphics.test_pneumatic_sender_review import moving_names, overlaps
from graphics.review.pneumatic_accumulator import (
    DESCRIPTIONS, STATES, REVIEW_ROOT, PORTS, SELECTED_REFERENCE, build_review, candidate_shape,
    charge, bag_node, BAG_PANELS, BAG_PANEL_LENGTH, BAG_BOTTOM, BAG_LOW, BAG_HIGH,
    LID_THICKNESS, NOZZLE_SIZE, NOZZLE_REACH, NOZZLE_HEIGHT, SOCKET_BORE, SOCKET_DEPTH,
)

CANDIDATE = 'a-leather-bellows'


class PneumaticAccumulatorReviewTests(unittest.TestCase):
    maxDiff = None

    def test_selected_a_replaces_one_review_directory_without_runtime_writes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            sentinel = root / 'assets/gearwright/sentinel'
            sentinel.parent.mkdir(parents=True)
            sentinel.write_text('untouched')
            current = build_review(root)
            old = current / 'b-spring-piston'
            old.mkdir()
            (old / 'obsolete').write_text('old')
            current = build_review(root)
            self.assertEqual((root / REVIEW_ROOT).resolve(), current.resolve())
            self.assertEqual('untouched', sentinel.read_text())
            self.assertFalse(old.exists())
            self.assertFalse(current.with_name('.current-build').exists())
            self.assertFalse(current.with_name('.current-old').exists())
            self.assertEqual(len(STATES), len(list(current.rglob('*.shape.json'))))
            manifest_text = (current / '.gearwright-review.json').read_text()
            manifest = json.loads(manifest_text)
            self.assertEqual('approved', manifest['decision']['status'])
            self.assertTrue(manifest['decision']['runtimePromotion'])
            self.assertEqual(SELECTED_REFERENCE, manifest['decision']['basisReference'])
            self.assertEqual([CANDIDATE], list(DESCRIPTIONS))
            self.assertEqual([CANDIDATE], manifest['candidates'])
            self.assertNotIn(str(root), manifest_text)
            self.assertEqual(PORTS, manifest['ports'])

    def test_three_fixed_air_sockets_match_nozzle_height_and_tube_outlet_stays_centred(self):
        self.assertEqual({'west', 'north', 'south'}, set(PORTS['inlets']))
        self.assertEqual({'east': [16, 8, 8]}, PORTS['outlet'])
        self.assertEqual(['up', 'down'], PORTS['closed'])
        compiled = compile_shape(candidate_shape(CANDIDATE))
        baseline = {b[0]: b for b in posed_boxes(compiled, 0) if b[0].startswith(('in-', 'out-', 'base-'))}
        for frame in (0, 50, 100, 190, 260):
            current = {b[0]: b for b in posed_boxes(compiled, frame)}
            for name, box in baseline.items():
                np.testing.assert_allclose(box[5], current[name][5], atol=1e-9)
                np.testing.assert_allclose(box[6], current[name][6], atol=1e-9)
        for prefix, axis in {'in-west': 0, 'in-north': 2, 'in-south': 2, 'out-east': 0}.items():
            collar = [box for name, box in baseline.items() if name.startswith(prefix + '-collar-')
                      and not name.startswith(prefix + '-collar-band')]
            lo, hi = np.min([b[5] for b in collar], axis=0) * 16, np.max([b[6] for b in collar], axis=0) * 16
            transverse = [i for i in range(3) if i != axis]
            center = (lo + hi) / 2
            self.assertAlmostEqual(8 if prefix == 'out-east' else NOZZLE_HEIGHT, center[1])
            self.assertAlmostEqual(8, center[2 if axis == 0 else 0])
            if prefix == 'out-east':
                np.testing.assert_allclose(lo[transverse], [4.5, 4.5], atol=1e-8)
                np.testing.assert_allclose(hi[transverse], [11.5, 11.5], atol=1e-8)
            self.assertAlmostEqual(16 if prefix in ('in-south', 'out-east') else 0,
                                   hi[axis] if prefix in ('in-south', 'out-east') else lo[axis])

    def test_lid_reaches_full_block_height_and_every_moving_pose_stays_inside(self):
        compiled = compile_shape(candidate_shape(CANDIDATE))
        animation = compiled['animations'][0]
        self.assertEqual(evaluate_animation(animation, 0), evaluate_animation(animation, 260))
        for frame in range(0, 261, 2):
            for box in posed_boxes(compiled, frame):
                self.assertTrue(np.all(box[5] >= -1e-7), (frame, box[0], box[5]))
                self.assertTrue(np.all(box[6] <= 1 + 1e-7), (frame, box[0], box[6]))
                if box[0] == 'bag-lid-weight':
                    self.assertAlmostEqual(BAG_BOTTOM + BAG_LOW + (BAG_HIGH - BAG_LOW) * charge(frame)
                                           + LID_THICKNESS, box[6][1] * 16)
        full = next(b for b in posed_boxes(compiled, 100) if b[0] == 'bag-lid-weight')
        self.assertAlmostEqual(16, full[6][1] * 16)
        self.assertGreater(BAG_HIGH - BAG_LOW, 8)

    def test_default_unanimated_view_matches_the_empty_assembled_pose(self):
        compiled = compile_shape(candidate_shape(CANDIDATE))
        rest = copy.deepcopy(compiled)
        rest['animations'] = [{'code': 'rest', 'quantityframes': 1,
                               'keyframes': [{'frame': 0, 'elements': {}}]}]
        empty = {b[0]: b for b in posed_boxes(compiled, 0)}
        for box in posed_boxes(rest, 0):
            np.testing.assert_allclose(box[2], empty[box[0]][2], atol=1e-8)
            np.testing.assert_allclose(box[3], empty[box[0]][3], atol=1e-8)

    def test_five_fold_pairs_keep_their_length_and_join_base_to_lid(self):
        self.assertEqual(10, BAG_PANELS)
        for frame in range(261):
            fill = charge(frame)
            nodes = [bag_node(i, fill) for i in range(BAG_PANELS + 1)]
            for first, second in zip(nodes, nodes[1:]):
                self.assertAlmostEqual(BAG_PANEL_LENGTH, math.dist(first, second))
            self.assertEqual(BAG_BOTTOM, nodes[0][1])
            self.assertAlmostEqual(BAG_BOTTOM + BAG_LOW + (BAG_HIGH - BAG_LOW) * fill, nodes[-1][1])
        compiled = compile_shape(candidate_shape(CANDIDATE))
        for frame in (0, 17.5, 50, 83.5, 100, 190, 260):
            folds = {b[0]: b for b in posed_boxes(compiled, frame) if b[0].startswith('bag-leather-')}
            for side in range(8):
                endpoints = []
                for panel in range(BAG_PANELS):
                    box = folds[f'bag-leather-{side}-{panel}']
                    half = box[3][:, 1] * BAG_PANEL_LENGTH / 32
                    endpoints.append((box[2] - half, box[2] + half))
                for first, second in zip(endpoints, endpoints[1:]):
                    self.assertLess(np.linalg.norm(first[1] - second[0]) * 16, .006)

    def test_moving_bag_lid_and_scale_reader_clear_the_fixed_housing(self):
        compiled = compile_shape(candidate_shape(CANDIDATE))
        animated = moving_names(compiled)
        conflicts = []
        for frame in range(0, 261, 2):
            boxes = posed_boxes(compiled, frame)
            moving = [b for b in boxes if b[0] in animated]
            fixed = [b for b in boxes if b[0] not in animated]
            for first, second in overlaps(moving, fixed):
                if first.startswith('bag-leather-') and first.endswith('-0') and second == 'base-reservoir-seat':
                    continue
                if len(conflicts) < 12:
                    conflicts.append((frame, first, second))
        self.assertEqual([], conflicts)

    def test_larger_bag_closes_the_space_beside_the_collectors(self):
        boxes = posed_boxes(compile_shape(candidate_shape(CANDIDATE)), 0)
        leather = [b for b in boxes if b[0].startswith('bag-leather-')]
        low, high = np.min([b[5] for b in leather], axis=0) * 16, np.max([b[6] for b in leather], axis=0) * 16
        self.assertGreater(high[0] - low[0], 8.7)
        for prefix, axis, high_side in (('in-west', 0, False), ('in-north', 2, False), ('in-south', 2, True)):
            collector = next(b for b in boxes if b[0] == prefix + '-collector-back')
            gap = collector[5][axis] * 16 - high[axis] if high_side else low[axis] - collector[6][axis] * 16
            self.assertGreater(gap, .04)
            self.assertLess(gap, .25)

    def test_scale_is_a_supported_guide_with_a_lid_attached_reader(self):
        compiled = compile_shape(candidate_shape(CANDIDATE))
        for frame in range(0, 261, 5):
            boxes = {b[0]: b for b in posed_boxes(compiled, frame)}
            self.assertEqual(21, len([n for n in boxes if n.startswith('reserve-tick-')]))
            index, lid = boxes['reserve-reader-line'], boxes['bag-lid-plate']
            self.assertAlmostEqual(.20, (index[2][1] - lid[5][1]) * 16)
            self.assertLess(index[6][2], boxes['reserve-scale'][5][2])
            self.assertLess(boxes['reserve-scale-foot'][5][1], boxes['reserve-scale'][5][1])

    def test_measured_nozzle_fits_through_the_entire_socket_without_intersection(self):
        self.assertAlmostEqual(.05, (SOCKET_BORE - NOZZLE_SIZE) / 2)
        self.assertAlmostEqual(.15, SOCKET_DEPTH - NOZZLE_REACH)
        compiled = compile_shape(candidate_shape(CANDIDATE, 'connected-bellow'))
        for frame in range(0, 261, 4):
            boxes = posed_boxes(compiled, frame)
            nozzle = [b for b in boxes if b[0].startswith('context-nozzle')]
            machine = [b for b in boxes if not b[0].startswith('context-')]
            self.assertEqual([], overlaps(nozzle, machine), frame)

    def test_installed_automatic_bellow_nozzle_matches_the_measured_interface(self):
        game = Path(os.environ.get('VINTAGE_STORY', Path(os.environ.get('APPDATA', '')) / 'Vintagestory'))
        if not (game / 'assets').is_dir():
            self.skipTest('Installed game assets are unavailable for the native nozzle reference')
        from graphics.review.large_bellows import read_source
        compiled = compile_shape(candidate_shape(CANDIDATE, 'connected-bellow', bellow_document=read_source(game)))
        boxes = posed_boxes(compiled, 0)
        nozzle = [b for b in boxes if b[0] in {'context-nozzle2', 'context-nozzle3', 'context-nozzle4', 'context-nozzle5'}]
        self.assertEqual(4, len(nozzle))
        np.testing.assert_allclose(np.min([b[5] for b in nozzle], axis=0) * 16, [0, 9.65, 7.25], atol=1e-7)
        np.testing.assert_allclose(np.max([b[6] for b in nozzle], axis=0) * 16, [3, 11.15, 8.75], atol=1e-7)
        machine = [b for b in boxes if not b[0].startswith('context-')]
        self.assertEqual([], overlaps(nozzle, machine))


if __name__ == '__main__':
    unittest.main()
