"""Link closure, review ownership and runtime attachment contracts."""
from pathlib import Path
import copy
import json
import math
import sys
import tempfile
import unittest
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'tools/graphics'))
from gearwright_graphics.compiler import compile_shape
from graphics.models.large_bellows import build, DYNAMIC
from graphics.review.large_bellows import STATES, HINGE, build_review, candidate_shape, reservoir_cycle, rocker_state
from graphics.review.large_bellows_top import TOP_ROCKER, TOP_OUTPUT, lift_length, lower_angle, top_state
from graphics.review.large_bellows_frame import build_review as build_frame_review
from graphics.review.lateral_drive_clearance import posed_boxes


def source_fixture():
    # Original synthetic hinge. The review audit checks the installed body too.
    return {'textureWidth': 16, 'textureHeight': 16,
        'textures': {'plainoak': 'block/wood/plainoak'},
        'elements': [{'name': 'BottomPlatePIVOT', 'from': [3.9095, 7.9055, 1],
            'to': [29.9095, 8.4055, 15], 'rotationOrigin': [*HINGE, 1], 'rotationZ': -12,
            'faces': {'north': {'texture': '#plainoak', 'uv': [0, 0, 16, 1]}},
            'children': [{'name': 'plate-anchor-marker', 'from': [20.8, -1.1, 6.9],
                          'to': [21, -.9, 7.1], 'faces': {}}]}],
        'animations': [{'keyframes': [{'elements': {'BottomPlatePIVOT': {'rotationZ': 0}}}]}]}


