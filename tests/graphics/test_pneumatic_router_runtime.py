"""Approved router promotion and clearance over every possible table heading."""
import copy
import hashlib
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'tools/graphics'))
from gearwright_graphics.compiler import compile_shape, json_bytes
from graphics.models.pneumatic_router import APPROVED_REFERENCE, hardware
from graphics.review.pneumatic_router import candidate_shape
from graphics.review.lateral_drive_clearance import posed_boxes
from tests.graphics.test_pneumatic_sender_review import overlaps, moving_names


class PneumaticRouterRuntimeTests(unittest.TestCase):
    def test_approved_reference_and_promotion_changes_are_explicit(self):
        source = candidate_shape('a-single-door')
        self.assertEqual(APPROVED_REFERENCE['sha256'], hashlib.sha256(json_bytes(compile_shape(source))).hexdigest())
        runtime = hardware()
        original = {e.name: e for e in source.elements}
        for element in runtime.elements:
            if element.name.startswith('router-cap-') or element.name.endswith('-valve-pivot'):
                continue
            if element.name.startswith('base-crossbeam-') or element.name in ('base-index-beam', 'base-table-beam', 'base-table-beam-right'):
                continue  # Explicit repair, verified by the solid intersection check below.
            if element.name.startswith('bypass-') and element.name.endswith(('-glass-outer', '-glass-before', '-glass-after')):
                self.assertEqual(original[element.name].faces, element.faces)
                self.assertEqual(original[element.name].parent, element.parent)
                if element.name.endswith('-glass-outer'):
                    self.assertAlmostEqual(-7.39, element.from_.x)
                    self.assertEqual(original[element.name].to, element.to)
                else:
                    self.assertEqual(original[element.name].from_, element.from_)
                    self.assertAlmostEqual(3.18, element.to.y)
                continue
            if element.name.endswith(('-isolation-valve', '-valve-handle')):
                self.assertEqual(original[element.name].faces, element.faces)
                self.assertTrue(element.parent.endswith('-valve-pivot'))
            else:
                self.assertEqual(original[element.name], element, element.name)
        self.assertFalse(any(e.group == 'cargo' or e.name.startswith('context-') for e in runtime.elements))
        self.assertEqual(source.textures, runtime.textures)
        self.assertEqual(4, sum(e.name.startswith('router-cap-') for e in runtime.elements))
        compile_shape(runtime)

    def test_base_panels_have_no_overlapping_coplanar_surfaces(self):
        elements = [e for e in hardware().elements if e.name.startswith('base-') and
                    any(face.texture == '#oak' for face in e.faces.values())]
        for i, a in enumerate(elements):
            for b in elements[i + 1:]:
                extents = [min(getattr(a.to, axis), getattr(b.to, axis)) -
                           max(getattr(a.from_, axis), getattr(b.from_, axis)) for axis in ('x', 'y', 'z')]
                self.assertFalse(all(size > 1e-6 for size in extents), (a.name, b.name))

    def test_closed_carriage_sweeps_all_headings_clear_of_ports_and_header(self):
        compiled = compile_shape(hardware())
        moving = moving_names(compiled)
        animation = compiled['animations'][0]
        first = copy.deepcopy(animation['keyframes'][0]['elements'])
        for angle in range(0, 361, 5):
            pose = copy.deepcopy(first)
            pose['cassette']['rotationY'] = angle
            pose['gate-in']['offsetY'] = 0
            for n in range(1, 5):
                pose[f'port-{n}-shutter']['offsetY'] = 0
            animation['keyframes'] = [{'frame': 0, 'elements': pose}]
            boxes = posed_boxes(compiled, 0)
            active = [b for b in boxes if b[0] in moving]
            fixed = [b for b in boxes if b[0] not in moving and b[0].startswith(('port-', 'bypass-'))]
            self.assertEqual([], overlaps(active, fixed), angle)

    def test_runtime_ports_have_real_numbered_marks_and_independent_valves(self):
        shape = hardware()
        names = {e.name for e in shape.elements}
        for n in range(1, 5):
            self.assertEqual(n, sum(name.startswith(f'port-{n}-label-') for name in names))
            self.assertIn(f'port-{n}-valve-pivot', names)
            self.assertIn(f'port-{n}-shutter', names)


if __name__ == '__main__':
    unittest.main()
