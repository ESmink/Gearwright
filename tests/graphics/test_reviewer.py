from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "graphics"))

from gearwright_graphics.reviewer import ReviewCatalog  # noqa: E402


class ReviewerCatalogTests(unittest.TestCase):
    def test_desktop_reviewer_uses_one_persistent_gpu_viewport(self):
        live_view = (ROOT / "tools" / "graphics" / "gearwright_graphics" / "live_view.py").read_text(encoding="utf-8")
        review_model = (ROOT / "tools" / "graphics" / "gearwright_graphics" / "review_model.py").read_text(encoding="utf-8")
        catalog = (ROOT / "tools" / "graphics" / "gearwright_graphics" / "reviewer.py").read_text(encoding="utf-8")

        self.assertIn("QOpenGLWidget", live_view)
        self.assertIn("LiveModelViewport", review_model)
        self.assertNotIn("subprocess", review_model)
        self.assertNotIn("RenderService", catalog)
        self.assertNotIn(".model-reviewer", catalog)

    def test_default_review_root_selects_latest_revision_and_rest_state(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            review = root / "generated" / "review"
            for revision in (1, 4):
                option = review / f"revision-{revision}" / "option-a"
                option.mkdir(parents=True)
                (option / "rest.shape.json").write_text(json.dumps({"elements": []}), encoding="utf-8")
                (option / "drawn.shape.json").write_text(json.dumps({"elements": []}), encoding="utf-8")
                (option / "fixed-render-rest").mkdir()
                (option / "fixed-render-rest" / "manifest.json").write_text(json.dumps({"camera": [{"position": [1, 1, -1], "target": [0, 0, 0], "orthographicScale": 4}]}), encoding="utf-8")
            catalog = ReviewCatalog(root, Path("generated/review"))
            self.assertEqual((review / "revision-4").resolve(), catalog.review_root)
            document = catalog.option_document()
            self.assertEqual("rest", document["defaults"]["stage"])
            self.assertEqual(["rest", "drawn"], [stage["id"] for stage in document["candidates"][0]["stages"]])

    def test_shape_lookup_rejects_unknown_paths(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            option = root / "review" / "option-a"
            option.mkdir(parents=True)
            (option / "rest.shape.json").write_text(json.dumps({"elements": []}), encoding="utf-8")
            catalog = ReviewCatalog(root, Path("review"))
            with self.assertRaises(ValueError):
                catalog.shape_path("../outside", "rest")

    def test_all_models_catalog_scans_nested_model_folders_and_tracks_changes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            review = root / "generated" / "review"
            first = review / "revision-1" / "option-a"
            second = review / "revision-2" / "option-b"
            first.mkdir(parents=True)
            second.mkdir(parents=True)
            (first / "rest.shape.json").write_text(json.dumps({"elements": []}), encoding="utf-8")
            (second / "drawn.shape.json").write_text(json.dumps({"elements": []}), encoding="utf-8")

            catalog = ReviewCatalog(root, Path("generated/review"), all_models=True)

            self.assertEqual(["revision-1/option-a", "revision-2/option-b"], [item["id"] for item in catalog.candidates])
            before = catalog.model_folder_signature()
            (second / "drawn.shape.json").write_text(json.dumps({"elements": [{"name": "changed"}]}), encoding="utf-8")
            self.assertNotEqual(before, catalog.model_folder_signature())


if __name__ == "__main__":
    unittest.main()
