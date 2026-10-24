"""art-pipeline-design.md §5.9: golem_set.py, without ComfyUI, Godot or godot/art/ writes."""
import os
import shutil
import tempfile
import unittest

try:
    from . import _art  # noqa: F401
except ImportError:
    import _art  # noqa: F401

import golem_set as g


def config(**extra):
    c = {"stem": "chassis_t", "title": "T", "draft": "scripts/t_draft.py", "ramps": ["outline", "brass"],
         "prompts": {"south": "s", "north": "n", "east": "e"}}
    c.update(extra)
    return c


def scores(**extra):
    s = {"feet": 95, "silhouette": 0.3, "lightness": 0.55, "stalledShift": 0.19, "tintsApart": 0.1,
         "edgeLight": 0.15, "texture": 300, "taken": 400}
    s.update(extra)
    return s


class GolemSetTests(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="golemset-")

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def test_a_config_without_its_own_parts_is_refused(self):
        for key in g.REQUIRED:
            c = config()
            del c[key]
            with self.assertRaises(g.SetError):
                g.GolemSet(self.root, c)

    def test_every_drawn_facing_needs_a_prompt(self):
        with self.assertRaises(g.SetError):
            g.GolemSet(self.root, config(prompts={"south": "s", "north": "n"}))

    def test_a_set_folder_under_godot_art_is_refused(self):
        with self.assertRaises(g.SetError):
            g.GolemSet(os.path.join(_art.GODOT_ART, "x"), config())

    def test_defaults_fill_what_the_config_leaves_out_and_dirs_merge(self):
        gs = g.GolemSet(self.root, config(dirs={"raw": "raw2"}, seeds=2))
        self.assertEqual(2, gs.seeds)
        self.assertEqual(0.5, gs.ipadapterWeight)
        self.assertTrue(gs.dir("raw").endswith("raw2"))
        self.assertTrue(gs.dir("after").endswith("after"))

    def test_the_28_frames_are_in_review_order_and_the_south_base_keeps_its_data_name(self):
        frames = g.GolemSet(self.root, config()).frames()
        self.assertEqual(28, len(frames))
        self.assertEqual("chassis_t.png", frames[0])
        self.assertEqual("chassis_t_idle_1_south.png", frames[1])
        self.assertEqual("chassis_t_base_north.png", frames[7])
        self.assertEqual("chassis_t_carry_west.png", frames[27])

    def test_candidates_are_named_as_comfy_redraw_writes_them(self):
        gs = g.GolemSet(self.root, config(denoise=[0.45], seeds=2, seedBase=7))
        labels = [(label, os.path.basename(path)) for label, path in gs.candidates("east")]
        self.assertEqual([("d0.45_s7", "base_east_d0.45_s7.png"), ("d0.45_s8", "base_east_d0.45_s8.png")], labels)

    def test_a_candidate_failing_any_bound_is_never_picked(self):
        for bad in (dict(feet=90), dict(silhouette=0.6), dict(lightness=0.3), dict(stalledShift=0.05),
                    dict(tintsApart=0.01), dict(edgeLight=-0.1), dict(taken=0)):
            s = scores(**bad)
            s["fails"] = g.failures(s)
            self.assertTrue(s["fails"], bad)
            ok = scores(taken=1)
            ok["fails"] = g.failures(ok)
            self.assertEqual("ok", g.best({"bad": s, "ok": ok}))

    def test_the_pick_is_the_most_texture_then_the_edge_light(self):
        a, b, c = scores(taken=500, edgeLight=0.1), scores(taken=600, edgeLight=0.1), scores(taken=600, edgeLight=0.2)
        for s in (a, b, c):
            s["fails"] = g.failures(s)
        self.assertEqual("c", g.best({"a": a, "b": b, "c": c}))

    def test_no_pick_when_every_candidate_fails(self):
        s = scores(feet=3)
        s["fails"] = g.failures(s)
        self.assertIsNone(g.best({"a": s}))

    def test_manifest_entries_are_the_south_base_replaced_and_west_names_its_mirror(self):
        gs = g.GolemSet(self.root, config())
        base = {"set": "S1", "kind": "standing", "owner": "retrodiffusion", "palette": "legacy",
                "readers": ["data/chassis.json chassisSprite"], "chassis": "chassis_t", "state": "base", "facing": "south"}
        out = g.manifest_entries(gs, {"chassis_t.png": base})
        self.assertEqual(28, len(out))
        for name, e in out.items():
            self.assertEqual("comfyui", e["owner"])
            self.assertEqual(["outline", "brass"], e["palette"])
            self.assertEqual(95, e["footRow"])
        self.assertEqual("chassis_t_work_1_east.png", out["chassis_t_work_1_west.png"]["mirroredFrom"])
        self.assertNotIn("mirroredFrom", out["chassis_t_work_1_east.png"])
        self.assertEqual(["data/chassis.json chassisSprite"], out["chassis_t.png"]["readers"])
        self.assertEqual("retrodiffusion", base["owner"], "the manifest given is not changed")

    def test_compose_never_writes_into_godot_art(self):
        with self.assertRaises(g.SetError):
            g.step_compose(g.GolemSet(self.root, config()), out_dir=_art.GODOT_ART, log=lambda *_: None)

    def test_install_refuses_until_the_user_approved(self):
        with self.assertRaises(g.SetError) as raised:
            g.step_install(g.GolemSet(self.root, config()), log=lambda *_: None)
        self.assertIn("approved", str(raised.exception))

    def test_layers_hide_the_held_tool_behind_the_body_from_the_north(self):
        self.assertLess(g.layer_rank("south", "body"), g.layer_rank("south", "ram"))
        self.assertLess(g.layer_rank("north", "ram"), g.layer_rank("north", "body"))
        self.assertEqual(len(g.LAYERS["east"]), g.layer_rank("east", "wings"))

    def test_a_chassis_can_reorder_a_facing(self):
        gs = g.GolemSet(self.root, config(layers={"south": ["body", "wings"]}))
        self.assertEqual(["body", "wings"], gs.layers["south"])
        self.assertEqual(g.LAYERS["north"], gs.layers["north"])


if __name__ == "__main__":
    unittest.main()
