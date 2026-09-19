from __future__ import annotations

import math
import re
import sys
import unittest
from dataclasses import replace
from pathlib import Path
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tools" / "graphics"))

from graphics.models.reciprocating_pump import MOTION_LAYERS, PUMP_OFFSET, build  # noqa: E402
from gearwright_graphics.animation import apply_animation  # noqa: E402
from gearwright_graphics.compiler import compile_shape  # noqa: E402
from gearwright_graphics.geometry import triangles  # noqa: E402
from gearwright_graphics.scene import transform_matrix  # noqa: E402


class ReciprocatingPumpRuntimeModelTests(unittest.TestCase):
    def test_liquid_bounds_meet_actual_basin_walls_floor_glass_and_piston(self):
        source = (ROOT / "code/Hydraulics/ReciprocatingPumpLiquidGeometry.cs").read_text()

        def bound(name):
            match = re.search(rf"{name} = ([\d.]+)f / 16f;", source)
            self.assertIsNotNone(match, name)
            return float(match.group(1)) / 16

        package = build()
        vertices = {}
        for suffix in ("body", "piston"):
            shape = package.outputs[f"assets/gearwright/shapes/block/reciprocating-pump-{suffix}.json"]
            for triangle in triangles(compile_shape(shape)):
                vertices.setdefault(triangle.name, []).extend(triangle.vertices)
        body = compile_shape(package.outputs['assets/gearwright/shapes/block/reciprocating-pump-body.json'])
        def nearest_wall(axis, direction, probe):
            hits = []
            for triangle in triangles(body):
                if not triangle.name.startswith('gw-pump-wet-body-'):
                    continue
                lo, hi = triangle.vertices.min(axis=0), triangle.vertices.max(axis=0)
                if hi[axis] - lo[axis] > 1e-8 or (lo[axis] - probe[axis]) * direction <= 0:
                    continue
                if all(lo[d] - 1e-8 <= probe[d] <= hi[d] + 1e-8 for d in range(3) if d != axis):
                    hits.append(lo[axis])
            self.assertTrue(hits)
            return min(hits, key=lambda value: abs(value - probe[axis]))
        self.assertAlmostEqual(bound("MinX"), nearest_wall(0, -1, (.5, .625, .5)))
        self.assertAlmostEqual(bound("MaxX"), nearest_wall(0, 1, (.5, .625, .5)))
        self.assertAlmostEqual(bound("Bottom"), nearest_wall(1, -1, (.5, .625, 5 / 16)))
        self.assertAlmostEqual(bound("UpperPistonBottom"), np.min(vertices["gw-piston-lower-seal"], axis=0)[1])
        # One thousandth of a model unit inside the glass avoids z-fighting.
        body_elements = {element.name: element for element in package.outputs[
            "assets/gearwright/shapes/block/reciprocating-pump-body.json"
        ].elements}
        # Glass renders only its outer face, so use its declared cuboid thickness.
        front_inner = (body_elements["gw-cylinder-front-glass"].to.z + PUMP_OFFSET.z) / 16
        back_inner = (body_elements["gw-cylinder-back-glass"].from_.z + PUMP_OFFSET.z) / 16
        self.assertAlmostEqual(bound("MinZ"), front_inner + .001 / 16)
        self.assertAlmostEqual(1 - bound("MinZ"), back_inner - .001 / 16)
        self.assertIn("MaxZ = 1 - MinZ;", source)

    def test_approved_a11_is_split_into_runtime_layers(self):
        package = build()
        self.assertEqual(14, len(package.outputs))

        one_sided = package.outputs[
            "assets/gearwright/shapes/block/lateral-crank-one-sided.json"
        ]
        crank_names = {element.name for element in one_sided.elements}
        crank_elements = {element.name: element for element in one_sided.elements}
        self.assertIn("gw-crank-journal-retainer", crank_names)
        self.assertFalse(any("output-shaft" in name for name in crank_names))
        self.assertEqual("gw-crank-phase", crank_elements["gw-crank-input-shaft-wide"].parent)
        self.assertEqual("gw-crank-phase", crank_elements["gw-crank-input-shaft-tall"].parent)
        self.assertEqual(["rotation"], [animation.code for animation in one_sided.animations])
        self.assertEqual("EaseOut", one_sided.animations[0].on_activity_stopped)

        inventory = package.outputs[
            "assets/gearwright/shapes/block/lateral-crank-inventory.json"
        ]
        self.assertEqual([], inventory.animations)

        body = package.outputs[
            "assets/gearwright/shapes/block/reciprocating-pump-body.json"
        ]
        supported = package.outputs[
            "assets/gearwright/shapes/block/reciprocating-pump-body-supported.json"
        ]
        mechanism = package.outputs[
            "assets/gearwright/shapes/block/reciprocating-pump-mechanism.json"
        ]
        body_names = {element.name for element in body.elements}
        supported_names = {element.name for element in supported.elements}
        mechanism_names = {element.name for element in mechanism.elements}

        self.assertNotIn("gw-piston-motion", body_names)
        self.assertIn("gw-piston-motion", mechanism_names)
        self.assertIn("gw-connecting-rod-motion", mechanism_names)
        self.assertFalse(any(name.startswith("gw-downward-stand-") for name in body_names))
        self.assertTrue(any(name.startswith("gw-downward-stand-") for name in supported_names))
        self.assertEqual(["singleacting"], [animation.code for animation in mechanism.animations])
        self.assertEqual("EaseOut", mechanism.animations[0].on_activity_stopped)
        animated = {
            name
            for frame in mechanism.animations[0].keyframes
            for name in frame.elements
        }
        self.assertNotIn("gw-crank-phase", animated)
        self.assertIn("gw-piston-motion", animated)

        mechanism_elements = {element.name: element for element in mechanism.elements}
        piston_origin = mechanism_elements["gw-piston-motion"].rotation_origin
        rod_origin = mechanism_elements["gw-connecting-rod-motion"].rotation_origin
        self.assertIsNotNone(piston_origin)
        self.assertIsNotNone(rod_origin)
        for keyframe in mechanism.animations[0].keyframes:
            piston_frame = keyframe.elements["gw-piston-motion"]
            rod_frame = keyframe.elements["gw-connecting-rod-motion"]
            angle = math.radians(float(rod_frame["rotationX"]))
            middle_y = rod_origin.y + float(rod_frame["offsetY"])
            middle_z = rod_origin.z + float(rod_frame["offsetZ"])
            half_rod = 3.0
            crank_y = middle_y + half_rod * math.cos(angle)
            crank_z = middle_z + half_rod * math.sin(angle)
            crosshead_y = middle_y - half_rod * math.cos(angle)
            crosshead_z = middle_z - half_rod * math.sin(angle)
            phase = math.radians(keyframe.frame * 6.0)
            self.assertAlmostEqual(3.0 * math.cos(phase), crank_y)
            self.assertAlmostEqual(3.0 * math.sin(phase), crank_z)
            self.assertAlmostEqual(
                piston_origin.y + float(piston_frame["offsetY"]),
                crosshead_y,
            )
            self.assertAlmostEqual(0.0, crosshead_z)

        for shape in (body, supported, mechanism):
            mount = next(element for element in shape.elements if element.name == "gw-pump-mount")
            self.assertEqual((8, 24, 8), mount.rotation_origin.values())

    def test_direct_motion_layers_preserve_all_approved_geometry_and_pivots(self):
        package = build()
        mechanism = package.outputs[
            "assets/gearwright/shapes/block/reciprocating-pump-mechanism.json"
        ]
        original = {element.name: element for element in mechanism.elements}
        covered = set()
        for suffix, root in MOTION_LAYERS.items():
            layer = package.outputs[f"assets/gearwright/shapes/block/reciprocating-pump-{suffix}.json"]
            self.assertEqual([], layer.animations)
            self.assertEqual(mechanism.textures, layer.textures)
            for element in layer.elements:
                self.assertNotIn(element.name, covered)
                covered.add(element.name)
                expected = original[element.name]
                if element.name == root:
                    expected = replace(
                        expected,
                        from_=expected.from_ + PUMP_OFFSET,
                        to=expected.to + PUMP_OFFSET,
                        rotation_origin=expected.rotation_origin + PUMP_OFFSET,
                        parent=None,
                    )
                self.assertEqual(expected, element)
        self.assertEqual(set(original) - {"gw-pump-mount"}, covered)

    def test_directly_transformed_meshes_match_approved_full_stroke_geometry(self):
        package = build()
        combined = compile_shape(package.outputs[
            "assets/gearwright/shapes/block/reciprocating-pump-mechanism.json"
        ])
        for phase in (0, 90, 180, 270):
            expected = {}
            for triangle in triangles(apply_animation(combined, "singleacting", phase / 6)):
                expected.setdefault(triangle.name, []).append(triangle.vertices)
            actual = {}
            for item in package.scenes[f"direct-pose-{phase}"].objects[2:]:
                matrix = transform_matrix(item["placement"], item["rotation"])
                for triangle in triangles(compile_shape(package.outputs[item["asset"]])):
                    vertices = (matrix[:3, :3] @ triangle.vertices.T).T + matrix[:3, 3]
                    actual.setdefault(triangle.name, []).append(vertices)
            self.assertEqual(set(expected), set(actual))
            for name in expected:
                np.testing.assert_allclose(actual[name], expected[name], atol=1e-9, err_msg=f"{phase}: {name}")


if __name__ == "__main__":
    unittest.main()
