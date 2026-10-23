"""art-pipeline-design.md §5: the shared measures."""
import unittest

from PIL import Image

try:
    from . import _art, _sprites  # noqa: F401
except ImportError:
    import _art, _sprites  # noqa: F401,E401

import art_metrics as m


class OklabTests(unittest.TestCase):
    def test_white_black_and_a_known_red(self):
        for got, want in zip(m.oklab((255, 255, 255)), (1.0, 0.0, 0.0)):
            self.assertAlmostEqual(want, got, places=3)
        for got, want in zip(m.oklab((0, 0, 0)), (0.0, 0.0, 0.0)):
            self.assertAlmostEqual(want, got, places=3)
        # Björn Ottosson's reference for sRGB red: (0.6280, 0.2249, 0.1258).
        for got, want in zip(m.oklab((255, 0, 0)), (0.6280, 0.2249, 0.1258)):
            self.assertAlmostEqual(want, got, places=3)

    def test_grey_has_no_chroma(self):
        L, a, b = m.oklab((128, 128, 128))
        self.assertAlmostEqual(0.0, a, places=4)
        self.assertAlmostEqual(0.0, b, places=4)
        self.assertTrue(0.55 < L < 0.65)


class MaskTests(unittest.TestCase):
    def test_iou_of_known_masks(self):
        a = {(0, 0), (1, 0), (2, 0), (3, 0)}
        b = {(2, 0), (3, 0), (4, 0), (5, 0)}
        self.assertAlmostEqual(2 / 6, m.iou(a, b))
        self.assertEqual(1.0, m.iou(a, set(a)))
        self.assertEqual(0.0, m.iou(a, {(9, 9)}))

    def test_masks_align_at_the_feet_whatever_the_canvas_height(self):
        short = _sprites.block(8, 10, [(2, 6, 4, 4)], (200, 100, 50))
        tall = _sprites.block(8, 20, [(2, 16, 4, 4)], (200, 100, 50))
        self.assertEqual(1.0, m.iou(m.foot_mask(short), m.foot_mask(tall)))

    def test_connected_pieces(self):
        self.assertEqual(2, m.connected_pieces({(0, 0), (1, 0), (5, 5)}))
        self.assertEqual(1, m.connected_pieces({(0, 0), (0, 1), (1, 1)}))
        self.assertEqual(2, m.connected_pieces({(0, 0), (1, 1)}))  # 4-connected only


class EdgeLightTests(unittest.TestCase):
    def test_a_left_lit_gradient_scores_positive_and_a_right_lit_one_negative(self):
        outline = m.Palette.load().outline()
        left = m.edge_light_score(_sprites.lit_body(lit_from="left"), outline)
        right = m.edge_light_score(_sprites.lit_body(lit_from="right"), outline)
        self.assertGreater(left, 0.02)
        self.assertLess(right, -0.02)
        self.assertAlmostEqual(left, -right, places=6)

    def test_without_an_outline_set_the_edge_pixel_is_skipped(self):
        self.assertGreater(m.edge_light_score(_sprites.lit_body(lit_from="left")), 0.02)


class ReducePhaseTests(unittest.TestCase):
    def test_each_phase_drops_a_different_column_in_four(self):
        image = Image.new("RGBA", (32, 32))
        px = image.load()
        for x in range(32):
            for y in range(32):
                px[x, y] = (x * 8, y * 8, 0, 255)
        for phase in range(4):
            out = m.reduce_phase(image, phase)
            self.assertEqual((24, 24), out.size)
            kept = [out.getpixel((i, 0))[0] // 8 for i in range(24)]
            self.assertEqual(len(set(kept)), 24)
            self.assertTrue(all(b - a in (1, 2) for a, b in zip(kept, kept[1:])))  # never two adjacent dropped
        self.assertNotEqual(m.reduce_phase(image, 0).tobytes(), m.reduce_phase(image, 1).tobytes())


class PaletteTests(unittest.TestCase):
    def test_classify_and_nearest(self):
        palette = m.Palette.load()
        self.assertEqual(("brass", 3), palette.classify(m.hex_to_rgb("#e2c27a")))
        self.assertIsNone(palette.classify((1, 2, 3)))
        self.assertEqual(m.hex_to_rgb("#2f211c"), palette.nearest((0, 0, 0), ["outline"]))


if __name__ == "__main__":
    unittest.main()
