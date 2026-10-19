# Onboarding and feedback: the hint, grey and red, and hover

**Status: approved 2026-10-19, with every recommendation in §11. Built: §3, §4 and §13 (§14 records the corrections), then §2 and §9 (§15).** Phase 2 of
`docs/roadmap.md`, from decisions 4, 5 and 6. Like `crates-design.md`, it is written before the
code and kept as written afterwards, with corrections recorded rather than hidden. Its sibling,
`docs/guide-chapters-design.md`, rewrites the guide's texts around what this one builds.

## 0. Invariants

A pass that touches the hint, the moods, hover or the guide's program steps keeps all of these.
Each gets a test in `Core.Tests` when it is built.

1. **The player makes every drag.** The hint never fills a socket, never moves a card, never
   sets a dial or a good, and never pulls the lever. Evaluating it does not change the draft
   (`WorkbenchSession.Version` is unchanged). It may scroll the vault so a card is in view.
2. **One answer per question.** The hint and the kit read one plan per program step
   (`TutorialStep.Program`, §2.1). A golem's colour, badge, the alerts strip and the hover card
   read one mood (`GolemEntity.Mood`, through `GolemMoodRules.Classify`). The carry preview, the
   proximity focus and hover draw source and target tiles through one path (`GolemNode`'s tiles,
   §4.2).
3. **`Mood` stays a read-only classification.** Nothing writes it and the tick loop never asks.
   Feedback is added by widening the mood, never by a second badge (root `CLAUDE.md`).
4. **Red means the player must act.** A golem drawn red or blue has stopped and nothing in the
   factory will restart it. A golem the factory will restart on its own is grey. The judgement is
   `StallTriage` (§3), never a colour picked from a `StallReason` alone.
5. **Append-only enums.** `GolemMood.Waiting` is appended (value 6). No `StallReason`,
   `AppendageActionType` or `InteractionKind` is added or renumbered.
6. **Opt-in by null** (`cozy-automation-design.md` §0.6). A golem with no stall-facts source
   (`GolemEntity.ConfigureStallFacts` never called: id-routed golems, bare tests) classifies
   exactly as today: every non-steam stall is `Stalled`, red.
7. **Rules in Core, on `SandboxWorld`.** The hint, the triage, the facts probe, the hover pick and
   every hover sentence are engine-free and testable without a scene. Nodes draw and forward the
   pointer.
8. **Text keeps the font rule** (root `CLAUDE.md`): printable Latin-1 plus → ≥ █ ░, `·` as the
   separator. `cozy-automation-design.md` §0.8's "no arrows" predates the font and is superseded
   by that rule.
9. **Determinism is untouched.** None of this is simulation state. Classification and hover run
   at frame rate on the presentation side; no tick-loop code changes, except the collecting Haul
   (§13), a rule change the user decided, deterministic like every other step.
10. **A Haul collects until it holds its batch** (§13). It takes only what the golem still needs
    to hold one batch of the good, stays on its step while short, and advances once it holds the
    batch. A golem never takes more than a batch of a hauled good; any extra stays in the source.

## 1. The problem

The last playtest timed the first program (step `program`) at **8 minutes** and turning a golem
with R (step `turn`) at **4½**, and answered the `badge` question "Vaguely". Phase 2 is done when
a new player finishes chapters 1–4 without the playtest kit in **under 15 minutes**.

Three causes, one fix each:

| Finding | Cause, read from the code and the texts | Fix |
|---|---|---|
| First program 8 min | The step names three cards and three sockets in one sentence; the vault is a list; nothing on screen says which card is next or that ENGAGE commits | §2: the next drag pulses, card and socket |
| Turning 4½ min | `turn` says "stand next to it and press R". R turns the *nearest* golem, and chapter 4's new Scavenger stands diagonal to the first, so R can turn the wrong one. Facing shows only as a small arrow on the body | `guide-chapters-design.md`: turn while carrying (R then turns only the carried golem); §4: arrows on the source and target tiles |
| Badge read "vaguely" | A badge is a short sentence in red, and every waiting golem wears one too (`godot-conversion-plan.md`, chapter 6 frames: a carrier with "no Slag" yet, an unloader "waiting on Belt(0,-12)#3") | §3: waiting goes grey; §4: hovering a red golem names the tile, the good and the fix |

The second cause is a reading of the code, not a measurement: the playtest didn't record which
golem turned. The Phase 2 playtest asks (`testscript/phase-2-playtest.md`).

## 2. The Workbench's next-drag hint (decision 4)

### 2.1 What is hinted: a program plan

A **`ProgramPlan`** (new, `Core/Golems`) is a program described as data: a logic core's name and
an ordered list of slots, each a card name, a good (or null) and a batch size.

```
Golems/ProgramPlan.cs            (new, engine-free)
    sealed class ProgramPlan
        string LogicCore                      "AlwaysOnCore"
        IReadOnlyList<PlanSlot> Slots         ("HaulScrap", "Scrap", 2), ("HaulScrap", "Coke", 1), ...
        Func<GolemProgram, bool> Matches      the step's check: exact by default
    readonly struct PlanSlot { string Card; string Good; int Quantity; }
```

**Where plans come from: the guide's program steps.** `TutorialStep` gains `Program`
(a `ProgramPlan`) and `ProgramTarget` (a `Func<GolemEntity, bool>`: is this the golem the step
means). Nine steps carry one: `program`, `program-presser`, `program-coker`, `stamp`,
`program-smelter`, `program-carrier`, `scav3`, `unloader` and `program-zeppelin`
(`guide-chapters-design.md` §3 lists each plan). Their targets are the golems the kit already
programs (`Pressers.FirstOrDefault()`, `NewestPresser`, the `scav2`/`extractor`/`unloader` roles,
and so on).

**The plan is the kit's action too.** Each of those steps' kit entry becomes
`PerformPlan(step)`: the same cards, goods and batch sizes `Program`/`ProgramSlots` pass today,
read from the plan instead of repeated in `TutorialGuide.Kit.cs`. **The plan's `Matches` is the
step's `Done` predicate**, moved onto the plan (`IsProgrammed`, `IsIronPresser`, `IsCoker`,
`IsSmelter`, `IsCarrier`, the unloader's and the Zeppelin's checks), so the hint stops exactly
when the step would pass. Lenient checks stay lenient: `IsSmelter` accepts its two Hauls in
either order, so the hint stops for either order too.

**`TutorialGuide.PlanFor(GolemEntity golem)`** returns the plan for this golem: the first step,
from the current one to the end of its chapter, whose `ProgramTarget` accepts it. Looking ahead
matters because the station opens the Workbench on a new golem at once: building the Brass
Presser (`presser`) opens it two steps before `program-presser`. With the guide dismissed or
finished, `PlanFor` returns null. Chapter 8's goal steps and `tower-feed` carry no plan: there the
program is the player's to design.

Why not the other sources the brief offered:

