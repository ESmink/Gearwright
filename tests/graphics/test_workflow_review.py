from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT / "tools" / "graphics"))

from graphics.review.slingshot_workflow import build_review  # noqa: E402


class WorkflowReviewTests(unittest.TestCase):
    def test_slingshot_review_is_managed_repeatable_and_never_runtime_output(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            managed = build_review(root, approved_candidate=None)
            stray = managed / "stray-agent-output.txt"
            stray.write_text("orphan", encoding="utf-8")

            rebuilt = build_review(root, approved_candidate=None)

            self.assertEqual(managed, rebuilt)
            self.assertFalse(stray.exists())
            self.assertFalse((root / "assets").exists())
            ownership = json.loads((rebuilt / ".gearwright-review.json").read_text(encoding="utf-8"))
            self.assertEqual("graphics/review/slingshot_workflow.py", ownership["source"])
            self.assertEqual(3, len(ownership["candidates"]))
            for candidate in ownership["candidates"]:
                document = json.loads((rebuilt / candidate / "rest.shape.json").read_text(encoding="utf-8"))
                self.assertEqual(
                    ["draw-steady", "release-snap", "idle-check"],
                    [animation["code"] for animation in document["animations"]],
                )

            finalized = build_review(root)
            ownership = json.loads((finalized / ".gearwright-review.json").read_text(encoding="utf-8"))
            self.assertEqual(["model-c-tall-sapling-fork"], ownership["candidates"])
            self.assertEqual("approved", ownership["decision"]["status"])
            self.assertEqual("model-c-tall-sapling-fork", ownership["decision"]["candidate"])
            self.assertFalse(ownership["decision"]["runtimePromotion"])
            self.assertFalse((finalized / "model-a-balanced-fork").exists())
            self.assertFalse((finalized / "model-b-workshop-yoke").exists())


if __name__ == "__main__":
    unittest.main()
