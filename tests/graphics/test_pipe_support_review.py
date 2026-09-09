from __future__ import annotations

import json
import itertools
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tools" / "graphics"))

from graphics.review.pipe_wood_supports import DESCRIPTIONS, STATES, build_review, candidate_shape, _pipe
from graphics.models.pipe_wood_support import compose_support, new_shape, part, FACES
from gearwright_graphics.compiler import compile_shape
from gearwright_graphics.geometry import triangles


def cuboids(shape):
    """Collect transformed vertices and face normals from the rendered shape."""
    parts = {}
    for triangle in triangles(compile_shape(shape)):
        entry = parts.setdefault(triangle.name, {"group": triangle.group, "points": [], "axes": []})
        entry["points"].extend(triangle.vertices)
        if not any(abs(np.dot(triangle.normal, axis)) > .99999 for axis in entry["axes"]):
            entry["axes"].append(triangle.normal)
    for part in parts.values():
        part["points"] = np.asarray(part["points"])
    return parts


def overlaps(a, b):
    """Separating-axis test; a touching joint is allowed, penetration is not."""
    pa, pb = a["points"], b["points"]
    if np.any(np.minimum(pa.max(axis=0), pb.max(axis=0)) - np.maximum(pa.min(axis=0), pb.min(axis=0)) <= 1e-7):
        return False
    axes = [*a["axes"], *b["axes"], *(np.cross(x, y) for x in a["axes"] for y in b["axes"])]
    for axis in axes:
        length = np.linalg.norm(axis)
        if length < 1e-7:
            continue
        aa, bb = pa @ (axis / length), pb @ (axis / length)
        if min(aa.max(), bb.max()) - max(aa.min(), bb.min()) <= 1e-7:
            return False
    return True