- **`TutorialStep.Card`** names a card to *claim* on the Assembly Line, one per step. A program is
  a core and up to six cards in order, with goods and dials.
- **The golem's role** (`RoleOpenings`) covers four golems and says nothing about cards.
- **A rule from the empty sockets alone** can say *where* (TRIGGER, then STEP 1) but never
  *which card*. It is kept as the fallback (§2.3), not the hint.

### 2.2 The rule

```
UI/WorkbenchHint.cs              (new, engine-free static)
    WorkbenchHintTarget Next(IWorkbenchDraftView draft, ProgramPlan plan, bool committedMatches)
    WorkbenchHintTarget Fallback(IWorkbenchDraftView draft, bool targetHasRunnableProgram)
    float PulseAlpha(float seconds)            0.35..0.95, 1.2 s period (TUNING)

    readonly struct WorkbenchHintTarget
        WorkbenchHintKind Kind                 None, VaultCard, GoodPicker, QuantityDial, Lever
        string Card                            the vault card's name (VaultCard)
        WorkbenchZone Zone                     the socket it goes to (VaultCard, GoodPicker, QuantityDial)
        int Delta                              +1 or -1 (QuantityDial)
```

`WorkbenchSession` implements `IWorkbenchDraftView` (the draft core, each socket's card, good and
batch size, the chassis's slot count) and exposes `Hint`, evaluated on read. `Next` walks the plan
in order and returns the first thing that differs:

1. **The trigger.** `DraftLogicCore` isn't the plan's core: the core's vault card, to TRIGGER.
2. **Each slot `i`, in order:**
   - socket `i` doesn't hold the plan's card: that vault card, to STEP `i+1` (dropping replaces
     whatever is there, so a wrong card needs no "drag it out" hint);
   - the card is right, its good isn't: the good picker on STEP `i+1`;
   - the good is right, its batch size isn't: the dial on STEP `i+1`, `Delta` toward the plan.
3. **A socket past the plan holds a card:** that socket, as a VaultCard target with `Card` null,
   which draws as "drag this out" (§2.4).
4. **The draft matches** (`plan.Matches` over the draft): the lever, unless the golem's committed
   program already matches (`committedMatches`), in which case `None`.

Socket-by-socket rather than all cards first: a card arrives with its own default good and
batch, so the good and the dial are fixed while the player's eye is still on that socket.

### 2.3 When it shows, and when it stops

- **With a plan** (`PlanFor(TargetGolem)` non-null): from the moment the Workbench opens on that
  golem until `Next` returns `None`, i.e. until the step's own check passes on the committed
  program. Retargeting re-asks `PlanFor`.
- **Without a plan, on a golem that has never been told what to do** (`!HasRunnableProgram`):
  `Fallback` outlines TRIGGER while the draft has no core, then STEP 1 while it has no steps,
  then the lever once the draft has both. No vault card is outlined, because no rule knows which.
  This covers a player who skipped the guide.
- **Never** on a programmed golem without a plan: reprogramming is the player's design.
- F1 or Skip guide stop the guide's plans at once (the fallback still applies). The kit's
  fast-forward needs none: it writes the plan itself.

### 2.4 How it draws

`WorkbenchScreen` draws `Session.Hint`; it decides nothing.

- **A pulsing brass outline** (`HintOutline`, a `StyleBoxFlat` border, 3 px, colour
  `#FFCC6A`, alpha `WorkbenchHint.PulseAlpha(t)`) around two things at once: the hinted vault
  card and the target socket. One colour, two places, so the pair reads as "this, there".
- **The good picker and the dial** get the same outline on their arrow button (the `Delta`'s
  side for the dial).
- **The lever** gets the outline on `EngageLever`.
- **A card to drag out** (step 3) gets the outline in a dimmer red-brown (`#C0603A`) on its
  socket.
- **No ghost card, no arrow across the screen, no pre-filled socket.** The outline is a frame
  around controls that already exist, so the screen's layout numbers (`Ugui.Place`) don't
  change.
- **The vault scrolls** the hinted card into view once, when the hint moves to it
  (`ScrollContainer.EnsureControlVisible`), never while the player is scrolling.
- **While the hinted card is in hand**, its vault outline hides; the socket's stays.

### 2.5 Living with the socket highlights and the loop labels

During a drag `BeginCardDrag` tints every socket green (`ValidSocketTint`) or dark
(`InvalidSocketTint`). Those are *tints* of the socket plate; the hint is a *frame* around it, so
both show: green says "it can go here", the brass frame says "the guide wants it here". The loop
labels (`WorkbenchLoopLabels`: "STEP 3 · loops back to 1") are captions and are untouched. The
guide plate's Workbench dock (`TutorialPanel.Dock(true)`, under the step sockets) is clear of
every outline; the `tutorial` scenario checks it (§8).

### 2.6 Failure states

| Situation | What happens |
|---|---|
| The plan's card isn't owned (not claimed yet) | `Next` returns `None` and the step's text says to claim it. `PlaytestKitTests` pins that every plan's cards are owned when its step comes (§7) |
| The chassis has fewer slots than the plan | `None`. Can't happen in the guide (each plan fits its chassis); pinned by a test |
| The player drops cards in another order the step accepts | `Matches` passes; the hint goes to the lever |
| The player engages a program that doesn't match | The step isn't done, so the hint stays on the first difference |
| The draft overflows (`DraftOverflowCount > 0`) | `None`: the status plate already says why ENGAGE is refused |
| The Workbench opens on a golem the step doesn't mean (a spare) | `PlanFor` returns null for it; the fallback applies only if it is unprogrammed |

## 3. Grey for waiting, red for stuck (decision 5)

### 3.1 The definition

A stalled golem is **waiting** when something already in the factory will clear its stall
without the player: a golem feeds its empty source, a golem empties its full target, a truckload
is on its way. It is **stuck** when nothing will: only the player can fix it. Waiting is grey and
quiet; stuck is red (or blue for steam) and loud.

That is decision 5's sentence made checkable. A rule from the `StallReason` alone can't draw the
line: "Crate(3,4) has no input" is waiting behind a Scavenger that fills it, and stuck behind a
crate only the player's hands fill (chapter 5's smelter).

### 3.2 Facts, and the probe that reads them

```
Golems/StallTriage.cs            (new, engine-free static)
    StallUrgency Urgency(StallReason reason, StallFacts facts)
    enum StallUrgency { Stuck = 0, Waiting = 1 }

Golems/StallFacts.cs             (new, readonly struct)
    AppendageActionType StalledAction  the step that stalled (Haul, Assemble, Push, ...)
    bool SourceGives                   the tile behind holds something that can give: a crate,
                                       a mast, a Post crate, a belt, a stall (not a boiler, heap,
                                       recycler or the tower)
    bool SourceFed                     something will put the wanted good there (below)
    bool TargetDrained                 something will take goods from the tile in front (below)
    bool TruckloadOnTheWay             the stall behind has an order in transit
    string WantedGood                  the good the stalled step needs, or null (§4.3)

World/StallFactsProbe.cs         (new, on SandboxWorld; reads Golems, Belts, Endpoints, Market)
    StallFacts Read(GolemEntity golem)
```

