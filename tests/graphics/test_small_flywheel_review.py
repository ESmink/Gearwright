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
from graphics.review.small_flywheel import DESCRIPTIONS, build_review  # noqa: E402


def _elements(document):
    def visit(elements, parent_origin=(0, 0, 0)):
        for element in elements:
            world = dict(element)
            for key in ("from", "to", "rotationOrigin"):
                if key in world:
                    world[key] = [
                        value + parent_origin[index]
                        for index, value in enumerate(world[key])
                    ]
            yield world
            yield from visit(element.get("children", ()), tuple(world["from"]))

    yield from visit(document["elements"])


def _plan_corners(element):
    origin_x, origin_y = element["rotationOrigin"][:2]
    angle = math.radians(element.get("rotationZ", 0))
    cosine, sine = math.cos(angle), math.sin(angle)
    result = []
    for x in (element["from"][0], element["to"][0]):
        for y in (element["from"][1], element["to"][1]):
            dx, dy = x - origin_x, y - origin_y
            result.append((origin_x + dx * cosine - dy * sine, origin_y + dx * sine + dy * cosine))
    return result


def _separation(first, second):
    axes = []
    for rectangle in (first, second):
        axes.extend((
            (rectangle[1][0] - rectangle[0][0], rectangle[1][1] - rectangle[0][1]),
            (rectangle[2][0] - rectangle[0][0], rectangle[2][1] - rectangle[0][1]),
        ))
    separations = []
    for axis_x, axis_y in axes:
        length = math.hypot(axis_x, axis_y)
        axis_x, axis_y = axis_x / length, axis_y / length
        first_projection = [x * axis_x + y * axis_y for x, y in first]
        second_projection = [x * axis_x + y * axis_y for x, y in second]
        separations.append(max(
            min(second_projection) - max(first_projection),
            min(first_projection) - max(second_projection),
        ))
    return max(separations)


