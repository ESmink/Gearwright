from __future__ import annotations

import json
import math
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
if not (ROOT / "tools" / "graphics").is_dir():
    ROOT = Path.cwd()
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tools" / "graphics"))

from graphics.review.lateral_motion_system import (  # noqa: E402
    AIR_CHECK_TRAVEL,
    AIR_ROOF_BORE_HALF,
    AIR_ROOF_CENTER_X,
    AIR_ROOF_CENTER_Z,
    AIR_ROOF_COLLAR_HALF,
    AIR_ROOF_COLLAR_WALL,
    AIR_ROOF_SEAT_Y,
    ANIMATIONS,
    BOTTOM_RESERVOIR_PORT_X,
    BOTTOM_RISER_BOTTOM,
    BOTTOM_RISER_TOP,
    COMPACT_CYLINDER_HALF_X,
    COMPACT_LOWER_PIPE_CENTER_Y,
    COMPACT_STAND_BASE_TOP,
    COMPACT_STAND_BOTTOM,
    COMPACT_STAND_CRADLE_BOTTOM,
    COMPACT_STAND_CRADLE_TOP,
    COMPACT_RISER_BOTTOM,
    COMPACT_INTAKE_CHECK_CENTER_Y,
    COMPACT_OUTPUT_CHECK_CENTER_Y,
    COMPACT_VERTICAL_CHECK_TRAVEL,
    CYLINDER_BOTTOM,
    CYLINDER_TOP,
    DESCRIPTIONS,
    PINION_RADIUS,
    PINION_Z,
    PIPE_CENTER_Y,
    ROCKER_ARM,
    ROCKER_Y,
    SELECTOR_Z,
    SINGLE_BORE_BOTTOM,
    SINGLE_CHECK_ANGLE,
    SINGLE_CHECK_TRAVEL,
    SINGLE_CYLINDER_BOTTOM,
    SINGLE_CYLINDER_HALF_X,
    SINGLE_CYLINDER_TOP,
    SINGLE_GLAND_POCKET_HALF_X,
    SINGLE_GLAND_POCKET_HALF_Z,
    SINGLE_GUIDE_BOTTOM,
    SINGLE_HEAD_TOP,
    SINGLE_CRANK_THROW,
    SINGLE_INTAKE_VALVE_SEAT_X,
    SINGLE_OUTPUT_VALVE_SEAT_X,
    SINGLE_PIPE_CENTER_Y,
    SINGLE_ROD_LENGTH,
    STAGES,
    TALL_PIPE_CENTER_Y,
    TALL_PUMP_BOTTOM,
    VALVE_ROD_X,
    _slider_state,
    build_review,
)


def _walk(elements):
    for element in elements:
        yield element
        yield from _walk(element.get("children", []))