`GolemEntity` gains `ConfigureStallFacts(Func<GolemEntity, StallFacts> source)`, wired by
`SandboxWorld` for every spawned and respawned golem (the station's spawn and `IGolemRespawner`,
so birth stays in one file). `Mood` becomes:

```
GolemMoodRules.Classify(state, StallReason, HasRunnableProgram, FullestTypeUnits,
    urgency: _stallFacts == null || program.State != GolemState.Stalled
        ? StallUrgency.Stuck
        : StallTriage.Urgency(StallReason, _stallFacts(this)))
```

The probe runs only for a stalled golem, so a running factory pays nothing.

**Fed.** A cell is fed with good *g* when another golem (live, not held, `HasRunnableProgram`)
whose `TargetCell` is that cell has a Push step and may push *g*: its program's Assemble outputs
and byproducts, its typed Hauls' goods, and, for an untyped Haul or an Extract, any good (its
source isn't known well enough to rule *g* out). A belt cell is fed when walking its run upstream
(a belt whose cell plus its facing step is this cell; a visited set bounds it) reaches a cell some
golem pushes into. A mast's crate is fed while any Zeppelin has Freight Launch. A Post crate is fed
when any Post crate is (they share one store). **The golem itself never counts**: chapter 4's
Scavenger, turned round to face its own depot, is not fed by itself.

**Drained.** A target cell is drained when another golem (as above) has it as its `SourceCell`
and has a Haul, Extract or Load step; when it is a belt whose run downstream ends at such a
golem's source; or when it is a building that consumes what it takes: a boiler, the Clock Tower,
a Slag Heap or a Scrap Recycler. A Post crate is drained when any Post crate is.

A golem waiting behind another that is itself stuck reads grey. That is deliberate: one red at
the root and a line of grey behind it reads as one fault with one fix, the same argument
`cozy-automation-design.md` §2 made for `Starved`.

### 3.3 Every `StallReason`, mapped

| `StallReason` | Waiting (grey) when | Otherwise stuck (red) because | The fix the hover names |
|---|---|---|---|
| `None` | not a stall | | |
| `Unconfigured` | never | its program or wiring can't run | reprogram it (E) |
| `NodeEmpty` | a truckload for that stall is on its way | stalls don't refill themselves | order a truckload (E at the stall) |
| `BeltFull` (any full target: crate, belt, mast, heap, recycler) | the target is drained | nothing will empty it | collect with E, or stand a golem that hauls from it |
| `BeltEmpty` / `BufferEmpty` | the source gives and is fed with the wanted good | nothing will fill it, or it is a boiler, heap or tower, which never give | fill it by hand (I), or stand a golem that pushes into it; or turn the golem |
| `NoSourceAtTile` | never | layout: nothing behind it | turn it (R) or move it (G) |
| `NoTargetAtTile` | never | layout: nothing in front | turn it (R), or build a depot there |
| `InputFull` | never | its program hauls a good no later step uses | change its program (E) |
| `OutputFull` | never | a good it makes has nowhere to go (a labelled crate refusing its Slag) | give that good a crate, a heap or a carrier |
| `MissingItem` at a Haul, Extract or Load | the source is fed with that good | nothing will bring it | as `BufferEmpty` |
| `MissingItem` at an Assemble | never | its hold won't refill: only Hauls fill it, and they come before (§12.1) | raise the Haul's batch, then ENGAGE again |
| `NoSteam` | never: it stays `Starved`, blue | | the steam cause (`SteamShortage`), as today |
| `NodeCrowded` | never | a third golem at a two-golem seam | move it to another stall |
| `FilterMismatch` | never | the crate's label refuses its load | relabel the crate (I) or reroute the good |

