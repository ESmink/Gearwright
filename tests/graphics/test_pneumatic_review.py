"""Approval boundary, open cargo paths and candidate pose geometry."""
from pathlib import Path
import json
import sys
import tempfile
import unittest

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'tools/graphics'))
from gearwright_graphics.compiler import compile_shape
from graphics.review.pneumatic_direct_line import DESCRIPTIONS, STATES, REVIEW_ROOT, build_review, candidate_shape


def overlaps(first, second):
    return np.all(np.minimum(first.to.values(), second.to.values()) -
                  np.maximum(first.from_.values(), second.from_.values()) > 1e-8)


class PneumaticReviewTests(unittest.TestCase):
    def test_candidates_stay_in_one_managed_review_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            protected = root / 'assets/gearwright/untouched.txt'
            protected.parent.mkdir(parents=True)
            protected.write_text('runtime sentinel')
            first = build_review(root)
            (first / 'stale.txt').write_text('old review output')
            second = build_review(root)
            self.assertEqual(first.resolve(), (root / REVIEW_ROOT).resolve())
            self.assertEqual(first.resolve(), second.resolve())
            self.assertFalse((second / 'stale.txt').exists())
            self.assertFalse(second.with_name('.current-build').exists())
            self.assertFalse(second.with_name('.current-old').exists())
            self.assertEqual('runtime sentinel', protected.read_text())
            manifest = json.loads((second / '.gearwright-review.json').read_text())
            self.assertEqual('pending', manifest['decision']['status'])
            self.assertFalse(manifest['decision']['runtimePromotion'])
            self.assertEqual(list(DESCRIPTIONS), manifest['candidates'])
            self.assertEqual(len(DESCRIPTIONS) * len(STATES), len(list(second.rglob('*.shape.json'))))
            self.assertNotIn(str(root), (second / '.gearwright-review.json').read_text())

    def test_every_component_fits_one_block_and_uses_resolved_logical_materials(self):
        for candidate in DESCRIPTIONS:
            for state in STATES:
                shape = candidate_shape(candidate, state)
                compile_shape(shape)
                self.assertTrue(all(':' in texture.location for texture in shape.textures.values()))
                if state == 'line':
                    continue
                for element in shape.elements:
                    if any(element.rotation.values()):
                        continue  # Clock teeth and chevrons have explicit pivots; checked by renders.
                    self.assertTrue(all(value >= 0 for value in element.from_.values()), (candidate, state, element.name))
                    self.assertTrue(all(value <= 16 for value in element.to.values()), (candidate, state, element.name))

    def test_cargo_clears_fixed_housing_at_every_loading_pose(self):
        for candidate in DESCRIPTIONS:
            for phase in np.linspace(0, 1, 41):
                shape = candidate_shape(candidate, 'sender-loading', phase=float(phase))
                load = next(e for e in shape.elements if e.group == 'cargo')
                for element in shape.elements:
                    if element.group not in ('housing', 'port', 'glass', 'moving-guide') or any(element.rotation.values()):
                        continue
                    self.assertFalse(overlaps(load, element), (candidate, phase, element.name))

    def test_sender_has_two_tube_ends_and_receiver_has_one(self):
        for candidate in DESCRIPTIONS:
            sender = candidate_shape(candidate, 'sender-rest')
            receiver = candidate_shape(candidate, 'receiver-ready')
            self.assertTrue(any(e.name.startswith('output-') for e in sender.elements))
            self.assertFalse(any(e.name.startswith('output-') for e in receiver.elements))
            self.assertTrue(any(e.name == 'end-cap' for e in receiver.elements))
            for shape in (sender, receiver):
                self.assertTrue(any(e.name.startswith('inventory-collar-') for e in shape.elements))
                self.assertTrue(any(e.name.startswith('temporal-tooth-') for e in shape.elements))
            for state in ('intake', 'straight-empty', 'straight-loaded', 'elbow'):
                self.assertFalse(any(e.name.startswith('temporal-') for e in candidate_shape(candidate, state).elements))

    def test_idle_sender_leaves_the_mainline_clear(self):
        for candidate in DESCRIPTIONS:
            shape = candidate_shape(candidate, 'sender-rest')
            # Swept box of a 2.6-unit representative item across the entire tube.
            from dataclasses import replace
            from gearwright_graphics.model import Vec3
            swept = replace(shape.elements[0], from_=Vec3(0, 6.7, 6.7), to=Vec3(16, 9.3, 9.3))
            for element in shape.elements:
                if any(element.rotation.values()) or element.group in ('clockwork', 'direction'):
                    continue
                self.assertFalse(overlaps(swept, element), (candidate, element.name))


if __name__ == '__main__':
    unittest.main()
