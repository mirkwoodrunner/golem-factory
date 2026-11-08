---
name: design-audit
description: "Read Golem Factory's design docs and data together and report what only shows across them: docs that contradict each other or the data, progression loops pulling against each other, dominant choices, resource loops with no sink, pacing jumps, multi-system moments nobody specified. Findings only; it never edits. Use when asked to audit, review or sanity-check the design, a phase's docs, the economy, or the late game."
argument-hint: "[full | phase8 | economy | <doc name or path>]"
---

# Design audit

A read-only review of the design as a whole. Adapted from Claude Code Game Studios' `review-all-gdds`
and `balance-check` (MIT), pointed at this repo's docs and `godot/data/`.

## Ground rules

- **Report, never edit.** No doc, data, code or test changes. The user decides every finding.
- **The project's standards are the project's own:** the §0 invariants of each design doc, the root
  `CLAUDE.md`'s "Settled rules", the folder `CLAUDE.md` rules, and decisions a doc records as the
  user's ("decided", "the user's call", a dated Questions section). The lenses below are ways of
  looking, **not standards**. Never write "violates the attention budget" or "fails lens 3c". A
  finding states what the player would meet, and cites where.
- **Every finding cites evidence:** doc and section (`supply-design.md` §3), or data file and key
  (`contracts.json` `C01_WatchBrackets.reward`). No citation, no finding.
- **Data beats docs.** A number quoted in a doc that differs from `godot/data/` is a stale doc, never
  a wrong datum ("a quoted price is the data's"). Read the number from the file each time, never
  from memory or from another doc.
- **Don't re-report what is already known.** Check `docs/open-items.md` and the doc's own Questions
  and critique sections first. A known item is listed once under "Already tracked", not as a finding.
- **Don't re-check what a test pins.** Before flagging a number, Grep `godot/Core.Tests` for it or its
  rule (`PriceReachTests`, `PriceCeilingTests`, `FullRunFloorTests`, `MarketThroughputTests`, ...).
  If a test pins it, cite the test. Report it only if the pinned value itself causes the problem.
- **Don't relitigate a recorded decision.** Report against one only with evidence the decision didn't
  have, and say which decision and what is new.
- **"Not assessed" is an answer.** If an input is missing, or a lens has nothing to work on, say
  which and why. A lens that found nothing says what it looked at. A clean result must never be
  indistinguishable from a check that didn't run.

## Scope

`$ARGUMENTS` picks the docs (all paths under `docs/`). Blank means `full`.

| Scope | Docs |
|---|---|
| `full` | the core, economy, late and onboarding sets below |
| `economy` | core + economy |
| `phase8` | `supply-design`, `contracts-design`, `endless-design`, `region-design`, plus the core set |
| a doc name or path | that doc, plus every doc it names that is in the four sets |

- **core:** `game-design`, `digital-design`, `progression-design`, `cozy-automation-design`
- **economy:** `crates-design`, `pack-upgrades-design`, `tuning-data-spec`, `supply-design`, `contracts-design`
- **late:** `endless-design`, `region-design`
- **onboarding:** `onboarding-feedback-design`, `guide-chapters-design`

Out of scope unless named: art, audio, game feel, menus, inventory screens, Steam Deck, performance
reports, the conversion plan, `godot-spike`, `docs/history/`. Always read `docs/roadmap.md`'s "Next
session: start here", `docs/open-items.md`, the root `CLAUDE.md`'s settled rules, and every
`godot/data/*.json` the scoped docs touch.

Large docs (several are 1000+ lines): Grep `^#` for headings, then Read the sections you need.
Always read a doc's §0, its decisions, and its "As built" section if it has one.

## Pass 1: Load

1. List each in-scope doc's §0 invariants and recorded decisions, by doc.
2. Build the **resource table**: every good, currency or limited thing the player manages (each raw
   and made good, steam/Coke, pack room, crate room, Post room, floor, cart spots, golem cap,
   time). For each: **sources** and **sinks**, each with its doc section or data key. This table
   feeds pass 3.
3. Note every system a scoped doc says is **active** (the player decides about it again and again
   while it runs) versus **passive** (it runs, the player sees results).

## Pass 2: Consistency

- **Doc against doc:** two docs stating different rules or numbers for the same thing; a doc that
  amends another without the other saying so (`contracts-design.md` §11 amends `progression-design.md`
  §7: does §7 point to it?); a doc describing a system as it was before a later doc replaced it.
- **Doc against data:** every number a scoped doc quotes, checked against the data file.
- **Doc against the engine and settled rules:** Unity-era wording left in a rule (`MonoBehaviour`,
  `.asset`, TMP), or a doc rule that contradicts a settled rule.