Two reasons that look like waiting are stuck, with reasons. A full crate that only the player
empties (chapter 1's first crate) is red: the factory has stopped and only the player restarts
it, and the hover turns the red into an instruction ("collect it with E"). An Assemble short of
its input is red: execution is linear, so nothing refills the hold until ENGAGE rewinds the
program (`GolemProgramWriter.TryWrite`).

### 3.4 The mood: `Waiting`, appended

`GolemMood.Waiting = 6`. `Classify` returns it for a stalled golem whose urgency is `Waiting` and
whose reason isn't `NoSteam`. The existing order of checks holds: `Unprogrammed` first, then the
stall branch (`Starved` for steam, `Waiting` or `Stalled` for the rest), then `Sleeping`, then
`Straining` or `Working`.

| Mood | Volume | Badge | Tint | Motion | Dwell |
|---|---|---|---|---|---|
| `Working` | silent | none | white | brisk bob | |
| `Sleeping` | quiet | slate, `zzz`, 0.7× | grey 0.88 | breathing bob | 1.5 s |
| `Straining` | **silent on the floor; hover only (§4.4)** | **none** | white | brisk bob | 2 s (hover line) |
| `Waiting` (new) | quiet | slate, `waiting for Scrap` (or `waiting`), 0.7× | grey 0.88 | breathing bob | **2 s, tint and badge both** (TUNING) |
| `Starved` | loud | steel blue, the steam cause | cold blue | still | 0 |
| `Stalled` | loud | red, `[!]` and the short reason | red | still, entry shake | 0 |
| `Unprogrammed` | quiet | violet, `no program` | grey 0.88 | breathing bob | 2 s |

- **`Waiting` shares `Sleeping`'s palette** (`GolemMoodPalette`): grey means "fine, just not
  busy", whatever the cause.
- **Its dwell covers the tint as well as the badge.** A Presser hauling Scrap as it arrives flicks
  between `Working` and `Waiting` many times a minute; a tint that changed at once would strobe.
  Until the mood is held for `WaitingDwellSeconds`, the golem is drawn in the mood it had.
  `GolemMoodRules.Presented(mood, heldSeconds, previous)` (new) answers which to draw; the view
  owns only the stopwatch.
- **`Waiting` → `Stalled` is immediate** (dwell 0), so taking a line's feeder away turns the
  golem behind it red at once.
- **The entry shake** plays only when the mood the frame after a `GolemStalledEvent` is `Stalled`
  or `Starved`. A golem that starts waiting doesn't jolt.
- **The red badge drops the golem's id line.** It read `[!] PlayerGolem-007` over the reason;
  the id is on the hover card now, so the badge is `[!] ` plus `StallDiagnostics.DescribeShort`.
- `IsStopped` stays `Stalled || Starved`. `Waiting` is not stopped in that sense: it is not
  counted, shaken or drawn loud.

### 3.5 What this changes in `cozy-automation-design.md` §2

§2 is approved design; these override it. Recorded here and pointed to from §2.

| §2 said | Now | Why | Decision |
|---|---|---|---|
| Six moods | Seven: `Waiting` appended | a stall the factory will clear isn't a failure | roadmap decision 5 |
| `Stalled`: "`Stalled`, any other reason · loud · red" | `Stalled` only when stuck (§3.3); waiting stalls are `Waiting`, quiet grey | the playtest: waiting golems wore red and the badge read "vaguely" | decision 5 |
| `Straining`: advisory, amber badge `hold nearly full` | no badge; a line on the hover card | a factory of near-full golems is a busy factory, not news | decision 6 |
| "nothing = fine, amber = soon, red/blue = stopped" | nothing or grey = fine; red/blue = stopped *and needs you*; amber leaves the floor | the hierarchy now says who must act | decisions 5 and 6 |
| "a pure function of state the golem already exposes" | still pure, but a stalled golem's urgency reads facts about its neighbours (`StallFacts`), from a Core probe, opt-in by null | "will the factory clear this?" is a question about the neighbours | decision 5 |
| §0.8: ASCII and Latin-1 only, no arrows | the root `CLAUDE.md` font rule (Latin-1 plus → ≥ █ ░) | the project font has the arrows; the `font-glyphs` scenario pins them | already true since the font change |
| The alerts strip counts every stall | it counts stuck golems only (§3.6) | grey must not raise an alert | decision 5 |

### 3.6 The alerts strip

`AlertsStrip.Reconcile` adds a golem only when `GolemMoodRules.IsStopped(golem.Mood)`, and
`StallTracker`'s event path takes a filter (`Func<string, bool> isStopped`, a roster lookup), so a
waiting golem never shows for the half second before the next reconcile. With nothing stopped
and some golems waiting, the strip reads **"No golem needs you · 3 waiting for goods."**; with
none waiting, "All golems running." as today (the ported tests keep passing).

### 3.7 Failure states

| Situation | What happens |
|---|---|
| A crate is fed, but with a different good than the golem wants | Red when the feeder's possible outputs exclude the good; grey when unknown (an untyped Haul feeding it). Erring grey costs a little time; the hover still names the good |
| The feeder is itself stuck | The golem behind it is grey; the feeder is red. One fault, one red |
| A heap or recycler out of Coke | Consumers count as drained, so the carrier pushing into it is grey. The heap's Coke is the carrier's own load (chapter 5), so this clears itself; a recycler starved for Coke is the open question in §11 |
| The player carries the feeder away | It is held, so it stops feeding: the golem behind turns red on the next frame |
| A golem with no facts source | Today's behaviour: `Stalled`, red (invariant 6) |
| 96 golems, many stalled | The probe is O(golems) per stalled golem per frame; at the endgame's worst, a few thousand cell comparisons. Phase 3's profile includes it |

## 4. Hover

### 4.1 What is hovered

```
UI/GolemInspector.cs             (new, on SandboxWorld.Inspector)
    void Point(Vector2Int? cell)              fed by a node each frame; null = no pointer in the world
    GolemEntity Hovered { get; }              the golem under the pointer, or null
    static GolemEntity Pick(Vector2Int cell, IEnumerable<GolemEntity> golems)
```

**The rule:** the golem standing on the pointer's cell; else the golem standing on the cell south
of it (Core +y is north), because a standing sprite is about two cells tall and its body covers
the cell above its feet. Ties (none should exist) go to the lowest `GolemId`. A held golem is
never hovered.

**No hover** while the pointer is over a UI control (`GuiGetHoveredControl() != null`), while a
full screen is open (`ModalScreens.AnyOpen`), or while the wrecking bar is active (its own ghost
says what a click would take). Holding any other placeable still hovers: seeing a golem's target
while placing a depot in front of it is the point.

**The player's proximity** keeps its job. `RoutingFocusNode` still lights the nearest golem
within 3.5 cells (`RoutingFocus.SelectNearestIndex`), now with arrows (§4.2), so a keyboard
player reads facing without a mouse. Only a hovered golem gets the card (§4.3).

### 4.2 Source and target arrows: one drawing path

`GolemNode` already draws two floor tiles (`_sourceTile` teal, `_targetTile` gold) for the
focused golem and for a carried golem's landing. Hover draws through the same tiles:

```
World/GolemTileOverlay.cs        (new, engine-free static)
    TileOverlay Decide(bool hovered, bool focused, Vector2Int? carryLanding, Vector2Int cell)
        -> show, and the cell the tiles hang off (the landing while carried, else the golem's cell)
```

`GolemNode.SyncToEntity` replaces its `tiles` boolean with `Decide`. Each tile gains a child
`facing_arrow.png`, rotated to the golem's facing on both tiles, since goods flow one way: into
the golem on the teal tile, out of it on the gold one. The arrow sits at the tile's centre, scaled
0.6.

**Z-order:** the tiles are floor level, z −1 global (`ZAsRelative = false`), as today and as the
root `CLAUDE.md` sets for floor things; the arrows are their children at relative z 0, so −1 too.
Never below −10 (`FloorLayer.FloorZ`). A hovered golem's tiles show **only while hovered**: no
dwell, no fade, so sweeping the mouse across a line reads each golem in turn.

At most three golems show tiles at once: the hovered one, the focused one, and the carried one.

### 4.3 The hover card

A small plate on the badge layer (`CanvasLayer` 4), 16 px right of and below the pointer, kept on
screen, shown after the golem has been hovered 0.25 s (TUNING) so the card doesn't flicker as the
mouse crosses a line. The hovered golem's badge hides while its card is up: one golem, one
readout.

Every sentence is composed in Core:

```
UI/GolemHoverText.cs             (new, engine-free static)
    HoverCard Compose(GolemHoverFacts facts)     four lines, any may be ""
UI/TileNames.cs                  (new, engine-free static)
    string Describe(string endpointDisplayName, Vector2Int cell)
```

**`TileNames`** turns ids into names: `Crate(3,4)` → `Crate (3,4)`, `Mast(9,1)` → `Freight Mast
(9,1)`, `PneumaticPost` → `Pneumatic Post`, a placed belt's `Belt(0,-12)#3` → `Belt (0,-12)`
(`StallDiagnostics.PlacedBeltCell`), a node id → its good's stall (`ScrapNode` → `Scrap stall`),
a boiler → `Boiler (5,2)`, nothing there → `the floor at (3,2)`. Cells print as `(x,y)`, no space,
in Core's frame, as the crate headers already do.

**The card's four lines:**

| Line | Format | Example |
|---|---|---|
| Title | `{chassis display name} · {GolemId}` | `Brass Presser · PlayerGolem-004` |
| State | by mood, below | `Stuck: Crate (3,4) is empty, needs Scrap` |
| Fix | stuck only: the fix column of §3.3, one sentence | `Nothing fills it. Put Scrap in by hand (I), or stand a golem that pushes into it.` |
| Route | `From {source} → to {target}` | `From Crate (3,4) → to Crate (3,6)` |

**The state line, by mood:**

| Mood | State line |
|---|---|
| `Working` | `Working · step 2 of 3, {card label}` |
| `Working`, push stock ≥ `StrainingUnits` for 2 s | `Working · output {n}/12 {good}, nearly full` |
| `Sleeping` | `Sleeping until its trigger: {CardSubtitle of the core}` |
| `Waiting` | `Waiting for {good} · {source} is empty` (or `· {target} is full`, or `· a truckload is on its way`) |
| `Stalled` | `Stuck: {what}`, from the table below |
| `Starved` | `No steam: {DescribeShort's steam cause}` with the alerts strip's fix as the fix line |
| `Unprogrammed` | `No program · press E at it to open the Workbench` |

**`Stuck:` sentences**, one per reason, every one naming the tile and, where there is one, the
good. This is the "Crate (3,4) is empty: needs Scrap" the roadmap asked for; a comma replaces the
colon after `Stuck:` so a line has one colon.

| Reason | `Stuck: …` |
|---|---|
| `BufferEmpty`, `BeltEmpty`, `MissingItem` at a take | `{source} is empty, needs {good}` (no good known: `{source} is empty`) |
| `NodeEmpty` | `{stall} is empty` |
| `BeltFull` | `{target} is full of {good}` (`{target} is full`) |
| `NoSourceAtTile` | `nothing to take from at {cell}, behind it` |
| `NoTargetAtTile` | `nothing to give to at {cell}, in front of it` |
| `FilterMismatch` | `{target} takes only {label}; it carries {good}` |
| `InputFull` | `its hold is full of {good}, and no step uses it` |
| `OutputFull` | `its output is full of {good}; {target} won't take it` |
| `MissingItem` at an Assemble | `needs {n} more {good} than its Hauls bring` |
| `NodeCrowded` | `{stall} already has 2 golems` |
| `Unconfigured` | `its program can't run` |

**`WantedGood`** (for "needs Scrap" when the stall reason carries a place, not a good): the
stalled step's own good (`GetItemTypeAt` or the card's `inputItemType`); else, for an untyped
Haul or Extract, the first input of the program's Assemble recipe that the hold lacks; else, at a
stall, the stall's good; else null.