class PipeSupportReviewTests(unittest.TestCase):
    def test_review_keeps_only_chosen_c_and_never_emits_runtime_assets(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            managed = build_review(root)
            (managed / "stale.txt").write_text("old", encoding="utf-8")
            self.assertEqual(managed, build_review(root))
            self.assertFalse((managed / "stale.txt").exists())
            self.assertFalse((root / "assets").exists())
            ownership = json.loads((managed / ".gearwright-review.json").read_text())
            self.assertEqual(["c-braced-trestle"], ownership["candidates"])
            self.assertEqual("approved", ownership["decision"]["status"])
            self.assertEqual(set(STATES), {p.name.removesuffix(".shape.json") for p in (managed / "c-braced-trestle").glob("*.shape.json")})

    def assert_no_pipe_clipping(self, shape):
        parts = cuboids(shape)
        pipes = {name: p for name, p in parts.items() if p["group"] == "pipe"}
        wood = {name: p for name, p in parts.items() if p["group"] not in {"pipe", "context"}}
        collisions = [(a, b) for a, part in wood.items() for b, pipe in pipes.items() if overlaps(part, pipe)]
        self.assertEqual([], collisions)

    def test_mounting_examples_do_not_clip(self):
        for state in STATES:
            with self.subTest(state=state):
                self.assert_no_pipe_clipping(candidate_shape("c-braced-trestle", state))

    def test_all_64_port_layouts_use_endpoint_collars_and_clear_the_pipe(self):
        for bits in itertools.product((False, True), repeat=6):
            faces = tuple(face for face, enabled in zip(FACES, bits) if enabled)
            with self.subTest(faces=faces):
                shape = new_shape("port-test")
                _pipe(shape, "pipe-", connections=faces)
                compose_support(shape, connections=faces)
                parts = cuboids(shape)
                collars = [p for p in parts.values() if p["group"] == "endpoint-collar"]
                braces = [p for p in parts.values() if p["group"] == "diagonal-brace"]
                self.assertEqual(4 * len(faces), len(collars))
                self.assertEqual(5 - len(set(faces) - {"up"}), len(braces))
                for name, piece in parts.items():
                    if piece["group"] != "pipe":
                        self.assertGreaterEqual(piece["points"].min(), -1e-7, name)
                        self.assertLessEqual(piece["points"].max(), 1 + 1e-7, name)
                self.assert_no_pipe_clipping(shape)

    def test_insulation_panels_clear_every_pipe_and_fitting_layout(self):
        for bits in itertools.product((False, True), repeat=6):
            faces = tuple(face for face, enabled in zip(FACES, bits) if enabled)
            with self.subTest(faces=faces):
                shape = new_shape("insulation-port-test")
                _pipe(shape, "pipe-", connections=faces)
                compose_support(shape, connections=faces, insulated=True)
                self.assert_no_pipe_clipping(shape)
        for face in FACES:
            with self.subTest(nozzle=face):
                shape = new_shape("insulation-nozzle-test")
                _pipe(shape, "pipe-", connections=(), nozzles=(face,))
                compose_support(shape, nozzles=(face,), insulated=True)
                self.assert_no_pipe_clipping(shape)
        shape = new_shape("insulation-sprinkler-test")
        _pipe(shape, "pipe-", connections=(), sprinkler=True)
        compose_support(shape, sprinkler=True, insulated=True)
        self.assert_no_pipe_clipping(shape)

    def test_unconnected_face_has_one_corner_to_corner_brace(self):
        pieces = cuboids(part("corners"))
        self.assertEqual(1, len(pieces))
        points = next(iter(pieces.values()))["points"] * 16
        self.assertLess(points[:, 0].min(), 1.5)
        self.assertLess(points[:, 1].min(), 1.5)
        self.assertGreater(points[:, 0].max(), 14.5)
        self.assertGreater(points[:, 1].max(), 14.5)

    def test_frame_meets_a_complete_matching_rim_at_every_block_boundary(self):
        pieces = cuboids(part("frame"))
        for axis in range(3):
            rims = []
            for boundary in (0, 1):
                rectangles = []
                for piece in pieces.values():
                    points = piece["points"]
                    at_boundary = np.unique(points[np.abs(points[:, axis] - boundary) < 1e-7], axis=0)
                    if len(at_boundary) != 4:
                        continue
                    plane = np.delete(at_boundary, axis, axis=1)
                    rectangles.append(tuple([*plane.min(axis=0), *plane.max(axis=0)]))
                rims.append(sorted(rectangles))
                area = sum((x2 - x1) * (y2 - y1) for x1, y1, x2, y2 in rectangles)
                self.assertAlmostEqual((16 * 16 - 13 * 13) / 256, area)
            self.assertEqual(rims[0], rims[1], f"Opposite rims differ on axis {axis}")

    def test_top_infill_meets_the_frame_without_overlapping_surfaces(self):
        frame = cuboids(part("frame"))
        for top in ("top", "top-opening"):
            for plank in cuboids(part(top)).values():
                self.assertTrue(all(not overlaps(plank, rail) for rail in frame.values()), top)

    def test_nozzles_clear_the_wider_endpoint_on_all_six_faces(self):
        for face in FACES:
            with self.subTest(face=face):
                shape = new_shape("nozzle-test")
                _pipe(shape, "pipe-", connections=(), nozzles=(face,))
                compose_support(shape, nozzles=(face,))
                self.assert_no_pipe_clipping(shape)

    def test_neighbors_have_matching_collar_faces_on_all_axes(self):
        for state, coordinate, boundary in (("linked-run", 0, 0), ("linked-north-south", 2, 0), ("vertical", 1, 1)):
            parts = cuboids(candidate_shape("c-braced-trestle", state))
            faces = []
            for part in parts.values():
                if part["group"] != "endpoint-collar":
                    continue
                points = part["points"]
                at_boundary = points[np.abs(points[:, coordinate] - boundary) < 1e-7]
                if len(at_boundary):
                    faces.append(tuple(map(tuple, np.unique(np.round(at_boundary, 8), axis=0))))
            self.assertEqual(8, len(faces), state)
            self.assertTrue(all(faces.count(face) == 2 for face in faces), state)

    def test_terminal_upward_pipe_removes_only_the_middle_slat(self):
        for state in ("top-terminal", "barrel-connection"):
            parts = cuboids(candidate_shape("c-braced-trestle", state))
            decks = [p for p in parts.values() if p["group"] == "top-platform"]
            self.assertEqual(2, len(decks))
            for deck in decks:
                points = deck["points"] * 16
                self.assertAlmostEqual(16, points[:, 1].max())
                self.assertTrue(points[:, 2].max() <= 3.5 or points[:, 2].min() >= 12.5)
        parts = cuboids(candidate_shape("c-braced-trestle", "top-continuation"))
        decks = [p for p in parts.values() if p["group"] == "top-platform"]
        self.assertEqual(3, len(decks))
        self.assertTrue(all(p["points"][:, 1].min() >= 30.5 / 16 for p in decks))


if __name__ == "__main__":
    unittest.main()
