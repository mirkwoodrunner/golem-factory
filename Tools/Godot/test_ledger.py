"""Regenerate docs/godot-test-ledger.md: where every Unity test stands in the Godot port.

    python Tools/Godot/test_ledger.py           # rewrite the ledger
    python Tools/Godot/test_ledger.py --check   # exit 1 if it is stale, or a file is unassigned

Rule 2 of docs/godot-conversion-plan.md: every Unity test is accounted for -- ported, or
held for a named milestone, or retired with a reason. Each milestone PR regenerates this.

A Unity test FILE is "ported" when a file with the same relative path exists under
godot/Core.Tests (PlayMode files included: a PlayMode suite ported to a unit test lands at
the same relative path). Counts are [Test]/[TestCase]/[UnityTest] attributes, which is what
the Unity Test Runner counts too, give or take parameterised sources.
"""
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UNITY = os.path.join(REPO, "Assets", "Tests")
CORE = os.path.join(REPO, "godot", "Core.Tests")
LEDGER = os.path.join(REPO, "docs", "godot-test-ledger.md")
TEST_ATTR = re.compile(r"^\s*\[(?:Test|TestCase|UnityTest)\b", re.M)

# Held files -> (milestone, what brings them across). Every unported Unity file must be
# named here; --check fails on one that is not, so nothing drops off the ledger silently.
HELD = {
    # G2: the rules move into Core services, and these become unit tests.
    "AssemblyLine/AssemblyLineGatingTests.cs": ("G2", "AssemblyLineStateHolder rules"),
    "Buildings/AssemblyBayCapTests.cs": ("G2", "StationService (assembly bay)"),
    "Buildings/AssemblyBayStructureTests.cs": ("G2", "StationService (assembly bay)"),
    "Buildings/GolemConstructionStationTests.cs": ("G2", "StationService"),
    "Buildings/GolemDismantleTests.cs": ("G2", "StationService (dismantle)"),
    "Buildings/HandCrankBenchTests.cs": ("G2", "HandCrankBench rules"),
    "Buildings/PlacedStationWiringTests.cs": ("G2", "StationService"),
    "ClockTower/ClockTowerSiteHolderTests.cs": ("G2", "ClockTowerSite wiring"),
    "Golems/BeltGolemHandoffTests.cs": ("G2", "GolemEntity + BeltNetwork (PlayMode only for scene setup)"),
    "Golems/GolemRefineTests.cs": ("G2", "GolemEntity (PlayMode only for scene setup)"),
    "Player/BoilerHandRefuelTests.cs": ("G2", "InteractionService"),
    "Player/BuildDemolitionRefundTests.cs": ("G2", "BuildService (refund)"),
    "Player/BuildDragRunTests.cs": ("G2", "BuildService (drag runs)"),
    "Player/BuildingRebuildTests.cs": ("G2", "BuildService (rebuild)"),
    "Player/BuildModeControllerTests.cs": ("G2", "BuildService"),
    "Player/BuildPlacementBoundsTests.cs": ("G2", "BuildService (bounds)"),
    "Player/GolemInteractableRefreshTests.cs": ("G2", "InteractionService"),
    "Player/GolemRepositioningTests.cs": ("G2", "InteractionService (carry/rotate)"),
    "Player/PlacedStationConfigurationTests.cs": ("G2", "StationService"),
    "Player/PlayerInteractorTests.cs": ("G2", "InteractionService"),
    "Player/WreckingBarGolemTests.cs": ("G2", "BuildService + StationService"),
    "Progression/BuildingSignalCoverageTests.cs": ("G2", "TechTreeProgressTracker rules"),
    "Save/BuildingPersistenceTests.cs": ("G2", "SaveLoadService over IPlacedBuilding"),
    "Save/GolemRespawnTests.cs": ("G2", "StationService (respawn)"),
    "Save/SaveLoadServiceTests.cs": ("G2", "SaveLoadService over IPlacedBuilding"),
    "UI/AssemblyBayRowPolicyTests.cs": ("G2", "StationService (assembly bay)"),
    "World/FloorExpansionTests.cs": ("G2", "FloorExpansionService rules"),
    "World/ResourceNodeMarkerTests.cs": ("G2", "SandboxSetup / node registration"),
    # G4: scene-level presentation.
    "Player/ArtificerWalkAnimatorTests.cs": ("G4", "PlayerNode walk animation (rules already in Core)"),
    "Player/PlayerControllerTests.cs": ("G4", "PlayerNode movement (rules already in Core)"),
    # G7/G8: the screens.
    "UI/WorkbenchControllerTests.cs": ("G7", "WorkbenchSession"),
    "UI/WorkbenchQuantityTests.cs": ("G7", "WorkbenchSession"),
    "UI/AlertsPanelReconcileTests.cs": ("G8", "Alerts panel"),
    "UI/AssemblyLinePanelTests.cs": ("G8", "Assembly Line panel"),
    "UI/BuildMenuDemolishRowTests.cs": ("G5", "Build menu panel"),
    "UI/HudScreenExclusivityTests.cs": ("G8", "HudScreenPolicy scenario"),
    "UI/InventoryPanelTests.cs": ("G8", "Inventory panel"),
    "UI/ManagementPanelTests.cs": ("G8", "Management panel"),
    "UI/PatentBrowserPanelTests.cs": ("G8", "Patents tab"),
    "UI/SaveLoadPanelTests.cs": ("G9", "Save/load panel"),
    "UI/TechTreeRecipeReadoutTests.cs": ("G8", "Ledger recipe readout"),
    # G9: save files on disk.
    "Save/SaveCatalogCoverageTests.cs": ("G9", "Save panel's catalog vs the JSON data"),
    "Save/SaveFileIOTests.cs": ("G9", "Godot SaveFileIO (user://)"),
}

