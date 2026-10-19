# Golem Factory — Phase 2 playtest: the first fifteen minutes

**Not runnable yet.** This judges Phase 2 (`docs/roadmap.md`): the Workbench hint, grey and red,
and hover (`docs/onboarding-feedback-design.md`), and the rewritten chapters 1–4
(`docs/guide-chapters-design.md`). Run it once those are built.

**Why a separate script.** `testscript/phase-1-playtest.md` judges crates and the pack over the
whole game, and lets the player skip chapters with F9. This one times a **new player** through
chapters 1–4 **without** F9, and a player who has already played can't be new again. Run this one
first with a fresh player if both are still to run; the Phase 1 script's start, keys and report
sections apply here unchanged.

---

## Who and how

1. **A player who has never played Golem Factory.** Not the developer, and not someone who
   watched a session. Tell them only: "Follow the guide. Think aloud if you like."
2. **A fresh game.** Move `save.json` out of the user folder
   (`%APPDATA%\Godot\app_userdata\Golem Factory\`) and put it back afterwards, so the guide
   starts at step 1. Playtest mode on
   (`"playtest": true` in `godot/data/sandbox.json`), so the report writes.
3. **No F9 in chapters 1–4.** The report records every kit use; a kit use before chapter 5 voids
   the timing, but the session's notes still count.
4. **Don't help.** If they ask, note the question and the step, and answer only after a full
   minute stuck. Mark that step "helped" in the notes.
5. Stop at the start of chapter 5 (the guide's counter reads `CHAPTER 5`), or carry on with the
   Phase 1 script.

## The timing

The report's step timings already record each step's real seconds (`PlaytestSession`). Phase 2
adds one line at the top: **"Chapters 1–4: mm:ss (target 15:00) · kit used: no"**, from the first
frame to entering `r4-card`.

| Ch. | Budget | Watch |
|---|---|---|
| 1 | 5:00 | `program` (was 8:00): did they follow the glowing card and socket? `work`: did they turn while carrying? |
| 2 | 4:30 | cranking for the Presser (about 3¾ of it is the bench, and no text can cut that) |
| 3 | 3:30 | `pipes2`, and whether the coker read grey while it waited |
| 4 | 2:00 | `turn` (was 4:30): which golem did R turn? `stall`: did they hover the red golem? |

**Passes when** chapters 1–4 take under 15:00 with no kit use. If it fails, the step timings say
where; a chapter over budget by its cranking alone goes to Phase 3's tuning, not to the guide.

## What to watch

- **The hint.** Did they drag the outlined card to the outlined socket, or hunt in the vault? Did
  they ever think a card had been placed for them? (It never is.)
- **Turning.** Did R ever turn the wrong golem? Did the teal and gold tiles, with their arrows,
  tell them which way the golem faced before they set it down?
- **Grey and red.** A Presser waiting for Scrap is grey; a golem facing nothing is red. Did they
  ever go to fix a grey golem? Ignore a red one?
- **Hover.** Did they hover golems without being told? On a red golem, did the card's fix line
  tell them what to do?
- **The alerts strip.** With only grey golems it reads "No golem needs you · N waiting for
  goods." Did that read as calm, or as a problem?
- **Straining.** A busy golem's "nearly full" is on hover only. Did anything back up without
  them noticing in time?

## The questions

Asked as **PLAYTEST · your call** cards at the step named (`PlaytestSession`). New in Phase 2:

| Asked at step | Question | Answers |
|---|---|---|
| `depot` | The glowing card and socket: did they show you what to drag? | Clear · Took a moment · In my way |
| `r4-card` | Turning golems with R: how did it go? | Easy · Fiddly · I turned the wrong one |
| `r4-card` | Waiting golems go grey, stuck ones red. Could you tell which needed you? | Always · Mostly · No |
| `r4-card` | Hovering a golem shows its arrows and, when stuck, why. Did you use it? | Often · Once or twice · Didn't know |
| `r4-card` | When the golem stopped red, did its badge and hover tell you what was wrong? (`badge`, reworded) | Clearly · Vaguely · I didn't hover it |
| `expand` | A busy golem's "nearly full" is on hover only. Did you ever miss a warning? | Never · Once (say where) · Often |

The existing questions stay where they are. Four cards open chapter 5; a player may skip some.

## Numbers to judge

| Number | Value |
|---|---|
| Hover delay before the card | 0.25 s |
| `Waiting` dwell (tint and badge) | 2 s |
| The hint's pulse | 1.2 s, alpha 0.35 to 0.95 |
| A chapter 1–4 guide text | at most 240 characters |
