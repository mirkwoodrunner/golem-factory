# Crates: local storage, and the pack

**Status: design, not built.** Written after the G10 playtest, for after the Godot cutover. Like
`cozy-automation-design.md`, it is written before the code and should be kept as written
afterwards, with corrections recorded rather than hidden.

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

## 3. The model: three kinds of store

| Store | Where | Who reaches it | Limit |
|---|---|---|---|
| **Crate** | one per depot, on its tile | golems on its neighbouring tiles; the player by hand | per good (§5 Q3) |
| **The pack** | on the player | the player only: building, claiming, ordering, hand-loading | none (§5 Q2) |
| **Pneumatic Post** (late game) | a crate wired to the pack | golems like any crate; its contents count as in the pack | per good, like a crate |

**The pack is what `FactoryStockpile` already is to the player**: every cost in the game is
paid from it today. What changes is that **golems lose access to it**. The id can even stay, but
its meaning narrows from "the factory's goods" to "what the player carries".

The late-game store is called a **Pneumatic Post** here as a placeholder: brass tubes carrying a
crate's contents to wherever the player is fits the clockwork look. The name is open (§5 Q5).

### What the player does with a crate

- **E at a crate takes everything into the pack.** One press, not one item per press: this is
  the cozy version of Satisfactory's inventory shuffle. The player becomes the factory's first
  truck, which is exactly the job the Pneumatic Post later takes off them.
- **Shift+E (or a second verb) puts goods from the pack into a crate**, so the player can
  hand-feed a line: Coal into a coker's crate, say. Which goods it puts in is §5 Q4.
- **Labels stay** (`cozy-automation-design.md` §1). A labelled crate takes and gives only its
  good, as today. They now mean more, because a crate is a real place.

## 4. What changes, system by system

| System | Today | With crates |
|---|---|---|
| `PlaceableDepot` | publishes `FactoryStockpile` | publishes its own buffer, id from the building (e.g. `Crate(x,y)`) |
| `StorageBuffer` | stockpile unlimited, others 100/good | crates capped per good (§5 Q3). §1.2's per-good rule is what keeps a full crate of Slag from blocking Iron Plate, and it carries over unchanged |
| Hand harvest (`PlayerInteractor`) | into the stockpile | into the pack (same buffer, same code) |
| Building / station / floor / Assembly Line / truck orders / hand-crank / boiler hand-load | spend the stockpile | spend the pack: **no change**, they already spend `FactoryStockpile` |
| Golem `Haul` / `Push` | into the shared stockpile through any depot | into that depot's crate |
| Freight mast / Zeppelin | delivers into the stockpile | delivers into **its own crate**, which the player or a golem empties |
| Demolishing a crate | refunds its cost | refunds its cost **and its contents**, into the pack. The pack has no limit, so `RefundWouldFit` is always true for contents and the full-refund rule (root `CLAUDE.md`) holds |
| Ledger rates (`TechTreeReadout`) | measured on the stockpile | measured on golem output (`GolemCompletedEvent`), since goods no longer pass through one place |
| `TechTreeProgressLedger.HasItem` | "has this good ever existed" | unchanged in meaning; it must hear about goods arriving in crates too, not only the pack |
| Save | buffers by id | crate buffers by id, saved with their building; loads key by cell |
| HUD inventory | the stockpile | the pack. A crate needs its own contents readout (new: today a depot shows nothing, since it has nothing of its own) |

The column that matters: **most of the economy does not change.** Everything that charges the
player already charges `FactoryStockpile`. The change is concentrated in the depot, the two golem
verbs that touch it, and the places goods arrive from outside (mast, Zeppelin).

## 5. Open questions, with a recommendation each

1. **Does the first golem push into a crate or into the pack?** Recommendation: a crate, from the
   start. Chapter 1 then teaches "E at the crate collects", which is the verb the whole game now
   runs on, at the moment it is simplest.
2. **Is the pack limited?** Recommendation: no. A limited pack makes the player's own inventory
   a puzzle on top of the factory's, and that is not this game's puzzle. The pressure to automate
   comes from walking, not from slots.
3. **Crate size.** Recommendation: 50 of each good. Big enough that a crate collected once a
   minute or so does not stall a Scavenger (about 1 Scrap per second), small enough that a
   converter's input crate empties and makes the player feed it. Tuning, not derived.
4. **What does "put into a crate" put?** Recommendation: a labelled crate takes its good from the
   pack, up to the crate's room. An unlabelled crate opens a small picker of the goods in the pack.
   Without the picker a player cannot hand-feed an unlabelled crate at all.
5. **When does the Pneumatic Post unlock, and what does it cost?** Recommendation: the tier where
   Brass and Casing exist (chapter 8, near the freight mast), so the early game is fully manual
   and the post arrives once walking between lines has become the bottleneck. A Ledger node
   marked `IsPlanned` until it ships, per the root `CLAUDE.md`.
6. **Do building costs also draw from nearby crates?** Recommendation: no. One wallet, the pack,
   keeps "can I afford this?" a single number. Proximity spending is a second rule the HUD would
   have to explain.

## 6. Invariants it must not break

- **No item is lost.** Collecting moves everything that fits (the pack always fits). Demolition
  refunds contents. A load never clamps a crate down to its cap (as `SteamBoiler.SetCoke` keeps
  above-cap Coke), so a save from before crates keeps every good it had.
- **The rigid-stall rule holds.** A golem behind an empty crate stalls "no input in Crate(x,y)",
  and in front of a full one "Crate(x,y) full". Those reasons already exist; no new
  `StallReason` is needed, and it stays append-only.
- **Demolition is still free** (root `CLAUDE.md`): contents come back, so moving a crate costs a
  collect and a re-place, nothing more.
- **The id-routed fork is untouched.** Golems with no spatial endpoint holder route by bare
  buffer id, as `IdRoutedDemoRegressionTests` pins.

## 7. Migrating a save

An existing save has one stockpile and depots that hold nothing of their own. On load, the
stockpile becomes the pack, contents intact, and every crate starts empty. Nothing is lost and
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

1. Crate buffers on depots, the collect verb, the pack in the HUD. Tests: a converter behind a
   crate stops when the crate is empty; collect moves everything; demolition refunds contents.
2. Mast and Zeppelin into their own crates; Ledger rates from golem output.
3. Save format and migration.
4. Put-into-a-crate, with the picker.
5. Guide rewrite and a playtest.
6. Pneumatic Post, as its own pass.

Steps 1–3 are one PR's worth: the game is playable, if harder, after them.
