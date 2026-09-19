from __future__ import annotations

import itertools
import json
from pathlib import Path
import sys
import tempfile
import unittest

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'tools/graphics'))

from graphics.review.reciprocating_pump_refinement import (
    CAPACITY, CLEARANCE, DESCRIPTIONS, FLOOR, STATES, STROKE,
    UPPER_PISTON_BOTTOM, build_review, candidate_shape, contact_phase, sample, volume,
)
from graphics.review.lateral_motion_system import _singleacting_pump_shape
from graphics.models.parts.reciprocating_pump_wet_end import DROP_OUTER_X
from graphics.models.reciprocating_pump import build as runtime_package
from gearwright_graphics.animation import apply_animation
from gearwright_graphics.compiler import compile_shape
from gearwright_graphics.geometry import triangles


def overlap(a, b):
    return all(min(a.to.values()[i], b.to.values()[i]) -
               max(a.from_.values()[i], b.from_.values()[i]) > 1e-8 for i in range(3))


class PumpRefinementReviewTests(unittest.TestCase):
    def test_compiled_runtime_pipes_stay_inside_their_block_in_every_orientation(self):
        package = runtime_package()
        axes = np.concatenate((np.eye(3), -np.eye(3)))
        for suffix in ('body', 'body-supported', 'inventory'):
            shape = package.outputs[f'assets/gearwright/shapes/block/reciprocating-pump-{suffix}.json']
            pipe_names = {e.name for e in shape.elements
                          if e.group in ('pump-wet-body', 'pump-input', 'pump-output')}
            vertices = np.concatenate([t.vertices for t in triangles(compile_shape(shape))
                                       if t.name in pipe_names])
            # Same centered basis as PumpOrientation.Matrix: local X is the
            # outlet, local Y points at the drive, Z is their cross product.
            for output, drive in itertools.product(axes, repeat=2):
                if abs(np.dot(output, drive)) > 1e-8:
                    continue
                basis = np.column_stack((output, drive, np.cross(output, drive)))
                oriented = (vertices - .5) @ basis.T + .5
                self.assertGreaterEqual(oriented.min(), -1e-8, (suffix, output, drive))
                self.assertLessEqual(oriented.max(), 1 + 1e-8, (suffix, output, drive))
            # The two terminal rims meet, but never cross, their block faces.
            for side, face in (('input', 0), ('output', 1)):
                collar = np.concatenate([t.vertices for t in triangles(compile_shape(shape))
                                        if t.name.startswith(f'gw-pump-{side}-end-coupling-')])
                self.assertAlmostEqual(face, collar[:, 0].min() if face == 0 else collar[:, 0].max())

    def test_runtime_uses_approved_wet_body_and_leaves_space_behind_both_end_collars(self):
        reviewed = candidate_shape('a-clean-folded-pipes', 'rest')
        body = [e for e in reviewed.elements if e.group == 'pump-wet-body']
        for element in body:
            if element.from_.y < -18 - 1e-8:
                self.assertLessEqual(max(abs(element.from_.x), abs(element.to.x)), DROP_OUTER_X)
        for side in ('input', 'output'):
            collar = next(e for e in reviewed.elements if e.name == f'gw-pump-{side}-end-coupling-bottom')
            collar_inner = min(abs(collar.from_.x), abs(collar.to.x))
            self.assertGreaterEqual(collar_inner - DROP_OUTER_X, .25 - 1e-8)
        runtime = runtime_package().outputs['assets/gearwright/shapes/block/reciprocating-pump-body.json']
        self.assertEqual(body, [e for e in runtime.elements if e.group == 'pump-wet-body'])

    def test_review_has_one_managed_output_and_cannot_promote_itself(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            managed = build_review(root)
            (managed / 'stale.txt').write_text('stale')
            self.assertEqual(managed, build_review(root))
            self.assertFalse((managed / 'stale.txt').exists())
            self.assertFalse((root / 'assets').exists())
            manifest = json.loads((managed / '.gearwright-review.json').read_text())
            self.assertEqual('approved', manifest['decision']['status'])
            self.assertEqual('a-clean-folded-pipes', manifest['decision']['candidate'])
            self.assertEqual(list(DESCRIPTIONS), manifest['candidates'])
            for candidate in DESCRIPTIONS:
                self.assertEqual(set(STATES), {p.name.removesuffix('.shape.json')
                    for p in (managed / candidate).glob('*.shape.json')})

    def test_existing_pipe_overlap_is_removed_without_joining_the_two_bores(self):
        old = _singleacting_pump_shape('a11-one-block-full-frame', label='pump-bottom')
        parts = {e.name: e for e in old.elements}
        self.assertTrue(overlap(parts['gw-pump-input-upper-stub-front'],
                                parts['gw-pump-input-outer-drop-front']))
        for candidate in DESCRIPTIONS:
            shape = candidate_shape(candidate, 'rest')
            body = [e for e in shape.elements if e.group == 'pump-wet-body']
            self.assertTrue(body)
            self.assertFalse(any(overlap(a, b) for a, b in itertools.combinations(body, 2)))
            # Follow both empty passages from their block face to the floor port.
            for direction in (-1, 1):
                path = [(x, -16, 0) for x in (5.5, 6.5, 7.5, 7.99)]
                path += [(6, y, 0) for y in (-16, -18, -20, -21.25)]
                path += [(x, -21.25, 0) for x in (2.5, 3.5, 4.5, 5.5, 6.5)]
                path += [(2.5, y, 0) for y in (-21.25, -20, -19, FLOOR)]
                for x, y, z in path:
                    point = (direction * x, y, z)
                    self.assertFalse(any(all(lo < p < hi for lo, p, hi in
                        zip(e.from_.values(), point, e.to.values())) for e in body), (candidate, point))
            # The central copper divider has exposed faces on both sides.
            center = [e for e in body if abs(e.from_.x) <= 1 and abs(e.to.x) <= 1]
            self.assertTrue(any('east' in e.faces for e in center))
            self.assertTrue(any('west' in e.faces for e in center))
            self.assertFalse(any('shadow' in e.name and e.group in ('pump-input', 'pump-output')
                                 for e in shape.elements))

    def test_water_height_and_chamber_geometry_have_the_same_volume_scale(self):
        self.assertAlmostEqual(STROKE * CLEARANCE / CAPACITY, UPPER_PISTON_BOTTOM - STROKE - FLOOR)
        for frame in range(361):
            pose = sample(frame)
            self.assertAlmostEqual(volume(frame), (pose['piston'] - FLOOR) * CAPACITY / STROKE)
            self.assertLessEqual(pose['surface'], pose['piston'] + 1e-10)
            if frame < contact_phase():
                self.assertEqual(2, pose['amount'])
                self.assertAlmostEqual(FLOOR + 3, pose['surface'])
                self.assertEqual(0, pose['output'])

    def test_blocked_outlet_holds_every_moving_part_and_the_stored_water(self):
        contact = sample(90, blocked=True)
        self.assertAlmostEqual(2, volume(contact['phase']))
        self.assertAlmostEqual(contact['surface'], contact['piston'])
        self.assertEqual(0, contact['output'])
        for frame in range(90, 181):
            self.assertEqual(contact, sample(frame, blocked=True))

    def test_rendered_full_stroke_keeps_piston_clear_of_floor_and_wet_checks(self):
        for candidate in DESCRIPTIONS:
            compiled = compile_shape(candidate_shape(candidate, 'rest'))
            for phase in range(0, 361, 15):
                vertices = {}
                for triangle in triangles(apply_animation(compiled, 'singleacting', phase)):
                    if triangle.name in ('gw-piston-lower-seal', 'gw-review-water-surface') or 'wet-intake-check' in triangle.name:
                        vertices.setdefault(triangle.name, []).extend(triangle.vertices)
                piston_y = np.min(vertices['gw-piston-lower-seal'], axis=0)[1] * 16
                water_y = np.max(vertices['gw-review-water-surface'], axis=0)[1] * 16
                self.assertGreaterEqual(piston_y - FLOOR, .075 - 1e-8)
                self.assertLessEqual(water_y, piston_y + 1e-8)
                for name, points in vertices.items():
                    if 'wet-intake-check' in name:
                        self.assertLess(np.max(points, axis=0)[1] * 16, piston_y)
                self.assertAlmostEqual(sample(phase)['piston'], piston_y, places=8)


if __name__ == '__main__':
    unittest.main()
