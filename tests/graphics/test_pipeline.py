from __future__ import annotations

import copy
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "graphics"))

from gearwright_graphics.animation import apply_animation, evaluate_animation
from gearwright_graphics.compiler import build_package, compile_shape, definition_paths, load_definition
from gearwright_graphics.geometry import Triangle, triangles
from gearwright_graphics.materials import Material, load_material
from gearwright_graphics.model import ModelError, Shape, Vec3, animate
from gearwright_graphics.raster import Camera, render


def _normalized_number(value):
    if isinstance(value, bool) or value is None:
        return value
    if isinstance(value, (int, float)):
        result = round(float(value), 10)
        return int(result) if result.is_integer() else result
    return value


def _normalized(value):
    if isinstance(value, dict):
        return {str(key): _normalized(value[key]) for key in sorted(value)}
    if isinstance(value, list):
        return [_normalized(item) for item in value]
    return _normalized_number(value)


def _canonical_face(face):
    return _normalized({
        "texture": face.get("texture", "missing"),
        "enabled": face.get("enabled", True),
        "uv": face.get("uv"),
        "autoUv": face.get("autoUv"),
        "rotation": face.get("rotation", 0),
        "glow": face.get("glow", 0),
        "reflectiveMode": face.get("reflectiveMode"),
    })


def _canonical_element(element):
    children = [_canonical_element(child) for child in element.get("children", []) or []]
    transformed = any(float(element.get(key, 0) or 0) != 0 for key in ("rotationX", "rotationY", "rotationZ"))
    transformed = transformed or any(float(element.get(key, 1) or 1) != 1 for key in ("scaleX", "scaleY", "scaleZ"))
    result = {
        "name": element.get("name"),
        "from": element.get("from"),
        "to": element.get("to"),
        "rotationX": element.get("rotationX", 0),
        "rotationY": element.get("rotationY", 0),
        "rotationZ": element.get("rotationZ", 0),
        "scaleX": element.get("scaleX", 1),
        "scaleY": element.get("scaleY", 1),
        "scaleZ": element.get("scaleZ", 1),
        "shade": element.get("shade"),
        "gradientShade": element.get("gradientShade"),
        "renderPass": element.get("renderPass"),
        "faces": {name: _canonical_face(face) for name, face in sorted((element.get("faces") or {}).items())},
        "children": children,
    }
    if transformed or children:
        result["rotationOrigin"] = element.get("rotationOrigin", element.get("from"))
    return _normalized(result)


