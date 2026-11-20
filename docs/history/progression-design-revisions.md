# Progression design: the review revisions

> **History, not instructions.** The revision 2 and revision 3 change tables from the head of
> `docs/progression-design.md`, moved here on 2026-11-20. What they fixed is written into that doc's §2, §3, §5, §6, §7 and §8.

**Revision history.** Revision 1 failed a rubric review on five structural points. Revision 2
passed. Revision 3 is a tightening pass fixing two must-fix defects and a set of corrections.

<details>
<summary><b>Revision 2 — the five structural fixes (kept, unchanged in principle)</b></summary>

| Failure in rev. 1 | Root cause | Fix |
|---|---|---|
| Nothing was scarce, so no ratio was a real ratio | Infinite nodes, unbounded land, no upkeep. Every bottleneck's answer was "place another golem," and golems were free. | **§3 Scarcity**: steam power with per-golem Coke upkeep, a 2-extractor cap per node, a bounded purchasable floor |
| The Clock Tower demanded less factory than the player already owned, and the hoard cap punished surplus with idle time | Stage rates costed at ~9 golems against a 40-golem factory; progress was binary fed/not-fed | **§7**: demands raised 3–4×; progress scales with delivered rate |
| The load-bearing spatial mechanic was the one thing the code refuses to do | `GolemEntity.BeginRefine` is explicitly exempt from spatial routing because `IItemEndpoint.TryTake` is untyped | **§2**: golems get an internal inventory and a typed, quantified `Haul`; slot count becomes the gate |
| One activity at N=1,2,3 for four hours | The buffer ring was the only spatial verb and caps at 3 | Zeppelin gets the **Freight Link** |
| Two soft-locks; Slag/Glass backwards; manual era ~30 s | Bay cap too late and mispriced; untyped takes + capacity = deadlock | Bay cap from Phase 1; per-item-type capacity; **Slag Heap**; **Hand-Crank Bench** |

</details>

**Revision 3 changes:**

| Defect | Fix |
|---|---|
| **Steam upkeep did not bite, and its marginal cost was literally zero.** A flat 1 Coke/8 s per Boiler powering *up to* 8 golems means 7 of every 8 golems added are free and the 8th costs 7.5 Coke/min. Players optimise on the margin; the margin was zero. It also drained the starting Coke on a fixed hidden timer the player could not affect. | **§3.1**: Boiler consumption is now **proportional to powered golem count** — 1 Coke per powered golem per 10 s = **6 Coke/min each**. Every golem's cost is felt on placement, an idle boiler burns nothing, and the coal-and-steam line becomes **~41 % of the factory**. |
| **Every logistics golem was a no-op.** §2 had `Haul` fill *input* stock and `Push` empty *output* stock, with only `Assemble` moving between them — so `Haul → Push` (the Scavenger, every extractor, every belt-to-buffer golem: ~25 of the endgame factory) filled input stock to the cap and stalled forever. | **§2**: a program containing **no `Assemble` step treats input stock as output stock**. Stated explicitly, because "Push empties the entire output stock" is also what makes byproducts free. |
| §5.3(b) still framed convergence as rev. 1's ring-vs-mixed-belt binary, and never showed the 4-input case actually fits | **§5.3(b)** rewritten around **carrier golems**: a carrier holds several `Haul` steps and delivers multiple types through one face, so chassis slots buy back buffer faces. The R17 two-stage mix is worked through. |
| The Overclocker had no verb — one chassis got a spatial ability and the other got a number | **§6**: Overclocker alone may hold **`Repeat`**, re-running its `Assemble` N times from one Haul batch. Competes for a slot against a third input type on the same chassis. |
| Extractor rate omitted its `Push` step (240/min stated, **150** actual); coker stated 50/min, **35** actual | Corrected throughout §3.2, §7, §9. Node cap is 300/min, and stage 4 needs ~8 extractors across ~8 sites — which *strengthens* the go-further-out pressure. |
| The Workbench had no decisions left — recipe + chassis fully determined the program | **§2**: `Haul` quantity is **player-set** (1–12). Batch size trades Push/Haul overhead against held stock and buffer pressure, and the right answer differs per line. |
| Hoard-blitz hole: the multiplier read delivery rate, and delivering from a warehouse *is* a delivery rate | **§7**: multiplier derived from `min(deliveryRate, freshProductionRate)` over a 60 s window. **Withdrawn 2026-11-06** at the user's call: any source counts (`contracts-design.md` §11). |
| Phase 2's blackout was an ambush | **§8**: Boiler fuel gauge with a countdown and a 25 % alert. |
