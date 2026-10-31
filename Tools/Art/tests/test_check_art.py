"""art-pipeline-design.md §5.2: one synthetic image per failure, each failing with its check,
and a clean image passing. Everything is built in a temporary art folder."""
import os
import shutil
import tempfile
import unittest

from PIL import Image

try:
    from . import _art, _sprites  # noqa: F401
except ImportError:
    import _art, _sprites  # noqa: F401,E401

import art_metrics as m
import check_art

TINTS = {"Stalled": (0.85, 0.35, 0.35), "Starved": (0.55, 0.68, 0.85), "Waiting": (0.88, 0.88, 0.90)}
STEM = "chassis_test"
FACINGS = check_art.FACINGS
STATES = check_art.FRAME_STATES


def entry(kind="standing", palette="bible", pivot="bottom-center", **extra):
    e = {"set": "T", "kind": kind, "pivot": pivot, "owner": "comfyui", "palette": palette}
    e.update(extra)
    return e


class CheckArtCase(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="check-art-")
        self.manifest = {}
        self.stems = []

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def add(self, rel, image, e, in_manifest=True):
        path = os.path.join(self.root, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        image.save(path)
        if in_manifest:
            e = dict(e)
            e["size"] = list(image.size)
            self.manifest[rel] = e

    def run_checks(self, **ctx):
        context = check_art.Context(m.Palette.load(), ctx.get("stems", self.stems), TINTS,
                                    known_hand_replaced=ctx.get("known", ()), claude_text=ctx.get("claude", ""))
        return check_art.Checker(self.manifest, self.root, context).run()

    def fails(self, report, check, rel=None):
        return [f for f in report.findings if f[0] == "FAIL" and f[1] == check and (rel is None or f[2] == rel)]

    def assertFails(self, report, check, rel=None):
        self.assertTrue(self.fails(report, check, rel),
                        f"expected a FAIL [{check}] on {rel}; got: " + "; ".join(report.lines()))


class CleanTests(CheckArtCase):
    def test_a_clean_left_lit_sprite_passes(self):
        self.add("prop.png", _sprites.lit_body(), entry())
        report = self.run_checks()
        self.assertFalse(report.failed, "; ".join(report.lines()))


class PerFileTests(CheckArtCase):
    def test_a_missing_manifest_entry_fails(self):
        self.add("prop.png", _sprites.lit_body(), entry())
        self.add("stray.png", _sprites.lit_body(), entry(), in_manifest=False)
        self.assertFails(self.run_checks(), 1, "stray.png")

    def test_an_entry_without_a_file_fails(self):
        self.manifest["gone.png"] = entry(size=[16, 24])
        self.assertFails(self.run_checks(), 1, "gone.png")

    def test_a_canvas_a_pixel_too_tall_fails(self):
        image = _sprites.lit_body(width=64, height=97, right=40, top=10)
        self.add("chassis_tall.png", image, entry(chassis="chassis_tall", state="base", facing="south"))
        self.assertFails(self.run_checks(), 2, "chassis_tall.png")

    def test_a_size_the_manifest_disagrees_with_fails(self):
        self.add("prop.png", _sprites.lit_body(), entry())
        self.manifest["prop.png"]["size"] = [16, 25]
        self.assertFails(self.run_checks(), 2, "prop.png")

    def test_a_floating_foot_row_fails(self):
        image = _sprites.lit_body()
        floating = Image.new("RGBA", (16, 26), (0, 0, 0, 0))
        floating.paste(image, (0, 0))
        self.add("prop.png", floating, entry())
        self.assertFails(self.run_checks(), 3, "prop.png")

    def test_a_half_alpha_pixel_fails(self):
        image = _sprites.lit_body()
        r, g, b, _ = image.getpixel((6, 10))
        image.putpixel((6, 10), (r, g, b, 128))
        self.add("prop.png", image, entry())
        self.assertFails(self.run_checks(), 4, "prop.png")

    def test_half_alpha_in_the_shadow_rows_is_allowed(self):
        image = _sprites.lit_body()
        r, g, b, _ = image.getpixel((6, 22))
        image.putpixel((6, 22), (r, g, b, 128))
        self.add("prop.png", image, entry(shadowRows=[21, 23]))
        self.assertFalse(self.fails(self.run_checks(), 4))

    def test_an_off_palette_pixel_fails(self):
        image = _sprites.lit_body()
        image.putpixel((6, 10), (1, 2, 3, 255))
        self.add("prop.png", image, entry())
        self.assertFails(self.run_checks(), 5, "prop.png")

    def test_a_red_body_fails(self):
        self.add("prop.png", _sprites.block(16, 24, [(2, 4, 12, 20)], (220, 30, 30)), entry())
        self.assertFails(self.run_checks(), 7, "prop.png")

    def test_legacy_art_only_warns(self):
        image = _sprites.block(16, 24, [(2, 4, 12, 20)], (220, 30, 30))
        self.add("prop.png", image, entry(palette="legacy"))
        report = self.run_checks()
        self.assertFalse(report.failed, "; ".join(report.lines()))
        self.assertTrue(any(f[0] == "WARN" and f[1] == 7 for f in report.findings))


class CopperOreTests(CheckArtCase):
    def test_copper_ore_turned_orange_fails(self):
        self.add("item_copper_ore.png", _sprites.block(32, 32, [(8, 8, 16, 16)], (230, 130, 40)),
                 entry(kind="good", pivot="center", palette="legacy"))
        self.assertFails(self.run_checks(), 12, "item_copper_ore.png")

    def test_green_copper_ore_passes(self):
        self.add("item_copper_ore.png", _sprites.block(32, 32, [(8, 8, 16, 16)], m.hex_to_rgb("#609e68")),
                 entry(kind="good", pivot="center", palette="legacy"))
        self.assertFalse(self.fails(self.run_checks(), 12))


class GoodsTests(CheckArtCase):
    def test_a_good_that_splits_at_one_sampling_phase_fails(self):
        # Two blocks joined by a one-pixel bridge on row 11, which phase 0 drops (3, 7, 11, ...).
        brass = _sprites.BRASS
        image = _sprites.block(32, 32, [(2, 2, 8, 19)], brass[0])
        image.alpha_composite(_sprites.block(32, 32, [(22, 2, 8, 19)], brass[2]))
        image.alpha_composite(_sprites.block(32, 32, [(10, 11, 12, 1)], brass[3]))
        self.add("item_bridge.png", image, entry(kind="good", pivot="center"))
        report = self.run_checks()
        self.assertTrue(any(f[1] == 11 and f[0] == "FAIL" and "phase 0" in f[3] for f in report.findings),
                        "; ".join(report.lines()))


class GolemTests(CheckArtCase):
    def tall(self, left, right, top=6):
        return _sprites.lit_body(width=24, height=32, left=left, right=right, top=top)

    def test_two_identical_silhouettes_in_one_facing_fail(self):
        self.add("chassis_a.png", self.tall(4, 18), entry(chassis="chassis_a", state="base", facing="south"))
        self.add("chassis_b.png", self.tall(4, 18), entry(chassis="chassis_b", state="base", facing="south"))
        self.assertFails(self.run_checks(), 8)

    def test_a_stall_pose_closer_to_another_chassis_fails(self):
        self.add("chassis_a.png", self.tall(2, 10), entry(chassis="chassis_a", state="base", facing="south"))
        self.add("chassis_b.png", self.tall(12, 22), entry(chassis="chassis_b", state="base", facing="south"))
        self.add("chassis_a_stall_south.png", self.tall(12, 22),
                 entry(chassis="chassis_a", state="stall", facing="south"))
        self.assertFails(self.run_checks(), 9, "chassis_a_stall_south.png")

    def test_a_chassis_missing_its_north_stall_frame_fails_once_any_frame_is_replaced(self):
        for facing in FACINGS:
            for state in STATES:
                name = check_art.frame_name(STEM, state, facing)
                if name == f"{STEM}_stall_north.png":
                    continue
                self.add(name, self.tall(4, 18), entry(chassis=STEM, state=state, facing=facing))
        report = self.run_checks(stems=[STEM])
        self.assertFails(report, 13, f"{STEM}_stall_north.png")

    def test_a_chassis_with_only_its_legacy_base_warns(self):
        self.add(f"{STEM}.png", self.tall(4, 18), entry(palette="legacy", chassis=STEM, state="base", facing="south"))
        report = self.run_checks(stems=[STEM])
        self.assertFalse(self.fails(report, 13))
        self.assertTrue(any(f[0] == "WARN" and f[1] == 13 for f in report.findings))

    def test_a_dark_body_fails_the_tint_check(self):
        dark = _sprites.lit_body(ramp=m.Palette.load().ramps["floor"])
        self.add("chassis_dark.png", dark, entry(chassis="chassis_dark", state="base", facing="south"))
        report = self.run_checks()
        self.assertTrue(any(f[1] == 10 and "lightness" in f[3] for f in self.fails(report, 10)), "; ".join(report.lines()))


class LightTests(CheckArtCase):
    def test_a_right_lit_frame_fails(self):
        self.add("prop.png", _sprites.lit_body(lit_from="right"), entry())
        self.assertFails(self.run_checks(), 14, "prop.png")

    def test_a_light_note_allows_a_low_score(self):
        self.add("prop.png", _sprites.lit_body(lit_from="right"), entry(lightNote="one-colour outline"))
        report = self.run_checks()
        self.assertFalse(self.fails(report, 14), "; ".join(report.lines()))
        self.assertTrue(any(f[0] == "INFO" and f[1] == 14 for f in report.findings), "; ".join(report.lines()))

    def test_a_plain_flipped_mirror_fails(self):
        east = _sprites.lit_body()
        self.add("x_base_east.png", east, entry())
        self.add("x_base_west.png", east.transpose(Image.FLIP_LEFT_RIGHT), entry(mirroredFrom="x_base_east.png"))
        self.assertFails(self.run_checks(), 14, "x_base_west.png")


class BookkeepingTests(CheckArtCase):
    def test_hand_replaced_must_be_listed_both_ways(self):
        self.add("stall_x.png", _sprites.lit_body(), entry(palette="legacy", owner="hand-replaced"))
        report = self.run_checks(known={"stall_y.png"}, claude="")
        self.assertFails(report, 15, "stall_x.png")
        self.assertFails(report, 15, "stall_y.png")
        report = self.run_checks(known={"stall_x.png"}, claude="the carts (`stall_x`)")
        self.assertFalse(self.fails(report, 15))

    def test_the_pack_must_be_gone_once_the_hud_lands(self):
        self.add("UI/Steampunk/button.png", _sprites.lit_body(), entry(kind="ui", pivot="ui", palette="legacy", owner="third-party"))
        self.assertFalse(self.fails(self.run_checks(), 16))
        self.add("UI/Hud/hud_button.png", _sprites.lit_body(), entry(kind="ui", pivot="ui"))
        self.assertFails(self.run_checks(), 16, "UI/Steampunk/button.png")


class ReadTintsTests(unittest.TestCase):
    def test_the_tints_are_read_from_the_csharp(self):
        source = '''
        private static readonly Color StalledTint = new Color(0.85f, 0.35f, 0.35f, 1f);
        private static readonly Color SleepingTint = new Color(0.88f, 0.88f, 0.90f, 1f);
        public static Color Tint(GolemMood mood)
        {
            switch (mood)
            {
                case GolemMood.Stalled:
                    return StalledTint;
                case GolemMood.Sleeping:
                case GolemMood.Waiting:
                    return SleepingTint;
                default:
                    return Color.white;
            }
        }
        // --- Badge plates
        '''
        tints = check_art.read_tints(source)
        self.assertEqual((0.85, 0.35, 0.35), tints["Stalled"])
        self.assertEqual((0.88, 0.88, 0.90), tints["Waiting"])
        self.assertEqual((0.88, 0.88, 0.90), tints["Sleeping"])

    def test_the_real_palette_file_gives_all_three(self):
        with open(check_art.MOOD_PALETTE_CS, encoding="utf-8") as handle:
            tints = check_art.read_tints(handle.read())
        for mood in check_art.MOOD_TINTS_CHECKED:
            self.assertIn(mood, tints)


class FootRowTests(unittest.TestCase):
    def test_foot_rows_follow_the_pivots(self):
        self.assertEqual(95, check_art.foot_row({"pivot": "bottom-center"}, (64, 96)))
        self.assertEqual(117, check_art.foot_row({"pivot": [0.5, 3 / 120]}, (96, 120)))
        self.assertEqual(93, check_art.foot_row({"pivot": [0.5, 3 / 96]}, (64, 96)))
        self.assertEqual(55, check_art.foot_row({"pivot": [0.5, 1 / 56]}, (56, 56)))
        self.assertIsNone(check_art.foot_row({"pivot": "center"}, (64, 64)))
        self.assertIsNone(check_art.foot_row({"pivot": [0.5, 1.0]}, (64, 24)))


if __name__ == "__main__":
    unittest.main()