class SmallFlywheelReviewTests(unittest.TestCase):
    def test_candidates_are_managed_and_fit_the_declared_three_by_three_by_one_footprint(self):
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
            self.assertEqual({"width": 3, "height": 3, "depth": 1}, ownership["footprintBlocks"])
            self.assertEqual(list(DESCRIPTIONS), ownership["candidates"])
            self.assertEqual("open", ownership["decision"]["status"])
            self.assertTrue(ownership["decision"]["runtimePromotion"])

            for candidate in DESCRIPTIONS:
                document = json.loads((rebuilt / candidate / "rest.shape.json").read_text(encoding="utf-8"))
                elements = list(_elements(document))
                names = [element["name"] for element in elements]
                self.assertEqual(["spin"], [animation["code"] for animation in document["animations"]])
                animation = document["animations"][0]
                self.assertEqual(
                    [(0, 0), (35, 360)],
                    [(keyframe["frame"], keyframe["elements"]["wheel"]["rotationZ"]) for keyframe in animation["keyframes"]],
                )
                self.assertEqual(
                    {"wheel"},
                    {target for keyframe in animation["keyframes"] for target in keyframe["elements"]},
                )
                wheel = next(element for element in document["elements"] if element["name"] == "wheel")
                rotating_names = {element["name"] for element in _elements({"elements": wheel.get("children", [])})}
                self.assertIn("wooden-axle-horizontal", rotating_names)
                self.assertIn("spoke-00", rotating_names)
                self.assertIn("stone-rim-block-00", rotating_names)
                self.assertIn("stone-clamp-00-0-front", rotating_names)
                self.assertIn("stone-clamp-bolt-00-0-0-front", rotating_names)
                self.assertIn("arm-socket-00-0-left", rotating_names)
                self.assertIn("stone-through-shank-00-0-0", rotating_names)
                self.assertEqual(8, sum(name.startswith("stone-rim-block-") for name in names))
                self.assertFalse(any(name.startswith("outer-plate-") for name in names))
                self.assertFalse(any(name.startswith("inner-anchor-plate-") for name in names))
                self.assertFalse(any(name.startswith("low-cross-tie-") for name in names))
                self.assertFalse(any(name.startswith("bearing-hinge-") for name in names))
                self.assertEqual(8, sum(name.startswith("spoke-") for name in names))
                self.assertIn("wooden-axle-horizontal", names)
                self.assertIn("wooden-axle-vertical", names)
                self.assertIn("left-depth-log", names)
                self.assertIn("right-depth-log", names)
                self.assertEqual(2, sum(name.startswith("base-upper-log-") for name in names))
                self.assertEqual(4, sum(name.startswith("base-laminate-bolt-") for name in names))

                stones = [element for element in elements if element["name"].startswith("stone-rim-block-")]
                stones.sort(key=lambda element: element["name"])
                for stone in stones:
                    self.assertGreaterEqual(stone["to"][0] - stone["from"][0], 10.4 - 1e-6)
                    self.assertGreaterEqual(stone["from"][2], 4.3 - 1e-6)
                    self.assertLessEqual(stone["to"][2], 11.7 + 1e-6)
                for index, stone in enumerate(stones):
                    neighbor = stones[(index + 1) % len(stones)]
                    stone_gap = _separation(_plan_corners(stone), _plan_corners(neighbor))
                    self.assertGreater(stone_gap, .05)
                    self.assertLess(stone_gap, .1)

                bottom_log = next(element for element in elements if element["name"] == "base-bottom-log-back")
                self.assertEqual("#strippedoak", bottom_log["faces"]["north"]["texture"])
                self.assertEqual("#oakend", bottom_log["faces"]["west"]["texture"])
                upper_log = next(element for element in elements if element["name"] == "base-upper-log-back")
                stack_height = upper_log["to"][1] - bottom_log["from"][1]
                for log in (bottom_log, upper_log):
                    self.assertAlmostEqual(stack_height, log["to"][2] - log["from"][2])
                self.assertGreater(
                    min(y for stone in stones for _, y in _plan_corners(stone)),
                    upper_log["to"][1],
                )

                depth_logs = [next(element for element in elements if element["name"] == name) for name in ("left-depth-log", "right-depth-log")]
                for log in depth_logs:
                    self.assertAlmostEqual(log["to"][0] - log["from"][0], log["to"][1] - log["from"][1])
                depth_centers = [(log["from"][0] + log["to"][0]) / 2 for log in depth_logs]
                depth_heights = [(log["from"][1] + log["to"][1]) / 2 for log in depth_logs]
                base_bolts = [
                    element for element in elements
                    if element["name"].startswith("base-laminate-bolt-") and element["name"].endswith("-back")
                ]
                self.assertEqual(depth_centers, sorted(bolt["rotationOrigin"][0] for bolt in base_bolts))
                self.assertEqual(depth_heights, sorted(bolt["rotationOrigin"][1] for bolt in base_bolts))

                bolts = [element for element in elements if element["name"].startswith("stone-clamp-bolt-")]
                self.assertGreaterEqual(len(bolts), 16)
                wheel_angle_offset = 22.5 if candidate == "model-e3-iron-four-way-hub" else 0
                for bolt in bolts:
                    stone_index = int(bolt["name"].split("-")[3])
                    self.assertAlmostEqual(wheel_angle_offset + stone_index * 45 + 45, bolt["rotationZ"])

                stone = next(element for element in stones if element["name"] == "stone-rim-block-00")
                spoke = next(element for element in elements if element["name"] == "spoke-00")
                clamp = next(element for element in elements if element["name"] == "stone-clamp-00-0-back")
                joint_bolt = next(element for element in elements if element["name"] == "stone-clamp-bolt-00-0-0-back")
                self.assertGreater(stone["to"][1] - stone["from"][1], clamp["to"][0] - clamp["from"][0])
                joint_x = joint_bolt["rotationOrigin"][0]
                if candidate == "model-e3-iron-four-way-hub":
                    self.assertLessEqual(_separation(_plan_corners(spoke), _plan_corners(clamp)), 0)
                    self.assertLessEqual(_separation(_plan_corners(stone), _plan_corners(clamp)), 0)
                else:
                    self.assertGreater(spoke["to"][0], clamp["from"][0])
                    self.assertLessEqual(clamp["from"][0], joint_x)
                    self.assertGreaterEqual(clamp["to"][0], joint_x)
                    self.assertLessEqual(spoke["from"][0], joint_x)
                    self.assertGreaterEqual(spoke["to"][0], joint_x)
                    stone_x = [point[0] for point in _plan_corners(stone)]
                    self.assertLessEqual(min(stone_x), joint_x)
                    self.assertGreaterEqual(max(stone_x), joint_x)

                left_socket = next(element for element in elements if element["name"] == "arm-socket-00-0-left")
                right_socket = next(element for element in elements if element["name"] == "arm-socket-00-0-right")
                self.assertAlmostEqual(spoke["from"][1], left_socket["to"][1])
                self.assertAlmostEqual(spoke["to"][1], right_socket["from"][1])
                self.assertGreater(left_socket["to"][0], clamp["from"][0])
                self.assertLess(left_socket["from"][0], spoke["to"][0])
                self.assertGreater(left_socket["to"][2], clamp["from"][2])
                self.assertLess(left_socket["from"][2], clamp["to"][2])
                shank = next(element for element in elements if element["name"] == "stone-through-shank-00-0-0")
                back_head = next(element for element in elements if element["name"] == "stone-clamp-bolt-00-0-0-back")
                front_head = next(element for element in elements if element["name"] == "stone-clamp-bolt-00-0-0-front")
                self.assertGreater(back_head["to"][2], shank["from"][2])
                self.assertLess(front_head["from"][2], shank["to"][2])

                if candidate == "model-e2-timber-wheel":
                    self.assertIn("stripped-oak-hub", names)
                    self.assertNotIn("bronze-hub-body", names)
                    timber_spoke = next(element for element in elements if element["name"] == "spoke-00")
                    self.assertEqual("#strippedoak", timber_spoke["faces"]["north"]["texture"])
                    self.assertAlmostEqual(
                        timber_spoke["to"][1] - timber_spoke["from"][1],
                        timber_spoke["to"][2] - timber_spoke["from"][2],
                    )
                elif candidate == "model-e3-iron-four-way-hub":
                    self.assertNotIn("stripped-oak-four-way-hub", names)
                    self.assertNotIn("bronze-hub-body", names)
                    self.assertEqual(4, sum(name.startswith("four-way-arm-") for name in names))
                    self.assertEqual(8, sum(name.startswith("four-way-yoke-") for name in names))
                    self.assertFalse(any(name.startswith("iron-hub-cross-") for name in names))
                    self.assertEqual(24, sum(name.startswith("iron-hub-socket-") for name in names))
                    self.assertEqual(8, sum(name.startswith("iron-hub-gusset-") for name in names))
                    self.assertEqual(8, sum(name.startswith("iron-hub-bolt-") for name in names))
                    self.assertEqual(32, sum(name.startswith("iron-fork-y-") for name in names))
                    self.assertEqual(48, sum(name.startswith("iron-bend-brace-") for name in names))
                    self.assertFalse(any(name.startswith("iron-stone-hub-") for name in names))
                    main_arm = next(element for element in elements if element["name"] == "four-way-arm-00")
                    self.assertLess(main_arm["to"][1] - main_arm["from"][1], 2)
                    self.assertAlmostEqual(
                        main_arm["to"][1] - main_arm["from"][1],
                        main_arm["to"][2] - main_arm["from"][2],
                    )
                    iron_spoke = next(element for element in elements if element["name"] == "spoke-00")
                    self.assertEqual("#strippedoak", iron_spoke["faces"]["north"]["texture"])
                    self.assertAlmostEqual(
                        iron_spoke["to"][1] - iron_spoke["from"][1],
                        iron_spoke["to"][2] - iron_spoke["from"][2],
                    )
                    branch = next(element for element in elements if element["name"] == "four-way-yoke-00-1")
                    branch_brace = next(
                        element for element in elements if element["name"] == "iron-fork-y-00-branch-1-front"
                    )
                    bend_incoming = next(
                        element for element in elements if element["name"] == "iron-bend-brace-00-incoming-front"
                    )
                    bend_outgoing = next(
                        element for element in elements if element["name"] == "iron-bend-brace-00-outgoing-front"
                    )
                    self.assertAlmostEqual(branch["rotationZ"], branch_brace["rotationZ"])
                    self.assertAlmostEqual(branch["rotationZ"], bend_incoming["rotationZ"])
                    self.assertAlmostEqual(iron_spoke["rotationZ"], bend_outgoing["rotationZ"])
                    iron_clamp = next(element for element in elements if element["name"] == "stone-clamp-00-0-front")
                    self.assertEqual("#iron", iron_clamp["faces"]["north"]["texture"])
                    iron_bearing = next(element for element in elements if element["name"] == "bearing-saddle-top-front")
                    self.assertEqual("#iron", iron_bearing["faces"]["north"]["texture"])
                elif candidate == "model-e4-iron-eight-way-frame":
                    self.assertNotIn("bronze-hub-body", names)
                    self.assertFalse(any(name.startswith("four-way-yoke-") for name in names))
                    self.assertFalse(any(name.startswith("iron-bend-brace-") for name in names))
                    self.assertFalse(any(name.startswith("iron-stone-hub-") for name in names))
                    self.assertEqual(8, sum(name.startswith("eight-way-hub-ring-segment-") for name in names))
                    self.assertEqual(8, sum(name.startswith("eight-way-hub-ring-corner-") for name in names))
                    self.assertEqual(48, sum(name.startswith("eight-way-hub-socket-") for name in names))
                    self.assertEqual(32, sum(name.startswith("eight-way-hub-bolt-") for name in names))
                    self.assertEqual(
                        16,
                        sum(
                            name.startswith("eight-way-arm-strip-") and not name.startswith("eight-way-arm-strip-bolt-")
                            for name in names
                        ),
                    )
                    self.assertEqual(16, sum(name.startswith("eight-way-arm-strip-bolt-") for name in names))
                    timber_spoke = next(element for element in elements if element["name"] == "spoke-00")
                    self.assertEqual("#strippedoak", timber_spoke["faces"]["north"]["texture"])
                    self.assertAlmostEqual(
                        timber_spoke["to"][1] - timber_spoke["from"][1],
                        timber_spoke["to"][2] - timber_spoke["from"][2],
                    )
                    iron_ring = next(
                        element for element in elements if element["name"] == "eight-way-hub-ring-segment-00"
                    )
                    self.assertEqual("#iron", iron_ring["faces"]["north"]["texture"])
                else:
                    self.assertIn("bronze-hub-body", names)

                bearing = next(element for element in elements if element["name"] == "bearing-block-back")
                bearing_top = next(element for element in elements if element["name"] == "bearing-saddle-top-back")
                self.assertAlmostEqual(bearing["to"][1], bearing_top["to"][1])
                if candidate in {
                    "model-e2-timber-wheel",
                    "model-e3-iron-four-way-hub",
                    "model-e4-iron-eight-way-frame",
                }:
                    self.assertAlmostEqual(bearing["from"][0], bearing_top["from"][0])
                    self.assertAlmostEqual(bearing["to"][0], bearing_top["to"][0])
                else:
                    bearing_left = next(element for element in elements if element["name"] == "bearing-saddle-left-back")
                    bearing_right = next(element for element in elements if element["name"] == "bearing-saddle-right-back")
                    self.assertAlmostEqual(bearing["from"][0], bearing_left["from"][0])
                    self.assertAlmostEqual(bearing["to"][0], bearing_right["to"][0])

                for support_name in ("left-cradle-back", "right-cradle-back"):
                    support = next(element for element in elements if element["name"] == support_name)
                    for x, y in _plan_corners(support)[2:]:
                        self.assertGreaterEqual(x, bearing["from"][0] - 1e-6)
                        self.assertLessEqual(x, bearing["to"][0] + 1e-6)
                        self.assertGreaterEqual(y, bearing["from"][1] - 1e-6)
                        self.assertLessEqual(y, bearing["to"][1] + 1e-6)

                back_support = next(element for element in elements if element["name"] == "left-cradle-back")
                front_support = next(element for element in elements if element["name"] == "left-cradle-front")
                back_hardware = min(
                    element["from"][2] for element in elements
                    if element["name"].startswith("stone-clamp-bolt-") and element["name"].endswith("-back")
                )
                front_hardware = max(
                    element["to"][2] for element in elements
                    if element["name"].startswith("stone-clamp-bolt-") and element["name"].endswith("-front")
                )
                self.assertGreater(back_hardware, back_support["to"][2])
                self.assertLess(front_hardware, front_support["from"][2])

                minimum, maximum = bounds(triangles(document))
                model_minimum = minimum * 16
                model_maximum = maximum * 16
                for actual in model_minimum:
                    self.assertGreaterEqual(float(actual), -1e-6)
                for actual, limit in zip(model_maximum, (48, 48, 16)):
                    self.assertLessEqual(float(actual), limit + 1e-6)
                self.assertAlmostEqual(0, float(model_minimum[0]))
                self.assertAlmostEqual(0, float(model_minimum[1]))
                self.assertAlmostEqual(0, float(model_minimum[2]))
                self.assertAlmostEqual(48, float(model_maximum[0]))
                self.assertAlmostEqual(16, float(model_maximum[2]))

    def test_approved_package_contains_only_the_selected_candidate(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            candidate = "model-e1-laminated-saddle"
            managed = build_review(root, approved_candidate=candidate)
            ownership = json.loads((managed / ".gearwright-review.json").read_text(encoding="utf-8"))

            self.assertEqual([candidate], ownership["candidates"])
            self.assertEqual("approved", ownership["decision"]["status"])
            self.assertEqual(candidate, ownership["decision"]["candidate"])
            self.assertTrue((managed / candidate / "rest.shape.json").is_file())
            self.assertFalse((managed / "model-e2-timber-wheel").exists())
            self.assertFalse((managed / "model-e3-iron-four-way-hub").exists())
            self.assertFalse((managed / "model-e4-iron-eight-way-frame").exists())


if __name__ == "__main__":
    unittest.main()
