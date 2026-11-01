"""art-pipeline-design.md §5.3."""
import os
import subprocess
import sys
import unittest

from PIL import Image

try:
    from . import _art, _sprites  # noqa: F401
except ImportError:
    import _art, _sprites  # noqa: F401,E401

import art_metrics as m
import palette_snap

P = m.Palette.load()
BRASS = P.ramps["brass"]


def quiet(*_):
    pass


class ReduceTests(unittest.TestCase):
    def test_a_2x2_block_reduces_to_its_majority_colour(self):
        image = Image.new("RGBA", (2, 2), BRASS[2] + (255,))
        image.putpixel((1, 1), BRASS[0] + (255,))
        self.assertEqual(BRASS[2] + (255,), palette_snap.reduce(image, 2).getpixel((0, 0)))

    def test_a_one_pixel_line_survives_reduce_8(self):
        small = _sprites.block(12, 12, [(0, 0, 12, 12)], BRASS[1])
        small.alpha_composite(_sprites.block(12, 12, [(5, 0, 1, 12)], BRASS[3]))
        big = small.resize((96, 96), Image.NEAREST)
        self.assertEqual(small.tobytes(), palette_snap.reduce(big, 8).tobytes())

    def test_a_mostly_clear_block_is_clear(self):
        image = Image.new("RGBA", (4, 4), (0, 0, 0, 0))
        image.putpixel((0, 0), BRASS[2] + (255,))
        self.assertEqual(0, palette_snap.reduce(image, 4).getpixel((0, 0))[3])


class SnapTests(unittest.TestCase):
    def test_a_half_alpha_pixel_goes_to_0_or_255(self):
        image = Image.new("RGBA", (2, 1))
        image.putpixel((0, 0), BRASS[2] + (200,))
        image.putpixel((1, 0), BRASS[2] + (60,))
        out, _ = palette_snap.run(image, P, ["brass"], log=quiet)
        self.assertEqual(255, out.getpixel((0, 0))[3])
        self.assertEqual(0, out.getpixel((1, 0))[3])

    def test_the_shadow_rows_keep_their_alpha(self):
        image = Image.new("RGBA", (1, 3))
        for y in range(3):
            image.putpixel((0, y), BRASS[2] + (100,))
        out, _ = palette_snap.run(image, P, ["brass"], shadow_rows=(1, 2), log=quiet)
        self.assertEqual([0, 100, 100], [out.getpixel((0, y))[3] for y in range(3)])

    def test_an_off_palette_pixel_lands_on_its_nearest_and_an_on_palette_one_stays(self):
        image = Image.new("RGBA", (10, 1), BRASS[1] + (255,))
        near = (BRASS[3][0] - 3, BRASS[3][1] + 2, BRASS[3][2] - 1)
        image.putpixel((0, 0), near + (255,))
        out, counts = palette_snap.run(image, P, ["brass"], log=quiet)
        self.assertEqual(BRASS[3] + (255,), out.getpixel((0, 0)))
        self.assertEqual(BRASS[1] + (255,), out.getpixel((1, 0)))
        self.assertEqual(1, counts["snap"])

    def test_a_render_near_the_palette_everywhere_is_not_refused(self):
        # Every pixel a shade off (as every ComfyUI render is): all move, none far.
        image = Image.new("RGBA", (4, 4), (BRASS[2][0] - 4, BRASS[2][1] + 3, BRASS[2][2] + 2, 255))
        out, counts = palette_snap.run(image, P, ["brass"], log=quiet)
        self.assertEqual(16, counts["snap"])
        self.assertEqual(0, counts["snap far"])
        self.assertEqual(BRASS[2] + (255,), out.getpixel((0, 0)))

    def test_too_much_off_palette_is_refused(self):
        image = Image.new("RGBA", (4, 4), (10, 200, 250, 255))
        with self.assertRaises(palette_snap.SnapError):
            palette_snap.run(image, P, ["brass"], log=quiet)

    def test_a_black_outline_turns_warm_brown(self):
        image = Image.new("RGBA", (5, 5), (0, 0, 0, 0))
        for x in range(5):
            for y in range(5):
                inner = 0 < x < 4 and 0 < y < 4
                image.putpixel((x, y), (BRASS[2] if inner else (0, 0, 0)) + (255,))
        out, counts = palette_snap.run(image, P, ["outline", "brass"], outline=True, max_change=1.0, log=quiet)
        self.assertIn(out.getpixel((0, 0))[:3], P.ramps["outline"])
        self.assertEqual(BRASS[2], out.getpixel((2, 2))[:3])
        self.assertEqual(16, counts["outline"])