def _semantic_digest(document):
    canonical = _normalized({
        "textureWidth": document.get("textureWidth", 16),
        "textureHeight": document.get("textureHeight", 16),
        "textures": document.get("textures", {}),
        "textureSizes": document.get("textureSizes", {}),
        "elements": [_canonical_element(element) for element in document.get("elements", [])],
        "animations": document.get("animations", []),
    })
    payload = json.dumps(canonical, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


class PipelineTests(unittest.TestCase):
    def test_all_definitions_validate_and_match_frozen_inventory(self):
        fixture = json.loads((ROOT / "tests/graphics/fixtures/model-contracts.json").read_text())
        outputs = {}
        for path in definition_paths(ROOT):
            outputs.update(build_package(load_definition(ROOT, path), ROOT, write=False))
        self.assertEqual(set(fixture["shapes"]), set(outputs))
        for relative, expected in fixture["shapes"].items():
            document = json.loads(outputs[relative])
            def count(elements):
                return sum(1 + count(item.get("children", [])) for item in elements)
            self.assertEqual(expected["elements"], count(document["elements"]))
            self.assertEqual(expected["textureWidth"], document["textureWidth"])
            self.assertEqual(expected["textureHeight"], document["textureHeight"])

    def test_python_definitions_preserve_accepted_runtime_model_semantics(self):
        fixture = json.loads((ROOT / "tests/graphics/fixtures/legacy-model-semantics.json").read_text())
        outputs = {}
        for path in definition_paths(ROOT):
            outputs.update(build_package(load_definition(ROOT, path), ROOT, write=False))
        self.assertEqual(set(fixture["shapes"]), {path for path in outputs if path.startswith("assets/gearwright/shapes/")})
        for relative, expected_digest in fixture["shapes"].items():
            self.assertEqual(expected_digest, _semantic_digest(json.loads(outputs[relative])), relative)

    def test_pressure_contract_and_fractional_pose(self):
        package = load_definition(ROOT, ROOT / "graphics/models/passive_fluid_pump.py")
        document = compile_shape(package.outputs["assets/gearwright/shapes/block/passive-fluid-pump-mechanism.json"])
        animation = document["animations"][0]
        self.assertEqual("pressure", animation["code"])
        self.assertEqual(30, animation["quantityframes"])
        self.assertEqual(-52, animation["keyframes"][0]["elements"]["b_gauge-needle"]["rotationX"])
        self.assertEqual(52, animation["keyframes"][1]["elements"]["b_gauge-needle"]["rotationX"])
        self.assertEqual(.85, animation["keyframes"][1]["elements"]["b_pressure-plunger"]["offsetY"])
        pose = evaluate_animation(animation, 14.5)
        self.assertAlmostEqual(0, pose["b_gauge-needle"]["rotationX"])
        self.assertAlmostEqual(.425, pose["b_pressure-plunger"]["offsetY"])
        applied = apply_animation(document, "pressure", 14.5)
        pivot = next(item for item in applied["elements"] if item["name"] == "b_pressure-plunger")
        self.assertAlmostEqual(.425, pivot["offsetY"])

    def test_approved_pipe_intake_preserves_gravity_drain_reach_without_joint_faces(self):
        pipe_package = load_definition(ROOT, ROOT / "graphics/models/fluid_pipe.py")
        pump_package = load_definition(ROOT, ROOT / "graphics/models/passive_fluid_pump.py")
        installed = compile_shape(pipe_package.outputs["assets/gearwright/shapes/block/fluid-pipe-intake.json"])
        item = compile_shape(pipe_package.outputs["assets/gearwright/shapes/item/fluid-pipe-intake-copper.json"])
        source = compile_shape(pump_package.outputs["assets/gearwright/shapes/block/passive-fluid-pump-intake.json"])

        installed_by_name = {element["name"]: element for element in installed["elements"]}
        item_by_name = {element["name"]: element for element in item["elements"]}
        self.assertEqual([element["name"] for element in source["elements"]], list(installed_by_name))
        self.assertEqual(list(installed_by_name), list(item_by_name))

        def approved(point, item_stage=False):
            x, y, z = point
            if z == 10.5:
                z = 10
            return [8 + (x - 8) * (4 / 3), 8 + (y - 8) * (4 / 3), z - (6.25 if item_stage else 0)]

        for source_element in source["elements"]:
            name = source_element["name"]
            for actual, expected in zip(installed_by_name[name]["from"], approved(source_element["from"])):
                self.assertAlmostEqual(expected, actual, places=9)
            for actual, expected in zip(installed_by_name[name]["to"], approved(source_element["to"])):
                self.assertAlmostEqual(expected, actual, places=9)
            for actual, expected in zip(item_by_name[name]["from"], approved(source_element["from"], True)):
                self.assertAlmostEqual(expected, actual, places=9)
            for actual, expected in zip(item_by_name[name]["to"], approved(source_element["to"], True)):
                self.assertAlmostEqual(expected, actual, places=9)

        installed_from = [value for element in installed["elements"] for value in [element["from"]]]
        installed_to = [value for element in installed["elements"] for value in [element["to"]]]
        self.assertAlmostEqual(10, min(value[2] for value in installed_from))
        self.assertAlmostEqual(18.5, max(value[2] for value in installed_to))
        self.assertAlmostEqual(4, installed_by_name["intake-throat-bottom"]["to"][0] - installed_by_name["intake-throat-bottom"]["from"][0])
        for name in ("intake-throat-bottom", "intake-throat-top", "intake-throat-left", "intake-throat-right"):
            self.assertNotIn("north", installed_by_name[name]["faces"])
            self.assertIn("north", item_by_name[name]["faces"])

    def test_deterministic_bytes(self):
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            first_root, second_root = Path(first), Path(second)
            first_bytes, second_bytes = {}, {}
            for path in definition_paths(ROOT):
                package = load_definition(ROOT, path)
                first_bytes.update(build_package(package, first_root, write=True))
                second_bytes.update(build_package(load_definition(ROOT, path), second_root, write=True))
            self.assertEqual(first_bytes, second_bytes)

    def test_hierarchy_and_renderer_output_are_nonempty(self):
        shape = Shape("test")
        shape.texture("paint", "project:missing.png")
        pivot = shape.pivot("pivot", (8, 8, 8))
        shape.box("child", (7, 7, 7), (9, 9, 9), texture="#paint", parent=pivot)
        document = compile_shape(shape)
        mesh = triangles(document)
        self.assertEqual(12, len(mesh))
        camera = Camera(Vec3(0, 0, -2).values(), Vec3(0, 0, 0).values(), 96, 64)
        image = render(mesh, {"missing": load_material("missing", None)}, camera)
        self.assertEqual((96, 64), image.size)
        self.assertGreater(max(image.getbbox()), 0)

    def test_invalid_animation_target_is_rejected(self):
        shape = Shape("bad")
        animation = animate(shape, "Bad", "bad", 2)
        with self.assertRaises(ModelError):
            animation.keyframe(0, type("Ref", (), {"shape_id": "bad", "name": "missing"})(), offsetY=1)

    def test_shortest_angle_interpolation(self):
        animation = {"quantityframes": 10, "keyframes": [{"frame": 0, "elements": {"needle": {"rotationY": 170, "rotShortestDistanceY": True}}}, {"frame": 9, "elements": {"needle": {"rotationY": -170, "rotShortestDistanceY": True}}}]}
        self.assertAlmostEqual(180, evaluate_animation(animation, 4.5)["needle"]["rotationY"])

    def test_renderer_depth_alpha_transparency_glow_and_views(self):
        shape = Shape("render-contract")
        shape.texture("opaque", "project:opaque.png")
        shape.texture("cutout", "project:cutout.png")
        shape.box("far", (0, 0, 8), (16, 16, 9), texture="#opaque")
        shape.box("near", (2, 2, 7), (14, 14, 8), texture="#opaque", glow=10)
        shape.box("glass", (4, 4, 6), (12, 12, 6.2), texture="#opaque", render_pass=1)
        document = compile_shape(shape)
        mesh = triangles(document)
        self.assertTrue(any(triangle.alpha_mode == "transparent" for triangle in mesh))
        opaque = np.ones((2, 2, 4), dtype=np.float32); opaque[..., :3] = (.3, .4, .5)
        cutout = opaque.copy(); cutout[..., 3] = 0
        materials = {"missing": load_material("missing", None), "opaque": load_material("opaque", None), "cutout": load_material("cutout", None)}
        camera = Camera(np.asarray((.5, .5, -3)), np.asarray((.5, .5, .5)), 111, 73)
        image = render(mesh, materials, camera)
        self.assertEqual((111, 73), image.size)
        self.assertEqual("RGBA", image.mode)

    def test_renderer_keeps_uv_and_depth_weights_on_the_same_vertex(self):
        pixels = np.ones((2, 2, 4), dtype=np.float32)
        pixels[1, 0, :3] = (1, 0, 0)
        pixels[1, 1, :3] = (0, 1, 0)
        material = Material("uv", pixels)
        triangle = Triangle(
            np.asarray(((-.8, -.8, 0), (.8, -.8, 0), (-.8, .8, 0)), dtype=float),
            np.asarray(((0, 0), (1, 0), (0, 1)), dtype=float),
            "uv", "opaque", 0, np.asarray((0, 0, -1), dtype=float), "triangle", None, 0,
        )
        camera = Camera(np.asarray((0, 0, -1)), np.asarray((0, 0, 0)), 101, 101, orthographic=True, orthographic_scale=2)
        image = np.asarray(render([triangle], {"missing": load_material("missing", None), "uv": material}, camera, key=(0, 0, -1), fill=(0, 0, -1), rim=(0, 0, -1)))
        sample = image[80, 80]
        self.assertGreater(sample[0], sample[1])
        self.assertEqual(255, sample[3])


if __name__ == "__main__":
    unittest.main()
