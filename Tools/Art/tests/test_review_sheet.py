"""art-pipeline-design.md §5.8."""
import json
import os
import shutil
import tempfile
import unittest

try:
    from . import _art, _sprites  # noqa: F401
except ImportError:
    import _art, _sprites  # noqa: F401,E401

import review_sheet


class ReviewSheetTests(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="review-")
        self.before = os.path.join(self.root, "before")
        self.after = os.path.join(self.root, "after")
        os.makedirs(self.before)
        os.makedirs(self.after)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def frames(self, n, with_before=True):
        names = [f"chassis_t_work_{i % 3}_{('north', 'east', 'south')[i % 3]}_{i}.png" for i in range(n)]
        for name in names:
            _sprites.lit_body().save(os.path.join(self.after, name))
            if with_before:
                _sprites.lit_body().save(os.path.join(self.before, name))
        return names

    def test_numbering_follows_the_order_given_and_is_stable(self):
        names = ["b.png", "a.png", "c.png"]
        self.assertEqual({1: "b.png", 2: "a.png", 3: "c.png"}, review_sheet.numbering(names))
        self.assertEqual(review_sheet.numbering(names), review_sheet.numbering(list(names)))

    def test_review_json_maps_every_number_to_its_file_and_a_page_holds_at_most_28(self):
        names = self.frames(30)
        out = os.path.join(self.root, "out")
        paths = review_sheet.write("S1", names, self.before, self.after, out)
        self.assertEqual(["review-1.png", "review-2.png", "review.json"], [os.path.basename(p) for p in paths])
        with open(os.path.join(out, "review.json")) as handle:
            review = json.load(handle)
        self.assertEqual(30, len(review["frames"]))
        for number, name in enumerate(names, start=1):
            self.assertEqual(name, review["frames"][str(number)]["file"])
        self.assertEqual(1, review["frames"]["28"]["page"])
        self.assertEqual(2, review["frames"]["29"]["page"])
        self.assertEqual([names[:28], names[28:]], review_sheet.pages(names))

    def test_a_frame_with_no_before_is_labelled_with_what_it_was_made_from(self):
        self.assertEqual("chassis_t.png", review_sheet.made_from("chassis_t_stall_north.png", {}))
        self.assertEqual("x.png", review_sheet.made_from("chassis_t_stall_north.png", {"chassis_t_stall_north.png": "x.png"}))
        names = self.frames(2, with_before=False)
        out = os.path.join(self.root, "out")
        review_sheet.write("S1", names, self.before, self.after, out)
        self.assertTrue(os.path.exists(os.path.join(out, "review-1.png")))

    def test_a_west_frame_shows_the_plain_flip_of_its_east_source(self):
        self.assertEqual("chassis_t_carry_east.png", review_sheet.mirror_source("chassis_t_carry_west.png", {}))
        self.assertIsNone(review_sheet.mirror_source("chassis_t_carry_east.png", {}))

    def test_it_refuses_godot_art(self):
        names = self.frames(1)
        with self.assertRaises(review_sheet.SheetError):
            review_sheet.write("S1", names, self.before, self.after, os.path.join(_art.GODOT_ART, "review"))
        self.assertFalse(os.path.exists(os.path.join(_art.GODOT_ART, "review")))


if __name__ == "__main__":
    unittest.main()