**For `BeltFull`'s good**: the fullest good in the golem's push stock (`GolemInventory.FullestType`,
new, beside `FullestTypeUnits`).

Every composed string passes the font rule; `GolemHoverTextTests` checks each against the same
character set the `font-glyphs` scenario pins.

### 4.4 `Straining` on hover only (decision 6)

`GolemMoodRules.ShouldShowBadge(Straining, _)` returns false, and `Caption(Straining)` is no longer
drawn. The classification is unchanged, so `Straining` still exists for anything that asks; the
hover card's state line shows it once held `AdvisoryDwellSeconds` (2 s). `StrainingUnits` stays 9.

### 4.5 Failure states

| Situation | What happens |
|---|---|
| The pointer is over a golem and a building | Golems aren't grid occupants; the golem wins, since it is what the pointer is drawn over |
| The hovered golem is carried away | It is held, so it stops being hovered; the carry preview takes over its tiles |
| The pointer leaves the window | The node feeds `Point(null)`; nothing is hovered |
| A modal screen opens under the pointer | Hover clears that frame |
| A golem is dismantled while hovered | `Hovered` re-picks every frame from live golems; the card hides |
| Two golems a cell apart, pointer on the upper one's cell | The pointer's own cell wins over the cell south of it |

## 5. The depot label picker

**Done in Phase 1.** It moved onto the crate screen (`inventory-screens-design.md` §4.4): I at a
crate, click the label, pick a good. Step `copper-depot` teaches it and the `inventory` scenario
covers it. The roadmap's Phase 2 bullet and the open item are closed.

## 6. What changes, system by system

| System | Today | Phase 2 |
|---|---|---|
| `GolemMood` | six moods | `Waiting = 6` appended |
| `GolemMoodRules` | `Classify(state, reason, runnable, fullest)` | gains `urgency` (default `Stuck`); `Presented` for the dwell; `ShouldShowBadge(Straining)` false; `Caption(Waiting)`; `WaitingDwellSeconds = 2` |
| `GolemMoodPalette` | five tints | `Waiting` shares `Sleeping`'s tint and badge |
| `StallTriage`, `StallFacts` | none | new (§3.2) |
| `StallFactsProbe` | none | new, on `SandboxWorld` |
| `GolemEntity` | `Mood` from its own state | `ConfigureStallFacts`; `Mood` passes the urgency |
| `GolemInventory` | `FullestTypeUnits` | adds `FullestType` |
| `AlertsStrip`, `StallTracker` | count every stall | count stopped golems; "No golem needs you · N waiting for goods." |
| `GolemNode` | tiles for focus and carry; shake on every stall; badge with the id line | tiles via `GolemTileOverlay`, with arrows; shake only when stopped; dwell on the tint; badge without the id; badge hides under the hover card |
| `GolemInspector` | none | new, on `SandboxWorld.Inspector` |
| A hover node (`Scripts/Golems/GolemHoverNode`) | none | new: feeds `Inspector.Point`, draws the card |
| `GolemHoverText`, `TileNames` | none | new |
| `ProgramPlan` | none | new |
| `TutorialStep` | `Card`, `Workbench`, `Crate` | adds `Program`, `ProgramTarget` |
| `TutorialGuide` | | `PlanFor(golem)`; the nine program steps' `Done` read `plan.Matches`; texts per `guide-chapters-design.md` |
| `TutorialGuide.Kit.cs` | `Program`/`ProgramSlots` per step | `PerformPlan(step)` for the nine |
| `WorkbenchSession` | | implements `IWorkbenchDraftView`; `Hint`; `HintSource` (`Func<GolemEntity, ProgramPlan>`, wired by `SandboxWorld` to `Guide.PlanFor`) |
| `WorkbenchHint` | none | new |
| `WorkbenchScreen` | socket tints | `HintOutline` frames on card, socket, picker, dial and lever; vault scroll |
| `PlaytestSession` | | the questions in §9; the report's chapter 1–4 total (`testscript/phase-2-playtest.md`) |

## 7. Tests (Core)

- **`WorkbenchHintTests`**: an empty draft hints the core to TRIGGER; each plan slot in order; a
  wrong card in a socket hints the right one to it; a good mismatch hints the picker, a batch
  mismatch the dial with the right sign; a card past the plan hints "drag out"; a matching draft
  hints the lever; a matching committed program hints `None`; `IsSmelter`'s either order passes;
  evaluating `Hint` leaves `Version` unchanged; the fallback outlines TRIGGER, STEP 1, the lever;
  no hint on a programmed golem without a plan.
- **`ProgramPlanTests`**: each of the nine steps' `Matches` agrees with the predicate it replaced
  on the programs those tests already build; every plan fits its chassis.
