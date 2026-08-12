from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tools" / "graphics"))

from gearwright_graphics.geometry import bounds, triangles  # noqa: E402
from graphics.review.overrunning_coupling import (  # noqa: E402
    DESCRIPTIONS,
    FOOTPRINTS,
    build_review,
)


class OverrunningCouplingReviewTests(unittest.TestCase):
    def test_candidates_are_managed_animated_and_use_minimum_acceptable_metal(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            managed = build_review(root)
            stray = managed / "stray-agent-output.txt"
            stray.write_text("orphan", encoding="utf-8")

            rebuilt = build_review(root)

            self.assertEqual(managed, rebuilt)
            self.assertFalse(stray.exists())
            self.assertFalse((root / "assets").exists())
            ownership = json.loads((rebuilt / ".gearwright-review.json").read_text(encoding="utf-8"))
            self.assertEqual(list(DESCRIPTIONS), ownership["candidates"])
            self.assertEqual("open", ownership["decision"]["status"])
            self.assertTrue(ownership["decision"]["runtimePromotion"])
            self.assertEqual(["drive", "overrun"], ownership["animations"])
            self.assertEqual(["model-a2-three-pawl-spider"], ownership["candidates"])

            for candidate in DESCRIPTIONS:
                document = json.loads((rebuilt / candidate / "rest.shape.json").read_text(encoding="utf-8"))
                self.assertEqual(["drive", "overrun"], [animation["code"] for animation in document["animations"]])
                self.assertEqual(72, document["animations"][1]["quantityframes"])
                self.assertEqual(
                    "game:block/metal/sheet/tinbronze1",
                    document["textures"]["bronze"],
                )
                self.assertFalse(any("copper" in texture or "iron" in texture for texture in document["textures"].values()))

                serialized = json.dumps(document)
                self.assertIn("ratchet", serialized)
                self.assertIn("orbiting-pawl", serialized)
                self.assertIn("tooth-hook", serialized)
                self.assertIn("leaf-spring", serialized)
                self.assertIn("carrier-cheek", serialized)
                self.assertIn("through-pin", serialized)
                self.assertIn("input-oak-ratchet-wheel", serialized)
                self.assertIn("output-oak-pawl-carrier", serialized)
                self.assertNotIn("output-oak-cage", serialized)
                self.assertNotIn("oak-base", serialized)
                self.assertIn("input-separate-foot", serialized)
                self.assertIn("output-separate-foot", serialized)

                expected_pawls = 3
                self.assertEqual(
                    expected_pawls,
                    sum(f'"name": "orbiting-pawl-{index}"' in serialized for index in range(1, 5)),
                )

                overrun = document["animations"][1]
                moving_frame = next(keyframe for keyframe in overrun["keyframes"] if keyframe["frame"] == 2)
                lifts = [
                    moving_frame["elements"][f"orbiting-pawl-{index}"]["rotationX"]
                    for index in range(1, expected_pawls + 1)
                ]
                self.assertEqual(3, len({round(lift, 4) for lift in lifts}))

                input_foot = next(element for element in document["elements"] if element["name"] == "input-separate-foot")
                output_foot = next(element for element in document["elements"] if element["name"] == "output-separate-foot")
                self.assertGreaterEqual(output_foot["from"][0] - input_foot["to"][0], 8)

                input_bearing = next(element for element in document["elements"] if element["name"] == "input-bearing-wood")
                axle_swept_radius = (2 ** 2 + 1 ** 2) ** .5
                self.assertLessEqual(input_bearing["from"][1], 8 - axle_swept_radius)
                self.assertGreaterEqual(input_bearing["to"][1], 8 + axle_swept_radius)

                serialized_names = {element["name"] for element in document["elements"]}
                self.assertFalse(any("foot-band" in name or "corner-base-tie" in name for name in serialized_names))
                front_beam = next(element for element in document["elements"] if element["name"] == "base-crossbeam-front")
                back_beam = next(element for element in document["elements"] if element["name"] == "base-crossbeam-back")
                self.assertEqual([1, 0, 2], front_beam["from"])
                self.assertEqual([15, 1.6, 3.6], front_beam["to"])
                self.assertEqual([1, 0, 12.4], back_beam["from"])
                self.assertEqual([15, 1.6, 14], back_beam["to"])
                for beam in (front_beam, back_beam):
                    self.assertAlmostEqual(beam["to"][1] - beam["from"][1], beam["to"][2] - beam["from"][2])
                self.assertEqual(3.62, input_foot["from"][2])
                self.assertEqual(12.38, input_foot["to"][2])
                self.assertAlmostEqual(.02, input_foot["from"][2] - front_beam["to"][2])
                self.assertAlmostEqual(.02, back_beam["from"][2] - input_foot["to"][2])
                for edge in ("front", "back"):
                    for side, foot in (("input", input_foot), ("output", output_foot)):
                        bolt = next(element for element in document["elements"] if element["name"] == f"base-crossbeam-{edge}-bolt-{side}")
                        self.assertGreaterEqual(bolt["from"][0], foot["from"][0])
                        self.assertLessEqual(bolt["to"][0], foot["to"][0])
                        self.assertGreaterEqual(bolt["from"][1], front_beam["from"][1])
                        self.assertLessEqual(bolt["to"][1], front_beam["to"][1])
                        if edge == "front":
                            self.assertLess(bolt["from"][2], front_beam["from"][2])
                            self.assertGreater(bolt["to"][2], front_beam["from"][2])
                        else:
                            self.assertLess(bolt["from"][2], back_beam["to"][2])
                            self.assertGreater(bolt["to"][2], back_beam["to"][2])

                input_rotor = next(element for element in document["elements"] if element["name"] == "input-rotor")
                shaft_parts = {element["name"]: element for element in input_rotor["children"]}
                self.assertEqual([-1, -2], shaft_parts["input-shaft-wide"]["from"][1:])
                self.assertEqual([1, 2], shaft_parts["input-shaft-wide"]["to"][1:])
                self.assertEqual([-2, -1], shaft_parts["input-shaft-tall"]["from"][1:])
                self.assertEqual([2, 1], shaft_parts["input-shaft-tall"]["to"][1:])

                minimum, maximum = bounds(triangles(document))
                model_minimum = minimum * 16
                model_maximum = maximum * 16
                footprint = FOOTPRINTS[candidate]
                for actual in model_minimum:
                    self.assertGreaterEqual(float(actual), -1e-6)
                for actual, limit in zip(
                    model_maximum,
                    (footprint["length"] * 16, footprint["height"] * 16, footprint["depth"] * 16),
                ):
                    self.assertLessEqual(float(actual), limit + 1e-6)

    def test_approved_package_contains_only_the_selected_candidate(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            candidate = "model-a2-three-pawl-spider"
            managed = build_review(root, approved_candidate=candidate)
            ownership = json.loads((managed / ".gearwright-review.json").read_text(encoding="utf-8"))
            self.assertEqual([candidate], ownership["candidates"])
            self.assertEqual(candidate, ownership["decision"]["candidate"])
            self.assertTrue((managed / candidate / "rest.shape.json").is_file())
            self.assertFalse((managed / "model-a1-twin-bridge-pawls").exists())


if __name__ == "__main__":
    unittest.main()
