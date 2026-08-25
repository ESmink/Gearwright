from __future__ import annotations

import argparse
import copy
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools" / "graphics"))

from gearwright_graphics.compiler import compile_shape  # noqa: E402
from gearwright_graphics.model import Shape, animate  # noqa: E402
from gearwright_graphics.photoshoot import _all_triangles  # noqa: E402
from gearwright_graphics.scene import SceneObject  # noqa: E402


class PhotoshootFrameTests(unittest.TestCase):
    def test_frame_sampling_is_absolute_and_does_not_mutate_source_shapes(self):
        shape = Shape("frame-sampling")
        moving = shape.box("moving", (0, 0, 0), (1, 1, 1), texture="#missing")
        animation = animate(shape, "Move", "move", 11)
        animation.keyframe(0, moving, offsetY=0)
        animation.keyframe(10, moving, offsetY=10)
        shape.add_animation(animation.build())
        document = compile_shape(shape)
        original = copy.deepcopy(document)
        objects = [SceneObject("target", "frame-sampling", document)]
        args = argparse.Namespace(
            explode=0,
            hide=[],
            only=[],
            ghost=[],
            animation="move",
            frame=2,
            interpolation="linear",
        )

        first = _all_triangles(objects, (), args, [])
        args.frame = 8
        second = _all_triangles(objects, (), args, [])

        self.assertEqual(original, objects[0].shape)
        self.assertAlmostEqual(2 / 16, min(float(vertex[1]) for triangle in first for vertex in triangle.vertices))
        self.assertAlmostEqual(8 / 16, min(float(vertex[1]) for triangle in second for vertex in triangle.vertices))


if __name__ == "__main__":
    unittest.main()
