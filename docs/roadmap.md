# Golem Factory: roadmap after the port

Written 2026-10-18, after the Godot conversion finished. It plans the work that takes the
prototype to a finished game, in order, and records the decisions taken for it. The backlog of
smaller items stays in `docs/open-items.md`.

## Decisions (2026-10-18)

| # | Question | Decision |
|---|---|---|
| 1 | Scope | **A polished 4–5 hour campaign first**, the size `progression-design.md` is built for, then endless play after the Clock Tower. No new tiers of goods: that re-opens tuning checked twice. |
| 2 | Art direction | **Top-down is final.** The isometric call in `digital-design.md` was an older version. |
| 3 | Who makes the art | **ComfyUI plus hand cleanup**, the route the Clock Tower and the carts took. Whether anything is commissioned is the user's call, made separately from this plan. |
| 4 | The first Workbench program | **A hint of the next drag**, never pre-filled cards. Programming is the core verb; the player makes every drag. |
| 5 | Waiting versus stuck | **A golem waiting for input gets a quiet grey idle mood.** Red is for failures the player has to fix. |
| 6 | `Straining` | **Kept, shown on hover only**, not as a badge, until a playtest says otherwise. |
| 7 | The Overclocker's identity | **Decided after a full playthrough.** If players never choose it over a Hauler, it gets a second exclusive card. The speed aura stays cut. |
| 8 | Lens at 3 Glass | **Tried once in the tuning spreadsheet**, kept only if stage 3 still balances. |
| 9 | Audio | **Placeholders for now.** Commissioning is the user's call, as with art. |
| 10 | Gamepad | **Mouse and keyboard first**, with input built so a controller can be added later. |
| 11 | Creative Mode | **Offered from the main menu.** |
| 12 | Depth additions | **City contracts first**, then endless play after the tower. A second region and selling golems wait. |
| 13 | Contract deadlines | **Open-ended, with a bonus for finishing quickly.** A hard deadline fights the cozy tone. |
| 14 | Release | **Not planned now.** Only the parts that shape development are kept (Phase 9). |

The crate questions were answered the same day; see `docs/crates-design.md` §5.

## Rules every phase keeps

The settled calls in the root `CLAUDE.md` hold throughout: full refund on demolish, free patents
and no Focus meter, rigid linear golems that stall rather than adapt, append-only enums, rules in
Core with nodes that only draw, and a new system on `SandboxWorld` with a scenario beside it.
`docs/cozy-automation-design.md` §0's invariants hold for anything touching golems, endpoints or
text.

## Phase 1: Make logistics matter (crates)

Build `docs/crates-design.md` in its §9 order. Steps 1–3 are one PR: local crates, the capped
pack, the collect verb, deliveries into their own crates, Ledger rates from golem output, and
save migration. Then the two inventory screens, the guide rewrite, and the Pneumatic Post.

**Done when:** a badly laid-out factory runs visibly worse than a good one, and belts are built
because a line needs them.

## Phase 2: Onboarding and readability

The playtest timings point here: the first program took 8 minutes, turning with R took 4½, and
the stall badge read "vaguely".

- The Workbench shows a hint of the next drag (decision 4).
- Waiting golems go grey; red means stuck, and hovering a stuck golem names the tile and the good
  (decision 5). `Straining` moves to hover (decision 6).
- Source and target arrows always show on a hovered golem, so facing reads without R.
- The depot label picker from `open-items.md`, folded into the crate screen.

**Done when:** a new player finishes chapters 1–4 without the playtest kit in under 15 minutes,
the manual-era budget.

## Phase 3: Tuning and a full playthrough

- Play chapters 1–10 to the Clock Tower without the kit.
- Tune in this order: the boiler fuel ratio (`progression-design.md` §12 says first), market
  prices and truckload size, then the other invented numbers, in a spreadsheet beside the play.
  Try Lens at 3 Glass (decision 8). No price may pass the pack's 100-per-good cap.
- Profile the endgame at about 96 golems and 500 belt items. If it doesn't hold, cut stage 4's
  Conduit demand, as the design says.
- Decide the Overclocker (decision 7).

**Done when:** the playtest report shows a complete run with no step anyone stalled on, and the
frame budget holds at endgame.

## Phase 4: Art

- **A style bible first**: palette (brass, mahogany, the warm brown outlines of the cart redraw),
  outline rules, light direction, pixel density at PPU 64, animation frame counts.
- **Replace in order of screen time**: golems (idle, work, stall and carry frames), floor and
  walls, buildings, the Workbench (lever housing, display face, card faces), goods icons, the HUD.
- Each set goes the Clock Tower's way: ComfyUI redraw, hand cleanup, `HAND_REPLACED` in its
  generator, listed in `verify_art.py`, and `CLAUDE.md`'s art section updated.
- **Lighting**: sconces that light the floor around them, a warm room tint, steam that glows.

## Phase 5: Audio

The game has no sound today. Placeholders throughout (decision 9), built so a final asset drops
into the same slot.

- Ambience: workshop hum, the ticking clock, steam hiss rising with boiler load.
- Work sounds per chassis, synced to `GolemWorkMotion`.
- UI: card snap, the Engage Gears lever, the patent stamp, a claim, a stall chime.
- Music: a calm score that adds layers as the factory grows, and a Clock Tower fanfare.
- Mix rules: a cap on copies of one sound at once, and panning by screen position, so a
  100-golem factory doesn't turn to noise.

## Phase 6: Game feel

Steam puffs, sparks and build dust; a small squash on placing and demolishing; goods that pop
into a crate; camera zoom levels and a smooth pan to alerts; a Clock Tower stage that plays as an
event (camera to the tower, the bell, the reveal); Workbench cards that land with weight and a
tape ticker that prints.

## Phase 7: The game around the game

- A title screen over a live workshop, save slots with thumbnails, autosave, Continue.
- Settings: audio buses, display and UI scale, rebindable keys (actions are already registered in
  code). Mouse and keyboard only; input is kept controller-ready (decision 10).
- Accessibility: colour-blind-safe card colours (teal against copper is a risky pair), text
  size, reduced motion.
- A factory overview: map or overlay, production graphs from the Ledger's rates, an alerts
  history.
- Creative Mode on the main menu (decision 11).

## Phase 8: Depth

Each addition gets its own design doc, like `crates-design.md`, with its invariants written
first. Additions grow along the game's identity (rigid, local, deterministic) rather than copying
Factorio's breadth.

1. **City contracts** (decision 12): requests that fill the gaps between tower stages, and give
   the Artificer's flexible half of the tabletop design a job. Open-ended, with a bonus for
   speed (decision 13).
2. **Endless play after the tower** (decision 1).

Waiting: a second map region, and selling golems to the city.

## Phase 9: What release asks of development

Release itself isn't planned (decision 14). These shape the code, so they are kept in mind
throughout rather than retrofitted:

- **Text kept in data**, not strings in code, so a translation is a data change. The font's
  glyph rule (`font-glyphs` scenario) already constrains characters.
- **Save files that can move between machines**: one folder, versioned, no absolute paths. A
  store's cloud save would need exactly that.
- **Errors captured to a file**, which the playtest report already does.
