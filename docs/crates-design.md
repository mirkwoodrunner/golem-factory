# Crates: local storage, and the pack

**Status: design, not built. §5's questions are answered (2026-10-18).** Written after the G10
playtest, for after the Godot cutover. Like `cozy-automation-design.md`, it is written before the
code and should be kept as written afterwards, with corrections recorded rather than hidden.

The answers changed one recommendation: **the pack is limited** (§5 Q2). The sections below are
updated to match, and §5 keeps each original recommendation beside the decision.

## 1. The problem

Every depot is a window onto one store, `FactoryStockpile`, and that store has no limit
(`PlaceableDepot.bufferId`, `SandboxSetup.ApplyBufferPolicy`). Three things follow, and the
playtest hit all three:

- **A golem anywhere reaches every good everywhere.** A plate-maker behind any depot hauls from
  the whole factory's Scrap, and converts all of it.
- **Nothing downstream ever fills**, so nothing ever stalls on "full" and tells a converter to
  stop. (The boiler half of this was fixed on its own: `SteamBoiler.CokeCapacity`.)
- **Layout does not matter.** Belts, carriers and unloaders exist because the guide asks for them,
  not because a factory needs them. A logistics game where logistics are optional has lost its
  verb.

## 2. Decisions (the user's)

1. **Crates are local.** A depot holds its own goods. A golem behind it reaches that crate and
   nothing else.
2. **A store reachable from anywhere is a late-game unlock**, in the role Satisfactory's
   Dimensional Depot plays: something you earn by building a factory, not the ground the factory
   stands on.
3. **The player's inventory is part of the game.** The pack is limited, as in other automation
   games (§5 Q2).

## 3. The model: three kinds of store

| Store | Where | Who reaches it | Limit |
|---|---|---|---|
| **Crate** | one per depot, on its tile | golems on its neighbouring tiles; the player by hand | 50 per good (§5 Q3) |
| **The pack** | on the player | the player only: building, claiming, ordering, hand-loading | 100 per good (§5 Q2) |
| **Pneumatic Post** (late game) | any number of Post crates, all sharing one store | golems like any crate; the player's spending, after the pack | per good, its own (§5 Q5) |

**The pack is what `FactoryStockpile` already is to the player**: every cost in the game is
paid from it today. What changes is that **golems lose access to it**, and that it is limited.
The id can even stay, but its meaning narrows from "the factory's goods" to "what the player
carries".

The late-game store is called a **Pneumatic Post**: brass tubes carrying a crate's contents to
wherever the player is fits the clockwork look. The name may still change (§5 Q5).

### What the player does with a crate

- **E at a crate takes what fits into the pack.** One press, not one item per press: this is
  the cozy version of Satisfactory's inventory shuffle. What doesn't fit stays in the crate, as a
  golem's partial haul does. The player becomes the factory's first truck, which is exactly the
  job the Pneumatic Post later takes off them.
- **Goods go from the pack into a crate through the inventory screens** (§5 Q4), so the player
  can hand-feed a line: Coal into a coker's crate, say.
- **Hand-harvesting with a full pack** greys out the prompt, which says "pack full".
- **Labels stay** (`cozy-automation-design.md` §1). A labelled crate takes and gives only its
  good, as today. They now mean more, because a crate is a real place.

## 4. What changes, system by system

| System | Today | With crates |
|---|---|---|
| `PlaceableDepot` | publishes `FactoryStockpile` | publishes its own buffer, id from the building (e.g. `Crate(x,y)`) |
| `StorageBuffer` | stockpile unlimited, others 100/good | crates capped per good (§5 Q3). §1.2's per-good rule is what keeps a full crate of Slag from blocking Iron Plate, and it carries over unchanged |
| Hand harvest (`PlayerInteractor`) | into the stockpile | into the pack (same buffer, same code); refused at the pack's cap |
| Building / station / floor / Assembly Line / truck orders / hand-crank / boiler hand-load | spend the stockpile | spend the pack: **no change**, they already spend `FactoryStockpile` |
| Golem `Haul` / `Push` | into the shared stockpile through any depot | into that depot's crate |
| Freight mast / Zeppelin | delivers into the stockpile | delivers into **its own crate**, which the player or a golem empties |
| Demolishing a crate | refunds its cost | refunds its cost **and its contents**, into the pack. `RefundWouldFit` now asks about the contents too: if they don't fit, the wrecking bar refuses with "pack full of ___", and the full-refund rule (root `CLAUDE.md`) holds |
| Ledger rates (`TechTreeReadout`) | measured on the stockpile | measured on golem output (`GolemCompletedEvent`), since goods no longer pass through one place |
| `TechTreeProgressLedger.HasItem` | "has this good ever existed" | unchanged in meaning; it must hear about goods arriving in crates too, not only the pack |
| Save | buffers by id | crate buffers by id, saved with their building; loads key by cell |
| HUD inventory | the stockpile | the pack, with its cap |
| Inventory screens (new) | none | **two separate screens**: the pack's, and a crate's. At a crate both show, side by side, and goods move between them (§5 Q4). Today a depot shows nothing, since it has nothing of its own |

The column that matters: **most of the economy does not change.** Everything that charges the
player already charges `FactoryStockpile`. The change is concentrated in the depot, the two golem
verbs that touch it, and the places goods arrive from outside (mast, Zeppelin).

## 5. Questions, answered (2026-10-18)

Each question keeps its original recommendation, then the user's decision.

