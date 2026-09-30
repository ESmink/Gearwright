"""Router A: one powered door, passive sealed mouths and connection-aware bypass."""
from pathlib import Path
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
from gearwright_graphics.animation import evaluate_animation
from graphics.review.lateral_drive_clearance import posed_boxes
from tests.graphics.test_pneumatic_sender_review import overlaps, moving_names
from graphics.review.pneumatic_router import (
    DESCRIPTIONS, STATES, REVIEW_ROOT, SELECTED_BASIS, DOOR_STROKE, SHUTTER_STROKE,
    build_review, candidate_shape, motion, door_lift, port_shutter_lift,
    port_configuration, airflow_paths, path_point,
)

CANDIDATE = 'a-single-door'


class PneumaticRouterReviewTests(unittest.TestCase):
    maxDiff = None
    def test_selected_a_replaces_one_review_directory_without_runtime_writes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            sentinel = root / 'assets/gearwright/sentinel'
            sentinel.parent.mkdir(parents=True)
            sentinel.write_text('unchanged')
            first = build_review(root)
            (first / 'stale').write_text('old')
            current = build_review(root)
            self.assertEqual((root / REVIEW_ROOT).resolve(), current.resolve())
            self.assertFalse((current / 'stale').exists())
            self.assertFalse(current.with_name('.current-build').exists())
            self.assertFalse(current.with_name('.current-old').exists())
            self.assertEqual('unchanged', sentinel.read_text())
            self.assertEqual(len(DESCRIPTIONS) * len(STATES), len(list(current.rglob('*.shape.json'))))
            manifest = json.loads((current / '.gearwright-review.json').read_text())
            self.assertEqual([CANDIDATE], manifest['candidates'])
            self.assertEqual('A', manifest['decision']['selectedFamily'])
            self.assertEqual(SELECTED_BASIS, manifest['decision']['selectedBasis'])
            self.assertTrue(manifest['decision']['runtimePromotion'])
            self.assertEqual('approved', manifest['decision']['status'])
            self.assertEqual(1, manifest['cycle']['doorCount'])
            self.assertTrue(manifest['cycle']['fixedRearStop'])
            self.assertNotIn(str(root), (current / '.gearwright-review.json').read_text())

    def test_cargo_path_is_continuous_and_cycle_ends_shut(self):
        for frame in range(1, 361):
            current, previous = motion(CANDIDATE, frame), motion(CANDIDATE, frame - 1)
            self.assertLess(np.linalg.norm(np.array(current['cargo']) - previous['cargo']), .36, frame)
            if frame >= 260:
                self.assertEqual((8., 8., -1.6), tuple(round(v, 6) for v in current['cargo']))
        self.assertEqual(0., motion(CANDIDATE, 0)['door'])
        self.assertEqual(0., motion(CANDIDATE, 360)['door'])
        self.assertEqual(0., motion(CANDIDATE, 360)['index'])

    def test_cargo_clears_the_carriage_and_every_connected_mouth(self):
        compiled = compile_shape(candidate_shape(CANDIDATE))
        for frame in range(0, 361, 4):
            boxes = posed_boxes(compiled, frame)
            cargo = [b for b in boxes if b[0] == 'cargo-envelope']
            machine = [b for b in boxes if not b[0].startswith('cargo-')]
            self.assertEqual([], overlaps(cargo, machine), frame)

    def test_machine_fits_one_block_with_context_pipes_excluded(self):
        for state in ('assembly', 'wall-mount'):
            compiled = compile_shape(candidate_shape(CANDIDATE, state))
            for frame in range(0, 361, 12):
                bad = [(b[0], b[5].tolist(), b[6].tolist()) for b in posed_boxes(compiled, frame)
                       if not b[0].startswith(('cargo-', 'context-')) and
                       (np.any(b[5] < -1e-7) or np.any(b[6] > 1 + 1e-7))]
                self.assertEqual([], bad, (state, frame))

    def test_moving_parts_clear_fixed_ports_guides_and_spring_cans(self):
        compiled = compile_shape(candidate_shape(CANDIDATE))
        moving = moving_names(compiled)
        for frame in range(0, 361, 4):
            boxes = posed_boxes(compiled, frame)
            active = [b for b in boxes if b[0] in moving and not b[0].startswith('cargo-')]
            fixed = [b for b in boxes if b[0] not in moving and b[0].startswith(('port-', 'bypass-'))]
            self.assertEqual([], overlaps(active, fixed), frame)

    def test_one_carriage_door_and_two_actual_temporal_drives(self):
        shape = candidate_shape(CANDIDATE, 'drive')
        compiled = compile_shape(shape)
        self.assertTrue(any(e.name == 'gate-in' for e in shape.elements))
        self.assertFalse(any(e.name == 'gate-out' for e in shape.elements))
        self.assertTrue(any(e.name.startswith('carriage-fixed-stop-') for e in shape.elements))
        for frame in range(0, 361, 6):
            pose = evaluate_animation(compiled['animations'][0], frame)
            boxes = posed_boxes(compiled, frame)
            for prefix, teeth in (('index', 14), ('door-in', 6)):
                self.assertAlmostEqual(pose[prefix + '-iron']['rotationZ'] * teeth,
                                       -pose[prefix + '-temporal']['rotationZ'] * 8)
                first = [b for b in boxes if b[0].startswith(prefix + '-iron-')
                         and not any(w in b[0] for w in ('bearing', 'axle'))]
                second = [b for b in boxes if b[0].startswith(prefix + '-temporal-')
                          and not any(w in b[0] for w in ('bearing', 'axle'))]
                self.assertEqual([], overlaps(first, second), (prefix, frame))
            lift = pose['gate-in']['offsetY']
            self.assertAlmostEqual(lift, door_lift(motion(CANDIDATE, frame)))
            self.assertAlmostEqual(lift, -math.radians(pose['door-in-iron']['rotationZ']) * 1.2)
            if 100 <= frame <= 165 or 295 <= frame <= 360:
                self.assertEqual(0., lift)

    def test_pinion_rolls_on_the_vertical_door_rack(self):
        compiled = compile_shape(candidate_shape(CANDIDATE, 'drive'))
        for frame in range(0, 361, 3):
            boxes = posed_boxes(compiled, frame)
            pinion = [b for b in boxes if b[0].startswith('door-in-iron-tooth-')]
            rack = [b for b in boxes if b[0].startswith('door-rack-')]
            self.assertEqual([], overlaps(pinion, rack), frame)

    def test_gear_sweeps_clear_rack_foundation_and_table_stand(self):
        shape = candidate_shape(CANDIDATE)
        gear_names = {e.name for e in shape.elements if e.group == 'gear'}
        compiled = compile_shape(shape)
        collisions = {}
        for frame in range(361):
            boxes = posed_boxes(compiled, frame)
            wheels = [b for b in boxes if b[0] in gear_names]
            structure = [b for b in boxes if b[0] not in gear_names and not b[0].endswith('-axle')]
            for pair in overlaps(wheels, structure):
                # The central shaft is keyed into its own driven hub.
                if pair != ('index-iron-hub', 'index-shaft'):
                    collisions.setdefault(pair, frame)
        self.assertEqual({}, collisions)

    def test_short_guides_keep_one_third_of_the_slider_engaged_at_full_lift(self):
        shape = candidate_shape(CANDIDATE)
        pairs = []
        for number in range(1, 5):
            guides = [e for e in shape.elements if e.name.startswith(f'port-{number}-shutter-guide-')
                      and 'slider' not in e.name]
            sliders = [e for e in shape.elements if e.name.startswith(f'port-{number}-shutter-guide-slider-')]
            pairs += [(guides, sliders, SHUTTER_STROKE)]
        pairs += [([e for e in shape.elements if e.name.startswith('door-guide-') and 'seat' not in e.name],
                   [e for e in shape.elements if e.name.startswith('door-crosshead-')], DOOR_STROKE)]
        for guides, sliders, stroke in pairs:
            self.assertEqual(2, len(guides))
            self.assertEqual(2, len(sliders))
            for guide, slider in zip(guides, sliders):
                self.assertLess(guide.to.y, 13.)
                height = slider.to.y - slider.from_.y
                engagement = min(guide.to.y, slider.to.y + stroke) - max(guide.from_.y, slider.from_.y + stroke)
                self.assertGreaterEqual(engagement + 1e-8, height / 3)

    def test_trimmed_rack_keeps_the_pinion_pitch_point_covered_for_its_full_stroke(self):
        shape = candidate_shape(CANDIDATE, 'drive')
        rack = next(e for e in shape.elements if e.name == 'door-rack-backbone')
        self.assertGreaterEqual(rack.from_.y, 2.5)
        for frame in range(361):
            lift = door_lift(motion(CANDIDATE, frame))
            self.assertLess(rack.from_.y + lift, 8.)
            self.assertGreater(rack.to.y + lift, 8.)

    def test_only_docked_shutter_moves_and_lifting_shoe_remains_in_contact(self):
        compiled = compile_shape(candidate_shape(CANDIDATE))
        for frame in range(0, 361, 2):
            pose = motion(CANDIDATE, frame)
            evaluated = evaluate_animation(compiled['animations'][0], frame)
            moving = []
            for number in range(1, 5):
                lift = evaluated[f'port-{number}-shutter']['offsetY']
                self.assertAlmostEqual(port_shutter_lift(pose, number), lift)
                if lift > 1e-7:
                    moving.append(number)
            self.assertLessEqual(len(moving), 1, frame)
            if not moving:
                continue
            boxes = posed_boxes(compiled, frame)
            shoe = next(b for b in boxes if b[0] == 'gate-docking-shoe')
            tab = next(b for b in boxes if b[0] == f'port-{moving[0]}-shutter-lifting-tab')
            self.assertAlmostEqual(shoe[6][1], tab[5][1])
            self.assertTrue(all(min(shoe[6][axis], tab[6][axis]) > max(shoe[5][axis], tab[5][axis])
                                for axis in (0, 2)), frame)

    def test_closed_mouth_has_no_hole_across_the_tube_bore(self):
        shape = candidate_shape(CANDIDATE)
        pieces = [e for e in shape.elements if e.name.startswith((
            'port-1-shutter-glass', 'port-1-shutter-side-', 'port-1-shutter-crossbar-', 'port-1-shutter-sill'))]
        # Authored in local west-port coordinates; these cover the full pressure opening.
        for y in np.linspace(5.21, 10.79, 37):
            for z in np.linspace(-2.79, 2.79, 37):
                self.assertTrue(any(e.from_.y - 1e-8 <= y <= e.to.y + 1e-8 and
                                    e.from_.z - 1e-8 <= z <= e.to.z + 1e-8 for e in pieces), (y, z))
        self.assertFalse(any('gear' in e.name and e.name.startswith('port-') for e in shape.elements))

    def test_connections_hide_frames_takeoffs_and_supports_at_unused_faces(self):
        for connected in ((), (1,), (1, 4), (1, 2, 3, 4)):
            outputs = tuple(n for n in connected if n != 1)
            config = dict(connected_ports=connected, inlet_pressures={1: 80.} if 1 in connected else {},
                          output_ports=outputs)
            shape = candidate_shape(CANDIDATE, **config)
            names = {e.name for e in shape.elements}
            for number in range(1, 5):
                present = number in connected
                self.assertEqual(present, any(n.startswith(f'port-{number}-') for n in names), number)
                self.assertEqual(present, any(n.startswith(f'context-port-{number}-') for n in names), number)
                self.assertEqual(not present, f'bypass-{number}-glass-unused-tee' in names, number)

    def test_strongest_connected_input_and_every_output_are_the_only_air_paths(self):
        for state, expected_source, expected_outputs in (
                ('airflow', 1, (3, 4)), ('second-inlet', 2, (3, 4)),
                ('three-outputs', 1, (2, 3, 4)), ('two-connections', 1, (4,))):
            config = port_configuration(state)
            self.assertEqual(expected_source, config['supply'])
            self.assertEqual(expected_outputs, config['outputs'])
            paths = airflow_paths(config)
            self.assertEqual(len(expected_outputs), len(paths))
            mouths = {1: (0, 6.05, 8.3), 2: (8.3, 6.05, 16), 3: (16, 6.05, 7.7), 4: (7.7, 6.05, 0)}
            self.assertTrue(all(p[0] == mouths[expected_source] for p in paths))
            self.assertEqual([mouths[n] for n in expected_outputs], [p[-1] for p in paths])
            boxes = posed_boxes(compile_shape(candidate_shape(CANDIDATE, state)), 140)
            obstacles = [b for b in boxes if not b[0].startswith(('air-marker-', 'cargo-'))]
            for path in paths:
                for step in range(1, 200):
                    center, half = path_point(path, step / 200) / 16, np.full(3, .04 / 16)
                    probe = ('air-path', 'probe', center, np.eye(3), half, center - half, center + half)
                    self.assertEqual([], overlaps([probe], obstacles), (state, step))
            # The weak input's closed butterfly spans the neck's horizontal cross-section.
            for number in config['inlets']:
                valve = next(b for b in boxes if b[0] == f'port-{number}-isolation-valve')
                extent = (valve[6] - valve[5]) * 16
                if number == expected_source:
                    self.assertGreater(extent[1], .8)
                else:
                    self.assertLess(extent[1], .2)

    def test_invalid_roles_are_rejected_and_equal_inputs_use_a_stable_tie(self):
        with self.assertRaises(ValueError):
            port_configuration(connected_ports=(1,), inlet_pressures={1: 80}, output_ports=(1,))
        with self.assertRaises(ValueError):
            port_configuration(inlet_pressures={1: float('nan'), 2: 30})
        config = port_configuration(inlet_pressures={1: 80, 2: 80})
        self.assertEqual(1, config['supply'])
        config = port_configuration(inlet_pressures={1: 0, 2: 0})
        self.assertIsNone(config['supply'])
        self.assertEqual((), airflow_paths(config))

    def test_lower_temporal_bearing_is_mounted_on_the_base_and_axles_are_free(self):
        shape = candidate_shape(CANDIDATE, 'drive')
        mount = next(e for e in shape.elements if e.name == 'index-source-mount')
        self.assertEqual(.2, mount.from_.y)
        self.assertEqual(.6, mount.to.y)
        compiled = compile_shape(shape)
        for frame in range(0, 361, 6):
            boxes = posed_boxes(compiled, frame)
            shafts = [b for b in boxes if b[0].endswith('-axle')]
            supports = [b for b in boxes if 'bearing' in b[0] or b[0].startswith((
                'index-source-mount', 'index-drive-bed'))]
            self.assertEqual([], overlaps(shafts, supports), frame)

    def test_port_posts_reach_base_and_support_inner_connection_frames(self):
        shape = candidate_shape(CANDIDATE)
        for number in range(1, 5):
            posts = [e for e in shape.elements if e.name.startswith(f'port-{number}-foot-')]
            self.assertEqual(2, len(posts))
            self.assertTrue(all(e.from_.y == .6 and e.to.y == 4.2 for e in posts))
            shoes = [e for e in shape.elements if e.name.startswith(f'port-{number}-support-shoe-')]
            self.assertTrue(all(e.from_.y == 4.2 and e.to.y == 4.5 for e in shoes))


if __name__ == '__main__':
    unittest.main()