class LargeBellowsReviewTests(unittest.TestCase):
    def test_links_close_and_repeat_in_both_directions(self):
        for top in (False, True):
            travel = []
            for phase in range(-360, 721):
                journal, pin, output, plate, _, value = (top_state if top else rocker_state)(phase)
                self.assertAlmostEqual(7, math.dist(journal, pin), places=10)
                self.assertAlmostEqual(lift_length() if top else 6.6, math.dist(output, plate), places=10)
                self.assertAlmostEqual(TOP_OUTPUT if top else 4.32,
                    math.dist(TOP_ROCKER if top else (16.8, -2), output), places=10)
                np.testing.assert_allclose(plate, (top_state if top else rocker_state)(phase + 360)[3], atol=1e-10)
                travel.append(value)
            self.assertGreater(min(travel), 0 if top else -12)
            self.assertLess(max(travel), 1 if top else -1.5)
            self.assertGreater(max(travel) - min(travel), .8 if top else 7)

    def test_compiled_bearings_follow_the_crank_and_board(self):
        for state in STATES:
            compiled = compile_shape(candidate_shape(source_fixture(), 'a-reinforced-wood', state))
            top = state == 'top-through-shaft'
            lane = .7 if state == 'shared-with-pump' else 0
            for phase in range(0, 361, 15):
                boxes = {box[0]: box for box in posed_boxes(compiled, phase)}
                journal, pin, output, plate, _, _ = (top_state if top else rocker_state)(phase)
                big = (boxes['gw-bellows-big-end-top'][2] + boxes['gw-bellows-big-end-bottom'][2]) / 2
                small = (boxes['gw-bellows-small-end-top'][2] + boxes['gw-bellows-small-end-bottom'][2]) / 2
                np.testing.assert_allclose(big, np.array((*journal, 8 - lane)) / 16, atol=1e-9)
                np.testing.assert_allclose(big[:2], boxes['gw-crank-bronze-journal'][2][:2], atol=1e-9)
                np.testing.assert_allclose(small[:2], np.array(pin) / 16, atol=1e-9)
                np.testing.assert_allclose(small[:2], boxes['gw-bellows-rocker-pin-input'][2][:2], atol=1e-9)
                np.testing.assert_allclose(boxes['gw-bellows-rocker-pin-output'][2][:2], np.array(output) / 16, atol=1e-9)
                for side in ('front', 'back'):
                    low = (boxes[f'gw-bellows-lift-eye-{side}-low-top'][2] + boxes[f'gw-bellows-lift-eye-{side}-low-bottom'][2]) / 2
                    high = (boxes[f'gw-bellows-lift-eye-{side}-high-top'][2] + boxes[f'gw-bellows-lift-eye-{side}-high-bottom'][2]) / 2
                    np.testing.assert_allclose(low[:2], np.array(plate if top else output) / 16, atol=1e-9)
                    np.testing.assert_allclose(high[:2], np.array(output if top else plate) / 16, atol=1e-9)
                if not top:
                    np.testing.assert_allclose(boxes['plate-anchor-marker'][2], np.array((*plate, 8)) / 16, atol=1e-9)
                if state == 'shared-with-pump':
                    pump = (boxes['gw-connecting-rod-crank-bearing-top'][2] + boxes['gw-connecting-rod-crank-bearing-bottom'][2]) / 2
                    np.testing.assert_allclose(big[:2], pump[:2], atol=1e-9)
                    self.assertAlmostEqual(1.4, abs(big[2] - pump[2]) * 16)

    def test_upper_reservoir_supplies_the_lower_intake_stroke(self):
        angles = [rocker_state(p)[-1] for p in range(361)]
        volume = -np.sin(np.radians(angles))
        capacity = 2 * (max(volume) - min(volume))
        upper, intake, incoming, outflow = reservoir_cycle(angles)
        self.assertAlmostEqual(upper[0], upper[-1], places=10)
        self.assertGreater(min(upper), 0)
        self.assertLess(max(upper), 1)
        for phase in range(1, 361):
            delta = upper[phase] - upper[phase - 1]
            self.assertAlmostEqual(incoming[phase - 1] - outflow, delta * capacity, places=10)
            if intake[phase]:
                self.assertEqual(0, incoming[phase - 1])
                self.assertLess(delta, 0)

    def test_runtime_contains_only_attachments(self):
        package = build()
        self.assertEqual(2, len(package.outputs))
        for path, shape in package.outputs.items():
            self.assertTrue(path.startswith('assets/gearwright/shapes/block/large-bellows-'))
            self.assertEqual(DYNAMIC, {e.name for e in shape.elements if e.name in DYNAMIC})
            self.assertTrue(all(e.name.startswith('gw-bellows-') for e in shape.elements))
            self.assertFalse(shape.animations)
            self.assertTrue(all(':' in t.location for t in shape.textures.values()))
            compile_shape(shape)

    def test_top_drive_breathes_through_the_lower_chamber(self):
        compiled = compile_shape(candidate_shape(source_fixture(), 'a-reinforced-wood', 'top-through-shaft'))
        poses = [f['elements']['BottomPlatePIVOT']['rotationZ'] for f in compiled['animations'][0]['keyframes']]
        self.assertGreater(max(poses) - min(poses), 6)
        for phase, angle in enumerate(poses):
            self.assertAlmostEqual(angle, lower_angle(top_state(phase)[-1]) + 12)
        self.assertAlmostEqual(poses[0], poses[-1])

    def test_approved_frame_extends_the_native_base_with_horizontal_rails(self):
        top = build().outputs['assets/gearwright/shapes/block/large-bellows-top.json']
        names = {e.name: e for e in top.elements}
        self.assertFalse(any(n.startswith(('gw-bellows-gantry-', 'gw-bellows-foot-', 'gw-bellows-upright-')) for n in names))
        for side in ('north', 'south'):
            rail = names[f'gw-bellows-extension-{side}-base-top-rail-0']
            self.assertEqual((7, 9.5, 14, 11.5), (rail.from_.x, rail.from_.y, rail.to.x, rail.to.y))
            self.assertEqual(0, rail.rotation.z)
        self.assertEqual(15, names['gw-bellows-rocker-pin-fulcrum'].to.z - names['gw-bellows-rocker-pin-fulcrum'].from_.z)

    def test_frame_review_records_the_approved_reference(self):
        with tempfile.TemporaryDirectory() as temporary:
            managed = build_frame_review(Path(temporary), source_fixture())
            decision = json.loads((managed / '.gearwright-review.json').read_text())['decision']
            self.assertEqual('approved', decision['status'])
            self.assertTrue(decision['runtimePromotion'])
            self.assertEqual('rest', decision['approvedState'])
            self.assertEqual([], decision['pendingCandidates'])
            self.assertFalse((managed / 'a-braced-uprights/previous.shape.json').exists())

    def test_review_records_approval_and_replaces_only_its_managed_directory(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            managed = build_review(root, source_fixture())
            (managed / 'stale.txt').write_text('stale')
            self.assertEqual(managed, build_review(root, source_fixture()))
            self.assertFalse((managed / 'stale.txt').exists())
            self.assertFalse((root / 'assets').exists())
            decision = json.loads((managed / '.gearwright-review.json').read_text())['decision']
            self.assertEqual('approved', decision['status'])
            self.assertEqual('a-reinforced-wood', decision['approvedCandidate'])
            self.assertEqual([], decision['pendingCandidates'])
            self.assertTrue(decision['runtimePromotion'])

    def test_rest_pose_and_both_shafts_loop_without_extra_linkage(self):
        for state in STATES:
            compiled = compile_shape(candidate_shape(source_fixture(), 'a-reinforced-wood', state))
            rest = copy.deepcopy(compiled)
            rest['animations'][0]['keyframes'] = [{'frame': 0, 'elements': {}}]
            still = {b[0]: b[2] for b in posed_boxes(rest, 0)}
            end = {b[0]: b[2] for b in posed_boxes(compiled, 360)}
            for box in posed_boxes(compiled, 0):
                np.testing.assert_allclose(box[2], still[box[0]], atol=1e-10)
                np.testing.assert_allclose(box[2], end[box[0]], atol=1e-10)
            self.assertEqual(2, sum(name.startswith('gw-bellows-lift-bar-') for name in still))


if __name__ == '__main__':
    unittest.main()
