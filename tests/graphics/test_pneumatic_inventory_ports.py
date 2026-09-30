"""Approved port geometry, clearance, and managed review output boundaries."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / 'tools/graphics'))
from gearwright_graphics.compiler import compile_shape
from graphics.models.pneumatic_transport import hardware
from graphics.review.pneumatic_inventory_ports import build_review, moving_fixed_audit, glass_surface_contacts, shape_for
from graphics.review.lateral_drive_clearance import posed_boxes


class PneumaticInventoryPortTests(unittest.TestCase):
    def test_review_replaces_only_its_managed_directory(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp).resolve()
            sentinel = root / 'assets/gearwright/shapes/block/pneumatic-sender.json'
            sentinel.parent.mkdir(parents=True)
            sentinel.write_text('runtime must remain untouched')
            managed = build_review(root)
            (managed / 'stale.txt').write_text('retire this')
            self.assertEqual(managed, build_review(root))
            self.assertFalse((managed / 'stale.txt').exists())
            self.assertEqual(sentinel.read_text(), 'runtime must remain untouched')
            manifest = json.loads((managed / '.gearwright-review.json').read_text())
            self.assertEqual(manifest['candidates'], ['a-brass-sleeve'])
            self.assertEqual(manifest['decision']['status'], 'approved')
            self.assertEqual(managed, root / manifest['managedPath'])

    def test_branches_are_opaque_and_spigots_reach_native_chest(self):
        for kind in ('sender', 'receiver'):
            shape = hardware(kind)
            branch = [e for e in shape.elements if e.group == 'branch']
            self.assertTrue(branch)
            self.assertTrue(all(e.render_pass == 0 and all(f.texture == '#brass' for f in e.faces.values()) for e in branch))
            self.assertTrue(any(e.render_pass == 1 and e.group == 'glass' for e in shape.elements))
            spigot = [e for e in shape.elements if e.name.startswith('inventory-spigot')]
            self.assertEqual(min(e.from_.y for e in spigot), -2.25)
            self.assertTrue(any(e.group == 'occlusion' for e in shape.elements))

    def test_receiver_full_cycle_clears_fixed_hardware_and_glass(self):
        self.assertEqual(moving_fixed_audit(shape_for('a-brass-sleeve', 'receiver')), [])

    def test_sender_initial_cargo_is_beneath_dark_throat(self):
        shape = shape_for('a-brass-sleeve', 'sender')
        boxes = posed_boxes(compile_shape(shape), 0)
        cargo = [b for b in boxes if b[0].startswith('cargo-')]
        self.assertTrue(cargo)
        mask = next(e for e in shape.elements if e.name == 'sender-throat-shadow')
        self.assertLess(max(b[6][1] for b in cargo) * 16, mask.from_.y)
        for b in cargo:
            self.assertGreaterEqual(b[5][0] * 16, mask.from_.x)
            self.assertLessEqual(b[6][0] * 16, mask.to.x)
            self.assertGreaterEqual(b[5][2] * 16, mask.from_.z)
            self.assertLessEqual(b[6][2] * 16, mask.to.z)

    def test_terminal_fitting_follows_the_canonical_exit(self):
        for kind, axis in (('straight-terminal', 0), ('elbow-terminal', 1), ('sender-terminal', 0), ('receiver-terminal', 0)):
            compiled = compile_shape(hardware(kind))
            compiled.setdefault('animations', [{}])
            boxes = posed_boxes(compiled, 0)
            spigot = [b for b in boxes if b[0].startswith('terminal-spigot')]
            self.assertTrue(spigot)
            self.assertAlmostEqual(max(b[6][axis] for b in spigot) * 16, 18.25)
            self.assertGreaterEqual(min(b[5][axis] for b in spigot) * 16, 15.49)

    def test_glass_does_not_share_metal_surface_planes(self):
        for state in ('sender', 'receiver'):
            self.assertTrue(glass_surface_contacts(shape_for('a-brass-sleeve', state, repaired=False)))
        for kind in ('straight', 'elbow', 'sender', 'receiver', 'straight-terminal', 'elbow-terminal', 'sender-terminal', 'receiver-terminal'):
            self.assertEqual(glass_surface_contacts(hardware(kind)), [], kind)

    def test_output_lip_sections_join_without_overlapping_walls(self):
        for kind in ('straight-terminal', 'elbow-terminal', 'sender-terminal', 'receiver-terminal'):
            compiled = compile_shape(hardware(kind))
            compiled.setdefault('animations', [{}])
            boxes = posed_boxes(compiled, 0)
            parts = [b for b in boxes if b[0].startswith(('output-', 'host-output-', 'terminal-spigot-', 'terminal-cuff-'))]
            self.assertEqual(len(parts), 12)
            for index, first in enumerate(parts):
                for second in parts[index + 1:]:
                    overlap = np.minimum(first[6], second[6]) - np.maximum(first[5], second[5])
                    self.assertFalse(np.all(overlap > 1e-8), (kind, first[0], second[0]))
            axis = 1 if kind == 'elbow-terminal' else 0
            for prefix in ('output-', 'host-output-', 'terminal-cuff-', 'terminal-spigot-'):
                ring = [b for b in parts if b[0].startswith(prefix)]
                if ring:
                    self.assertEqual(len(ring), 4)
                    self.assertTrue(all(b[6][axis] > b[5][axis] for b in ring))


if __name__ == '__main__':
    unittest.main()
