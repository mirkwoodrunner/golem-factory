Folder rules for the art, its generators and AI image generation. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths in the root's engine sections are relative to `godot/`; paths here are from the repo
root unless they say otherwise. The project font rule stays in the root.

# Art

- **The art lives in `godot/art/`**, including `UI/…`. The generators in `Tools/Art/` write
  here. Their root comes from `Tools/Art/art_paths.py`, and `--out-root <dir>` redirects any
  of them.
- **`python Tools/Art/verify_art.py`** regenerates everything into a scratch folder and
  compares it with `godot/art/`. Exit 1 means a generated sprite drifted. It also lists the
  sprites that are authored rather than generated (walk frames, item and chassis art, the
  Steampunk pack), so "not generated" is never mistaken for "verified".
- Texture filtering is nearest-neighbour project-wide. Standing sprites are placed with
  `GridConversions.StandOnCell`, Unity's BottomCenter pivot. The five pipe tiles are
  centre-pivoted (they rotate in quarter turns). 9-slice UI uses `StyleBoxTexture` margins.
  Those are set where each sprite is drawn, in G5 and G7, not by an import pass.

# AI image generation (optional, personal MCP)

If an image-generation MCP server is configured in the user's global Claude config, use it for
**throwaway placeholders only** (UI mockups, icon ideas, concept sketches), never for shipped art.

- Save results to `./public/images/` with a descriptive kebab-case name (`brass-gear-icon.png`).
  Create the folder if it is missing. Do not write into `godot/art/`:
  `Tools/Art/` generators own those, and `verify_art.py` expects them to reproduce exactly.
- Prefer a tool that returns a URL, then download it with `curl -o`. Base64 tool results cannot be
  saved to disk without extra steps.
- Match the game's look in the prompt: cozy top-down pixel art, brass and mahogany palette. Ask for
  a square size (64, 128 or 256 px) so a mockup lines up with the PPU 64 grid.
- Say which file you wrote and the prompt used. Do not wire a generated image into a scene unless
  asked.
- **One exception, at the user's call: the Clock Tower.** Its six sprites in `godot/art/`
  (`clock_tower_site`, `clock_tower_stage0`–`4`) are a 192×640 ComfyUI redraw, composited by
  hand, and they replaced `generate_clock_tower_art.py`'s 192×448 pictures. That generator now
  refuses to write into `godot/art/`, `verify_art.py` lists the six as hand-replaced, and
  `ClockTowerArt.CanvasHeight` is 640. Any change to the tower's art has to keep all three
  in step.
- **And a second: the five market carts** (`stall_scrap`, `_coal`, `_copper_ore`,
  `_zinc_ore`, `_aether`). They were redrawn in ComfyUI from hand-drawn drafts, then softened
  to match the props: the near-black ink became warm brown outlines, and the wood was shifted
  toward the props' red-brown. They keep the generator's 96×120 canvas and its pivot. Their
  wheels stand on row 117, so `SpritePivots` is unchanged. `generate_topdown_environment.py`
  skips those five names when writing to `godot/art/` (`HAND_REPLACED`), and `verify_art.py`
  lists them as hand-replaced. **Copper ore is green**, as malachite, on the cart and on the
  `item_copper_ore` good alike. The good is still generated (`generate_placeholder_art.py`),
  so a change to one has to be matched in the other.
