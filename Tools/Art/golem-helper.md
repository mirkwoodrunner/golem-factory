# Golem set: the helper's checklist

For a helper agent (a smaller model) running a golem chassis' mechanical steps. The lead writes
the chassis' draft script and `chassis.json`, decides what to fix, and talks to the user. You run
the pipeline, read its numbers and report. Read this file, then `Tools/Art/CLAUDE.md`; nothing
else unless the lead's brief names it.

Paths are from the repo root, `G:\GitHub\golem-factory`. `<set>` is the folder the lead names,
`public/images/S1/<chassis>/`.

## Hard rules

- **Never write into `godot/art/`** except through `golem_set.py <set> install`, and only when
  the lead's brief says to install.
- **Never edit** the draft script, `chassis.json`'s `picks` or `approved`, the style bible or the
  pipeline doc. If a step needs one changed, stop and report what and why.
- **Never commit, push or `git stash`.** Commit only when the brief says so, with its message.
  `git add` here fails at random with `Permission denied`: retry it, and never commit on an
  empty index (root `CLAUDE.md`, "Git in this repo").
- **Don't stop processes you didn't start.** `golem_set.py` starts and stops its own ComfyUI and
  Godot. Never `taskkill` by image name: the user may have the editor open.
- **Don't look at images unless the brief asks.** They cost the most usage. The numbers below
  say what you need. The lead and the user look at the grid.
- **Report, don't redesign.** If something fails twice the same way, stop and report.

## The steps

```sh
python Tools/Art/golem_set.py <set> run         # draft → render → pick → compose → check → review
```

Run it in the background: the render takes about 15 minutes (5 of them ComfyUI loading). Then:

1. **Render.** It ends with `render: 3 facings in N min` and `stopped ComfyUI`. If ComfyUI
   didn't come up, report the last 20 lines of the log it names. A rerun skips the facings
   already on disk; `--force` renders again only if the brief says so.
2. **Pick.** One line per facing: the pick, and `auto would pick` if `chassis.json` overrides.
   Under each, every candidate's numbers; `FAILS ...` marks the ones that can't be used. If a
   facing has **no** passing candidate, the run stops: report that facing's FAILS reasons.
3. **Check.** `check: N FAIL, M WARN`. Report every FAIL line verbatim. Zero FAIL is required
   for install. Common ones: [7] off-palette (a ramp missing from `chassis.json`'s `ramps`);
   [10] tints too close (the body is too dark or too grey); [11] silhouette over 0.50 against
   another chassis (a draft change: the lead's).
4. **Review.** It writes `grid.png`, `review-1.png` and `review.json` in `<set>`, and
   `candidates_<facing>.png` from the pick. Give the lead their paths.

One step alone: `python Tools/Art/golem_set.py <set> pick` (or `compose`, `check`, `review`).
After the lead changes the draft: `draft`, then `compose`, `check`, `review`. The bases are
rendered from the base drafts, so a change to a base draft needs `render --force` and `pick`.

## Install (only when the brief says the user approved)

The lead sets `"approved"` in `chassis.json` first; install refuses without it.

```sh
python Tools/Art/golem_set.py <set> install
```

It reruns the check, copies the 28 frames into `godot/art/`, writes their manifest entries,
imports them and runs the `art`, `golems` and `golem-motion` scenarios. Report each
`[scenario …]` line. Then, unless the brief says otherwise:

```sh
python Tools/Art/golem_set.py <set> verify      # must say 28 of 28 reproduce
python Tools/Art/check_art.py                   # report its last line
python -m unittest discover Tools/Art/tests     # report Ran/OK
```

## Your report

Keep it short, in this order:

1. The command(s) run and how long the render took.
2. Picks per facing, and any `auto would pick` disagreement.
3. `check:` FAIL/WARN counts and every FAIL line.
4. The paths written (grid, review, candidates), or the scenario lines after an install.
5. Anything that failed, with the exact error text, and what you didn't do because of it.
