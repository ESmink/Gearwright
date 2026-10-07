"""Approved hardware promotion and rendering contracts for the first playable line."""
import hashlib
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'tools/graphics'))
from gearwright_graphics.compiler import compile_shape, json_bytes
from graphics.models.pneumatic_transport import APPROVALS, PORT_APPROVALS, base_hardware, hardware, sender, receiver, accumulator
from graphics.review.pneumatic_inventory_ports import shape_for


class PneumaticRuntimeModelTests(unittest.TestCase):
    def test_approved_geometry_and_motion_survive_runtime_promotion(self):
        for kind, source in (('sender', sender('a-rack-lift')), ('receiver', receiver('a1-return-cam')),
                             ('accumulator', accumulator('a-leather-bellows'))):
            compiled = compile_shape(source)
            self.assertEqual(APPROVALS[kind], hashlib.sha256(json_bytes(compiled)).hexdigest())
            runtime = base_hardware(kind)
            hardware_elements = [e for e in source.elements if e.group != 'cargo' and 'representative-cargo' not in e.name]
            self.assertEqual(hardware_elements, runtime.elements)
            self.assertFalse(any(e.group == 'cargo' for e in runtime.elements))
            self.assertEqual(source.textures, runtime.textures)
            for a, b in zip(source.animations, runtime.animations):
                self.assertEqual(a.quantityframes, b.quantityframes)
                for original, promoted in zip(a.keyframes, b.keyframes):
                    for name, values in promoted.elements.items():
                        self.assertEqual(original.elements[name], values)
            compile_shape(runtime)

    def test_approved_brass_sleeve_reference_survives_promotion(self):
        for state, digest in PORT_APPROVALS.items():
            source = shape_for('a-brass-sleeve', state)
            approved = shape_for('a-brass-sleeve', state, repaired=False)
            self.assertEqual(digest, hashlib.sha256(json_bytes(compile_shape(approved))).hexdigest(), state)
            runtime = hardware('straight-terminal' if state == 'terminal' else state, reinforced=False)
            self.assertEqual([e for e in source.elements if e.group != 'cargo' and 'representative-cargo' not in e.name], runtime.elements)
            self.assertEqual(source.textures, runtime.textures)
            for a, b in zip(source.animations, runtime.animations):
                for original, promoted in zip(a.keyframes, b.keyframes):
                    for name, values in promoted.elements.items():
                        self.assertEqual(original.elements[name], values)
            compile_shape(runtime)

    def test_both_tube_forms_have_empty_bores(self):
        for kind in ('straight', 'elbow'):
            shape = hardware(kind)
            self.assertFalse(any(e.group == 'cargo' for e in shape.elements))
            self.assertTrue(any(e.group == 'glass' for e in shape.elements))
            self.assertTrue(any(e.name.startswith('input-') for e in shape.elements))
            self.assertTrue(any(e.name.startswith('output-') for e in shape.elements))
            compile_shape(shape)


if __name__ == '__main__':
    unittest.main()
