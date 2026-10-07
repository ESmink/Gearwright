"""Approved printer promotion, tangent paper seams and thicker receiver linkage."""
import hashlib
import json
import math
from pathlib import Path
import sys
import unittest
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'tools/graphics'))
from gearwright_graphics.compiler import compile_shape, json_bytes
from gearwright_graphics.geometry import triangles
from graphics.models.pneumatic_smart_receiver import APPROVAL, smart_receiver
from graphics.models.parts.jonas_printer import PAPER_TOP, ROLL_RADIUS, FEED_DISTANCE
from graphics.models.pneumatic_transport import hardware
from graphics.review.pneumatic_smart_receiver import candidate_shape
from graphics.review.pneumatic_inventory_ports import moving_fixed_audit
from graphics.review.lateral_drive_clearance import posed_boxes, intersects


class SmartReceiverRuntimeTests(unittest.TestCase):
    def test_approved_reference_and_unrelated_receiver_parts_are_preserved(self):
        approved = compile_shape(candidate_shape('d3-request-printer'))
        self.assertEqual(APPROVAL['sha256'], hashlib.sha256(json_bytes(approved)).hexdigest())
        original = hardware('receiver', reinforced=False)
        runtime = hardware('receiver')
        changed = {a.name for a, b in zip(original.elements, runtime.elements) if a != b}
        self.assertTrue(changed)
        self.assertTrue(all(n.startswith(('cam-rail-', 'cam-back-web-')) or n in
            ('follower-lever', 'weighted-arm', 'brass-return-weight', 'follower-pin') for n in changed))
        self.assertEqual(original.animations, runtime.animations)
        for terminal in (False, True):
            shape = smart_receiver(terminal)
            receiver = hardware('receiver-terminal' if terminal else 'receiver')
            self.assertEqual(receiver.elements, [e for e in shape.elements if not e.name.startswith('stock-')])
            self.assertEqual(receiver.animations[0], shape.animations[0])

    def test_paper_surfaces_meet_exactly_with_continuous_uvs(self):
        compiled = compile_shape(smart_receiver())
        boxes = {b[0]: b for b in posed_boxes(compiled, 0)}
        web, supply, takeup = [boxes[n] for n in ('stock-paper-web', 'stock-supply-paper-0', 'stock-takeup-paper-0')]
        self.assertAlmostEqual(web[5][0], supply[6][0])
        self.assertAlmostEqual(web[6][0], takeup[5][0])
        for box in (web, supply, takeup): self.assertAlmostEqual(PAPER_TOP, box[6][1] * 16)
        shape = smart_receiver()
        parts = {e.name: e for e in shape.elements}
        self.assertAlmostEqual(parts['stock-paper-web'].faces['up'].uv.u0, parts['stock-supply-paper-0'].faces['up'].uv.u1)
        self.assertAlmostEqual(parts['stock-paper-web'].faces['up'].uv.u1, parts['stock-takeup-paper-0'].faces['up'].uv.u0)
        animated = {name for k in shape.animations[1].keyframes for name in k.elements}
        self.assertFalse(any('paper-roll' in name for name in animated))
        self.assertGreater(5.25 + FEED_DISTANCE - .23, 6.09)

    def test_print_cycle_clears_hood_and_keeps_the_web_attached(self):
        shape = smart_receiver()
        shape.elements = [e for e in shape.elements if e.group == 'stock-controls']
        shape.animations = shape.animations[1:]
        compiled = compile_shape(shape)
        reference_web = None
        for frame in range(0, 121, 2):
            boxes = posed_boxes(compiled, frame)
            fixed = [b for b in boxes if b[0].startswith('stock-hood-') or b[0] == 'stock-printer-floor']
            moving = [b for b in boxes if any(token in b[0] for token in ('-flange-', 'stock-plunger-', 'stock-print-foot', 'stock-print-type'))]
            for a in moving:
                for b in fixed:
                    if np.all(np.minimum(a[6], b[6]) > np.maximum(a[5], b[5])):
                        self.assertFalse(intersects(a, b), (frame, a[0], b[0]))
            web = next(b for b in boxes if b[0] == 'stock-paper-web')
            if reference_web is None: reference_web = web[5:7]
            np.testing.assert_allclose(web[5:7], reference_web)

    def test_thicker_linkage_clears_fixed_parts_and_independent_motion(self):
        shape = smart_receiver()
        self.assertEqual(moving_fixed_audit(shape), [])
        compiled = compile_shape(shape)
        for frame in range(0, 361, 3):
            boxes = posed_boxes(compiled, frame)
            cam = [b for b in boxes if b[0].startswith('cam-')]
            drive = [b for b in boxes if b[0].startswith(('temporal-drive-', 'iron-left-feed-', 'brass-feed-'))]
            lever = [b for b in boxes if b[0] in ('follower-lever', 'weighted-arm', 'brass-return-weight')]
            bearings = [b for b in boxes if '-bearing-' in b[0] and not b[0].startswith('stock-')]
            follower = [b for b in boxes if b[0].startswith('follower-roller-')]
            rails = [b for b in cam if b[0].startswith('cam-rail-')]
            for left, right in ((cam, drive), (cam, lever), (lever, bearings), (follower, rails)):
                lows, highs = np.array([b[5] for b in right]), np.array([b[6] for b in right])
                for a in left:
                    for i in np.where(np.all(np.minimum(highs, a[6]) > np.maximum(lows, a[5]), axis=1))[0]:
                        self.assertFalse(intersects(a, right[i]), (frame, a[0], right[i][0]))

    def test_block_and_recipe_are_available_in_both_creative_tabs(self):
        block = json.loads((ROOT / 'assets/gearwright/blocktypes/pneumatic-smart-receiver.json').read_text())
        self.assertEqual(['*'], block['creativeinventory']['general'])
        self.assertEqual(['*'], block['creativeinventory']['gearwright'])
        self.assertEqual('Gearwright', json.loads((ROOT / 'assets/game/lang/en.json').read_text())['tabname-gearwright'])
        recipe = json.loads((ROOT / 'assets/gearwright/recipes/grid/pneumatic-smart-receiver.json').read_text())
        self.assertEqual('gearwright:pneumatic-smart-receiver', recipe['output']['code'])
        self.assertTrue(any(i['code'].startswith('game:jonasframes-') for i in recipe['ingredients'].values()))


if __name__ == '__main__':
    unittest.main()
