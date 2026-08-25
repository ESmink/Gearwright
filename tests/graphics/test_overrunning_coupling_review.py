from __future__ import annotations

import json
import math
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tools" / "graphics"))

from gearwright_graphics.geometry import bounds, triangles  # noqa: E402
from graphics.review.overrunning_coupling import (  # noqa: E402
    ORBITAL_CONFIGS,
    DESCRIPTIONS,
    FOOTPRINTS,
    build_review,
)


def _walk_elements(elements):
    for element in elements:
        yield element
        yield from _walk_elements(element.get("children", []))


def _textures(element):
    return {face["texture"] for face in element.get("faces", {}).values()}


class OverrunningCouplingReviewTests(unittest.TestCase):
    def test_candidates_are_managed_animated_and_have_positive_locking_faces(self):
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
            candidate_ids = [
                "lock-a-stepped-hook",
                "lock-b-pocket-bar",
                "lock-c-concept-crook",
            ]
            self.assertEqual(candidate_ids, list(DESCRIPTIONS))
            self.assertEqual(candidate_ids, ownership["candidates"])
            self.assertEqual("open", ownership["decision"]["status"])
            self.assertTrue(ownership["decision"]["runtimePromotion"])
            self.assertEqual(["drive", "overrun", "contactcycle"], ownership["animations"])
            self.assertEqual(["rest", "interface", "input-side", "output-side"], ownership["states"])

            for candidate in DESCRIPTIONS:
                candidate_root = rebuilt / candidate
                self.assertEqual(
                    {
                        "rest.shape.json",
                        "interface.shape.json",
                        "input-side.shape.json",
                        "output-side.shape.json",
                    },
                    {path.name for path in candidate_root.glob("*.shape.json")},
                )
                document = json.loads((candidate_root / "rest.shape.json").read_text(encoding="utf-8"))
                self.assertEqual(["drive", "overrun", "contactcycle"], [animation["code"] for animation in document["animations"]])
                self.assertEqual(49, document["animations"][0]["quantityframes"])
                self.assertEqual(73, document["animations"][1]["quantityframes"])
                self.assertEqual(29, document["animations"][2]["quantityframes"])
                self.assertEqual("game:block/metal/sheet/tinbronze1", document["textures"]["bronze"])
                self.assertEqual("game:block/metal/sheet/brass1", document["textures"]["brass"])
                self.assertFalse(any("copper" in texture or "iron" in texture for texture in document["textures"].values()))

                serialized = json.dumps(document)
                for required in (
                    "input-ratchet-lock-face",
                    "orbiting-pawl",
                    "spring-follower-cap",
                    "leaf-spring-back",
                    "leaf-spring-tip",
                    "large-pin",
                    "output-broad-orbital-arm",
                    "output-gearward-head",
                    "input-separate-foot",
                    "output-separate-foot",
                ):
                    self.assertIn(required, serialized)
                for removed in (
                    "output-open-clevis-cheek",
                    "counterweight",
                    "output-oak-cage",
                    "oak-base",
                ):
                    self.assertNotIn(removed, serialized)
                self.assertEqual(
                    3,
                    sum(f'"name": "orbiting-pawl-{index}"' in serialized for index in range(1, 5)),
                )

                config = ORBITAL_CONFIGS[candidate]
                if candidate == "lock-a-stepped-hook":
                    self.assertEqual(30, config["lift"])
                drive = document["animations"][0]
                drive_frames = {keyframe["frame"]: keyframe for keyframe in drive["keyframes"]}
                self.assertEqual({0, 10, 48}, set(drive_frames))
                self.assertEqual(-config["drive_approach"], drive_frames[0]["elements"]["input-rotor"]["rotationX"])
                self.assertEqual(0, drive_frames[0]["elements"]["output-rotor"]["rotationX"])
                self.assertEqual(0, drive_frames[10]["elements"]["input-rotor"]["rotationX"])
                self.assertEqual(0, drive_frames[10]["elements"]["output-rotor"]["rotationX"])
                self.assertEqual(240, drive_frames[48]["elements"]["input-rotor"]["rotationX"])
                self.assertEqual(240, drive_frames[48]["elements"]["output-rotor"]["rotationX"])

                overrun = document["animations"][1]
                open_run = 0
                longest_open_run = 0
                saw_open_pawl = False
                for keyframe in overrun["keyframes"]:
                    lifts = [
                        keyframe["elements"][f"orbiting-pawl-{index}"]["rotationX"]
                        for index in range(1, 4)
                    ]
                    spring_lifts = [
                        keyframe["elements"][f"orbiting-pawl-{index}-spring-flex"]["rotationX"]
                        for index in range(1, 4)
                    ]
                    self.assertEqual(1, len({round(lift, 4) for lift in lifts}))
                    self.assertEqual(1, len({round(lift, 4) for lift in spring_lifts}))
                    self.assertAlmostEqual(lifts[0] * .35, spring_lifts[0])
                    self.assertGreaterEqual(lifts[0], 0)
                    if lifts[0] > 1e-6:
                        saw_open_pawl = True
                        open_run += 1
                        longest_open_run = max(longest_open_run, open_run)
                    else:
                        open_run = 0
                self.assertTrue(saw_open_pawl)
                self.assertLessEqual(longest_open_run, 4)

                contact_cycle = document["animations"][2]
                contact_frames = {keyframe["frame"]: keyframe for keyframe in contact_cycle["keyframes"]}
                self.assertEqual(set(range(29)), set(contact_frames))
                self.assertEqual(0, contact_frames[0]["elements"]["input-rotor"]["rotationX"])
                self.assertEqual(0, contact_frames[0]["elements"]["output-rotor"]["rotationX"])
                for frame in (24, 28):
                    self.assertEqual(
                        -(360 / config["teeth"]),
                        contact_frames[frame]["elements"]["input-rotor"]["rotationX"],
                    )
                self.assertEqual(0, contact_frames[28]["elements"]["output-rotor"]["rotationX"])
                contact_lifts = [
                    contact_frames[frame]["elements"]["orbiting-pawl-1"]["rotationX"]
                    for frame in range(29)
                ]
                self.assertEqual(0, contact_lifts[0])
                self.assertEqual(0, contact_lifts[2])
                self.assertEqual(0, contact_lifts[7])
                self.assertGreater(contact_lifts[14], 0)
                self.assertLess(contact_lifts[14], config["lift"] * .35)
                self.assertGreater(contact_lifts[19], contact_lifts[14])
                self.assertLess(contact_lifts[19], config["lift"] * .75)
                self.assertEqual(config["lift"], contact_lifts[23])
                self.assertEqual(config["lift"], contact_lifts[25])
                self.assertGreater(contact_lifts[25], contact_lifts[26])
                self.assertGreater(contact_lifts[26], contact_lifts[27])
                self.assertEqual(0, contact_lifts[28])

                elements = list(_walk_elements(document["elements"]))
                by_name = {element["name"]: element for element in elements}
                tooth_count = int(config["teeth"])
                self.assertEqual(0, tooth_count % 3)
                self.assertEqual(
                    tooth_count,
                    sum(name.startswith("input-ratchet-lock-face-") for name in by_name),
                )
                self.assertEqual(
                    tooth_count,
                    sum(name.startswith("input-locking-oak-ring-") for name in by_name),
                )
                self.assertEqual(
                    4,
                    sum(name.startswith("input-oak-ring-spoke-") for name in by_name),
                )
                self.assertEqual(0, config["tooth_angle_offset"])

                lock_face = by_name["input-ratchet-lock-face-00"]
                contact_part_name = (
                    "orbiting-pawl-1-contact-stick"
                    if config["pawl"] == "restored-hook"
                    else "orbiting-pawl-1-locking-tip"
                )
                contact_part = by_name[contact_part_name]
                pawl_mount = by_name["orbiting-pawl-1-mount"]
                self.assertGreater(config["pivot_lead"], 0)
                self.assertAlmostEqual(config["pivot_lead"], pawl_mount["from"][2])
                self.assertEqual(0, lock_face["to"][2])
                self.assertAlmostEqual(0, config["pivot_lead"] + contact_part["from"][2])
                tooth_radial = (lock_face["from"][1], lock_face["to"][1])
                pawl_radial = (
                    config["orbit_radius"] + contact_part["from"][1],
                    config["orbit_radius"] + contact_part["to"][1],
                )
                self.assertLess(max(tooth_radial[0], pawl_radial[0]), min(tooth_radial[1], pawl_radial[1]))
                self.assertLess(
                    max(lock_face["from"][0], contact_part["from"][0]),
                    min(lock_face["to"][0], contact_part["to"][0]),
                )
                self.assertEqual({"#brass"}, _textures(lock_face))
                lift_radians = math.radians(config["lift"])
                clearance_radius = min(
                    config["orbit_radius"] + y * math.cos(lift_radians) - z * math.sin(lift_radians)
                    for y in (contact_part["from"][1], contact_part["to"][1])
                    for z in (contact_part["from"][2], contact_part["to"][2])
                )
                self.assertGreaterEqual(clearance_radius, lock_face["to"][1] + .05)

                gear_style = config["gear"]
                if gear_style == "stepped-ratchet":
                    ramp = by_name["input-smooth-ramp-00"]
                    self.assertEqual({"#brass"}, _textures(ramp))
                    self.assertEqual(55, ramp["rotationX"])
                    outer_ring_radius = 4.16 + .62 / 2
                    minimum_ring_length = 2 * outer_ring_radius * math.tan(math.pi / tooth_count)
                    ring = by_name["input-locking-oak-ring-00"]
                    tire = by_name["input-thin-brass-tire-00"]
                    self.assertGreater(ring["to"][2] - ring["from"][2], minimum_ring_length)
                    self.assertGreater(
                        tire["to"][2] - tire["from"][2],
                        ring["to"][2] - ring["from"][2],
                    )
                    pawl = by_name["orbiting-pawl-1"]
                    pawl_children = {child["name"] for child in pawl["children"]}
                    self.assertIn("orbiting-pawl-1-spring-follower-cap", pawl_children)
                    self.assertIn("orbiting-pawl-1-contact-stick", pawl_children)
                    self.assertEqual(-1.08, by_name["orbiting-pawl-1-contact-stick"]["from"][1])
                    self.assertNotIn("orbiting-pawl-1-hook-shank", by_name)
                    self.assertNotIn("orbiting-pawl-1-reinforced-elbow", by_name)
                    self.assertNotIn("orbiting-pawl-1-locking-tip", by_name)
                elif gear_style == "deep-pocket-ring":
                    ramp = by_name["input-pocket-ramp-wear-00"]
                    self.assertIn("input-pocket-lug-oak-00", by_name)
                    self.assertEqual({"#brass"}, _textures(ramp))
                    self.assertEqual(54, ramp["rotationX"])
                    self.assertEqual({"#strippedoak"}, _textures(by_name["input-pocket-lug-oak-00"]))
                    self.assertNotIn("input-thin-brass-tire-00", by_name)
                elif gear_style == "swept-concept":
                    ramp = by_name["input-concept-swept-point-00"]
                    self.assertEqual({"#brass"}, _textures(ramp))
                    self.assertEqual(45, ramp["rotationX"])
                else:
                    self.fail(f"unhandled gear style {gear_style}")

                arm = by_name["output-broad-orbital-arm-00"]
                head = by_name["output-gearward-head-01"]
                carrier_plate = by_name["output-reinforced-head-plate-01-carrier"]
                for element in (arm, head, carrier_plate):
                    self.assertAlmostEqual(config["arm_width"], element["to"][2] - element["from"][2])
                self.assertLess(by_name["output-gearward-neck-01"]["from"][0], 0)
                self.assertEqual(3, sum(name.startswith("output-head-clamp-bolt-") for name in by_name))
                self.assertEqual(6, sum(name.startswith("output-head-clamp-nut-") for name in by_name))
                self.assertFalse(any(name.startswith("output-head-through-bolt-") for name in by_name))
                pin = by_name["orbiting-pawl-1-large-pin"]
                self.assertAlmostEqual(2.04, pin["to"][0] - pin["from"][0])

                expected_parts = {
                    "restored-hook": (
                        "orbiting-pawl-1-contact-stick",
                        "orbiting-pawl-1-pivot-collar",
                    ),
                    "drop-bar": (
                        "orbiting-pawl-1-drop-bar",
                        "orbiting-pawl-1-locking-tip",
                        "orbiting-pawl-1-pivot-block",
                    ),
                    "concept-crook": (
                        "orbiting-pawl-1-crook-stem",
                        "orbiting-pawl-1-crook-elbow",
                        "orbiting-pawl-1-locking-tip",
                        "orbiting-pawl-1-pivot-block",
                    ),
                }[config["pawl"]]
                for part in expected_parts:
                    self.assertIn(part, by_name)

                interface_text = (candidate_root / "interface.shape.json").read_text(encoding="utf-8")
                self.assertIn("input-ratchet-lock-face", interface_text)
                self.assertIn("orbiting-pawl", interface_text)
                self.assertNotIn("input-separate-foot", interface_text)
                input_text = (candidate_root / "input-side.shape.json").read_text(encoding="utf-8")
                self.assertIn("input-ratchet-lock-face", input_text)
                self.assertNotIn("output-broad-orbital-arm", input_text)
                output_text = (candidate_root / "output-side.shape.json").read_text(encoding="utf-8")
                self.assertIn("output-broad-orbital-arm", output_text)
                self.assertNotIn("input-ratchet-lock-face", output_text)

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
                        bolt = next(
                            element
                            for element in document["elements"]
                            if element["name"] == f"base-crossbeam-{edge}-bolt-{side}"
                        )
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
            candidate = "lock-b-pocket-bar"
            managed = build_review(root, approved_candidate=candidate)
            ownership = json.loads((managed / ".gearwright-review.json").read_text(encoding="utf-8"))
            self.assertEqual([candidate], ownership["candidates"])
            self.assertEqual(candidate, ownership["decision"]["candidate"])
            for state in ("rest", "interface", "input-side", "output-side"):
                self.assertTrue((managed / candidate / f"{state}.shape.json").is_file())
            self.assertFalse((managed / "lock-a-stepped-hook").exists())
            self.assertFalse((managed / "lock-c-concept-crook").exists())


if __name__ == "__main__":
    unittest.main()
