Folder rules for the Clock Tower. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise. The tower's art (six
hand-replaced sprites, `ClockTowerArt.CanvasHeight`) is covered in `Tools/Art/CLAUDE.md`.

# The Clock Tower

**The Clock Tower is a fixture** (`PlaceableBuilding.IsFixture`,
`BuildModeController.RegisterFixture`): it stands from the start on a 3×3 footprint in the
town square (`Core/World/TownSquare`), is not in the build menu (`SandboxWorld.Placeables`
versus `AllPlaceables`), and the wrecking bar refuses it. A fixture is never in
`Build.Buildings`, which is the player's factory (save, refund, counts); its progress saves in
`ProgressEntry` instead. It is roped off until the ledger has seen a Zeppelin
(`ClockTowerSite.OpenWhen`). **Any delivered good counts** toward a stage, from a line or from
stock (`docs/contracts-design.md` §11); there is no fresh-production rule, and the 3× cap and
the meter's 60-unit clamp are what keep a stockpile from finishing a stage instantly. A line's
supply meter drains at the rate the line delivers, up to its demand, so a stage short of its
demand takes nominal × demand ÷ supply (`ClockTowerSite.DecayRate`, tuning-data-spec §18).
