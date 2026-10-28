"""art-pipeline-design.md §5.4: the workflow is a function, so no server is needed. The node
class names are the ones the install proved on 2026-10-23 (ControlNet control-LoRA canny and
IP-Adapter Plus, the smoke test in §1.4)."""
import os
import subprocess
import sys
import tempfile
import unittest

try:
    from . import _art  # noqa: F401
except ImportError:
    import _art  # noqa: F401

import comfy_redraw as c

CONTROL = "control-lora-canny-rank128.safetensors"
IPADAPTER = "ip-adapter-plus_sdxl_vit-h.safetensors"
CLIP_VISION = "CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors"


def classes(wf):
    return sorted(node["class_type"] for node in wf.values())


def check_graph(test, wf):
    """Every link names a node that exists, and every node but the loaders is reachable."""
    for nid, node in wf.items():
        for value in node["inputs"].values():
            if isinstance(value, list) and len(value) == 2 and isinstance(value[0], str):
                test.assertIn(value[0], wf, f"node {nid} links to a missing node {value[0]}")


class WorkflowTests(unittest.TestCase):
    def test_the_base_workflow(self):
        wf = c.build_workflow("init.png", denoise=0.35, seed=7)
        self.assertEqual(sorted(["CheckpointLoaderSimple", "VAELoader", "LoraLoader", "LoraLoader",
                                 "CLIPTextEncode", "CLIPTextEncode", "LoadImage", "VAEEncode",
                                 "KSampler", "VAEDecode", "SaveImage"]), classes(wf))
        k = wf["8"]["inputs"]
        self.assertEqual(("lcm", 8, 1.5, 0.35, 7), (k["sampler_name"], k["steps"], k["cfg"], k["denoise"], k["seed"]))
        self.assertEqual(["41", 0], k["latent_image"])
        self.assertEqual(["4", 0], k["model"])
        self.assertEqual(1.2, wf["4"]["inputs"]["strength_model"])
        self.assertEqual("pixel-art-xl.safetensors", wf["4"]["inputs"]["lora_name"])
        self.assertNotIn("ControlNetLoader", classes(wf))
        self.assertNotIn("IPAdapterAdvanced", classes(wf))
        check_graph(self, wf)

    def test_a_mask_redraws_only_part_of_the_init(self):
        wf = c.build_workflow("init.png", denoise=0.55, mask_image="mask.png")
        self.assertEqual("SetLatentNoiseMask", wf["52"]["class_type"])
        self.assertEqual(["41", 0], wf["52"]["inputs"]["samples"])
        self.assertEqual("red", wf["51"]["inputs"]["channel"])
        self.assertEqual(["52", 0], wf["8"]["inputs"]["latent_image"])
        check_graph(self, wf)
        with self.assertRaises(c.RedrawError):
            c.build_workflow(txt2img=True, mask_image="mask.png")

    def test_txt2img_starts_from_an_empty_latent_at_full_denoise(self):
        wf = c.build_workflow(txt2img=True, width=512, height=768, denoise=0.5)
        self.assertIn("EmptyLatentImage", classes(wf))
        self.assertNotIn("VAEEncode", classes(wf))
        self.assertEqual(1.0, wf["8"]["inputs"]["denoise"])
        self.assertEqual((512, 768, 1), tuple(wf["7"]["inputs"][k] for k in ("width", "height", "batch_size")))

    def test_a_guide_adds_controlnet_on_both_conditionings(self):
        wf = c.build_workflow("init.png", guide_image="guide.png", control_model=CONTROL,
                              control_strength=0.8, control_end=0.8)
        apply = wf["33"]
        self.assertEqual("ControlNetApplyAdvanced", apply["class_type"])
        self.assertEqual(["5", 0], apply["inputs"]["positive"])
        self.assertEqual(["6", 0], apply["inputs"]["negative"])
        self.assertEqual(["30", 0], apply["inputs"]["image"])
        self.assertEqual((0.8, 0.0, 0.8), (apply["inputs"]["strength"], apply["inputs"]["start_percent"], apply["inputs"]["end_percent"]))
        self.assertEqual(["2", 0], apply["inputs"]["vae"])
        self.assertEqual(CONTROL, wf["32"]["inputs"]["control_net_name"])
        self.assertEqual(["33", 0], wf["8"]["inputs"]["positive"])
        self.assertEqual(["33", 1], wf["8"]["inputs"]["negative"])
        self.assertNotIn("CannyEdgePreprocessor", classes(wf))
        check_graph(self, wf)

    def test_a_canny_preprocess_sits_between_the_guide_and_controlnet(self):
        wf = c.build_workflow("init.png", guide_image="guide.png", control_model=CONTROL, guide_preprocess="canny",
                              width=512, height=768)
        self.assertEqual(["31", 0], wf["33"]["inputs"]["image"])
        self.assertEqual(768, wf["31"]["inputs"]["resolution"])

    def test_a_reference_adds_ipadapter_on_the_model(self):
        wf = c.build_workflow("init.png", reference_image="ref.png", ipadapter_model=IPADAPTER,
                              clip_vision=CLIP_VISION, ipadapter_weight=0.6)
        ip = wf["23"]
        self.assertEqual("IPAdapterAdvanced", ip["class_type"])
        self.assertEqual(["4", 0], ip["inputs"]["model"])
        self.assertEqual(["20", 0], ip["inputs"]["ipadapter"])
        self.assertEqual(["21", 0], ip["inputs"]["clip_vision"])
        self.assertEqual(["22", 0], ip["inputs"]["image"])
        self.assertEqual(0.6, ip["inputs"]["weight"])
        self.assertEqual("IPAdapterModelLoader", wf["20"]["class_type"])
        self.assertEqual(IPADAPTER, wf["20"]["inputs"]["ipadapter_file"])
        self.assertEqual("CLIPVisionLoader", wf["21"]["class_type"])
        self.assertEqual(["23", 0], wf["8"]["inputs"]["model"])
        self.assertEqual(["5", 0], wf["8"]["inputs"]["positive"])
        self.assertNotIn("ControlNetLoader", classes(wf))
        check_graph(self, wf)

    def test_both_together_form_one_graph(self):
        wf = c.build_workflow("init.png", guide_image="guide.png", control_model=CONTROL,
                              reference_image="ref.png", ipadapter_model=IPADAPTER, clip_vision=CLIP_VISION)
        k = wf["8"]["inputs"]
        self.assertEqual((["23", 0], ["33", 0], ["33", 1], ["41", 0]),
                         (k["model"], k["positive"], k["negative"], k["latent_image"]))
        self.assertEqual(1, classes(wf).count("ControlNetLoader"))
        self.assertEqual(3, classes(wf).count("LoadImage"))
        check_graph(self, wf)

    def test_a_guide_without_a_control_model_is_refused(self):
        with self.assertRaises(c.RedrawError):
            c.build_workflow("init.png", guide_image="guide.png")
        with self.assertRaises(c.RedrawError):
            c.build_workflow("init.png", reference_image="ref.png", ipadapter_model=IPADAPTER)

    def test_candidates_sweep_denoise_over_seeds(self):
        plan = c.candidates([0.35, 0.5], 2, seed_base=10)
        self.assertEqual([(0.35, 10), (0.35, 11), (0.5, 10), (0.5, 11)], plan)
        self.assertEqual([(1.0, 10), (1.0, 11)], c.candidates([0.35, 0.5], 2, seed_base=10, txt2img=True))


