"""art-pipeline-design.md §5.7."""
import unittest

from PIL import Image

try:
    from . import _art, _sprites  # noqa: F401
except ImportError:
    import _art, _sprites  # noqa: F401,E401

import art_metrics as m
import reshade_mirror

P = m.Palette.load()
OUTLINE = P.outline()


def source():
    """Left-lit, with a stepped-up (lighter) outline on its lit left edge."""
    image = _sprites.lit_body(width=16, height=24)
    for y in range(6, 20):
        image.putpixel((3, y), P.ramps["outline"][2] + (255,))
    return image


class ReshadeMirrorTests(unittest.TestCase):
    def test_a_plain_flip_scores_negative_and_the_reshade_keeps_the_light(self):
        src = source()
        before = m.edge_light_score(src, OUTLINE)
        flip = src.transpose(Image.FLIP_LEFT_RIGHT)
        self.assertLess(m.edge_light_score(flip, OUTLINE), 0)
        out, _ = reshade_mirror.reshade(src, P)
        self.assertGreaterEqual(m.edge_light_score(out, OUTLINE), 0.8 * before)

    def test_the_alpha_mask_is_the_plain_flips_and_the_foot_row_stays(self):
        src = source()
        out, _ = reshade_mirror.reshade(src, P)
        self.assertEqual(m.xy_mask(src.transpose(Image.FLIP_LEFT_RIGHT)), m.xy_mask(out))
        self.assertEqual(m.lowest_opaque_row(src), m.lowest_opaque_row(out))

    def test_every_pixel_keeps_its_plain_flip_ramp_and_stays_on_the_palette(self):
        src = source()
        flip = src.transpose(Image.FLIP_LEFT_RIGHT)
        out, _ = reshade_mirror.reshade(src, P)
        for x, y, p in m.opaque_pixels(out):
            self.assertIsNotNone(P.classify(p))
            self.assertEqual(P.classify(flip.getpixel((x, y)))[0], P.classify(p)[0])

    def test_the_stepped_up_outline_moves_back_to_the_left(self):
        out, _ = reshade_mirror.reshade(source(), P)
        # The light outline was on the source's left (x 3); the flip puts it at x 12, and the
        # re-shade swaps it back to the left end of the span.
        self.assertEqual(P.ramps["outline"][2], out.getpixel((3, 10))[:3])
        self.assertEqual(P.ramps["outline"][0], out.getpixel((12, 10))[:3])

    def test_a_pixel_in_no_ramp_is_untouched_and_counted(self):
        src = source()
        src.putpixel((6, 12), (1, 2, 3, 255))
        out, untouched = reshade_mirror.reshade(src, P)
        self.assertEqual(1, untouched)
        self.assertEqual((1, 2, 3, 255), out.getpixel((15 - 6, 12)))

    def test_the_mirror_of_a_mirror_is_the_source(self):
        src = source()
        once, _ = reshade_mirror.reshade(src, P)
        twice, _ = reshade_mirror.reshade(once, P)
        self.assertEqual(src.tobytes(), twice.tobytes())

    def test_an_odd_width_is_refused(self):
        with self.assertRaises(reshade_mirror.MirrorError):
            reshade_mirror.reshade(Image.new("RGBA", (15, 24)), P)


if __name__ == "__main__":
    unittest.main()