# Ported to a different path than the original.
MOVED = {
    "PlayMode/Golems/GolemSignalTriggerTests.cs": "Golems/GolemSignalTriggerTests.cs",
}


def count(path):
    with open(path, encoding="utf-8-sig") as f:
        return len(TEST_ATTR.findall(f.read()))


def build():
    rows, problems = [], []
    totals = {"ported": 0, "held": 0}
    for suite in ("EditMode", "PlayMode"):
        for root, _, files in os.walk(os.path.join(UNITY, suite)):
            for name in sorted(files):
                if not name.endswith(".cs"):
                    continue
                rel = os.path.relpath(os.path.join(root, name), os.path.join(UNITY, suite)).replace(os.sep, "/")
                n = count(os.path.join(root, name))
                if n == 0:
                    continue
                target = MOVED.get(f"{suite}/{rel}", rel)
                if os.path.exists(os.path.join(CORE, target)):
                    ported = count(os.path.join(CORE, target))
                    status = "ported" if ported >= n else f"ported ({ported}/{n})"
                    totals["ported"] += n
                    rows.append((suite, rel, n, status, f"`Core.Tests/{target}`"))
                elif rel in HELD:
                    milestone, how = HELD[rel]
                    totals["held"] += n
                    rows.append((suite, rel, n, f"held: {milestone}", how))
                else:
                    problems.append(f"{suite}/{rel} is neither ported nor assigned a milestone")
                    rows.append((suite, rel, n, "**UNASSIGNED**", ""))

    new = []
    for root, dirs, files in os.walk(CORE):
        dirs[:] = [d for d in dirs if d not in ("bin", "obj")]
        for name in sorted(files):
            if name.endswith(".cs"):
                rel = os.path.relpath(os.path.join(root, name), CORE).replace(os.sep, "/")
                in_unity = any(os.path.exists(os.path.join(UNITY, s, rel)) for s in ("EditMode", "PlayMode"))
                if not in_unity and rel not in MOVED.values() and count(os.path.join(root, name)):
                    new.append((rel, count(os.path.join(root, name))))

    total = totals["ported"] + totals["held"]
    lines = [
        "# Godot test ledger",
        "",
        "Generated by `python Tools/Godot/test_ledger.py`. **Don't edit this file by hand.**",
        "Rule 2 of [the conversion plan](godot-conversion-plan.md): every Unity test is",
        "ported, held for a named milestone, or retired with a reason.",
        "",
        f"**{totals['ported']} of {total} Unity tests ported** "
        f"({totals['held']} held). Counts are `[Test]`/`[TestCase]`/`[UnityTest]` attributes.",
        "",
        "| Suite | Unity file | Tests | Status | Where / how |",
        "|---|---|---|---|---|",
    ]
    for suite, rel, n, status, where in sorted(rows, key=lambda r: (r[3] != "ported", r[3], r[0], r[1])):
        lines.append(f"| {suite} | `{rel}` | {n} | {status} | {where} |")
    lines += ["", "## Tests that exist only on the Godot side", "",
              "| File | Tests |", "|---|---|"]
    lines += [f"| `Core.Tests/{rel}` | {n} |" for rel, n in new]
    return "\n".join(lines) + "\n", problems


def main():
    text, problems = build()
    for p in problems:
        print("error:", p)
    current = open(LEDGER, encoding="utf-8").read() if os.path.exists(LEDGER) else None
    if "--check" in sys.argv[1:]:
        stale = current != text
        if stale:
            print("docs/godot-test-ledger.md is stale; regenerate it")
        sys.exit(1 if stale or problems else 0)
    with open(LEDGER, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    print(text.splitlines()[6])
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