- **Ownership:** two docs each claiming to define the same rule.
- **Questions about systems that changed:** `docs/open-items.md` and `testscript/*.md` asking the
  player or the user about something a later doc replaced (a question about truckloads after the
  carts came in).

## Pass 3: Lenses

Each lens ends with findings, or one line saying what it examined and found nothing.

- **a. Competing goals.** Which systems hand out the player's main progress (Ledger unlocks, chapters,
  tower stages, contracts, civic works, Docks rows)? If two pull at the same moment with comparable
  weight, which wins and does the other go quiet? Is it clear what the player is working towards at
  each phase?
- **b. Attention.** At the key moments (each chapter's new mechanic, the tower's last stage, the first
  contract, opening the Docks), list the active systems. A cozy game should pile up fewer than an
  action game; report the count and what could stay passive, not a pass or fail.
- **c. Dominant choices.** A chassis, logic core, card, cart rung, recipe route or layout that is
  better on every axis than its alternatives at the same point in the game, so the others are never
  worth picking. Show the comparison as numbers from the data.
- **d. Resource loops.** Over the resource table, look for:

  | Condition | Shows as | Risk |
  |---|---|---|
  | Source, no sink | Piles up indefinitely | It stops mattering |
  | Sink, no source | Runs to zero | A system stops working |
  | Source far above sink | Surplus | Prices stop mattering |
  | Sink far above source | Constant shortage | Frustration, a wall |
  | More of it earns more of it, uncapped | Snowball | Late game trivial |
  | Falling behind makes it worse | Spiral | A save that can't recover |

  Endless play (`endless-design.md`) is where these usually surface: check what a good or reward is
  for once the tower is won and the authored contracts are done.
- **e. Pacing.** For each thing that scales with progress (prices, rates, stage demands, contract
  asks, floor costs), say how it scales and when. Compare curves that meet: one growing per chapter
  against another growing per purchase. Use `tuning-data-spec.md` §14's budgets and floors where
  they exist; flag a jump between neighbours, and a stretch with nothing new.
- **f. Invariants and anti-goals.** Every scoped system should serve what the docs say the game is:
  cozy, rigid golems that stall and retry and never adapt, red only when the player must act,
  deterministic integers, no item loss. Flag a system that leans against one, and any that does
  what a doc says the game will not do.
- **g. Fantasy.** Do the systems ask the player to be one consistent person (an artificer running a
  clockwork works for a city)? Flag a system whose texts, verbs or rewards cast the player as
  someone else.

## Pass 4: Multi-system moments

Pick 3 to 5 moments where several systems fire at once (examples: a contract's par running out while
a cart empties; the tower's last stage while generated contracts arrive; a pack-full refund during a
demolition next to a Post crate; a Docks row bought while the golem cap is near). For each, walk:

1. **Trigger:** what the player does or what the clock reaches.
2. **Order:** which systems act, in what order, as the docs and Core say.
3. **Hand-off:** is each system's output a valid input to the next?
4. **What the player sees and hears** at each step.
5. **Failure modes:** two systems changing the same state; a loop with no damper; a system assuming
   a state an earlier step changed; two messages that disagree (a success sound over a red mood);
   two rewards for one action that together exceed what either doc intends; a combined state that
   no doc specifies.

Read the Core code when the docs leave the order open (`godot/Core/`), and say which you used.

## Numbers

A finding that rests on arithmetic shows it:

1. The expression, with named variables.
2. A table: variable, where it comes from (file and key), its range in the data.
3. The output's range over those inputs, and the inputs that give each end.
4. One worked example with real values.

Compute from the data files, using Python (`python -I`) when it's more than a line or two. Paste the
inputs you used.

## The report

Reply in the conversation. Don't write a file unless the user asks for one.

```
# Design audit: <scope>, <date>

Read: <docs, data files, tests consulted>
Not read / not assessed: <what, and why>

## Findings
### <n>. <one-line claim>  [Conflict | Risk | Question]
Where: <doc §, data key>
What the player meets: <concrete>
Evidence: <quotes, numbers>
Options: <2-3, no pick unless asked>

## Already tracked
<items found that open-items.md or a doc's Questions already hold>

## Lenses with nothing found
<lens: what was examined>
```

**Severity:** *Conflict*: contradicts a §0 invariant, a settled rule, a recorded decision, or another
doc. *Risk*: a likely player-facing problem with nothing contradicted. *Question*: the docs leave it
open and play depends on the answer. Order: Conflicts, then Risks, then Questions; within each, the
biggest player impact first.

End with the three findings you would look at first, and why. Then stop: fixing is a separate
request.