class FixTests(unittest.TestCase):
    def test_a_fix_paints_its_rectangle(self):
        image = Image.new("RGBA", (4, 4), BRASS[1] + (255,))
        fixes = [{"rect": [1, 1, 2, 2], "tone": "aether:2"}, {"rect": [0, 0, 1, 1], "tone": "transparent"}]
        out, counts = palette_snap.run(image, P, ["brass", "aether"], fixes=fixes, log=quiet)
        self.assertEqual(P.ramps["aether"][2] + (255,), out.getpixel((2, 2)))
        self.assertEqual(0, out.getpixel((0, 0))[3])
        self.assertEqual(5, counts["fixes"])

    def test_a_ramp_fix_makes_a_part_one_material_and_keeps_its_shading(self):
        mahogany, outline = P.ramps["mahogany"], P.ramps["outline"]
        image = Image.new("RGBA", (4, 1), (0, 0, 0, 0))
        image.putpixel((0, 0), outline[0] + (255,))
        image.putpixel((1, 0), BRASS[1] + (255,))     # a highlight the snap put on brass
        image.putpixel((2, 0), mahogany[1] + (255,))
        fixes = [{"rect": [0, 0, 4, 1], "ramp": "mahogany"}]
        out, _ = palette_snap.run(image, P, ["outline", "brass", "mahogany"], fixes=fixes, log=quiet)
        self.assertEqual(outline[0] + (255,), out.getpixel((0, 0)))
        self.assertIn(out.getpixel((1, 0))[:3], mahogany)
        self.assertEqual(mahogany[1] + (255,), out.getpixel((2, 0)))
        self.assertEqual(0, out.getpixel((3, 0))[3])
        self.assertGreater(m.oklab(out.getpixel((1, 0)))[0], m.oklab(mahogany[1])[0])
        with self.assertRaises(palette_snap.SnapError):
            palette_snap.run(image, P, ["brass"], fixes=[{"rect": [0, 0, 1, 1], "ramp": "chrome"}], log=quiet)

    def test_a_fix_with_an_off_palette_tone_is_refused(self):
        image = Image.new("RGBA", (4, 4), BRASS[1] + (255,))
        for tone in ("brass:9", "chrome:0", "#ff0000"):
            with self.assertRaises(palette_snap.SnapError):
                palette_snap.run(image, P, ["brass"], fixes=[{"rect": [0, 0, 1, 1], "tone": tone}], log=quiet)


class RefusalTests(unittest.TestCase):
    def test_it_refuses_godot_art_without_into_art(self):
        target = os.path.join(_art.GODOT_ART, "never_written.png")
        result = subprocess.run([sys.executable, os.path.join(_art.ART, "palette_snap.py"),
                                 os.path.join(_art.GODOT_ART, "item_coal.png"), target, "--ramps", "brass"],
                                capture_output=True, text=True)
        self.assertEqual(2, result.returncode, result.stderr)
        self.assertFalse(os.path.exists(target))


if __name__ == "__main__":
    unittest.main()


class KeyAndCanvasTests(unittest.TestCase):
    GREEN = (40, 190, 70)

    def test_the_key_clears_the_background_from_the_edge_only(self):
        image = Image.new("RGBA", (9, 9), self.GREEN + (255,))
        for y in range(2, 7):
            for x in range(2, 7):
                image.putpixel((x, y), BRASS[1] + (255,))
        image.putpixel((4, 4), (44, 186, 72, 255))  # key-coloured, but inside the body
        cleared = palette_snap.key_out(image, self.GREEN)
        self.assertEqual(81 - 25, cleared)
        self.assertEqual(0, image.getpixel((0, 0))[3])
        self.assertEqual(255, image.getpixel((4, 4))[3])
        self.assertEqual(255, image.getpixel((2, 2))[3])

    def test_key_holes_clears_an_enclosed_gap_but_not_a_small_patch(self):
        image = Image.new("RGBA", (12, 12), self.GREEN + (255,))
        for y in range(1, 11):
            for x in range(1, 11):
                image.putpixel((x, y), BRASS[1] + (255,))
        for y in range(3, 7):
            for x in range(3, 7):
                image.putpixel((x, y), self.GREEN + (255,))  # a 16-pixel gap in the body
        image.putpixel((8, 8), self.GREEN + (255,))  # a 1-pixel patch
        kept = image.copy()
        palette_snap.key_out(kept, self.GREEN)
        self.assertEqual(255, kept.getpixel((4, 4))[3])
        palette_snap.key_out(image, self.GREEN, holes=4)
        self.assertEqual(0, image.getpixel((4, 4))[3])
        self.assertEqual(255, image.getpixel((8, 8))[3])

    def test_the_key_takes_a_near_shade_and_leaves_a_far_colour(self):
        image = Image.new("RGBA", (3, 1), self.GREEN + (255,))
        image.putpixel((1, 0), (48, 180, 76, 255))
        image.putpixel((2, 0), BRASS[2] + (255,))
        palette_snap.key_out(image, self.GREEN)
        self.assertEqual([0, 0, 255], [image.getpixel((x, 0))[3] for x in range(3)])

    def test_the_canvas_puts_the_feet_on_the_bottom_row_centred(self):
        image = Image.new("RGBA", (20, 20), (0, 0, 0, 0))
        for y in range(3, 9):
            for x in range(1, 5):
                image.putpixel((x, y), BRASS[2] + (255,))
        out = palette_snap.place_on_canvas(image, (10, 12))
        self.assertEqual((10, 12), out.size)
        self.assertEqual((3, 6, 7, 12), out.getbbox())

    def test_a_body_larger_than_the_canvas_is_refused(self):
        image = Image.new("RGBA", (20, 20), BRASS[2] + (255,))
        with self.assertRaises(palette_snap.SnapError):
            palette_snap.place_on_canvas(image, (10, 12))

    def test_run_keys_reduces_and_places(self):
        big = Image.new("RGB", (32, 32), self.GREEN)
        for y in range(8, 24):
            for x in range(8, 16):
                big.putpixel((x, y), BRASS[2])
        out, counts = palette_snap.run(big, P, ["brass"], reduce_by=8, key=self.GREEN, canvas=(4, 4), log=quiet)
        self.assertEqual((4, 4), out.size)
        self.assertEqual((1, 2, 2, 4), out.getbbox())
        self.assertGreater(counts["key"], 0)