- **`PlaytestKitTests`** (extended): every plan's cards are owned when its step comes; the kit's
  `PerformPlan` passes each step; `PlanFor` finds `program-presser`'s plan while `presser` is
  current.
- **`StallTriageTests`**: every `StallReason` value has an answer (enumerated, like `ItemTiers`'
  reflection test, so a new reason can't be added without one); each row of §3.3, both ways.
- **`StallFactsProbeTests`** (`SandboxWorld`): a crate fed by a golem; the golem itself never
  feeds; a belt fed upstream; a Post crate fed through another; a boiler never gives and always
  drains; a truckload in transit; a feeder carried away stops feeding; a feeder that can't push
  the good doesn't count.
- **`GolemMoodRulesTests`** (extended): `Waiting` from a waiting urgency; `NoSteam` stays
  `Starved` whatever the urgency; `Waiting == 6`; `Presented` holds the old mood through the
  dwell and drops it at once for `Stalled`; `Straining` never shows a badge.
- **`AlertsStripTests`** (extended): a waiting golem isn't counted, by event or by reconcile; the
  "N waiting" text; the ported texts unchanged.
- **`GolemInspectorTests`**: the pointer's cell, then the cell south; a held golem never; `null`
  clears.
- **`GolemHoverTextTests`**: every mood's state line, every reason's `Stuck:` sentence and fix,
  `TileNames` for each id shape, and every string within the font's character set.
- **`TutorialGuideTests`** (extended): chapter 4's three wrong facings each read `Stalled` (red),
  not `Waiting`; the guide-text budgets of `guide-chapters-design.md` §1.
- **`LayoutMattersTests`** unchanged: nothing here changes what a golem does.

## 8. Scenarios

Three new, one extended. Each prints `[scenario <name>] PASS|FAIL` and is registered in
`ScenarioRunner.Scenarios`.

**`hint`** (Sandbox, new). From a fresh game, the scenario grants chapter 1's goods and places
and fuels the boiler as the kit's actions would, so the guide settles on `golem`; then it builds
the Scavenger at the station with real input, which puts the guide on `program`:

1. The Workbench opens on the new golem: Always On's vault card and TRIGGER are outlined, and no
   socket holds a card.
2. A real mouse drag of Always On to TRIGGER: the outline moves to Extract and STEP 1.
3. A wrong card dropped into STEP 1: the hint still points Extract at STEP 1.
4. Extract, then Push Output, by real drags: the lever is outlined.
5. Pull ENGAGE: no outline, and the guide passes `program`.
6. At no point did a socket change except by a drop (the scenario compares the draft before and
   after each frame without a drop).
7. Free play: guide skipped, a new bare golem: TRIGGER outlined, no vault card outlined; a
   programmed golem: no outline.
8. Frames for `--write-movie`: the outline is visible on the card and the socket.

**`moods`** (Sandbox, new). On steam, with real golems and crates:

1. A Presser behind an empty crate that a Scavenger fills: `Waiting`, grey tint, `waiting for
   Scrap` after the dwell, no shake; the alerts strip doesn't count it and reads "No golem needs
   you · 1 waiting for goods."
2. The Scavenger carried away (G): the Presser is `Stalled`, red, at once, and counted.
3. A golem facing nothing behind: red, `NoSourceAtTile`.
4. A full crate nothing hauls from: red; a golem set to haul from it: grey.
5. A Coal stall emptied: red; a truckload ordered: grey until it lands.
6. A golem out of steam: blue, as the `golems` scenario already pins.
7. A golem at 10 of 12 output: no badge.

**`hover`** (Sandbox, new). With the pointer warped as `BuildScenario.Hover` does:

1. Pointer on a golem's cell, then on the cell above it: the golem is hovered both times; its
   source and target tiles show with arrows, at z −1.
2. The card's title, state and route lines match `GolemHoverText` for that golem.
3. A stuck golem behind an empty, unfed crate: the card says `Stuck: Crate (x,y) is empty, needs
   Scrap` and the fix line; its badge is hidden under the card.
4. A straining golem: the card's state line says `nearly full`; no badge.
5. Pointer over the build menu, then with the Workbench open: nothing hovered, no card.
6. Carrying a golem: its tiles follow the landing, and hovering another golem shows that one's
   tiles too (three at most).
7. Frames: the arrows and the card are visible.

**`tutorial`** (extended): on "Program it", the guide plate is clear of every hint outline as
well as the sockets, lever and vault.

**`golems`, `golem-motion`, `workbench`** keep passing unchanged: the `workbench` scenario's golem
has no plan and is programmed, so it sees no hint.

## 9. Playtest questions

Added to `PlaytestSession.Questions` (ids new; `AfterStepId` is where each is asked):

| Id | Asked at | Question | Answers |
|---|---|---|---|
| `hint` | `depot` | The glowing card and socket: did they show you what to drag? | Clear · Took a moment · In my way |
| `turning` | `r4-card` | Turning golems with R: how did it go? | Easy · Fiddly · I turned the wrong one |
| `grey-red` | `r4-card` | Waiting golems go grey, stuck ones red. Could you tell which needed you? | Always · Mostly · No |
| `hover` | `r4-card` | Hovering a golem shows its arrows and, when stuck, why. Did you use it? | Often · Once or twice · Didn't know |
| `straining` | `expand` | A busy golem's "nearly full" is on hover only. Did you ever miss a warning? | Never · Once (say where) · Often |

`badge` (asked at `r4-card`) is reworded, id kept: "When the golem stopped red, did its badge and
hover tell you what was wrong?" (Clearly · Vaguely · I didn't hover it). Four questions open
chapter 5; the playtest script says so, since a player may skip some.

## 10. Order of work

Each row is one commit: Core and its tests, then the node, then the scenario.

1. `StallTriage`, `StallFacts`, `StallFactsProbe`, `GolemMood.Waiting`, the mood and strip
   changes; scenario `moods`.
2. `GolemInspector`, `GolemTileOverlay`, `GolemHoverText`, `TileNames`, the hover node, the
   arrows, `Straining` off the floor; scenario `hover`.
3. `ProgramPlan`, `TutorialStep.Program`, `PlanFor`, `PerformPlan`; `PlaytestKitTests`.
4. `WorkbenchHint` and the outlines; scenario `hint`; `tutorial` extended.
5. The guide's new texts (`guide-chapters-design.md`), the questions, and the playtest script.

Steps 1 and 2 stand alone; 4 needs 3.

## 11. Questions, decided

**Decided 2026-10-19: the user took every recommendation below.** Kept as asked, with the
recommendation that became the decision.

1. **Is a full crate only the player empties red?** It stops the factory until the player acts,
   which is decision 5's red; but in chapter 1 the first golem will go red often while the player
   cranks. **Recommendation: red**, with the hover's "collect it with E" doing the teaching.
2. **The fallback hint without the guide.** It outlines sockets but no card. **Recommendation:
   keep it**, for a player who skipped the guide; it never applies to a programmed golem.
3. **The red badge loses the golem's id line.** **Recommendation: yes**; the id is on the card.
4. **The strip with only waiting golems** reads "No golem needs you · N waiting for goods."
   **Recommendation: yes**, so grey is visible from the HUD but never an alert.
5. **The [E] prompt at a stuck golem carries the problem as its detail** (for a keyboard player,
   and decision 10's controller later). Not specified above. **Recommendation: add it in Phase
   2**, one line, from `GolemHoverText`'s state line.
6. **A Scrap Recycler out of Coke** counts as drained, so a golem pushing into it reads grey
   while it waits for Coke that may never come. **Recommendation: accept for now**; it is a late,
   optional building, and the recycler's own refusal is visible.
7. **Hover delay 0.25 s, `Waiting` dwell 2 s.** TUNING, listed in the playtest script.
8. **Should a Haul wait for its full batch?** (§12.1, not one of the seven above.) **Decided
   2026-10-19 by the user: the Haul collects** into the golem until it holds its batch, showing
   progress ("5/10 Scrap"), §13. A first draft, "wait until the source holds the whole batch",
   was withdrawn the same day, since a belt may never hold a whole batch; it is superseded.

## 12. Found in the code while writing this

1. **A partial Haul can jam an Assemble for good.** `StorageBufferEndpoint.TryTake` gives what it
   holds, up to the batch, and `BeginFillInputStock` proceeds with any amount taken. So a smelter
   whose `Haul(Scrap ×2)` finds one Scrap in its crate takes one, and its Assemble then stalls on
   `MissingItem` forever: execution is linear and nothing re-runs the Haul until ENGAGE rewinds
   the program. Crates make this likely (chapter 5 feeds the smelter by hand, one click moves
   one). This spec reads it as stuck (§3.3) and names the fix; whether a Haul should wait for its
   full batch is a simulation change and **the user's call**. In `open-items.md`.
   **Closed: decided 2026-10-19, the collecting Haul (§13)**, which removes the jam: an Assemble
   now only runs after its Hauls hold full batches.
2. **An empty labelled crate reports `BeltEmpty`.** `GolemEntity.EmptyReasonFor` asks `is
   StorageBufferEndpoint`, and a labelled crate publishes a `FilteredBufferEndpoint`, so the badge
   says "waiting on Crate(3,4)" where an unlabelled one says "no input in Crate(3,4)". The triage
   and the hover treat both alike (§3.3), so Phase 2 is unaffected; the badge wording is in
   `open-items.md`. **Fixed in Phase 2:** `EmptyReasonFor` reads a labelled crate
   (`FilteredBufferEndpoint`) as `BufferEmpty`, so it says "no input in Crate(3,4)" too.

## 13. The collecting Haul (decided 2026-10-19)

**The user's decision:** a Haul collects goods into the golem until it holds its batch. It ends
§12.1's jam, and it works from a belt of any length. It supersedes a first draft of the same day
("take nothing until the source holds the whole batch"), withdrawn because a placed belt offers
one good at a time and so could never hold a batch.

### 13.1 The rule

A **spatially placed** golem's `Haul` with batch *n* (`GolemProgram.appendageQuantities`,
clamped 1..12) of good *g*, each attempt:

1. *held* = how many *g* the golem's input already holds. *need* = *n* − *held*.
2. **need ≤ 0: take nothing and advance.** The golem already holds a batch. This happens after a
   batch was lowered at the Workbench, after a load, or when a recipe leaves a remainder: an
   Assemble consumes only its recipe's amounts, so the rest waits for the next cycle, and the
   next Haul tops up only what is missing. The input stock never exceeds 12 of a good.
3. Otherwise take min(available, *need*) from the source. **The golem never takes more than it
   needs; any extra stays on the belt or in the crate.**
4. Holding *n* now: the step runs its duration, `max(2, n)` as before, and advances.
5. Still short: the step **stays put**: no skip, no reorder, the rigid-stall rule. It retries
   every tick and keeps taking as goods arrive. With part of a batch held it stalls
   **`MissingItem`** naming *g*, with `StallShortfall` = *n* − *held*; with nothing held it
   reports what it reports today (`BufferEmpty`, `BeltEmpty`, `NodeEmpty`, or `MissingItem` when
   the source holds other goods).

**No new `StallReason`** (invariant 5 holds). `MissingItem` already means "the good this step
names isn't available where it needs it", and §3.3 already maps it at a Haul: grey when the source
is fed with the good (or a truckload is on its way to the stall behind), red when nothing will
bring it.

**Which good an untyped Haul collects:** what the source offers (as today); with the source empty,
the first good its input already holds, so a half-collected batch keeps its good.

**Logistics golems** (no Assemble, input is output) follow the same rule: collect up to the batch,
then the Push runs. A Push that left a remainder counts toward the next batch.

**Two Hauls of the same good in one program** don't add: each tops the input up to its own batch
of that good, measured over everything held. `Haul(Scrap ×2)` then `Haul(Scrap ×3)` holds 3 after
the second, not 5. That is the simplest rule consistent with "never more than a batch"; a recipe
that needs more of one good sets one Haul's batch to that amount.

**Unchanged:** `ExtractFromNode` still takes up to its batch and keeps partial takes (a stall
yields what it has, a truckload refills it). The id-routed Haul is still a no-op success, so
`IdRoutedDemoRegressionTests` is untouched. `Push`, `Assemble` and `Repeat` are untouched.
`InputFull` can no longer arise at a Haul: *held* + *need* = *n* ≤ 12.

### 13.2 What the player sees

- **Badge:** while a Haul is collecting (stalled `MissingItem` with part of its batch held), the
  badge shows progress, `5/10 Scrap` (grey: `5/10 Scrap`, red: `[!] 5/10 Scrap`), in place of
  `need 5 more Scrap`. `StallDiagnostics.HaulProgress` composes it.
- **Hover state line:** `Waiting for Scrap · 5/10 · Crate (3,4) is empty` when fed; `Stuck:
  Crate (3,4) is empty, needs Scrap (5/10)` when not, with §3.3's fix line.
- **Mood:** `StallTriage` as for any `MissingItem` at a take.

### 13.3 Tests

`HaulCollectsTests` (Core): a crate short of the batch: the golem takes what is there and stays
on the step; arrivals top it up to exactly the batch and it advances; extra stays in the crate;
the old jam (a smelter hand-fed one Scrap at a time) now assembles once two are held; a one-slot
belt feeding a batch of 5 completes; input already over the batch takes nothing and advances; two
Hauls of the same good don't add; `ExtractFromNode` still takes a partial batch.
`IdRoutedDemoRegressionTests` and `LayoutMattersTests` keep passing.

**As built (2026-10-19):** `HaulCollectsTests` covers the list. `LayoutMattersTests`' numbers
didn't move (103, 0, 82, 101, 50): its lines haul batches their sources always hold.

## 14. Corrections, as built (§3, §4 and §13; 2026-10-19)

Built: §3 and §4 in full, §11 question 5, §12.2 and §13. Not built here: §2 (the hint,
`ProgramPlan`), §9's questions and the guide texts, which come next. Where the build differs from
the text above:

1. **`GolemHoverText.Compose(golem, mood, facts, tiles, strainingHeld)`** takes the golem, its
   drawn mood, its `StallFacts` and a `TileNames`, not a `GolemHoverFacts` struct: every fact the
   struct would carry is already on the golem. `SandboxWorld.HoverCard(golem, mood)` wires it.
2. **`TileNames` is an instance** over the endpoint registry and the masts, and names a tile from
   what stands on it and its cell, never by parsing an id (a placed belt is `Belt (0,-12)` because
   a belt endpoint stands there). The Clock Tower reads "the Clock Tower".
3. **`GolemTileOverlay.Decide` takes a `carried` flag**: a carried golem's tiles hang off its
   landing, or hide while there is none.
4. **Straining's hover dwell is timed over the hold**, not the mood: an AlwaysOn golem is `Idle`
   for a tick at the end of each cycle, which restarted a mood stopwatch every cycle, so "nearly
   full" never reached the card. The hover node counts how long the fullest good has held
   `StrainingUnits` or more. The line reads `output` or `hold` for the stock that is full, since
   `FullestTypeUnits` reads both: `Working · hold 11/12 Coal, nearly full`.
5. **The hover card is on layer 42**, not 4: the guide's arrow (layer 40) covered its text in the
   frames. It is transient and never shown over a control. The pointer is read from motion
   events, as the GUI tracks it, so a scenario's pointer is the pointer hover sees.
6. **Drained** counts golems with a Haul or Extract step at the cell (§3.2 also named Load):
   `LoadIntoBuffer` on a placed golem is a Push, so it never takes.
7. **Fed**: a golem with an Assemble pushes only its recipe's output and byproduct; its Hauls
   fill its hold, not its target. The Scrap Recycler counts as a source (it gives Scrap), against
   §3.2's parenthesis.
8. **Stuck sentences at a take:** `MissingItem` where the source holds other goods reads
   `{source} has no {good}`; a collecting Haul reads `{source} is empty, needs {good} (5/10)`.
   No steam's fix lines are written per cause (`Lay a steam pipe to it from a lit boiler.`, `Fuel
   its boiler with Coke (E at the boiler).`, `Build another boiler: this one already powers 8
   golems.`).
9. **The entry shake** plays when a golem goes from not stopped to stopped, not on every stall
   event: a collecting Haul republishes its stall each time a good arrives.
10. **The alerts strip** counts a stall event from a golem the roster doesn't know, as every stall
    counted before. Its red line keeps `StallDiagnostics.Describe`'s ids (`Crate(2,-1)`).
11. **`StallTriage.Urgency` throws** for a reason it doesn't name; `StallTriageTests` walks the
    enum, so a new reason fails a test, not a frame.
12. **The [E] prompt** puts a stopped golem's problem first in its detail:
    `[E]  Program PlayerGolem-003  -  Stuck: nothing to take from at (-4,-1), behind it · faces E · [R] turn · [G] carry`.
13. **Tile arrows** are cream on the tinted tiles (the tile's tint is its self-modulate, so the
    arrow keeps its colour). A tile under a crate is covered by the crate, arrow and all, as any
    floor-level thing is.
14. **Tests** are `StallTriageTests` (triage, probe, mood, strip), `GolemHoverTextTests` (card,
    tile names, the pick, the overlay, the font) and `HaulCollectsTests`; the scenarios `moods`
    and `hover` share `Scripts/Scenarios/StreetFixture`. Not written yet: `TutorialGuideTests`'
    chapter 4 facings and the guide-text budgets (with the guide texts).

## 15. Corrections, as built (§2 and §9; 2026-10-19)

Built: §2 (`ProgramPlan`, `PlanFor`, `PerformPlan`, `WorkbenchHint`, the outlines), §9's
questions, and `guide-chapters-design.md`'s texts. Where the build differs from the text above:

1. **No `hint` scenario.** §8's checks live in the `workbench` scenario instead: its golem is
   built while the guide is on chapter 1, so it is the guide's first Scavenger and the "Program
   it" plan applies. It checks the frames on the vault card and the socket, real drags following
   them, a wrong card keeping the hint, the lever framed last, ENGAGE clearing it, the vault
   frame hiding while the card is in hand, and no frame without a drop changing the draft. §8's
   free-play case (the fallback, and nothing on a programmed golem) is pinned in
   `WorkbenchHintTests`, not on screen. `tutorial` checks the plate is clear of both frames.
2. **A drop on a filled socket now replaces its card.** §2.2 assumed it did; the session always
   did, but on screen the socketed card swallowed the drop, so a wrong card had to be dragged out
   first. A socketed `WorkbenchCardControl` now passes a drop on to its socket row.
3. **`WorkbenchHintKind.DragOut`** is its own kind (§2.2 step 3 used `VaultCard` with no card).
   `VaultCard` with no card is the fallback's socket-only frame.
4. **`Next` asks `plan.Matches` before walking**, so an order the step accepts goes straight to
   the lever, and returns `None` as soon as the committed program matches, even while the player
   edits the draft. The good picker's frame is always on `>` (`Delta` +1).
5. **`IWorkbenchDraftView.OffersCard`** answers §2.6's "the card isn't owned"; `Next` also
   returns `None` with no chassis fitted.
6. **The frames** are two `Panel`s (a `StyleBoxFlat` border) on the screen's root, laid each frame
   over the target's rect grown 4 px; the vault card's frame is clipped to the vault's view. They
   also **glow**: an 8 px shadow in the frame's colour at 0.55 of its alpha. In the frames a bare
   3 px border was lost against the brass vault at the pulse's low point, and the guide calls
   them "the glowing card and socket".
7. **Targets:** `program-carrier`'s is the newest Presser that is neither the iron Presser nor a
   coker (§2.1 said the newest Presser): looking ahead from `r4-card`, the newest Presser is the
   coker. The unloader's and the Zeppelin's `Matches` now need a logic core, so the lever is
   never framed for a program with no trigger; the programs the old checks were tested on all
   had one.
8. **`PerformPlan` writes through `GolemProgramWriter`**, as ENGAGE does (the program rewinds to
   step 0); the kit's own `Program`/`ProgramSlots` are gone.
9. **`StallFactsProbe`: a stall is never drained.** §3.2's rule counted a golem extracting from
   the stall as draining it, so chapter 4's Scavenger turned to face the Scrap stall read grey.
   A stall never accepts a push. Found by `TutorialGuideTests`' wrong-facings test.
10. **The `hint` question** is queued after `workbench`, both at `depot`. The report's first
    lines add "Chapters 1–4: mm:ss (target 15:00) · kit used: no" once `r4-card` is reached
    (`PlaytestSession.ChaptersOneToFour`).
