"""art-pipeline-design.md §5.6."""
import os
import subprocess
import sys
import tempfile
import unittest

try:
    from . import _art, _sprites  # noqa: F401
except ImportError:
    import _art, _sprites  # noqa: F401,E401

import art_metrics as m
import pose_guide

P = m.Palette.load()


def edges(image):
    px = image.load()
    return {(x, y) for y in range(image.size[1]) for x in range(image.size[0]) if px[x, y] == (255, 255, 255)}


def base():
    """A brass body with an aether 'ram' block on top of it."""
    image = _sprites.block(24, 32, [(6, 12, 12, 20)], P.ramps["brass"][2])
    image.alpha_composite(_sprites.block(24, 32, [(9, 4, 6, 6)], P.ramps["aether"][2]))
    return image


class PoseGuideTests(unittest.TestCase):
    def test_an_identity_pose_gives_the_bases_own_edges(self):
        guide = pose_guide.lineart(pose_guide.pose(base(), {}, P), P)
        self.assertEqual(edges(pose_guide.lineart(base(), P)), edges(guide))
        self.assertIn((6, 20), edges(guide))      # the body's silhouette
        self.assertNotIn((10, 20), edges(guide))  # inside one ramp: no edge

    def test_a_translated_part_moves_its_edges_by_exactly_its_offset(self):
        frame = {"parts": [{"rect": [9, 4, 6, 6], "translate": [0, 3]}]}
        moved = pose_guide.lineart(pose_guide.pose(base(), frame, P), P)
        alone = _sprites.block(24, 32, [(9, 4, 6, 6)], P.ramps["aether"][2])
        ram_before = edges(pose_guide.lineart(alone, P))
        shifted = {(x, y + 3) for x, y in ram_before}
        # The ram's top and sides moved down 3; its old top row is gone.
        self.assertTrue({(x, y) for x, y in shifted if y < 12} <= edges(moved))
        self.assertNotIn((11, 4), edges(moved))

    def test_a_part_by_ramp_and_a_quarter_turn(self):
        frame = {"parts": [{"ramps": ["aether"], "rotate": 90, "pivot": [11.5, 6.5]}]}
        moved = pose_guide.pose(base(), frame, P)
        # A 6x6 block turned a quarter about its centre covers the same pixels.
        self.assertEqual(m.xy_mask(base()), m.xy_mask(moved))

    def test_a_hidden_rectangle_leaves_no_edges_in_it(self):
        frame = {"hide": [[6, 12, 6, 6]]}
        guide = pose_guide.lineart(pose_guide.pose(base(), frame, P), P)
        self.assertFalse({(x, y) for x, y in edges(guide) if 6 <= x < 12 and 12 <= y < 18})

    def test_both_files_are_scale_times_the_base(self):
        poses = {"work_1": {"parts": [{"rect": [9, 4, 6, 6], "translate": [0, 2]}]}}
        guide, init = pose_guide.make(base(), poses, "work_1", P, scale=8)
        self.assertEqual((24 * 8, 32 * 8), guide.size)
        self.assertEqual((24 * 8, 32 * 8), init.size)

    def test_an_unknown_frame_is_an_error(self):
        with self.assertRaises(pose_guide.PoseError):
            pose_guide.make(base(), {}, "work_9", P)

    def test_it_refuses_godot_art(self):
        with tempfile.TemporaryDirectory() as scratch:
            src = os.path.join(scratch, "base.png")
            base().save(src)
            poses = os.path.join(scratch, "poses.json")
            with open(poses, "w") as handle:
                handle.write('{"work_1": {}}')
            out = os.path.join(_art.GODOT_ART, "never_written")
            result = subprocess.run([sys.executable, os.path.join(_art.ART, "pose_guide.py"), src,
                                     "--poses", poses, "--frame", "work_1", "--out", out],
                                    capture_output=True, text=True)
        self.assertEqual(2, result.returncode, result.stderr)
        self.assertFalse(os.path.exists(out))


if __name__ == "__main__":
    unittest.main()