class RefusalTests(unittest.TestCase):
    def test_it_refuses_godot_art(self):
        with self.assertRaises(c.RedrawError):
            c.validate_out(os.path.join(_art.GODOT_ART, "raw"))
        with tempfile.TemporaryDirectory() as scratch:
            c.validate_out(scratch)

    def test_the_command_line_refuses_without_a_server(self):
        out = os.path.join(_art.GODOT_ART, "never_written")
        result = subprocess.run([sys.executable, os.path.join(_art.ART, "comfy_redraw.py"),
                                 "--txt2img", "--out", out, "--dry-run"], capture_output=True, text=True)
        self.assertEqual(2, result.returncode)
        self.assertFalse(os.path.exists(out))
        with tempfile.TemporaryDirectory() as scratch:
            result = subprocess.run([sys.executable, os.path.join(_art.ART, "comfy_redraw.py"),
                                     "--txt2img", "--out", scratch, "--guide", "g.png"], capture_output=True, text=True)
            self.assertEqual(2, result.returncode)
            self.assertIn("--control-model", result.stderr)


if __name__ == "__main__":
    unittest.main()


class UploadNameTests(unittest.TestCase):
    def test_two_images_with_one_file_name_upload_under_two_names(self):
        a = c.upload_name("init", b"one image")
        b = c.upload_name("reference", b"another image")
        self.assertNotEqual(a, b)
        self.assertNotEqual(c.upload_name("init", b"one image"),
                            c.upload_name("init", b"another image"))

    def test_the_same_image_and_role_upload_under_one_name(self):
        self.assertEqual(c.upload_name("guide", b"x"), c.upload_name("guide", b"x"))
        self.assertTrue(c.upload_name("guide", b"x").endswith(".png"))