def _document(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


class LateralMotionReviewTests(unittest.TestCase):
    def test_managed_package_preserves_a7_and_compares_singleacting_candidates(self):
        for phase in range(0, 361, 15):
            pin_y, pin_z, crosshead_y, _, _, _ = _slider_state(phase)
            self.assertAlmostEqual(8, math.hypot(pin_y - crosshead_y, pin_z))

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            managed = build_review(root)
            stray = managed / "orphan.txt"
            stray.write_text("orphan", encoding="utf-8")
            rebuilt = build_review(root)

            self.assertEqual(managed, rebuilt)
            self.assertFalse(stray.exists())
            self.assertFalse((root / "assets").exists())
            ownership = _document(rebuilt / ".gearwright-review.json")
            review_notes = (rebuilt / "README.md").read_text(encoding="utf-8")
            self.assertIn("6-unit stroke", review_notes)
            self.assertIn("four pieces around the clear 3-by-3 side openings", review_notes)
            self.assertIn("pipe-fit", review_notes)
            self.assertIn("low oak cradle", review_notes)
            self.assertIn("supported downward-facing", review_notes)
            self.assertIn("output half of the pressure ceiling", review_notes)
            self.assertIn("fully bronze output coupling", review_notes)
            self.assertEqual(list(DESCRIPTIONS), ownership["candidates"])
            self.assertEqual(list(STAGES), ownership["stages"])
            self.assertEqual(list(ANIMATIONS), ownership["animations"])
            self.assertEqual(
                ["gearwright:block/inspection-glass", "gearwright:block/inspection-shadow"],
                ownership["logicalReferences"],
            )
            self.assertEqual("a10-two-block-bottom-pipes", ownership["decision"]["baseApproved"])
            self.assertEqual("a11-one-block-full-frame", ownership["decision"]["preferredDirection"])
            self.assertEqual("a11-one-block-full-frame", ownership["decision"]["candidate"])
            self.assertEqual(["automatic-large-bellows"], ownership["shelved"])
            self.assertEqual("approved", ownership["decision"]["status"])
            self.assertTrue(ownership["decision"]["runtimePromotion"])
            self.assertNotIn(str(root), json.dumps(ownership))

            self.assertEqual(
                [
                    "a7-rear-pinion-external-openers",
                    "a9-single-acting-caged-poppets",
                    "a10-two-block-bottom-pipes",
                    "a11-one-block-full-frame",
                ],
                list(DESCRIPTIONS),
            )
            candidate_root = rebuilt / "a7-rear-pinion-external-openers"
            self.assertEqual(
                {f"{stage}.shape.json" for stage in STAGES},
                {path.name for path in candidate_root.glob("*.shape.json")},
            )

            one_axle = _document(candidate_root / "one-axle-crank.shape.json")
            one_names = {element["name"] for element in _walk(one_axle["elements"])}
            self.assertIn("gw-crank-single-web", one_names)
            self.assertIn("gw-crank-journal-retainer", one_names)
            self.assertNotIn("gw-crank-output-shaft-wide", one_names)
            self.assertNotIn("gw-crank-output-shaft-tall", one_names)
            self.assertFalse(any("output-web" in name for name in one_names))
            self.assertFalse(any("handle" in name.lower() for name in one_names))
            self.assertEqual(["rotation"], [animation["code"] for animation in one_axle["animations"]])

            through = _document(candidate_root / "through-crank.shape.json")
            through_names = {element["name"] for element in _walk(through["elements"])}
            self.assertIn("gw-crank-output-shaft-wide", through_names)
            self.assertIn("gw-crank-output-web", through_names)

            detail = _document(candidate_root / "valve-detail.shape.json")
            detail_elements = {element["name"]: element for element in _walk(detail["elements"])}
            self.assertFalse(any("liquid" in name for name in detail_elements))
            self.assertFalse(any("liquid/" in value for value in detail["textures"].values()))
            self.assertIn("gw-valve-upper-intake-flap-motion", detail_elements)
            self.assertIn("gw-valve-upper-intake-tappet-motion", detail_elements)
            self.assertIn("gw-valve-detail-drive-rod-motion", detail_elements)
            self.assertEqual(-VALVE_ROD_X, detail_elements["gw-valve-detail-drive-rod-motion"]["rotationOrigin"][0])
            self.assertEqual(1.15, detail_elements["gw-valve-detail-drive-rod-motion"]["rotationOrigin"][2])
            self.assertNotIn("gw-valve-upper-intake-glass", detail_elements)
            self.assertEqual(["valvegear"], [animation["code"] for animation in detail["animations"]])
            detail_frames = {keyframe["frame"]: keyframe["elements"] for keyframe in detail["animations"][0]["keyframes"]}
            self.assertEqual(20, detail_frames[15]["gw-valve-upper-intake-flap-motion"]["rotationZ"])
            self.assertEqual(-.70, detail_frames[15]["gw-valve-upper-intake-tappet-motion"]["offsetY"])
            self.assertEqual(-.70, detail_frames[15]["gw-valve-detail-drive-rod-motion"]["offsetY"])
            origin = detail_elements["gw-valve-upper-intake-flap-motion"]["rotationOrigin"]
            flap = detail_elements["gw-valve-upper-intake-flap"]
            stop = detail_elements["gw-valve-upper-intake-travel-stop"]
            angle = math.radians(20)
            contact_x = origin[0] + flap["to"][0] * math.cos(angle) - flap["from"][1] * math.sin(angle)
            contact_y = origin[1] + flap["to"][0] * math.sin(angle) + flap["from"][1] * math.cos(angle)
            self.assertLessEqual(stop["from"][0], contact_x)
            self.assertGreaterEqual(stop["to"][0], contact_x)
            self.assertLessEqual(stop["from"][1], contact_y)
            self.assertGreaterEqual(stop["to"][1], contact_y)

            for stage, base_angle in (
                ("pump-bottom", 0),
                ("pump-cutaway", 0),
                ("pump-top-fit", 180),
                ("pump-side-fit", 90),
            ):
                pump = _document(candidate_root / f"{stage}.shape.json")
                elements = {element["name"]: element for element in _walk(pump["elements"])}
                self.assertEqual(base_angle, elements["gw-pump-mount"].get("rotationX", 0))
                self.assertFalse(any("liquid/" in value for value in pump["textures"].values()))
                self.assertFalse(any("liquid" in name for name in elements))
                self.assertFalse(any("valve" in name and "glass" in name for name in elements))
                self.assertIn("gw-cylinder-back-glass", elements)
                if stage == "pump-cutaway":
                    self.assertNotIn("gw-cylinder-front-glass", elements)
                    self.assertNotIn("gw-cylinder-front-left-rail", elements)
                else:
                    self.assertIn("gw-cylinder-front-glass", elements)
                    self.assertIn("gw-cylinder-front-left-rail", elements)

                for suffix in ("bottom", "top", "front", "back"):
                    input_piece = elements[f"gw-pump-input-pipe-{suffix}"]
                    output_piece = elements[f"gw-pump-output-pipe-{suffix}"]
                    self.assertEqual(input_piece["from"][1:], output_piece["from"][1:])
                    self.assertEqual(input_piece["to"][1:], output_piece["to"][1:])
                    self.assertEqual(-input_piece["to"][0], output_piece["from"][0])
                    self.assertEqual(-input_piece["from"][0], output_piece["to"][0])
                front = elements["gw-pump-input-pipe-front"]
                back = elements["gw-pump-input-pipe-back"]
                self.assertEqual(-front["to"][2], back["from"][2])
                self.assertEqual(-front["from"][2], back["to"][2])

                input_pipe = elements["gw-pump-input-pipe-left"] if "gw-pump-input-pipe-left" in elements else elements["gw-pump-input-pipe-bottom"]
                input_chest_left = elements["gw-input-manifold-left"]
                output_pipe = elements["gw-pump-output-pipe-bottom"]
                output_chest_right = elements["gw-output-manifold-right"]
                self.assertEqual(input_pipe["to"][0], input_chest_left["from"][0])
                self.assertEqual(output_pipe["from"][0], output_chest_right["to"][0])
                for side in ("input", "output"):
                    self.assertIn(f"gw-{side}-manifold-front", elements)
                    self.assertIn(f"gw-{side}-manifold-back", elements)
                    self.assertIn(f"gw-{side}-manifold-top-cap", elements)
                    self.assertIn(f"gw-{side}-manifold-bottom-cap", elements)
                    chest_left = elements[f"gw-{side}-manifold-left"]
                    chest_right = elements[f"gw-{side}-manifold-right"]
                    self.assertAlmostEqual(3.2, chest_right["to"][0] - chest_left["from"][0])

                self.assertNotIn("gw-crosshead-guide-head", elements)
                for side in ("left", "right"):
                    self.assertIn(f"gw-crosshead-guide-{side}", elements)
                    self.assertIn(f"gw-crosshead-guide-{side}-foot", elements)
                    guide = elements[f"gw-crosshead-guide-{side}"]
                    foot = elements[f"gw-crosshead-guide-{side}-foot"]
                    self.assertLessEqual(guide["from"][1], foot["to"][1])
                    self.assertGreaterEqual(guide["to"][1], foot["from"][1])

                self.assertIn("gw-valve-drive-rack-spine", elements)
                self.assertIn("gw-valve-drive-rack-rear-bracket", elements)
                self.assertIn("gw-valve-drive-rack-crosshead-bracket", elements)
                self.assertEqual(9, sum(name.startswith("gw-valve-drive-rack-tooth-") for name in elements))
                self.assertIn("gw-valve-pinion-motion", elements)
                self.assertEqual(12, sum(name.startswith("gw-valve-pinion-tooth-") for name in elements))
                self.assertIn("gw-valve-selector-drive-motion", elements)
                self.assertIn("gw-valve-selector-driving-dog", elements)
                self.assertIn("gw-valve-reverser-collar-top", elements)
                self.assertIn("gw-valve-rocker-motion", elements)
                self.assertIn("gw-valve-left-rod-motion", elements)
                self.assertIn("gw-valve-right-rod-motion", elements)
                self.assertLess(PINION_RADIUS, 3.0)
                self.assertGreater(ROCKER_Y - PINION_RADIUS, elements["gw-cylinder-top-cap"]["to"][1])
                rack = elements["gw-valve-drive-rack-spine"]
                rear_glass = elements["gw-cylinder-back-glass"]
                crank_web = elements["gw-crank-single-web"]
                pinion_origin = elements["gw-valve-pinion-motion"]["rotationOrigin"]
                selector_origin = elements["gw-valve-selector-drive-motion"]["rotationOrigin"]
                shaft = elements["gw-valve-pinion-shaft"]
                self.assertGreater(rack["from"][2], rear_glass["to"][2])
                self.assertGreater(rack["from"][2], crank_web["to"][2])
                self.assertEqual(PINION_Z, pinion_origin[2])
                self.assertEqual(SELECTOR_Z, selector_origin[2])
                self.assertLessEqual(shaft["from"][2], SELECTOR_Z)
                self.assertGreaterEqual(shaft["to"][2], PINION_Z)

                glass = elements.get("gw-cylinder-front-glass")
                for valve in ("upper-intake", "lower-intake", "upper-output", "lower-output"):
                    flap_motion = elements[f"gw-valve-{valve}-flap-motion"]
                    tappet_motion = elements[f"gw-valve-{valve}-tappet-motion"]
                    self.assertIn(f"gw-valve-{valve}-travel-stop", elements)
                    self.assertGreater(abs(tappet_motion["rotationOrigin"][0]), 4.0)
                    side = "input" if "intake" in valve else "output"
                    front_wall = elements[f"gw-{side}-manifold-front"]
                    back_wall = elements[f"gw-{side}-manifold-back"]
                    self.assertGreater(tappet_motion["rotationOrigin"][2], front_wall["to"][2])
                    self.assertLess(tappet_motion["rotationOrigin"][2], back_wall["from"][2])
                    if glass is not None:
                        self.assertLessEqual(abs(flap_motion["rotationOrigin"][0]) - glass["to"][0], .25)

                self.assertIn("gw-connecting-rod-crank-bearing-top", elements)
                self.assertIn("gw-connecting-rod-crosshead-bearing-front", elements)
                self.assertNotIn("gw-connecting-rod-crank-eye", elements)
                self.assertNotIn("gw-connecting-rod-crosshead-eye", elements)

                self.assertEqual(["doubleacting"], [animation["code"] for animation in pump["animations"]])
                cycle = pump["animations"][0]
                frames = {keyframe["frame"]: keyframe["elements"] for keyframe in cycle["keyframes"]}
                self.assertEqual(set(range(0, 61, 5)), set(frames))
                self.assertAlmostEqual(-8, frames[30]["gw-piston-motion"]["offsetY"])
                self.assertAlmostEqual(math.degrees(8 / PINION_RADIUS), frames[30]["gw-valve-pinion-motion"]["rotationZ"])
                self.assertLess(frames[15]["gw-valve-pinion-motion"]["rotationZ"], frames[30]["gw-valve-pinion-motion"]["rotationZ"])
                self.assertGreater(frames[30]["gw-valve-pinion-motion"]["rotationZ"], frames[45]["gw-valve-pinion-motion"]["rotationZ"])
                self.assertEqual(7, frames[15]["gw-valve-rocker-motion"]["rotationZ"])
                self.assertEqual(-7, frames[45]["gw-valve-rocker-motion"]["rotationZ"])

                self.assertGreater(frames[15]["gw-valve-upper-intake-flap-motion"]["rotationZ"], 0)
                self.assertGreater(frames[15]["gw-valve-lower-output-flap-motion"]["rotationZ"], 0)
                self.assertEqual(0, frames[15]["gw-valve-lower-intake-flap-motion"]["rotationZ"])
                self.assertEqual(0, frames[15]["gw-valve-upper-output-flap-motion"]["rotationZ"])
                self.assertLess(frames[45]["gw-valve-lower-intake-flap-motion"]["rotationZ"], 0)
                self.assertLess(frames[45]["gw-valve-upper-output-flap-motion"]["rotationZ"], 0)

                for frame in frames.values():
                    self.assertEqual(
                        frame["gw-valve-pinion-motion"]["rotationZ"],
                        frame["gw-valve-selector-drive-motion"]["rotationZ"],
                    )
                    rocker_angle = math.radians(frame["gw-valve-rocker-motion"]["rotationZ"])
                    for side_sign, side in ((-1, "left"), (1, "right")):
                        valve_rod = frame[f"gw-valve-{side}-rod-motion"]
                        self.assertAlmostEqual(side_sign * ROCKER_ARM * (math.cos(rocker_angle) - 1), valve_rod["offsetX"])
                        self.assertAlmostEqual(side_sign * ROCKER_ARM * math.sin(rocker_angle), valve_rod["offsetY"])

                piston_origin_y = elements["gw-piston-motion"]["rotationOrigin"][1]
                crosshead_bottom_at_lower_dead_center = piston_origin_y + frames[30]["gw-piston-motion"]["offsetY"] + elements["gw-crosshead-shoe-left"]["from"][1]
                crosshead_top_at_upper_dead_center = piston_origin_y + elements["gw-crosshead-shoe-left"]["to"][1]
                cap_top = elements["gw-cylinder-top-cap"]["to"][1]
                gland_top = elements["gw-piston-rod-gland"]["to"][1]
                self.assertGreaterEqual(crosshead_bottom_at_lower_dead_center - cap_top, .19)
                self.assertGreaterEqual(crosshead_bottom_at_lower_dead_center - gland_top, .44)
                guide = elements["gw-crosshead-guide-left"]
                self.assertLessEqual(guide["from"][1], crosshead_bottom_at_lower_dead_center)
                self.assertGreaterEqual(guide["to"][1], crosshead_top_at_upper_dead_center)

                upper_seal = elements["gw-piston-upper-seal"]
                lower_seal = elements["gw-piston-lower-seal"]
                self.assertAlmostEqual(8.0, upper_seal["to"][0] - upper_seal["from"][0])
                self.assertEqual(-VALVE_ROD_X, elements["gw-valve-left-rod-motion"]["rotationOrigin"][0])
                self.assertEqual(VALVE_ROD_X, elements["gw-valve-right-rod-motion"]["rotationOrigin"][0])
                self.assertGreater(VALVE_ROD_X, upper_seal["to"][0])
                upper_clearance = elements["gw-cylinder-top-cap"]["from"][1] - (piston_origin_y + upper_seal["to"][1])
                lower_clearance = (piston_origin_y + frames[30]["gw-piston-motion"]["offsetY"] + lower_seal["from"][1]) - elements["gw-cylinder-bottom-cap"]["to"][1]
                self.assertGreaterEqual(upper_clearance, .39)
                self.assertLessEqual(upper_clearance, .45)
                self.assertGreaterEqual(lower_clearance, .39)
                self.assertLessEqual(lower_clearance, .45)

                self.assertEqual(CYLINDER_TOP, elements["gw-cylinder-top-cap"]["from"][1])
                self.assertEqual(CYLINDER_BOTTOM, elements["gw-cylinder-bottom-cap"]["from"][1])



            for candidate, wet_mode in (
                ("a9-single-acting-caged-poppets", "poppet"),
                ("a10-two-block-bottom-pipes", "poppet"),
                ("a11-one-block-full-frame", "vertical-poppet"),
            ):
                passive_root = rebuilt / candidate
                self.assertEqual(
                    {f"{stage}.shape.json" for stage in STAGES},
                    {path.name for path in passive_root.glob("*.shape.json")},
                )

                passive_detail = _document(passive_root / "valve-detail.shape.json")
                detail_elements = {element["name"]: element for element in _walk(passive_detail["elements"])}
                self.assertEqual(["passivechecks"], [animation["code"] for animation in passive_detail["animations"]])
                for forbidden in ("pinion", "selector", "rocker", "tappet", "drive-rack", "drive-rod", "breather"):
                    self.assertFalse(any(forbidden in name for name in detail_elements))
                detail_frames = {keyframe["frame"]: keyframe["elements"] for keyframe in passive_detail["animations"][0]["keyframes"]}
                self.assertEqual({0, 10, 20, 30, 40}, set(detail_frames))
                if wet_mode == "clapper":
                    self.assertIn("gw-detail-wet-intake-check-flap", detail_elements)
                    self.assertIn("gw-detail-wet-output-check-hinge-pin", detail_elements)
                    self.assertEqual(SINGLE_CHECK_ANGLE, detail_frames[10]["gw-detail-wet-output-check-motion"]["rotationZ"])
                    self.assertEqual(0, detail_frames[10]["gw-detail-wet-intake-check-motion"]["rotationZ"])
                    self.assertEqual(SINGLE_CHECK_ANGLE, detail_frames[30]["gw-detail-wet-intake-check-motion"]["rotationZ"])
                    self.assertEqual(0, detail_frames[30]["gw-detail-wet-output-check-motion"]["rotationZ"])
                elif wet_mode == "vertical-poppet":
                    self.assertNotIn("gw-detail-wet-gallery-bottom", detail_elements)
                    self.assertIn("gw-detail-wet-input-riser-back", detail_elements)
                    self.assertIn("gw-detail-wet-output-riser-back", detail_elements)
                    self.assertNotIn("gw-detail-wet-input-riser-front", detail_elements)
                    self.assertIn("gw-detail-wet-intake-check-seat-front", detail_elements)
                    self.assertIn("gw-detail-wet-output-check-stop-spider-x", detail_elements)
                    self.assertIn("gw-detail-wet-output-check-stop-spider-z", detail_elements)
                    self.assertIn("gw-detail-wet-output-check-stop-bushing", detail_elements)
                    self.assertEqual(4, sum(name.startswith("gw-detail-wet-intake-check-cage-rail-") for name in detail_elements))
                    self.assertEqual(-COMPACT_VERTICAL_CHECK_TRAVEL, detail_frames[10]["gw-detail-wet-output-check-motion"]["offsetY"])
                    self.assertEqual(0, detail_frames[10]["gw-detail-wet-intake-check-motion"]["offsetY"])
                    self.assertEqual(COMPACT_VERTICAL_CHECK_TRAVEL, detail_frames[30]["gw-detail-wet-intake-check-motion"]["offsetY"])
                    self.assertEqual(0, detail_frames[30]["gw-detail-wet-output-check-motion"]["offsetY"])
                else:
                    self.assertIn("gw-detail-wet-intake-check-disc", detail_elements)
                    self.assertIn("gw-detail-wet-output-check-guide-rod", detail_elements)
                    self.assertIn("gw-detail-wet-output-check-guide-sleeve", detail_elements)
                    self.assertIn("gw-detail-wet-output-check-glass-witness", detail_elements)
                    for brace in ("spider-y", "spider-z", "bushing"):
                        self.assertNotIn(f"gw-detail-wet-output-check-seat-{brace}", detail_elements)
                        self.assertIn(f"gw-detail-wet-output-check-stop-{brace}", detail_elements)
                    self.assertEqual(4, sum(name.startswith("gw-detail-wet-intake-check-cage-rail-") for name in detail_elements))
                    self.assertEqual(SINGLE_CHECK_TRAVEL, detail_frames[10]["gw-detail-wet-output-check-motion"]["offsetX"])
                    self.assertEqual(0, detail_frames[10]["gw-detail-wet-intake-check-motion"]["offsetX"])
                    self.assertEqual(SINGLE_CHECK_TRAVEL, detail_frames[30]["gw-detail-wet-intake-check-motion"]["offsetX"])
                    self.assertEqual(0, detail_frames[30]["gw-detail-wet-output-check-motion"]["offsetX"])

                for stage, base_angle in (
                    ("pump-bottom", 0),
                    ("pump-cutaway", 0),
                    ("pump-top-fit", 180),
                    ("pump-side-fit", 90),
                ):
                    pump = _document(passive_root / f"{stage}.shape.json")
                    elements = {element["name"]: element for element in _walk(pump["elements"])}
                    self.assertEqual(base_angle, elements["gw-pump-mount"].get("rotationX", 0))
                    self.assertFalse(any("liquid/" in value for value in pump["textures"].values()))
                    self.assertFalse(any("liquid" in name for name in elements))
                    for forbidden in (
                        "valve-pinion",
                        "valve-selector",
                        "valve-rocker",
                        "valve-drive-rack",
                        "tappet",
                        "valve-left-rod",
                        "valve-right-rod",
                        "breather-gallery",
                        "breather-neck",
                    ):
                        self.assertFalse(any(forbidden in name for name in elements))

                    self.assertIn("gw-wet-intake-check-motion", elements)
                    self.assertIn("gw-wet-output-check-motion", elements)
                    self.assertIn("gw-breather-intake-check-motion", elements)
                    self.assertIn("gw-breather-exhaust-check-motion", elements)
                    body_half_x = COMPACT_CYLINDER_HALF_X if candidate == "a11-one-block-full-frame" else SINGLE_CYLINDER_HALF_X
                    roof_parts = (
                        "input-deck",
                        "gland-front-bridge",
                        "gland-back-bridge",
                        "output-inner-web",
                        "output-outer-web",
                        "output-front-strip",
                        "output-center-strip",
                        "output-back-strip",
                    )
                    for part in roof_parts:
                        roof_piece = elements[f"gw-cylinder-top-cap-roof-{part}"]
                        self.assertEqual(SINGLE_CYLINDER_TOP, roof_piece["from"][1])
                        self.assertEqual(SINGLE_HEAD_TOP, roof_piece["to"][1])
                    input_deck = elements["gw-cylinder-top-cap-roof-input-deck"]
                    inner_web = elements["gw-cylinder-top-cap-roof-output-inner-web"]
                    outer_web = elements["gw-cylinder-top-cap-roof-output-outer-web"]
                    front_strip = elements["gw-cylinder-top-cap-roof-output-front-strip"]
                    center_strip = elements["gw-cylinder-top-cap-roof-output-center-strip"]
                    back_strip = elements["gw-cylinder-top-cap-roof-output-back-strip"]
                    self.assertEqual((-body_half_x, -SINGLE_GLAND_POCKET_HALF_X), (input_deck["from"][0], input_deck["to"][0]))
                    self.assertEqual(SINGLE_GLAND_POCKET_HALF_X, inner_web["from"][0])
                    self.assertEqual(AIR_ROOF_CENTER_X - AIR_ROOF_COLLAR_HALF, inner_web["to"][0])
                    self.assertEqual(AIR_ROOF_CENTER_X + AIR_ROOF_COLLAR_HALF, outer_web["from"][0])
                    self.assertEqual(body_half_x, outer_web["to"][0])
                    self.assertEqual(-AIR_ROOF_CENTER_Z - AIR_ROOF_COLLAR_HALF, front_strip["to"][2])
                    self.assertAlmostEqual(-AIR_ROOF_CENTER_Z + AIR_ROOF_COLLAR_HALF, center_strip["from"][2])
                    self.assertAlmostEqual(AIR_ROOF_CENTER_Z - AIR_ROOF_COLLAR_HALF, center_strip["to"][2])
                    self.assertEqual(AIR_ROOF_CENTER_Z + AIR_ROOF_COLLAR_HALF, back_strip["from"][2])
                    self.assertNotIn("gw-cylinder-top-cap-upper-left", elements)
                    self.assertNotIn("gw-cylinder-top-cap-output-lower-0", elements)
                    self.assertNotIn("gw-piston-rod-gland", elements)
                    for part in ("front", "back", "left", "right"):
                        self.assertIn(f"gw-piston-rod-gland-plate-{part}", elements)
                        self.assertIn(f"gw-piston-rod-gland-liner-{part}", elements)
                    self.assertEqual(4, sum(name.startswith("gw-piston-rod-gland-fastener-") for name in elements))
                    self.assertNotIn("gw-breather-intake-weather-cap", elements)
                    self.assertNotIn("gw-breather-exhaust-weather-cap", elements)

                    for face in ("front", "back"):
                        glass_name = f"gw-cylinder-{face}-glass"
                        if stage == "pump-cutaway" and face == "front":
                            self.assertNotIn(glass_name, elements)
                        else:
                            self.assertIn(glass_name, elements)
                            self.assertAlmostEqual(SINGLE_BORE_BOTTOM + .43, elements[glass_name]["from"][1])
                        self.assertFalse(any(name.startswith(f"gw-cylinder-{face}-input-valve") for name in elements))
                        self.assertFalse(any(name.startswith(f"gw-cylinder-{face}-output-valve") for name in elements))

                    if candidate in ("a10-two-block-bottom-pipes", "a11-one-block-full-frame"):
                        self.assertNotIn("gw-cylinder-bottom-cap", elements)
                        self.assertIn("gw-cylinder-bottom-cap-back", elements)
                        if stage == "pump-cutaway":
                            self.assertNotIn("gw-cylinder-bottom-cap-front", elements)
                        else:
                            self.assertIn("gw-cylinder-bottom-cap-front", elements)
                        for part in ("left-web", "center-web", "right-web"):
                            cap = elements[f"gw-cylinder-bottom-cap-{part}"]
                            self.assertEqual(SINGLE_CYLINDER_BOTTOM, cap["from"][1])
                            self.assertEqual(SINGLE_BORE_BOTTOM, cap["to"][1])
                        self.assertEqual(-4.0, elements["gw-cylinder-bottom-cap-left-web"]["to"][0])
                        self.assertEqual((-1.0, 1.0), (
                            elements["gw-cylinder-bottom-cap-center-web"]["from"][0],
                            elements["gw-cylinder-bottom-cap-center-web"]["to"][0],
                        ))
                        self.assertEqual(4.0, elements["gw-cylinder-bottom-cap-right-web"]["from"][0])
                        self.assertIn("gw-single-cylinder-input-wall-solid", elements)
                        self.assertIn("gw-single-cylinder-output-wall-solid", elements)
                        for side in ("input", "output"):
                            for part in ("beneath-pipe", "above-pipe", "left-of-pipe", "right-of-pipe"):
                                self.assertNotIn(f"gw-single-cylinder-{side}-wall-{part}", elements)
                        self.assertEqual(SINGLE_BORE_BOTTOM, elements["gw-single-cylinder-output-wall-solid"]["from"][1])
                        self.assertEqual(SINGLE_CYLINDER_TOP, elements["gw-single-cylinder-output-wall-solid"]["to"][1])
                    else:
                        self.assertEqual(SINGLE_CYLINDER_BOTTOM, elements["gw-cylinder-bottom-cap"]["from"][1])
                        self.assertEqual(SINGLE_BORE_BOTTOM, elements["gw-cylinder-bottom-cap"]["to"][1])
                        for side in ("input", "output"):
                            for part in ("beneath-pipe", "left-of-pipe", "right-of-pipe"):
                                self.assertIn(f"gw-single-cylinder-{side}-wall-{part}", elements)
                            beneath = elements[f"gw-single-cylinder-{side}-wall-beneath-pipe"]
                            left = elements[f"gw-single-cylinder-{side}-wall-left-of-pipe"]
                            right = elements[f"gw-single-cylinder-{side}-wall-right-of-pipe"]
                            self.assertEqual(SINGLE_PIPE_CENTER_Y - 1.5, beneath["to"][1])
                            self.assertEqual(-1.5, left["to"][2])
                            self.assertEqual(1.5, right["from"][2])
                        self.assertIn("gw-single-cylinder-input-wall-above-pipe", elements)
                        self.assertNotIn("gw-single-cylinder-output-wall-above-pipe", elements)
                        self.assertEqual(
                            SINGLE_PIPE_CENTER_Y + 1.5,
                            elements["gw-single-cylinder-input-wall-above-pipe"]["from"][1],
                        )
                        self.assertEqual(
                            SINGLE_PIPE_CENTER_Y + 1.5,
                            elements["gw-single-cylinder-output-wall-solid"]["from"][1],
                        )
                        self.assertEqual(SINGLE_CYLINDER_TOP, elements["gw-single-cylinder-output-wall-solid"]["to"][1])
                    self.assertFalse(any("wet-divider" in name for name in elements))

                    if candidate == "a10-two-block-bottom-pipes":
                        for side, center_x in (("input", -BOTTOM_RESERVOIR_PORT_X), ("output", BOTTOM_RESERVOIR_PORT_X)):
                            left_riser = elements[f"gw-pump-{side}-reservoir-riser-left"]
                            right_riser = elements[f"gw-pump-{side}-reservoir-riser-right"]
                            self.assertEqual(BOTTOM_RISER_BOTTOM, left_riser["from"][1])
                            self.assertEqual(BOTTOM_RISER_TOP, left_riser["to"][1])
                            self.assertAlmostEqual(center_x, (left_riser["from"][0] + right_riser["to"][0]) / 2)
                            self.assertIn(f"gw-pump-{side}-reservoir-riser-back", elements)
                            self.assertIn(f"gw-pump-{side}-elbow-back", elements)
                            if stage == "pump-cutaway":
                                self.assertNotIn(f"gw-pump-{side}-reservoir-riser-front", elements)
                                self.assertNotIn(f"gw-pump-{side}-elbow-front", elements)
                            else:
                                self.assertIn(f"gw-pump-{side}-reservoir-riser-front", elements)
                                self.assertIn(f"gw-pump-{side}-elbow-front", elements)
                            self.assertIn(f"gw-pump-{side}-reservoir-collar-left", elements)
                            self.assertIn(f"gw-pump-{side}-block-joint-band-right", elements)
                            coupling_bottom = elements[f"gw-pump-{side}-end-coupling-bottom"]
                            coupling_top = elements[f"gw-pump-{side}-end-coupling-top"]
                            self.assertAlmostEqual(TALL_PIPE_CENTER_Y, (coupling_bottom["from"][1] + coupling_top["to"][1]) / 2)
                        input_pipe = elements["gw-pump-input-elbow-bottom"]
                        output_pipe = elements["gw-pump-output-elbow-bottom"]
                        self.assertEqual(-4.0, elements["gw-pump-input-elbow-top-outer"]["to"][0])
                        self.assertEqual(4.0, elements["gw-pump-output-elbow-top-outer"]["from"][0])
                        self.assertEqual(
                            elements["gw-cylinder-bottom-cap-left-web"]["to"][0],
                            elements["gw-pump-input-reservoir-riser-left"]["to"][0],
                        )
                        self.assertEqual(
                            elements["gw-cylinder-bottom-cap-center-web"]["from"][0],
                            elements["gw-pump-input-reservoir-riser-right"]["from"][0],
                        )
                        self.assertEqual(
                            elements["gw-cylinder-bottom-cap-center-web"]["to"][0],
                            elements["gw-pump-output-reservoir-riser-left"]["to"][0],
                        )
                        self.assertEqual(
                            elements["gw-cylinder-bottom-cap-right-web"]["from"][0],
                            elements["gw-pump-output-reservoir-riser-right"]["from"][0],
                        )
                        self.assertIn("gw-tall-frame-left-front-post", elements)
                        self.assertIn("gw-tall-frame-right-back-post", elements)
                        left_post = elements["gw-tall-frame-left-front-post"]
                        right_post = elements["gw-tall-frame-right-back-post"]
                        self.assertEqual(-8.0, left_post["from"][0])
                        self.assertEqual(8.0, right_post["to"][0])
                        self.assertEqual(TALL_PUMP_BOTTOM + .40, left_post["from"][1])
                        self.assertLessEqual(right_post["to"][1], -8.0)
                        for brace in (
                            "gw-tall-frame-back-cross-0",
                            "gw-tall-frame-back-cross-1",
                            "gw-tall-frame-front-knee-lower-left",
                            "gw-tall-frame-front-knee-upper-right",
                            "gw-tall-frame-front-knee-vessel-left",
                        ):
                            self.assertIn(brace, elements)
                        self.assertIn("gw-tall-frame-front-lower-left-joint-plate", elements)
                        self.assertIn("gw-tall-frame-back-cross-joint-plate", elements)
                        self.assertIn("gw-tall-frame-pipe-bed-front-rail", elements)
                        self.assertIn("gw-tall-frame-vessel-sill-back-rail", elements)
                        self.assertNotIn("gw-pump-input-bottom-coupling-left", elements)
                    elif candidate == "a11-one-block-full-frame":
                        for side, center_x in (
                            ("input", -BOTTOM_RESERVOIR_PORT_X),
                            ("output", BOTTOM_RESERVOIR_PORT_X),
                        ):
                            left_riser = elements[f"gw-pump-{side}-floor-riser-left"]
                            right_riser = elements[f"gw-pump-{side}-floor-riser-right"]
                            self.assertEqual(COMPACT_RISER_BOTTOM, left_riser["from"][1])
                            self.assertEqual(SINGLE_BORE_BOTTOM, left_riser["to"][1])
                            self.assertAlmostEqual(center_x, (left_riser["from"][0] + right_riser["to"][0]) / 2)

                            outer_wall = elements[f"gw-pump-{side}-outer-drop-outer-wall"]
                            inner_wall = elements[f"gw-pump-{side}-outer-drop-inner-wall-lower"]
                            drop_back = elements[f"gw-pump-{side}-outer-drop-back"]
                            self.assertEqual(COMPACT_RISER_BOTTOM, outer_wall["from"][1])
                            self.assertEqual(SINGLE_PIPE_CENTER_Y + 1.5, outer_wall["to"][1])
                            self.assertEqual(COMPACT_RISER_BOTTOM, inner_wall["from"][1])
                            self.assertEqual(SINGLE_CYLINDER_BOTTOM, inner_wall["to"][1])
                            self.assertIn(f"gw-pump-{side}-upper-stub-back", elements)
                            self.assertIn(f"gw-pump-{side}-lower-run-back", elements)
                            self.assertIn(f"gw-pump-{side}-floor-riser-back", elements)
                            self.assertNotIn(f"gw-pump-{side}-floor-collar-left", elements)
                            self.assertNotIn(f"gw-pump-{side}-outer-drop-left", elements)
                            self.assertNotIn(f"gw-pump-{side}-outer-drop-right", elements)
                            if stage == "pump-cutaway":
                                self.assertNotIn(f"gw-pump-{side}-upper-stub-front", elements)
                                self.assertNotIn(f"gw-pump-{side}-outer-drop-front", elements)
                                self.assertNotIn(f"gw-pump-{side}-lower-run-front", elements)
                                self.assertNotIn(f"gw-pump-{side}-floor-riser-front", elements)
                            else:
                                drop_front = elements[f"gw-pump-{side}-outer-drop-front"]
                                self.assertEqual(drop_back["from"][:2], drop_front["from"][:2])
                                self.assertEqual(drop_back["to"][:2], drop_front["to"][:2])
                                self.assertIn(f"gw-pump-{side}-upper-stub-front", elements)
                                self.assertIn(f"gw-pump-{side}-lower-run-front", elements)
                                self.assertIn(f"gw-pump-{side}-floor-riser-front", elements)

                            cylinder_wall = (
                                elements["gw-single-cylinder-input-wall-solid"]
                                if side == "input"
                                else elements["gw-single-cylinder-output-wall-solid"]
                            )
                            if side == "input":
                                self.assertEqual((-8.0, -7.5), (outer_wall["from"][0], outer_wall["to"][0]))
                                self.assertEqual((-7.5, -COMPACT_CYLINDER_HALF_X), (drop_back["from"][0], drop_back["to"][0]))
                                self.assertEqual((-COMPACT_CYLINDER_HALF_X, -4.0), (inner_wall["from"][0], inner_wall["to"][0]))
                                self.assertEqual(drop_back["to"][0], cylinder_wall["from"][0])
                            else:
                                self.assertEqual((7.5, 8.0), (outer_wall["from"][0], outer_wall["to"][0]))
                                self.assertEqual((COMPACT_CYLINDER_HALF_X, 7.5), (drop_back["from"][0], drop_back["to"][0]))
                                self.assertEqual((4.0, COMPACT_CYLINDER_HALF_X), (inner_wall["from"][0], inner_wall["to"][0]))
                                self.assertEqual(drop_back["from"][0], cylinder_wall["to"][0])

                            coupling_bottom = elements[f"gw-pump-{side}-end-coupling-bottom"]
                            coupling_top = elements[f"gw-pump-{side}-end-coupling-top"]
                            self.assertAlmostEqual(SINGLE_PIPE_CENTER_Y, (coupling_bottom["from"][1] + coupling_top["to"][1]) / 2)

                        input_pipe = elements["gw-pump-input-lower-run-bottom"]
                        output_pipe = elements["gw-pump-output-lower-run-bottom"]
                        self.assertEqual(-8.0, elements["gw-pump-input-upper-stub-top"]["from"][0])
                        self.assertEqual(8.0, elements["gw-pump-output-upper-stub-top"]["to"][0])
                        self.assertEqual(-7.5, elements["gw-pump-input-upper-stub-bottom-outer"]["to"][0])
                        self.assertEqual(7.5, elements["gw-pump-output-upper-stub-bottom-outer"]["from"][0])
                        self.assertEqual(
                            elements["gw-cylinder-bottom-cap-left-web"]["to"][0],
                            elements["gw-pump-input-floor-riser-left"]["to"][0],
                        )
                        self.assertEqual(
                            elements["gw-cylinder-bottom-cap-center-web"]["from"][0],
                            elements["gw-pump-input-floor-riser-right"]["from"][0],
                        )
                        self.assertEqual(
                            elements["gw-cylinder-bottom-cap-center-web"]["to"][0],
                            elements["gw-pump-output-floor-riser-left"]["to"][0],
                        )
                        self.assertEqual(
                            elements["gw-cylinder-bottom-cap-right-web"]["from"][0],
                            elements["gw-pump-output-floor-riser-right"]["from"][0],
                        )

                        self.assertFalse(any(name.startswith("gw-compact-frame-") for name in elements))
                        supported_downward_state = stage in {"pump-bottom", "pump-cutaway"}
                        if supported_downward_state:
                            front_runner = elements["gw-downward-stand-base-front-runner"]
                            right_back_leg = elements["gw-downward-stand-right-back-leg"]
                            self.assertEqual((-7.25, -6.60), (front_runner["from"][0], front_runner["from"][2]))
                            self.assertEqual((7.25, -5.40), (front_runner["to"][0], front_runner["to"][2]))
                            self.assertEqual(COMPACT_STAND_BOTTOM, front_runner["from"][1])
                            self.assertEqual(COMPACT_STAND_BASE_TOP, front_runner["to"][1])
                            self.assertEqual(COMPACT_STAND_BASE_TOP, right_back_leg["from"][1])
                            self.assertEqual(-19.25, right_back_leg["to"][1])
                            for brace in (
                                "gw-downward-stand-front-knee-left",
                                "gw-downward-stand-front-knee-right",
                                "gw-downward-stand-back-knee-left",
                                "gw-downward-stand-back-knee-right",
                                "gw-downward-stand-cradle-corner-strut-0",
                                "gw-downward-stand-cradle-corner-strut-3",
                            ):
                                self.assertIn(brace, elements)
                            self.assertEqual(
                                4,
                                sum(name.startswith("gw-downward-stand-cradle-corner-strut-") for name in elements),
                            )
                            cradle_front = elements["gw-downward-stand-cradle-front"]
                            self.assertEqual(COMPACT_STAND_CRADLE_BOTTOM, cradle_front["from"][1])
                            self.assertEqual(COMPACT_STAND_CRADLE_TOP, cradle_front["to"][1])
                            self.assertLessEqual(COMPACT_STAND_CRADLE_TOP, SINGLE_BORE_BOTTOM)
                            for side in ("left", "right"):
                                front_cradle = elements[f"gw-downward-stand-cradle-{side}-front"]
                                back_cradle = elements[f"gw-downward-stand-cradle-{side}-back"]
                                self.assertLessEqual(front_cradle["to"][2], -2.30)
                                self.assertGreaterEqual(back_cradle["from"][2], 2.30)
                            self.assertIn("gw-downward-stand-front-left-joint-plate", elements)
                            self.assertIn("gw-downward-stand-back-right-joint-plate", elements)
                            self.assertFalse(any("head" in name for name in elements if name.startswith("gw-downward-stand-")))
                            self.assertLessEqual(front_runner["to"][1], input_pipe["from"][1])
                            self.assertGreater(input_pipe["from"][2], elements["gw-downward-stand-left-front-leg"]["to"][2])
                        else:
                            self.assertFalse(any(name.startswith("gw-downward-stand-") for name in elements))
                    else:
                        for side in ("input", "output"):
                            pipe_bottom = elements[f"gw-pump-{side}-pipe-bottom"]
                            pipe_top = elements[f"gw-pump-{side}-pipe-top"]
                            self.assertAlmostEqual(SINGLE_PIPE_CENTER_Y, (pipe_bottom["from"][1] + pipe_top["to"][1]) / 2)
                            if stage == "pump-cutaway":
                                self.assertNotIn(f"gw-pump-{side}-pipe-front", elements)
                            else:
                                self.assertIn(f"gw-pump-{side}-pipe-front", elements)
                        input_pipe = elements["gw-pump-input-pipe-bottom"]
                        output_pipe = elements["gw-pump-output-pipe-bottom"]
                        self.assertFalse(any(name.startswith("gw-compact-frame-") for name in elements))
                    self.assertEqual(input_pipe["from"][1:], output_pipe["from"][1:])
                    self.assertEqual(input_pipe["to"][1:], output_pipe["to"][1:])
                    self.assertEqual(-input_pipe["to"][0], output_pipe["from"][0])
                    self.assertEqual(-input_pipe["from"][0], output_pipe["to"][0])
                    if candidate == "a9-single-acting-caged-poppets":
                        self.assertEqual(
                            input_pipe["to"][0],
                            elements["gw-single-cylinder-input-wall-beneath-pipe"]["from"][0],
                        )
                        self.assertEqual(
                            output_pipe["from"][0],
                            elements["gw-single-cylinder-output-wall-beneath-pipe"]["to"][0],
                        )
                    for part in ("bottom", "top", "front", "back"):
                        input_rim = elements[f"gw-pump-input-end-coupling-{part}"]
                        output_rim = elements[f"gw-pump-output-end-coupling-{part}"]
                        self.assertEqual({"#gw-copper"}, {face["texture"] for face in input_rim["faces"].values()})
                        self.assertEqual({"#gw-bronze"}, {face["texture"] for face in output_rim["faces"].values()})
                        self.assertEqual(input_rim["from"][1:], output_rim["from"][1:])
                        self.assertEqual(input_rim["to"][1:], output_rim["to"][1:])
                    self.assertFalse(any(name.startswith("gw-pump-output-identity-band-") for name in elements))

                    intake_origin = elements["gw-breather-intake-check-motion"]["rotationOrigin"]
                    exhaust_origin = elements["gw-breather-exhaust-check-motion"]["rotationOrigin"]
                    for actual, expected in zip(
                        intake_origin,
                        (AIR_ROOF_CENTER_X, AIR_ROOF_SEAT_Y - .04, AIR_ROOF_CENTER_Z),
                    ):
                        self.assertAlmostEqual(expected, actual)
                    for actual, expected in zip(
                        exhaust_origin,
                        (AIR_ROOF_CENTER_X, AIR_ROOF_SEAT_Y + .04, -AIR_ROOF_CENTER_Z),
                    ):
                        self.assertAlmostEqual(expected, actual)
                    self.assertGreater(intake_origin[0], 0)
                    self.assertLess(AIR_ROOF_CENTER_X + AIR_ROOF_COLLAR_HALF, body_half_x)
                    for obsolete in ("connector", "chamber-mouth", "valve-chest", "head-collar"):
                        self.assertFalse(any(obsolete in name for name in elements if name.startswith("gw-breather-")))

                    for mode, center_z, direction in (
                        ("intake", AIR_ROOF_CENTER_Z, -1),
                        ("exhaust", -AIR_ROOF_CENTER_Z, 1),
                    ):
                        collar_left = elements[f"gw-breather-{mode}-roof-collar-left"]
                        collar_right = elements[f"gw-breather-{mode}-roof-collar-right"]
                        collar_back = elements[f"gw-breather-{mode}-roof-collar-back"]
                        self.assertEqual(AIR_ROOF_CENTER_X - AIR_ROOF_COLLAR_HALF, collar_left["from"][0])
                        self.assertEqual(AIR_ROOF_CENTER_X - AIR_ROOF_BORE_HALF, collar_left["to"][0])
                        self.assertEqual(AIR_ROOF_CENTER_X + AIR_ROOF_BORE_HALF, collar_right["from"][0])
                        self.assertEqual(AIR_ROOF_CENTER_X + AIR_ROOF_COLLAR_HALF, collar_right["to"][0])
                        self.assertAlmostEqual(SINGLE_CYLINDER_TOP - .12, collar_left["from"][1])
                        self.assertAlmostEqual(SINGLE_HEAD_TOP + .20, collar_left["to"][1])
                        self.assertAlmostEqual(center_z - AIR_ROOF_COLLAR_HALF, collar_left["from"][2])
                        self.assertAlmostEqual(center_z + AIR_ROOF_COLLAR_HALF, collar_left["to"][2])
                        self.assertAlmostEqual(center_z + AIR_ROOF_BORE_HALF, collar_back["from"][2])
                        self.assertAlmostEqual(
                            AIR_ROOF_BORE_HALF,
                            AIR_ROOF_COLLAR_HALF - AIR_ROOF_COLLAR_WALL,
                        )
                        for flange_part in ("back", "left", "right"):
                            self.assertIn(f"gw-breather-{mode}-roof-flange-{flange_part}", elements)
                        if stage == "pump-cutaway" and center_z < 0:
                            self.assertNotIn(f"gw-breather-{mode}-roof-collar-front", elements)
                            self.assertNotIn(f"gw-breather-{mode}-roof-flange-front", elements)
                            self.assertNotIn(f"gw-breather-{mode}-seat-front", elements)
                            expected_rails = 2
                        else:
                            self.assertIn(f"gw-breather-{mode}-roof-collar-front", elements)
                            self.assertIn(f"gw-breather-{mode}-roof-flange-front", elements)
                            self.assertIn(f"gw-breather-{mode}-seat-front", elements)
                            expected_rails = 4
                        self.assertEqual(expected_rails, sum(name.startswith(f"gw-breather-{mode}-cage-rail-") for name in elements))
                        self.assertIn(f"gw-breather-{mode}-stop-spider-x", elements)
                        self.assertIn(f"gw-breather-{mode}-stop-spider-z", elements)
                        self.assertIn(f"gw-breather-{mode}-stop-bushing", elements)
                        self.assertIn(f"gw-breather-{mode}-guide-rod", elements)
                        self.assertIn(f"gw-breather-{mode}-guide-sleeve", elements)
                        stop = elements[f"gw-breather-{mode}-stop-spider-x"]
                        self.assertAlmostEqual(AIR_ROOF_SEAT_Y + direction * .55, (stop["from"][1] + stop["to"][1]) / 2)
                    self.assertEqual(["singleacting"], [animation["code"] for animation in pump["animations"]])
                    cycle = pump["animations"][0]
                    frames = {keyframe["frame"]: keyframe["elements"] for keyframe in cycle["keyframes"]}
                    self.assertEqual(set(range(0, 61, 5)), set(frames))
                    self.assertAlmostEqual(-2 * SINGLE_CRANK_THROW, frames[30]["gw-piston-motion"]["offsetY"])
                    self.assertEqual(-AIR_CHECK_TRAVEL, frames[15]["gw-breather-intake-check-motion"]["offsetY"])
                    self.assertEqual(0, frames[15]["gw-breather-exhaust-check-motion"]["offsetY"])
                    self.assertEqual(AIR_CHECK_TRAVEL, frames[45]["gw-breather-exhaust-check-motion"]["offsetY"])
                    self.assertEqual(0, frames[45]["gw-breather-intake-check-motion"]["offsetY"])
                    if wet_mode == "clapper":
                        self.assertEqual(SINGLE_CHECK_ANGLE, frames[15]["gw-wet-output-check-motion"]["rotationZ"])
                        self.assertEqual(0, frames[15]["gw-wet-intake-check-motion"]["rotationZ"])
                        self.assertEqual(SINGLE_CHECK_ANGLE, frames[45]["gw-wet-intake-check-motion"]["rotationZ"])
                        self.assertEqual(0, frames[45]["gw-wet-output-check-motion"]["rotationZ"])
                        wet_valve_y = elements["gw-wet-intake-check-motion"]["rotationOrigin"][1]
                        self.assertAlmostEqual(SINGLE_PIPE_CENTER_Y + 1.10, wet_valve_y)
                    elif wet_mode == "vertical-poppet":
                        self.assertEqual(-COMPACT_VERTICAL_CHECK_TRAVEL, frames[15]["gw-wet-output-check-motion"]["offsetY"])
                        self.assertEqual(0, frames[15]["gw-wet-intake-check-motion"]["offsetY"])
                        self.assertEqual(COMPACT_VERTICAL_CHECK_TRAVEL, frames[45]["gw-wet-intake-check-motion"]["offsetY"])
                        self.assertEqual(0, frames[45]["gw-wet-output-check-motion"]["offsetY"])
                        intake_valve_origin = elements["gw-wet-intake-check-motion"]["rotationOrigin"]
                        output_valve_origin = elements["gw-wet-output-check-motion"]["rotationOrigin"]
                        self.assertEqual(-BOTTOM_RESERVOIR_PORT_X, intake_valve_origin[0])
                        self.assertEqual(BOTTOM_RESERVOIR_PORT_X, output_valve_origin[0])
                        self.assertAlmostEqual(COMPACT_INTAKE_CHECK_CENTER_Y + .07, intake_valve_origin[1])
                        self.assertAlmostEqual(COMPACT_OUTPUT_CHECK_CENTER_Y - .07, output_valve_origin[1])
                    else:
                        self.assertEqual(SINGLE_CHECK_TRAVEL, frames[15]["gw-wet-output-check-motion"]["offsetX"])
                        self.assertEqual(0, frames[15]["gw-wet-intake-check-motion"]["offsetX"])
                        self.assertEqual(SINGLE_CHECK_TRAVEL, frames[45]["gw-wet-intake-check-motion"]["offsetX"])
                        self.assertEqual(0, frames[45]["gw-wet-output-check-motion"]["offsetX"])
                        wet_valve_y = elements["gw-wet-intake-check-motion"]["rotationOrigin"][1]
                        expected_wet_y = TALL_PIPE_CENTER_Y if candidate == "a10-two-block-bottom-pipes" else SINGLE_PIPE_CENTER_Y
                        self.assertAlmostEqual(expected_wet_y, wet_valve_y)

                    piston_origin_y = elements["gw-piston-motion"]["rotationOrigin"][1]
                    piston_top_at_upper_dead_center = piston_origin_y + elements["gw-piston-upper-seal"]["to"][1]
                    intake_stop_bottom = elements["gw-breather-intake-stop-spider-x"]["from"][1]
                    intake_disc = elements["gw-breather-intake-check-disc"]
                    intake_open_bottom = (
                        intake_origin[1]
                        + frames[15]["gw-breather-intake-check-motion"]["offsetY"]
                        + intake_disc["from"][1]
                    )
                    self.assertGreaterEqual(intake_stop_bottom - piston_top_at_upper_dead_center, .10)
                    self.assertGreaterEqual(intake_open_bottom - piston_top_at_upper_dead_center, .30)
                    crosshead_bottom_at_dead_center = (
                        piston_origin_y
                        + frames[30]["gw-piston-motion"]["offsetY"]
                        + elements["gw-crosshead-shoe-left"]["from"][1]
                    )
                    self.assertGreaterEqual(crosshead_bottom_at_dead_center - SINGLE_HEAD_TOP, .14)
                    for side in ("left", "right"):
                        guide = elements[f"gw-crosshead-guide-{side}"]
                        foot = elements[f"gw-crosshead-guide-{side}-foot"]
                        self.assertEqual(SINGLE_GUIDE_BOTTOM, guide["from"][1])
                        self.assertLessEqual(guide["from"][1], crosshead_bottom_at_dead_center)
                        self.assertEqual(SINGLE_HEAD_TOP, foot["from"][1])
                        self.assertEqual((-.66, .66), (foot["from"][2], foot["to"][2]))
                    self.assertGreater(
                        elements["gw-crosshead-guide-right-foot"]["from"][0],
                        AIR_ROOF_CENTER_X + AIR_ROOF_COLLAR_HALF,
                    )

                    gland_left = elements["gw-piston-rod-gland-liner-left"]
                    gland_right = elements["gw-piston-rod-gland-liner-right"]
                    piston_rod = elements["gw-piston-rod"]
                    self.assertAlmostEqual(.08, piston_rod["from"][0] - gland_left["to"][0])
                    self.assertAlmostEqual(.08, gland_right["from"][0] - piston_rod["to"][0])
                    bearing_bottom = elements["gw-connecting-rod-crosshead-bearing-bottom"]
                    bearing_bottom_at_dead_center = (
                        elements["gw-connecting-rod-motion"]["rotationOrigin"][1]
                        + frames[30]["gw-connecting-rod-motion"]["offsetY"]
                        + bearing_bottom["from"][1]
                    )
                    gland_plate_top = elements["gw-piston-rod-gland-plate-front"]["to"][1]
                    self.assertGreaterEqual(bearing_bottom_at_dead_center - gland_plate_top, .019)
                    self.assertLessEqual(abs(bearing_bottom["from"][0]), SINGLE_GLAND_POCKET_HALF_X)
                    self.assertLessEqual(abs(bearing_bottom["from"][2]), SINGLE_GLAND_POCKET_HALF_Z)

                    lower_seal = elements["gw-piston-lower-seal"]
                    lower_seal_at_dead_center = piston_origin_y + frames[30]["gw-piston-motion"]["offsetY"] + lower_seal["from"][1]
                    self.assertGreaterEqual(lower_seal_at_dead_center - SINGLE_BORE_BOTTOM, .54)
                    self.assertLessEqual(lower_seal_at_dead_center - SINGLE_BORE_BOTTOM, .60)
                    self.assertAlmostEqual(
                        .14,
                        lower_seal_at_dead_center - elements["gw-cylinder-back-glass"]["from"][1],
                        places=2,
                    )
                    self.assertAlmostEqual(8.0, lower_seal["to"][0] - lower_seal["from"][0])
                    self.assertLess(SINGLE_CYLINDER_TOP - SINGLE_CYLINDER_BOTTOM, 13.0)
                    self.assertEqual(-16.0, SINGLE_PIPE_CENTER_Y)
                    if candidate == "a11-one-block-full-frame":
                        self.assertEqual(-7.5, input_pipe["from"][0])
                        self.assertEqual(-.5, input_pipe["to"][0])
                        self.assertEqual(.5, output_pipe["from"][0])
                        self.assertEqual(7.5, output_pipe["to"][0])
                        self.assertEqual(COMPACT_LOWER_PIPE_CENTER_Y - 2.0, input_pipe["from"][1])
                        self.assertEqual(COMPACT_LOWER_PIPE_CENTER_Y - 1.5, input_pipe["to"][1])
                        input_pipe_bore = (-7.0, -1.0)
                        output_pipe_bore = (1.0, 7.0)
                        input_seat = elements["gw-wet-intake-check-seat-front"]
                        output_seat = elements["gw-wet-output-check-seat-front"]
                        self.assertNotIn("gw-wet-intake-check-seat-top", elements)
                        self.assertEqual((-4.0, -1.0), (input_seat["from"][0], input_seat["to"][0]))
                        self.assertEqual((1.0, 4.0), (output_seat["from"][0], output_seat["to"][0]))
                        self.assertEqual(
                            (COMPACT_INTAKE_CHECK_CENTER_Y - .11, COMPACT_INTAKE_CHECK_CENTER_Y + .11),
                            (input_seat["from"][1], input_seat["to"][1]),
                        )
                        self.assertEqual(
                            (COMPACT_OUTPUT_CHECK_CENTER_Y - .11, COMPACT_OUTPUT_CHECK_CENTER_Y + .11),
                            (output_seat["from"][1], output_seat["to"][1]),
                        )
                        intake_origin = elements["gw-wet-intake-check-motion"]["rotationOrigin"]
                        output_origin = elements["gw-wet-output-check-motion"]["rotationOrigin"]
                        disc = elements["gw-wet-intake-check-disc"]
                        input_stop = elements["gw-wet-intake-check-stop-spider-x"]
                        open_input_top = intake_origin[1] + COMPACT_VERTICAL_CHECK_TRAVEL + disc["to"][1]
                        open_output_bottom = output_origin[1] - COMPACT_VERTICAL_CHECK_TRAVEL + disc["from"][1]
                        self.assertGreaterEqual(lower_seal_at_dead_center - input_stop["to"][1], .35)
                        self.assertGreaterEqual(lower_seal_at_dead_center - open_input_top, .45)
                        self.assertGreater(open_output_bottom, COMPACT_RISER_BOTTOM)
                    else:
                        inner_pipe_x = 1.0 if candidate == "a10-two-block-bottom-pipes" else SINGLE_CYLINDER_HALF_X
                        expected_pipe_y = TALL_PIPE_CENTER_Y if candidate == "a10-two-block-bottom-pipes" else SINGLE_PIPE_CENTER_Y
                        self.assertEqual(-8.0, input_pipe["from"][0])
                        self.assertEqual(-inner_pipe_x, input_pipe["to"][0])
                        self.assertEqual(inner_pipe_x, output_pipe["from"][0])
                        self.assertEqual(8.0, output_pipe["to"][0])

                        input_pipe_bore = (-8.0, -inner_pipe_x)
                        output_pipe_bore = (inner_pipe_x, 8.0)
                        input_seat = elements["gw-wet-intake-check-seat-top"]
                        output_seat = elements["gw-wet-output-check-seat-top"]
                        self.assertEqual(expected_pipe_y + 1.10, input_seat["from"][1])
                        self.assertEqual(expected_pipe_y + 1.50, input_seat["to"][1])
                        self.assertEqual((-1.5, 1.5), (input_seat["from"][2], input_seat["to"][2]))
                        self.assertEqual(SINGLE_INTAKE_VALVE_SEAT_X, (input_seat["from"][0] + input_seat["to"][0]) / 2)
                        self.assertEqual(SINGLE_OUTPUT_VALVE_SEAT_X, (output_seat["from"][0] + output_seat["to"][0]) / 2)
                    if wet_mode == "clapper":
                        angle = math.radians(SINGLE_CHECK_ANGLE)
                        input_flap = elements["gw-wet-intake-check-flap"]
                        open_input_max_x = (
                            SINGLE_INTAKE_VALVE_SEAT_X
                            + input_flap["to"][0] * math.cos(angle)
                            - input_flap["from"][1] * math.sin(angle)
                        )
                        open_output_max_x = (
                            SINGLE_OUTPUT_VALVE_SEAT_X
                            + input_flap["to"][0] * math.cos(angle)
                            - input_flap["from"][1] * math.sin(angle)
                        )
                        self.assertGreater(open_input_max_x, input_pipe_bore[0])
                        self.assertLess(open_input_max_x, input_pipe_bore[1])
                        self.assertGreater(open_output_max_x, output_pipe_bore[0])
                        self.assertLess(open_output_max_x, output_pipe_bore[1])
                    elif wet_mode == "vertical-poppet":
                        for brace in ("spider-x", "spider-z", "bushing"):
                            self.assertNotIn(f"gw-wet-output-check-seat-{brace}", elements)
                            self.assertIn(f"gw-wet-output-check-stop-{brace}", elements)
                        self.assertEqual(4, sum(name.startswith("gw-wet-intake-check-cage-rail-") for name in elements))
                        self.assertEqual(4, sum(name.startswith("gw-wet-output-check-cage-rail-") for name in elements))
                        witness = elements["gw-wet-output-check-glass-witness"]
                        self.assertGreaterEqual(witness["from"][2], -1.5)
                        self.assertLessEqual(witness["to"][2], 1.5)
                        self.assertLess(
                            elements["gw-wet-output-check-guide-rod"]["from"][1],
                            elements["gw-wet-output-check-motion"]["rotationOrigin"][1],
                        )
                    else:
                        input_disc_origin = elements["gw-wet-intake-check-motion"]["rotationOrigin"][0]
                        output_disc_origin = elements["gw-wet-output-check-motion"]["rotationOrigin"][0]
                        disc = elements["gw-wet-intake-check-disc"]
                        self.assertGreater(input_disc_origin + disc["from"][0], input_pipe_bore[0])
                        self.assertLess(input_disc_origin + SINGLE_CHECK_TRAVEL + disc["to"][0], input_pipe_bore[1])
                        self.assertGreater(output_disc_origin + disc["from"][0], output_pipe_bore[0])
                        self.assertLess(output_disc_origin + SINGLE_CHECK_TRAVEL + disc["to"][0], output_pipe_bore[1])
                        for brace in ("spider-y", "spider-z", "bushing"):
                            self.assertNotIn(f"gw-wet-output-check-seat-{brace}", elements)
                            self.assertIn(f"gw-wet-output-check-stop-{brace}", elements)
                        witness = elements["gw-wet-output-check-glass-witness"]
                        self.assertGreaterEqual(witness["from"][2], -1.5)
                        self.assertLessEqual(witness["to"][2], 1.5)
                        self.assertLessEqual(elements["gw-wet-output-check-guide-rod"]["to"][0], output_pipe_bore[1])

                    _, _, compact_top, _, _, _ = _slider_state(
                        0,
                        radius=SINGLE_CRANK_THROW,
                        rod_length=SINGLE_ROD_LENGTH,
                    )
                    _, _, compact_bottom, _, _, _ = _slider_state(
                        180,
                        radius=SINGLE_CRANK_THROW,
                        rod_length=SINGLE_ROD_LENGTH,
                    )
                    self.assertAlmostEqual(2 * SINGLE_CRANK_THROW, compact_top - compact_bottom)

            for candidate, pipe_center_y, pipe_block_center_x in (
                ("a7-rear-pinion-external-openers", PIPE_CENTER_Y, 19.5),
                ("a9-single-acting-caged-poppets", SINGLE_PIPE_CENTER_Y, 16.0),
                ("a10-two-block-bottom-pipes", TALL_PIPE_CENTER_Y, 16.0),
                ("a11-one-block-full-frame", SINGLE_PIPE_CENTER_Y, 16.0),
            ):
                pipe_fit = _document(rebuilt / candidate / "pipe-fit.shape.json")
                elements = {element["name"]: element for element in _walk(pipe_fit["elements"])}
                if candidate == "a11-one-block-full-frame":
                    self.assertIn("gw-downward-stand-base-front-runner", elements)
                else:
                    self.assertFalse(any(name.startswith("gw-downward-stand-") for name in elements))
                input_inner_arm = elements["gw-fit-input-pipe-block-right-arm-bottom"]
                output_inner_arm = elements["gw-fit-output-pipe-block-left-arm-bottom"]
                input_outer_arm = elements["gw-fit-input-pipe-block-left-arm-bottom"]
                output_outer_arm = elements["gw-fit-output-pipe-block-right-arm-bottom"]
                self.assertEqual(
                    (-pipe_block_center_x - 8, -pipe_block_center_x - 2),
                    (input_outer_arm["from"][0], input_outer_arm["to"][0]),
                )
                self.assertEqual(
                    (-pipe_block_center_x + 2, -pipe_block_center_x + 8),
                    (input_inner_arm["from"][0], input_inner_arm["to"][0]),
                )
                self.assertEqual(
                    (pipe_block_center_x - 8, pipe_block_center_x - 2),
                    (output_inner_arm["from"][0], output_inner_arm["to"][0]),
                )
                self.assertEqual(
                    (pipe_block_center_x + 2, pipe_block_center_x + 8),
                    (output_outer_arm["from"][0], output_outer_arm["to"][0]),
                )
                self.assertEqual(-input_inner_arm["from"][0], output_inner_arm["to"][0])
                self.assertEqual(-input_inner_arm["to"][0], output_inner_arm["from"][0])
                self.assertEqual(pipe_center_y - 2.0, input_inner_arm["from"][1])
                self.assertEqual(pipe_center_y - 1.5, input_inner_arm["to"][1])
                self.assertEqual((-2.0, 2.0), (input_inner_arm["from"][2], input_inner_arm["to"][2]))

                input_fit_coupling = elements["gw-fit-input-pipe-block-right-half-coupling-bottom"]
                output_fit_coupling = elements["gw-fit-output-pipe-block-left-half-coupling-bottom"]
                pump_input_coupling = elements["gw-pump-input-end-coupling-bottom"]
                pump_output_coupling = elements["gw-pump-output-end-coupling-bottom"]
                connection_face = pipe_block_center_x - 8
                self.assertEqual(-connection_face, input_fit_coupling["to"][0])
                self.assertEqual(-connection_face, pump_input_coupling["from"][0])
                self.assertEqual(connection_face, pump_output_coupling["to"][0])
                self.assertEqual(connection_face, output_fit_coupling["from"][0])
                self.assertEqual(pipe_center_y - 2.5, input_fit_coupling["from"][1])
                self.assertEqual(pipe_center_y - 2.0, input_fit_coupling["to"][1])
                self.assertEqual((-2.5, 2.5), (input_fit_coupling["from"][2], input_fit_coupling["to"][2]))

if __name__ == "__main__":
    unittest.main()
