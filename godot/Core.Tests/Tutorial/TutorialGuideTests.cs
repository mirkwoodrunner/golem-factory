using System.IO;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.Tutorial;
using GolemFactory.World;
using NUnit.Framework;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.Tutorial
{
    /// <summary>
    /// The step-by-step guide (G10, at the user's call). Walked end to end through a fresh
    /// Sandbox with the game's own verbs, so every step's "done" is a state the real game reaches.
    /// </summary>
    public class TutorialGuideTests
    {
        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            return SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
        }

        private static void Give(SandboxWorld world, string item, int quantity) =>
            world.Buffers.Deposit(world.StockpileBufferId, item, quantity);

        private static PlaceableBuilding Place(SandboxWorld world, string key, int x, int y)
        {
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == key).Prefab);
            world.Build.PlaceOrRemove(new Vector2Int(x, y));
            world.Build.CancelPlacement();
            return world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == new Vector2Int(x, y));
        }

        private static string StepId(SandboxWorld world)
        {
            world.Tutorial.Update();
            return world.Tutorial.Current?.Id ?? "finished";
        }

        [Test]
        public void TheSandboxStartsTheGuide_OnItsFirstStep_PointingAtTheScrapStall()
        {
            SandboxWorld world = Compose();

            Assert.IsNotNull(world.Tutorial, "sandbox.json turns the guide on");
            Assert.IsTrue(world.Tutorial.IsShowing);
            Assert.AreEqual("scrap", StepId(world));
            SandboxSetup.NodeEntry stall = world.Setup.nodes.Single(n => n.id == "ScrapNode");
            Assert.AreEqual(new Vector2Int(stall.x, stall.y), world.Tutorial.TargetCell);
            Assert.AreEqual("Scrap  0 / 20", world.Tutorial.Progress);
        }

        [Test]
        public void TheWholeGuide_FromAColdStartToTheLastChapter()
        {
            SandboxWorld world = Compose();
            DefinitionSet defs = world.Definitions;

            Give(world, ItemType.Scrap, 20);
            Assert.AreEqual("coal", StepId(world));

            Give(world, ItemType.Coal, 5);
            Assert.AreEqual("coke", StepId(world));

            Give(world, ItemType.Coke, 5);
            Assert.AreEqual("iron", StepId(world));

            Give(world, ItemType.IronPlate, 10);
            Assert.AreEqual("boiler", StepId(world));
            Assert.AreEqual("BoilerPrefab", world.Tutorial.Current.MenuKey, "the step lights the build menu's Boiler row");

            Give(world, ItemType.Scrap, 100);
            Give(world, ItemType.IronPlate, 10);
            PlaceableBuilding boiler = Place(world, "BoilerPrefab", world.Tutorial.BoilerSpot.x, world.Tutorial.BoilerSpot.y);
            Assert.AreEqual("fuel", StepId(world));
            Assert.AreEqual(boiler.Cell, world.Tutorial.TargetCell, "points at the boiler it means");

            Assert.IsTrue(world.Interactor.TryRefuelBoiler(boiler.GetPart<PlaceableBoiler>()), "precondition: fuelled");
            Assert.AreEqual("golem", StepId(world));

            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["ClockworkScavenger"], out GolemEntity golem));
            Assert.AreEqual("program", StepId(world));

            golem.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            golem.Program.TryAddAppendage(defs.Appendages["ExtractScrap"]);
            golem.Program.TryAddAppendage(defs.Appendages["PushOutput"]);
            Assert.AreEqual("depot", StepId(world));

            Place(world, "DepotPrefab", -8, -14);
            Assert.AreEqual("work", StepId(world));

            // Scrap stall behind, depot in front, boiler beside: the marked layout.
            golem.SetPlacement(world.Tutorial.GolemSpot, Facing.North);
            world.Clock.Play();
            for (int frame = 0; frame < 600 && StepId(world) == "work"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("gears", StepId(world), "a finished cycle ends chapter 1");

            // --- Chapter 2: the Brass Presser ---------------------------------------------------
            Give(world, ItemType.Gear, 10);
            Assert.AreEqual("claim", StepId(world));

            // Claim through the real board: other cards first if Scrap Reclamation is not yet
            // offered, exactly as the step tells the player to.
            Give(world, ItemType.Scrap, 400);
            Give(world, ItemType.IronPlate, 100);
            Give(world, ItemType.Gear, 20);
            Give(world, ItemType.Coal, 40);
            Give(world, ItemType.Coke, 100);
            var claimed = new System.Collections.Generic.List<string>();
            for (int attempt = 0; attempt < 30 && StepId(world) == "claim"; attempt++)
            {
                int slot = Enumerable.Range(0, world.AssemblyLine.SlotCount)
                    .FirstOrDefault(i => world.AssemblyLine.GetCard(i)?.appendage?.name == "AssembleScrapReclamation");
                claimed.Add(world.AssemblyLine.GetCard(slot)?.name);
                Assert.IsTrue(world.AssemblyLineBoard.Claim(slot), world.AssemblyLineBoard.Status + " after " + string.Join(", ", claimed)
                    + " | waiting: " + string.Join(", ", world.AssemblyLine.WaitingCards.Select(c => c.name)));
            }
            Assert.AreEqual(1, claimed.Count, "Scrap Reclamation is on show without claiming anything else: " + string.Join(", ", claimed));
            Assert.AreEqual("presser", StepId(world));

            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["BrassPresser"], out GolemEntity presser));
            Assert.AreEqual("pipe", StepId(world));
            Assert.AreEqual("SteamPipePrefab", world.Tutorial.Current.MenuKey);
            CollectionAssert.AreEqual(world.Tutorial.PipeSpots, world.Tutorial.Current.Spots, "both pipe tiles marked");

            foreach (Vector2Int pipe in world.Tutorial.PipeSpots)
            {
                Place(world, "SteamPipePrefab", pipe.x, pipe.y);
            }
            Assert.AreEqual("depot2", StepId(world));

            Place(world, "DepotPrefab", world.Tutorial.PresserDepotSpot.x, world.Tutorial.PresserDepotSpot.y);
            Assert.AreEqual("program-presser", StepId(world));

            presser.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            Assert.IsTrue(presser.Program.TryAddAppendage(defs.Appendages["HaulScrap"]));
            Assert.IsTrue(presser.Program.TryAddAppendage(defs.Appendages["AssembleScrapReclamation"]));
            Assert.IsTrue(presser.Program.TryAddAppendage(defs.Appendages["PushOutput"]));
            Assert.AreEqual("work-presser", StepId(world));

            // Depot 1 behind (Scrap from the stockpile), depot 2 in front, a pipe beside.
            presser.SetPlacement(world.Tutorial.PresserSpot, Facing.North);
            for (int frame = 0; frame < 1200 && StepId(world) == "work-presser"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("coking-card", StepId(world),
                $"the Presser's first cycle ends chapter 2 (presser {presser.Program.State}, {presser.StallReason} {presser.StallResourceId})");

            // --- Chapter 3: the Coke line -------------------------------------------------------
            int cokingSlot = Enumerable.Range(0, world.AssemblyLine.SlotCount)
                .FirstOrDefault(i => world.AssemblyLine.GetCard(i)?.appendage?.name == "AssembleCoking");
            Assert.AreEqual("AssembleCoking", world.AssemblyLine.GetCard(cokingSlot)?.appendage?.name, "Coking is on show");
            Assert.IsTrue(world.AssemblyLineBoard.Claim(cokingSlot), world.AssemblyLineBoard.Status);
            Assert.AreEqual("presser2", StepId(world));

            Give(world, ItemType.Scrap, 200);
            Give(world, ItemType.IronPlate, 60);
            Give(world, ItemType.Gear, 20);
            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["BrassPresser"], out GolemEntity coker));
            Assert.AreEqual("boiler2", StepId(world));

            Place(world, "BoilerPrefab", world.Tutorial.Boiler2Spot.x, world.Tutorial.Boiler2Spot.y);
            Assert.AreEqual("pipes2", StepId(world));
            CollectionAssert.AreEqual(world.Tutorial.Pipe2Spots, world.Tutorial.Current.Spots);

            foreach (Vector2Int pipe in world.Tutorial.Pipe2Spots)
            {
                Place(world, "SteamPipePrefab", pipe.x, pipe.y);
            }
            Assert.AreEqual("coal-order", StepId(world));

            // Order the truckload at the stall, as the player does, and wait for the cart.
            SandboxSetup.NodeEntry coalStall = world.Setup.nodes.Single(n => n.id == "CoalNode");
            world.Interactor.Position = new Compat.Vector3(coalStall.x, coalStall.y + 1f, 0f);
            world.Interactor.Poll();
            Assert.IsTrue(world.Interactor.Interact(), "ordered: " + world.Interactor.LastStatusMessage);
            for (int frame = 0; frame < 600 && StepId(world) == "coal-order"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("program-coker", StepId(world), "the cart arrived");

            coker.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            Assert.IsTrue(coker.Program.TryAddAppendage(defs.Appendages["ExtractScrap"]));
            Assert.IsTrue(coker.Program.TryAddAppendage(defs.Appendages["AssembleCoking"]));
            Assert.IsTrue(coker.Program.TryAddAppendage(defs.Appendages["PushOutput"]));
            Assert.AreEqual("work-coker", StepId(world));

            // Coal stall behind, the new boiler in front -- and steam from the FIRST boiler, along
            // the pipes, because the new one starts with no Coke at all.
            PlaceableBoiler boiler2 = world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == world.Tutorial.Boiler2Spot).GetPart<PlaceableBoiler>();
            Assert.AreEqual(0, boiler2.Boiler.CokeStock, "precondition: the new boiler starts empty");
            coker.SetPlacement(world.Tutorial.CokerSpot, Facing.North);
            for (int frame = 0; frame < 1200 && StepId(world) == "work-coker"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("patent", StepId(world),
                $"the coking Presser's first cycle ends chapter 3 (coker {coker.Program.State}, {coker.StallReason} {coker.StallResourceId})");
            Assert.Greater(boiler2.Boiler.CokeStock, 0, "its Coke went into the new boiler's firebox");

            // --- Chapter 4: a patent, R, and a stall on purpose ------------------------------------
            Assert.IsTrue(world.Patents.TryPatent(new GolemFactory.Blueprints.Blueprint(
                "BP-001", "LocalPlayer", golem.Program.chassis, golem.Program.logicCore, golem.Program.appendages.ToList())));
            Assert.AreEqual("scav2", StepId(world));

            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["ClockworkScavenger"], out GolemEntity scav2));
            Assert.AreEqual("stamp", StepId(world));

            // Stamping is the Patents tab's Load: the blueprint's program onto the new golem.
            var blueprint = world.Patents.Blueprints.Values.Single();
            scav2.Program.logicCore = blueprint.LogicCore;
            foreach (var card in blueprint.Appendages)
            {
                scav2.Program.TryAddAppendage(card);
            }
            Assert.AreEqual("depot3", StepId(world));

            Place(world, "DepotPrefab", world.Tutorial.Depot3Spot.x, world.Tutorial.Depot3Spot.y);
            Assert.AreEqual("turn", StepId(world));
            Assert.AreEqual(Facing.East, world.Tutorial.Current.SpotFacing, "the marker shows east");

            scav2.SetPlacement(world.Tutorial.Scav2Spot, Facing.East);
            for (int frame = 0; frame < 900 && StepId(world) == "turn"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("stall", StepId(world), $"facing the depot, it works ({scav2.StallReason} {scav2.StallResourceId})");

            scav2.SetPlacement(world.Tutorial.Scav2Spot, Facing.North);
            for (int frame = 0; frame < 300 && StepId(world) == "stall"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual(Events.StallReason.NoSourceAtTile, scav2.StallReason, "the street behind it: 'nothing behind me'");
            Assert.AreEqual("unstall", StepId(world), $"facing away, it stalls ({scav2.Program.State} {scav2.StallReason} {scav2.StallResourceId} step {scav2.Program.CurrentStepIndex} cell {scav2.Cell} facing {scav2.Facing})");

            scav2.SetPlacement(world.Tutorial.Scav2Spot, Facing.East);
            for (int frame = 0; frame < 900 && StepId(world) == "unstall"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("r4-card", StepId(world), "turned back, it resumes");

            // --- Chapter 5: metal, and its Slag ---------------------------------------------------
            Give(world, ItemType.Scrap, 400);
            Give(world, ItemType.IronPlate, 200);
            Give(world, ItemType.Gear, 60);
            Give(world, ItemType.Coke, 120);
            int r4 = Enumerable.Range(0, world.AssemblyLine.SlotCount)
                .FirstOrDefault(i => world.AssemblyLine.GetCard(i)?.appendage?.name == "AssembleIronSmelting");
            Assert.AreEqual("AssembleIronSmelting", world.AssemblyLine.GetCard(r4)?.appendage?.name, "Iron Smelting is on show");
            Assert.IsTrue(world.AssemblyLineBoard.Claim(r4), world.AssemblyLineBoard.Status);
            Assert.AreEqual("hauler", StepId(world));

            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["AetherHauler"], out GolemEntity smelter));
            Assert.AreEqual("smelt-depots", StepId(world));
            Place(world, "DepotPrefab", world.Tutorial.SmeltInSpot.x, world.Tutorial.SmeltInSpot.y);
            Place(world, "DepotPrefab", world.Tutorial.SmeltOutSpot.x, world.Tutorial.SmeltOutSpot.y);
            Assert.AreEqual("pipes3", StepId(world));
            foreach (Vector2Int pipe in world.Tutorial.Pipe3Spots)
            {
                Place(world, "SteamPipePrefab", pipe.x, pipe.y);
            }
            Assert.AreEqual("program-smelter", StepId(world));

            // Two Hauls of the one card, set to two goods with the picker.
            smelter.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            smelter.Program.TryAddAppendage(defs.Appendages["HaulScrap"]);
            smelter.Program.SetQuantityAt(0, 2);
            smelter.Program.TryAddAppendage(defs.Appendages["HaulScrap"]);
            smelter.Program.SetItemTypeAt(1, ItemType.Coke);
            Assert.IsTrue(smelter.Program.TryAddAppendage(defs.Appendages["AssembleIronSmelting"]));
            Assert.IsTrue(smelter.Program.TryAddAppendage(defs.Appendages["PushOutput"]));
            Assert.AreEqual("work-smelter", StepId(world));

            smelter.SetPlacement(world.Tutorial.SmelterSpot, Facing.North);
            for (int frame = 0; frame < 900 && StepId(world) == "work-smelter"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("slag-heap", StepId(world), $"the smelter cycles ({smelter.StallReason} {smelter.StallResourceId})");
            Assert.Greater(world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.Slag), 0, "and makes Slag");

            Place(world, "SlagHeapPrefab", world.Tutorial.SlagHeapSpot.x, world.Tutorial.SlagHeapSpot.y);
            Assert.AreEqual("carrier", StepId(world));
            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["BrassPresser"], out GolemEntity carrier));
            Assert.AreEqual("program-carrier", StepId(world));

            carrier.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            carrier.Program.TryAddAppendage(defs.Appendages["HaulScrap"]);
            carrier.Program.SetItemTypeAt(0, ItemType.Slag);
            carrier.Program.SetQuantityAt(0, 4);
            carrier.Program.TryAddAppendage(defs.Appendages["HaulScrap"]);
            carrier.Program.SetItemTypeAt(1, ItemType.Coke);
            carrier.Program.TryAddAppendage(defs.Appendages["PushOutput"]);
            Assert.AreEqual("work-carrier", StepId(world));

            Give(world, ItemType.Slag, 8);
            carrier.SetPlacement(world.Tutorial.CarrierSpot, Facing.North);
            for (int frame = 0; frame < 1200 && StepId(world) == "work-carrier"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("copper", StepId(world), $"the heap burns Slag ({carrier.StallReason} {carrier.StallResourceId})");

            // --- Chapter 6: belts, and a label ------------------------------------------------------
            // Order Copper at the stall, wait for the cart, take one by hand.
            SandboxSetup.NodeEntry copperStall = world.Setup.nodes.Single(n => n.id == "CopperOreNode");
            world.Interactor.Position = new Compat.Vector3(copperStall.x, copperStall.y + 1f, 0f);
            world.Interactor.Poll();
            Assert.IsTrue(world.Interactor.Interact(), "ordered Copper: " + world.Interactor.LastStatusMessage);
            for (int frame = 0; frame < 600 && !world.Nodes.TryGetNode("CopperOreNode", out var n) | (n != null && n.RemainingQuantity == 0); frame++)
            {
                world.Advance(1f / 30f);
            }
            world.Interactor.Poll();
            Assert.IsTrue(world.Interactor.Interact(), "harvested one: " + world.Interactor.LastStatusMessage);
            Assert.AreEqual("belts", StepId(world));

            // A dragged run: build mode's own drag, which points the run along itself.
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == "BeltPrefab").Prefab);
            Vector2Int[] belts = world.Tutorial.BeltSpots;
            world.Build.Click(belts[0], false);       // press on the first tile...
            foreach (Vector2Int cell in belts.Skip(1))
            {
                world.Build.Hover(cell);              // ...drag along the run...
            }
            world.Build.Release();                    // ...and let go
            world.Build.CancelPlacement();
            Assert.AreEqual("pipes4", StepId(world), "a north run on the marked tiles");

            foreach (Vector2Int pipe in world.Tutorial.Pipe4Spots)
            {
                Place(world, "SteamPipePrefab", pipe.x, pipe.y);
            }
            Assert.AreEqual("scav3", StepId(world));

            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["ClockworkScavenger"], out GolemEntity extractor));
            extractor.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            extractor.Program.TryAddAppendage(defs.Appendages["ExtractScrap"]);
            extractor.Program.TryAddAppendage(defs.Appendages["PushOutput"]);
            Assert.AreEqual("work-extractor", StepId(world));

            extractor.SetPlacement(world.Tutorial.ExtractorSpot, Facing.North);
            for (int frame = 0; frame < 900 && StepId(world) == "work-extractor"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("copper-depot", StepId(world), $"ore rides the belt ({extractor.StallReason} {extractor.StallResourceId})");

            Place(world, "DepotPrefab", world.Tutorial.CopperDepotSpot.x, world.Tutorial.CopperDepotSpot.y);
            PlaceableDepot copperDepot = world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == world.Tutorial.CopperDepotSpot).GetPart<PlaceableDepot>();
            for (int press = 0; press < 30 && copperDepot.FilterItemType != ItemType.CopperOre; press++)
            {
                Assert.IsTrue(world.Interactor.TryRelabelDepot(copperDepot)); // [E] at the depot
            }
            Assert.AreEqual("unloader", StepId(world), "labelled Copper Ore by pressing E");

            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["ClockworkScavenger"], out GolemEntity unloader));
            unloader.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            unloader.Program.TryAddAppendage(defs.Appendages["HaulScrap"]);
            unloader.Program.SetItemTypeAt(0, ItemType.CopperOre);
            unloader.Program.TryAddAppendage(defs.Appendages["PushOutput"]);
            Assert.AreEqual("work-unloader", StepId(world));

            unloader.SetPlacement(world.Tutorial.UnloaderSpot, Facing.North);
            for (int frame = 0; frame < 900 && StepId(world) == "work-unloader"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("expand", StepId(world), $"the unloader empties the belt into the labelled depot ({unloader.StallReason} {unloader.StallResourceId})");

            // --- Chapter 7: room to grow, and keeping it -------------------------------------------
            Give(world, ItemType.Scrap, 200);
            Give(world, ItemType.IronPlate, 100);
            Assert.IsTrue(world.AssemblyLineBoard.ExtendFloor(), world.AssemblyLineBoard.Status);
            Assert.AreEqual("bays", StepId(world));
            Assert.IsTrue(world.AssemblyLineBoard.UpgradeBays(), world.AssemblyLineBoard.Status);
            Assert.AreEqual("ledger", StepId(world));
            world.LedgerReadout.Select("r1.coking");
            Assert.AreEqual("save", StepId(world));

            string savePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "golem-factory-guide-" + System.Guid.NewGuid() + ".json");
            try
            {
                world.SaveTo(savePath);
                Assert.AreEqual("load", StepId(world));
                world.LoadFrom(savePath);
                Assert.AreEqual("copper-ingot", StepId(world), "the load lands past the Save step, and completes Load");
            }
            finally
            {
                System.IO.File.Delete(savePath);
            }

            // --- Chapter 8: goal steps ---------------------------------------------------------------
            // Each asks for a card -- which the line must be showing -- then a good. Made here by
            // depositing it: the lines that make them are chapters 2-6's shapes again.
            (string step, string card, string good)[] goals =
            {
                ("copper-ingot", "AssembleCopperSmelting", ItemType.CopperIngot),
                ("zinc-ingot", "AssembleZincSmelting", ItemType.ZincIngot),
                ("brass", "AssembleBrassAlloying", ItemType.Brass),
                ("casing", "AssembleCasingPress", ItemType.Casing),
                ("glass", "AssembleGlassmaking", ItemType.Glass),
                ("lens", "AssembleLensGrinding", ItemType.Lens),
                ("mainspring", "AssembleMainspringWinding", ItemType.Mainspring),
                ("aether-cell", "AssembleAetherContainment", ItemType.AetherCell),
            };
            Give(world, ItemType.ZincOre, 8);   // bought at the Zinc stall
            Give(world, ItemType.Aether, 4);    // and the Aether stall
            foreach ((string step, string card, string good) in goals)
            {
                world.Advance(1f / 30f);
                Assert.AreEqual(step, StepId(world));
                var line = world.AssemblyLine;
                int slot = Enumerable.Range(0, line.SlotCount).FirstOrDefault(i => line.GetCard(i)?.appendage?.name == card, -1);
                Assert.GreaterOrEqual(slot, 0, $"{step}: {card} is on the line");
                Give(world, ItemType.Scrap, 100);
                Give(world, ItemType.IronPlate, 100);
                Give(world, ItemType.Gear, 50);
                foreach (var cost in line.GetCurrentCostBundle(slot))
                {
                    Give(world, cost.itemType, cost.quantity);
                }
                Assert.IsTrue(world.AssemblyLineBoard.Claim(slot), $"{step}: claimed {card}");
                Give(world, good, 10);
                for (int frame = 0; frame < 60; frame++) // the tech tree polls the stockpile, not every frame
                {
                    world.Advance(1f / 30f);
                }
            }
            Assert.AreEqual("zeppelin-card", StepId(world), "every good the Zeppelin costs has been made");

            // --- Chapter 9: the Zeppelin --------------------------------------------------------------
            var zline = world.AssemblyLine;
            int zslot = Enumerable.Range(0, zline.SlotCount).FirstOrDefault(i => zline.GetCard(i)?.chassis?.name == "ZeppelinFreightLoader", -1);
            Assert.GreaterOrEqual(zslot, 0, "the Zeppelin's card is on the line");
            foreach (var cost in zline.GetCurrentCostBundle(zslot))
            {
                Give(world, cost.itemType, cost.quantity);
            }
            Assert.IsTrue(world.AssemblyLineBoard.Claim(zslot));
            Assert.AreEqual("freight-card", StepId(world));

            // The Workbench offers only owned cards from its roster: Freight Launch must be both,
            // or the Zeppelin can never be programmed by hand (it once was neither).
            Assert.IsFalse(world.Workbench.VaultAppendages.Any(a => a.name == "FreightLaunch"), "precondition: not owned yet");
            for (int frame = 0; frame < 3; frame++)
            {
                world.Advance(1f / 30f); // the line rebalances on its tick
            }
            int launchSlot = Enumerable.Range(0, zline.SlotCount).FirstOrDefault(i => zline.GetCard(i)?.appendage?.name == "FreightLaunch", -1);
            Assert.GreaterOrEqual(launchSlot, 0, "the guide keeps Freight Launch on show");
            Assert.IsTrue(world.AssemblyLineBoard.Claim(launchSlot), world.AssemblyLineBoard.Status);
            Assert.IsTrue(world.Workbench.VaultAppendages.Any(a => a.name == "FreightLaunch"), "claimed, the vault offers it");
            Assert.AreEqual("zeppelin", StepId(world));

            Give(world, ItemType.Mainspring, 6);
            Give(world, ItemType.Lens, 8);
            Give(world, ItemType.AetherCell, 3);
            Give(world, ItemType.Casing, 40);
            Give(world, ItemType.Brass, 60);
            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["ZeppelinFreightLoader"], out GolemEntity zeppelin));
            Assert.AreEqual("mast", StepId(world));

            Place(world, "FreightMastPrefab", world.Tutorial.MastSpot.x, world.Tutorial.MastSpot.y);
            Assert.AreEqual("zeppelin-pipe", StepId(world));
            Place(world, "SteamPipePrefab", world.Tutorial.ZeppelinPipeSpot.x, world.Tutorial.ZeppelinPipeSpot.y);
            Assert.AreEqual("program-zeppelin", StepId(world));

            zeppelin.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            Assert.IsTrue(zeppelin.Program.TryAddAppendage(defs.Appendages["HaulScrap"]));
            zeppelin.Program.SetItemTypeAt(0, ItemType.CopperOre);
            Assert.IsTrue(zeppelin.Program.TryAddAppendage(defs.Appendages["FreightLaunch"]));
            Assert.AreEqual("launch", StepId(world));

            int oreBefore = world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.CopperOre);
            zeppelin.SetPlacement(world.Tutorial.ZeppelinSpot, Facing.North);
            for (int frame = 0; frame < 1800 && StepId(world) == "launch"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("tower-visit", StepId(world), $"the Zeppelin flies ({zeppelin.StallReason} {zeppelin.StallResourceId})");
            Assert.Greater(world.Buffers.GetQuantity(world.StockpileBufferId, ItemType.CopperOre), oreBefore,
                "the ore it flew landed in the stockpile, through the mast");

            // --- Chapter 10: the Clock Tower -------------------------------------------------------
            Assert.IsTrue(world.ClockTower.Site.IsOpen, "the Zeppelin took the rope down");
            world.Interactor.Position = new Compat.Vector3(0f, TownSquare.Top - 1, 0f); // walked out to the square
            world.Advance(1f / 30f);
            Assert.AreEqual("frame-section", StepId(world));

            var fline = world.AssemblyLine;
            int fslot = -1;
            for (int frame = 0; frame < 60 && fslot < 0; frame++)
            {
                world.Advance(1f / 30f);
                fslot = Enumerable.Range(0, fline.SlotCount).FirstOrDefault(i => fline.GetCard(i)?.appendage?.name == "AssembleFrameSection", -1);
            }
            Assert.GreaterOrEqual(fslot, 0, "the Frame Section card is on the line");
            foreach (var cost in fline.GetCurrentCostBundle(fslot))
            {
                Give(world, cost.itemType, cost.quantity);
            }
            Assert.IsTrue(world.AssemblyLineBoard.Claim(fslot));
            Give(world, ItemType.FrameSection, 1);
            for (int frame = 0; frame < 60; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("tower-feed", StepId(world));

            // Six a minute, through the same input a golem's push uses, until the Foundation is laid.
            Assert.IsTrue(world.Endpoints.TryGetEndpoint(TownSquare.TowerOrigin, out IItemEndpoint tower));
            for (int second = 0; second < 900 && StepId(world) != "done"; second++)
            {
                if (second % 10 == 0)
                {
                    // A line that MAKES them: the tower counts only fresh production, so a
                    // stockpile fed to it builds nothing (ClockTowerSite's fresh-production rule).
                    EventBus.Publish(new ItemAssembledEvent("FrameLine", ItemType.FrameSection, 1, world.Clock.CurrentTick));
                    Assert.IsTrue(tower.TryGive(new Belts.ItemStack { ItemType = ItemType.FrameSection }), $"second {second}: the tower takes it");
                }
                for (int frame = 0; frame < 30; frame++)
                {
                    world.Advance(1f / 30f);
                }
            }
            Assert.AreEqual("done", StepId(world), "the Foundation is laid: " +
                GolemFactory.ClockTower.ClockTowerReadout.FormatHeadline(world.ClockTower.Site.BuildReading()));
            Assert.AreEqual("clock_tower_stage1", GolemFactory.ClockTower.ClockTowerArt.SpriteFor(world.ClockTower.Site));

            world.Tutorial.Finish();
            Assert.IsFalse(world.Tutorial.IsShowing);
        }

        [Test]
        public void StepsAlreadyDone_ArePassedStraightOver_AndTheGuideNeverGoesBack()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 20);
            Give(world, ItemType.Coal, 5);
            Give(world, ItemType.Coke, 5);

            Assert.AreEqual("iron", StepId(world), "three steps the stockpile already shows");

            world.Buffers.TryWithdraw(world.StockpileBufferId, ItemType.Coke, 5);
            Assert.AreEqual("iron", StepId(world), "spending the Coke does not undo the step");
        }

        [Test]
        public void WhileShortOfScrap_TheArrowPointsBackAtTheScrapStall()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 20);
            Give(world, ItemType.Coal, 5);
            Give(world, ItemType.Coke, 5);
            Give(world, ItemType.IronPlate, 10);
            Assert.AreEqual("boiler", StepId(world));
            world.Buffers.TryWithdraw(world.StockpileBufferId, ItemType.Scrap, 20);

            SandboxSetup.NodeEntry stall = world.Setup.nodes.Single(n => n.id == "ScrapNode");
            Assert.AreEqual(new Vector2Int(stall.x, stall.y), world.Tutorial.TargetCell);
        }

        [Test]
        public void TheFirstGolemsLayout_IsMarkedBesideTheScrapStall()
        {
            SandboxWorld world = Compose();
            SandboxSetup.NodeEntry stall = world.Setup.nodes.Single(n => n.id == "ScrapNode");
            var s = new Vector2Int(stall.x, stall.y);

            // depot / golem + boiler / stall, the golem facing the depot.
            Assert.AreEqual(s + new Vector2Int(0, 1), world.Tutorial.GolemSpot);
            Assert.AreEqual(s + new Vector2Int(0, 2), world.Tutorial.DepotSpot);
            Assert.AreEqual(s + new Vector2Int(1, 1), world.Tutorial.BoilerSpot);

            TutorialStep boiler = world.Tutorial.Steps.Single(t => t.Id == "boiler");
            TutorialStep depot = world.Tutorial.Steps.Single(t => t.Id == "depot");
            TutorialStep work = world.Tutorial.Steps.Single(t => t.Id == "work");
            Assert.AreEqual(world.Tutorial.BoilerSpot, boiler.Spot);
            Assert.AreEqual(world.Tutorial.DepotSpot, depot.Spot);
            Assert.AreEqual(world.Tutorial.GolemSpot, work.Spot);
            Assert.AreEqual(Facing.North, work.SpotFacing, "facing the depot");
        }

        [Test]
        public void ABoilerOnTheMarkedTile_CompletesTheStep_ButOneFarAwayDoesNot()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 200);
            Give(world, ItemType.Coal, 5);
            Give(world, ItemType.Coke, 5);
            Give(world, ItemType.IronPlate, 40);
            Assert.AreEqual("boiler", StepId(world));
            Assert.AreEqual(world.Tutorial.BoilerSpot, world.Tutorial.TargetCell, "the arrow points at the marked tile");

            Place(world, "BoilerPrefab", 8, 8);
            Assert.AreEqual("boiler", StepId(world), "a boiler whose steam cannot reach the golem's tile");

            Place(world, "BoilerPrefab", world.Tutorial.BoilerSpot.x, world.Tutorial.BoilerSpot.y);
            Assert.AreEqual("fuel", StepId(world));
            Assert.AreEqual(world.Tutorial.BoilerSpot, world.Tutorial.TargetCell, "fuel the one beside the golem's tile");
        }

        [Test]
        public void ADepotOffTheMarkedTile_DoesNotCompleteTheStep()
        {
            SandboxWorld world = Compose();
            DefinitionSet defs = world.Definitions;
            Give(world, ItemType.Scrap, 300);
            Give(world, ItemType.Coal, 5);
            Give(world, ItemType.Coke, 30); // 20 go into the boiler; the Coke step still sees 10
            Give(world, ItemType.IronPlate, 20);
            PlaceableBuilding boiler = Place(world, "BoilerPrefab", world.Tutorial.BoilerSpot.x, world.Tutorial.BoilerSpot.y);
            world.Interactor.TryRefuelBoiler(boiler.GetPart<PlaceableBoiler>());
            world.StarterStation.TryConstructGolem(defs.Chassis["ClockworkScavenger"], out GolemEntity golem);
            golem.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            golem.Program.TryAddAppendage(defs.Appendages["ExtractScrap"]);
            golem.Program.TryAddAppendage(defs.Appendages["PushOutput"]);
            Assert.AreEqual("depot", StepId(world));

            Place(world, "DepotPrefab", 8, 8);
            Assert.AreEqual("depot", StepId(world));
            Place(world, "DepotPrefab", world.Tutorial.DepotSpot.x, world.Tutorial.DepotSpot.y);
            Assert.AreEqual("work", StepId(world));
            Assert.AreEqual(world.Tutorial.GolemSpot, world.Tutorial.Current.Spot);
        }

        private static void Program(SandboxWorld world, GolemEntity golem, params string[] cards)
        {
            golem.Program.logicCore = world.Definitions.LogicCores["AlwaysOnCore"];
            foreach (string card in cards)
            {
                Assert.IsTrue(golem.Program.TryAddAppendage(world.Definitions.Appendages[card]), card);
            }
        }

        private static void GoTo(SandboxWorld world, string stepId)
        {
            world.Tutorial.Restore(stepId, 0, false);
            Assert.AreEqual(stepId, world.Tutorial.Current?.Id, "precondition: on " + stepId);
        }

        [Test]
        public void ASavedStep_IsRestoredByItsId_NotItsIndex()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-guide-" + System.Guid.NewGuid() + ".json");
            try
            {
                // A finished guide, saved with the index a SHORTER guide had for its end: every
                // chapter added since has inserted steps, and the bare index used to bring the
                // finished guide back mid-chapter.
                SandboxWorld world = Compose();
                world.Tutorial.Finish();
                world.SaveTo(path);
                File.WriteAllText(path, System.Text.RegularExpressions.Regex.Replace(
                    File.ReadAllText(path), "\"tutorialStep\"\\s*:\\s*\\d+", "\"tutorialStep\": 18"));

                SandboxWorld fresh = Compose();
                fresh.LoadFrom(path);
                Assert.IsTrue(fresh.Tutorial.IsFinished, $"still finished, not on '{fresh.Tutorial.Current?.Id}'");

                // A step in the middle comes back by id the same way.
                world.Tutorial.Restore("coal-order", 0, false);
                world.SaveTo(path);
                File.WriteAllText(path, System.Text.RegularExpressions.Regex.Replace(
                    File.ReadAllText(path), "\"tutorialStep\"\\s*:\\s*\\d+", "\"tutorialStep\": 3"));
                fresh = Compose();
                fresh.LoadFrom(path);
                Assert.AreEqual("coal-order", fresh.Tutorial.Current?.Id);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ASaveWithNoStepId_FallsBackToItsIndex()
        {
            SandboxWorld world = Compose();
            world.Tutorial.Restore(null, 2, false);
            Assert.AreEqual(2, world.Tutorial.Index, "a save from before ids were saved");
            world.Tutorial.Restore("a-step-this-build-dropped", 3, false);
            Assert.AreEqual(3, world.Tutorial.Index, "an id this build does not have");
        }

        [Test]
        public void ClaimCoking_PointsAtTheCoalStall_WhileShortOfItsCoal()
        {
            SandboxWorld world = Compose();
            GoTo(world, "coking-card");
            SandboxSetup.NodeEntry coal = world.Setup.nodes.Single(n => n.id == "CoalNode");

            Assert.AreEqual(new Vector2Int(coal.x, coal.y), world.Tutorial.TargetCell, "chapter 1 cranked every Coal into Coke");
            Assert.AreEqual("Coal  0 / 4", world.Tutorial.Progress, "the card's claimCost");
            StringAssert.Contains("Coal", world.Tutorial.Current.Body);

            Give(world, ItemType.Coal, 4);
            Assert.IsNull(world.Tutorial.TargetCell, "enough Coal: the claim is in Management");
        }

        [Test]
        public void ProgramTheCoker_PointsAtTheNewPresser_EvenIfTheFirstWasProgrammedDifferently()
        {
            SandboxWorld world = Compose();
            DefinitionSet defs = world.Definitions;
            Give(world, ItemType.Scrap, 400);
            Give(world, ItemType.IronPlate, 100);
            Give(world, ItemType.Gear, 40);
            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["BrassPresser"], out GolemEntity first));
            Program(world, first, "HaulScrap", "AssembleGearCutting", "PushOutput"); // not the guide's iron program
            first.SetPlacement(world.Tutorial.PresserSpot, Facing.North);
            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["BrassPresser"], out GolemEntity second));
            Assert.AreNotEqual(first.Cell, second.Cell, "precondition: two tiles to tell apart");

            GoTo(world, "program-coker");
            Assert.AreEqual(second.Cell, world.Tutorial.TargetCell, "the Presser just built, not the working one");
        }

        [Test]
        public void FeedTheBoiler_OnDryBoilers_PointsAtTheNewBoiler_AndRecoversOnceFuelled()
        {
            SandboxWorld world = Compose();
            DefinitionSet defs = world.Definitions;
            TutorialGuide guide = world.Tutorial;
            Give(world, ItemType.Scrap, 400);
            Give(world, ItemType.IronPlate, 100);
            Give(world, ItemType.Gear, 20);
            Place(world, "BoilerPrefab", guide.BoilerSpot.x, guide.BoilerSpot.y);
            PlaceableBoiler boiler2 = Place(world, "BoilerPrefab", guide.Boiler2Spot.x, guide.Boiler2Spot.y).GetPart<PlaceableBoiler>();
            foreach (Vector2Int pipe in guide.Pipe2Spots)
            {
                Place(world, "SteamPipePrefab", pipe.x, pipe.y);
            }
            Assert.IsTrue(world.Nodes.TryGetNode("CoalNode", out ResourceNode coalStall));
            coalStall.Deliver(40);

            // Chapter 1's few Coke long burned: both boilers dry, and the coker can never make
            // the first Coke that would fuel them.
            Assert.AreEqual(0, world.Steam.TotalCokeStock, "precondition: dry");
            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["BrassPresser"], out GolemEntity coker));
            Program(world, coker, "ExtractScrap", "AssembleCoking", "PushOutput");
            coker.SetPlacement(guide.CokerSpot, Facing.North);
            GoTo(world, "work-coker");

            world.Clock.Play();
            for (int frame = 0; frame < 300; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("work-coker", StepId(world), "no steam, no cycle");
            Assert.AreEqual(guide.Boiler2Spot, guide.TargetCell, "the arrow goes to the boiler in front of the coker");
            StringAssert.StartsWith("Boilers dry", guide.Progress);

            Give(world, ItemType.Coke, 2);
            Assert.IsTrue(world.Interactor.TryRefuelBoiler(boiler2));
            for (int frame = 0; frame < 1200 && StepId(world) == "work-coker"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("patent", StepId(world), $"fuelled, the coker runs ({coker.Program.State}, {coker.StallReason})");
        }

        // --- Pipe steps are done by where the steam goes, not by which tiles hold pipe ---------

        private static SandboxWorld Plenty()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 1000);
            Give(world, ItemType.IronPlate, 500);
            return world;
        }

        private static void Pipe(SandboxWorld world, params Vector2Int[] cells)
        {
            foreach (Vector2Int cell in cells)
            {
                Place(world, "SteamPipePrefab", cell.x, cell.y);
            }
        }

        /// <summary>Both boilers and chapter 2's pipes: the ground "Join the boilers" opens on.</summary>
        private static SandboxWorld AtJoinTheBoilers()
        {
            SandboxWorld world = Plenty();
            TutorialGuide guide = world.Tutorial;
            Place(world, "BoilerPrefab", guide.BoilerSpot.x, guide.BoilerSpot.y);
            Pipe(world, guide.PipeSpots);
            Place(world, "BoilerPrefab", guide.Boiler2Spot.x, guide.Boiler2Spot.y);
            GoTo(world, "pipes2");
            return world;
        }

        [Test]
        public void JoinTheBoilers_IsNotDone_ByTheNewBoilerAloneReachingTheCoker()
        {
            SandboxWorld world = AtJoinTheBoilers();
            Assert.IsTrue(world.Steam.Reaches(world.Tutorial.CokerSpot, world.Clock.CurrentTick),
                "precondition: the new boiler's own neighbour, so ANY-boiler reach is already true");
            Assert.AreEqual("pipes2", StepId(world));
        }

        [Test]
        public void JoinTheBoilers_NeedsAPipeTouchingBothBoilers()
        {
            SandboxWorld world = AtJoinTheBoilers();
            TutorialGuide guide = world.Tutorial;
            // Every marked tile but the one beside the new boiler: the first boiler reaches the
            // coker's tile, but nothing joins them. A golem standing between does not count.
            Pipe(world, guide.Pipe2Spots.Take(guide.Pipe2Spots.Length - 1).ToArray());
            Assert.AreEqual("pipes2", StepId(world), "reaches the coker, not joined");

            Pipe(world, guide.Pipe2Spots.Last());
            Assert.AreNotEqual("pipes2", StepId(world), "joined");
        }

        [Test]
        public void JoinTheBoilers_IsDone_ByAnotherRouteThatDoesBothJobs()
        {
            SandboxWorld world = AtJoinTheBoilers();
            TutorialGuide guide = world.Tutorial;
            // Off chapter 2's pipe, along the new boiler's row, then down beside the coker.
            Vector2Int fromChapter2 = guide.Boiler2Spot + new Vector2Int(-2, 0);
            Assert.IsTrue(guide.PipeSpots.Any(p => GolemFactory.Steam.SteamPipeRules.AreOrthogonallyAdjacent(p, fromChapter2)),
                "precondition: the route leaves chapter 2's run");
            Pipe(world, fromChapter2, guide.Boiler2Spot + new Vector2Int(-1, 0), guide.CokerSpot + new Vector2Int(-1, 0));
            Assert.IsFalse(world.Steam.HasPipe(guide.Pipe2Spots[0]), "precondition: a marked tile left bare");

            Assert.AreNotEqual("pipes2", StepId(world));
        }

        [Test]
        public void JoinTheBoilers_IsNotDone_ByARouteThatJoinsButLeavesTheCokerToTheEmptyBoiler()
        {
            SandboxWorld world = AtJoinTheBoilers();
            TutorialGuide guide = world.Tutorial;
            Pipe(world, guide.Boiler2Spot + new Vector2Int(-2, 0), guide.Boiler2Spot + new Vector2Int(-1, 0));
            Assert.IsTrue(world.Steam.AreJoined(guide.BoilerSpot, guide.Boiler2Spot, world.Clock.CurrentTick), "precondition: joined");

            Assert.AreEqual("pipes2", StepId(world),
                "steam never passes through the new boiler to its neighbours: the coker would sit beside a cold firebox");
        }

        private static SandboxWorld AtColumnPipes()
        {
            SandboxWorld world = Plenty();
            Place(world, "BoilerPrefab", world.Tutorial.Boiler2Spot.x, world.Tutorial.Boiler2Spot.y);
            GoTo(world, "pipes3");
            return world;
        }

        [Test]
        public void SteamForTheColumn_IsDone_ByARouteAroundAMarkedTile()
        {
            SandboxWorld world = AtColumnPipes();
            TutorialGuide guide = world.Tutorial;
            Vector2Int column = guide.SmelterSpot;
            Pipe(world, column + new Vector2Int(-1, 0));
            Assert.AreEqual("pipes3", StepId(world), "the smelter's tile only");

            // Up the boiler's north side instead of the column's middle tile.
            Pipe(world, column + new Vector2Int(-2, 1), column + new Vector2Int(-2, 2), column + new Vector2Int(-1, 2));
            Assert.IsFalse(world.Steam.HasPipe(guide.Pipe3Spots[1]), "precondition: the middle marked tile bare");
            Assert.AreNotEqual("pipes3", StepId(world));
        }

        [Test]
        public void DragAPipeRun_IsDone_ByARunWithADetour()
        {
            SandboxWorld world = AtColumnPipes();
            TutorialGuide guide = world.Tutorial;
            Pipe(world, guide.Pipe3Spots);
            GoTo(world, "pipes4");

            // One tile of the run's westward stretch swapped for a hop over it, to the north.
            Vector2Int[] run = guide.Pipe4Spots;
            Vector2Int skipped = run.First(c => c.y == guide.UnloaderSpot.y + 1
                && run.Contains(c + new Vector2Int(1, 0)) && run.Contains(c + new Vector2Int(-1, 0)));
            Pipe(world, run.Where(c => c != skipped).ToArray());
            Assert.AreEqual("pipes4", StepId(world), "a gap ends the run");

            Pipe(world, skipped + new Vector2Int(1, 1), skipped + new Vector2Int(0, 1), skipped + new Vector2Int(-1, 1));
            Assert.AreNotEqual("pipes4", StepId(world));
        }

        [Test]
        public void SteamForTheZeppelin_IsDone_FromAnotherSide()
        {
            SandboxWorld world = AtColumnPipes();
            TutorialGuide guide = world.Tutorial;
            Pipe(world, guide.Pipe3Spots);
            Pipe(world, guide.Pipe4Spots);
            GoTo(world, "zeppelin-pipe");

            // Off the run's westward stretch and round to the Zeppelin's north side.
            Vector2Int z = guide.ZeppelinSpot;
            Assert.IsTrue(world.Steam.HasPipe(z + new Vector2Int(-2, -1)), "precondition: the route leaves the run");
            Pipe(world, z + new Vector2Int(-2, 0), z + new Vector2Int(-2, 1), z + new Vector2Int(-1, 1), z + new Vector2Int(0, 1));
            Assert.IsFalse(world.Steam.HasPipe(guide.ZeppelinPipeSpot), "precondition: the marked tile bare");

            Assert.AreNotEqual("zeppelin-pipe", StepId(world));
        }

        [Test]
        public void ClearTheSlag_IsDone_ByAHeapOffItsMarkedTile()
        {
            SandboxWorld world = Plenty();
            Vector2Int elsewhere = world.Tutorial.SlagHeapSpot + new Vector2Int(3, 0);
            SlagHeap heap = Place(world, "SlagHeapPrefab", elsewhere.x, elsewhere.y).GetPart<PlaceableSlagHeap>().Heap;
            GoTo(world, "work-carrier");

            heap.AddCoke(1);
            Assert.IsTrue(heap.TryVoid(), "precondition: it burns");
            Assert.AreNotEqual("work-carrier", StepId(world));
        }

        [Test]
        public void FeedTheTower_TheWayTheStepSays_ADepotAGolemAndABoilerInTheSquare()
        {
            SandboxWorld world = Plenty();
            DefinitionSet defs = world.Definitions;
            world.TechTree.Ledger.RecordChassis(SandboxWorld.ZeppelinChassis); // the rope is down
            Assert.IsTrue(world.ClockTower.Site.IsOpen, "precondition: open");

            // West of the footprint's bottom-left cell: depot, golem facing the tower, boiler below.
            Vector2Int tower = TownSquare.TowerOrigin;
            Vector2Int golemCell = tower + new Vector2Int(-1, 0);
            Place(world, "DepotPrefab", golemCell.x - 1, golemCell.y);
            PlaceableBoiler boiler = Place(world, "BoilerPrefab", golemCell.x, golemCell.y - 1).GetPart<PlaceableBoiler>();
            Give(world, ItemType.Coke, 10);
            Assert.IsTrue(world.Interactor.TryRefuelBoiler(boiler));

            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["ClockworkScavenger"], out GolemEntity feeder));
            Program(world, feeder, "HaulScrap", "PushOutput");
            feeder.Program.SetItemTypeAt(0, ItemType.FrameSection);
            feeder.SetPlacement(golemCell, Facing.East);
            Give(world, ItemType.FrameSection, 20);

            GoTo(world, "tower-feed");
            world.Clock.Play();
            for (int frame = 0; frame < 900 && StepId(world) == "tower-feed"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreNotEqual("tower-feed", StepId(world),
                $"the tower took a delivery ({feeder.Program.State}, {feeder.StallReason} {feeder.StallResourceId})");
        }

        // --- The golem a step means is the one it built, not the Nth by build order -----------

        private static GolemEntity Scavenger(SandboxWorld world, params string[] cards)
        {
            Assert.IsTrue(world.StarterStation.TryConstructGolem(world.Definitions.Chassis["ClockworkScavenger"], out GolemEntity golem));
            if (cards.Length > 0)
            {
                Program(world, golem, cards);
            }
            return golem;
        }

        [Test]
        public void ASpareScavenger_DoesNotStandInForTheExtractorOrTheUnloader()
        {
            SandboxWorld world = Plenty();
            world.AssemblyBay.RestoreTier(2); // room for six
            Scavenger(world, "ExtractScrap", "PushOutput"); // chapter 1's
            Scavenger(world, "ExtractScrap", "PushOutput"); // chapter 4's
            Scavenger(world, "ExtractScrap", "PushOutput"); // a spare, built along the way

            // Build order would call the spare "the third Scavenger" and finish this step at once.
            GoTo(world, "scav3");
            Assert.AreEqual("scav3", StepId(world), "the spare is not the extractor");

            GolemEntity extractor = Scavenger(world, "ExtractScrap", "PushOutput");
            Assert.AreNotEqual("scav3", StepId(world), "the one built for the step is");

            // And "An unloader" waits on the golem built for IT -- the old Skip(3) found the
            // extractor, which has no Haul, and never finished.
            GoTo(world, "unloader");
            GolemEntity unloader = Scavenger(world, "HaulScrap", "PushOutput");
            unloader.Program.SetItemTypeAt(0, ItemType.CopperOre);
            Assert.AreNotEqual("unloader", StepId(world));
            StringAssert.Contains("extractor=" + extractor.GolemId, string.Join(",", world.Tutorial.RoleEntries));
            StringAssert.Contains("unloader=" + unloader.GolemId, string.Join(",", world.Tutorial.RoleEntries));
        }

        [Test]
        public void ALoadOnAndBackToWork_StillFinishesIt()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-guide-" + System.Guid.NewGuid() + ".json");
            try
            {
                SandboxWorld world = Plenty();
                Scavenger(world, "ExtractScrap", "PushOutput");
                GoTo(world, "scav2");
                GolemEntity scav2 = Scavenger(world, "ExtractScrap", "PushOutput");
                GoTo(world, "unstall");
                world.SaveTo(path);

                SandboxWorld fresh = Compose();
                fresh.LoadFrom(path);
                Assert.AreEqual("unstall", fresh.Tutorial.Current?.Id);
                Assert.IsTrue(fresh.Golems.Any(g => g.GolemId == scav2.GolemId), "precondition: it came back");

                // Its next finished cycle ends the step, as it does without the load. The stalled
                // golem's id used to live only in the last session, so this never happened.
                EventBus.Publish(new GolemCompletedEvent(scav2.GolemId));
                Assert.AreEqual("r4-card", StepId(fresh));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void SkipAndReopen_AndTheStepIsSaved()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-guide-" + System.Guid.NewGuid() + ".json");
            try
            {
                SandboxWorld world = Compose();
                Give(world, ItemType.Scrap, 20);
                StepId(world);
                world.Tutorial.Dismiss();
                Assert.IsFalse(world.Tutorial.IsShowing);
                world.SaveTo(path);

                SandboxWorld fresh = Compose();
                fresh.LoadFrom(path);
                Assert.AreEqual(1, fresh.Tutorial.Index, "the step it was on");
                Assert.IsFalse(fresh.Tutorial.IsShowing, "still put away");

                fresh.Tutorial.Reopen();
                Assert.IsTrue(fresh.Tutorial.IsShowing);
                Assert.AreEqual("coal", fresh.Tutorial.Current.Id);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