1. **Does the first golem push into a crate or into the pack?** Recommendation: a crate, from the
   start. Chapter 1 then teaches "E at the crate collects", which is the verb the whole game now
   runs on, at the moment it is simplest.
   **Decided: a crate.**
2. **Is the pack limited?** Recommendation: no. A limited pack makes the player's own inventory
   a puzzle on top of the factory's, and that is not this game's puzzle. The pressure to automate
   comes from walking, not from slots.
   **Decided: limited, 100 of each good.** The player's inventory is part of automation games;
   the number can be tuned later. What follows from the limit, all agreed:
   - Collecting takes what fits; the rest stays in the crate.
   - Demolishing a crate whose contents won't fit is refused, with "pack full of ___".
   - Hand-harvesting with a full pack greys out the prompt, which says "pack full".
   - A migrated save keeps any good above 100 (§7): it can be spent, not added to, until it is
     under the cap.
   - No price in `data/` asks for 100 or more of one good today (checked 2026-10-18), so nothing
     becomes unaffordable. A tuning pass that raises a price past the cap must not.
3. **Crate size.** Recommendation: 50 of each good. Big enough that a crate collected once a
   minute or so does not stall a Scavenger (about 1 Scrap per second), small enough that a
   converter's input crate empties and makes the player feed it. Tuning, not derived.
   **Decided: 50, as a starting point.**
4. **What does "put into a crate" put?** Recommendation: a labelled crate takes its good from the
   pack, up to the crate's room. An unlabelled crate opens a small picker of the goods in the pack.
   Without the picker a player cannot hand-feed an unlabelled crate at all.
   **Decided: inventory screens, kept separate.** The pack and a crate each get their own
   inventory interface. At a crate both show, side by side, and goods move between them. This
   replaces the picker.
5. **When does the Pneumatic Post unlock, and what does it cost?** Recommendation: the tier where
   Brass and Casing exist (chapter 8, near the freight mast), so the early game is fully manual
   and the post arrives once walking between lines has become the bottleneck. A Ledger node
   marked `IsPlanned` until it ships, per the root `CLAUDE.md`.
   **Decided: the Satisfactory model, unlocked in chapter 8.** The original model ("its contents
   count as in the pack") breaks with a 100-per-good pack: a Post line would fill the pack and
   stall. Instead:
   - Every Post crate opens onto **one shared Post store**, separate from the pack, with its own
     cap per good (placeholder: 200).
   - **Spending takes from the pack first, then the Post store.** Both are the player's wallet,
     so this does not reopen Q6.
   - Unlocks with Brass and Casing (chapter 8, near the freight mast); a Ledger node marked
     `IsPlanned` until it ships.
   - Cost: some Brass and Casing, set in the tuning pass. Name: Pneumatic Post, unless a better
     one turns up.
6. **Do building costs also draw from nearby crates?** Recommendation: no. One wallet, the pack,
   keeps "can I afford this?" a single number. Proximity spending is a second rule the HUD would
   have to explain.
   **Decided: no; costs come from the pack** (and, once it exists, the Post store, per Q5).

## 6. Invariants it must not break

- **No item is lost.** Collecting moves what fits and leaves the rest in the crate. Demolition
  refunds contents, or is refused when they don't fit. A load never clamps a crate or the pack
  down to its cap (as `SteamBoiler.SetCoke` keeps above-cap Coke), so a save from before crates
  keeps every good it had.
- **The rigid-stall rule holds.** A golem behind an empty crate stalls "no input in Crate(x,y)",
  and in front of a full one "Crate(x,y) full". Those reasons already exist; no new
  `StallReason` is needed, and it stays append-only.
- **Demolition is still free** (root `CLAUDE.md`): contents come back, so moving a crate costs a
  collect and a re-place, nothing more.
- **The id-routed fork is untouched.** Golems with no spatial endpoint holder route by bare
  buffer id, as `IdRoutedDemoRegressionTests` pins.

## 7. Migrating a save

An existing save has one stockpile and depots that hold nothing of their own. On load, the
stockpile becomes the pack, contents intact even above the 100 cap, and every crate starts empty.
A good above the cap can be spent but not added to until it is under 100. Nothing is lost and
nothing is duplicated; the player's golems simply find their crates empty until the player or a
line fills them. The first session after the change should say so once, in the guide or a toast.

## 8. The guide

Every chapter assumes the shared stockpile. Roughly:

- **Chapter 1** gains "collect from the crate" (E) right after the first golem's first cycle.
- **Chapters 2–4**: converters (the Presser's plates, the coker) need their input crate fed: by
  hand at first, which is the lesson, then by a golem.
- **Chapter 5's carrier and chapter 6's belts and unloader** become necessary rather than taught
  for their own sake. This is the point of the change.
- **The Pneumatic Post** gets its own step where it unlocks.

Steps detect state rather than order (`TutorialGuide`), so most step checks survive. Their text
and the playtest kit's fast-forwards are what change.

## 9. Order of work

1. Crate buffers on depots, the collect verb, the capped pack in the HUD. Tests: a converter
   behind a crate stops when the crate is empty; collect moves what fits and leaves the rest;
   demolition refunds contents, and is refused when they don't fit; harvest stops at the cap.
2. Mast and Zeppelin into their own crates; Ledger rates from golem output.
3. Save format and migration, including a pack above its cap.
4. The two inventory screens, pack and crate, with transfers between them.
5. Guide rewrite and a playtest.
6. Pneumatic Post, as its own pass.

Steps 1–3 are one PR's worth: the game is playable, if harder, after them.
