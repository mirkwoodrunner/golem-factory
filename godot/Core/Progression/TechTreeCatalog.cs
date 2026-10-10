using System.Collections.Generic;
using GolemFactory.Economy;

namespace GolemFactory.Progression
{
    /// <summary>
    /// The research track, transcribed from docs/progression-design.md: §9's six phases are the
    /// columns, §6's five chassis are the keystones, and §5.2's nineteen recipes plus the
    /// placeables hang off whichever keystone first makes them runnable.
    ///
    /// <para>
    /// <b>This is a transcription, not a second source of truth.</b> The recipes, chassis and
    /// costs it names are authored as <c>.asset</c> files under
    /// <c>Assets/_Project/ScriptableObjects/</c>; this table restates their <em>ordering</em>,
    /// which lives nowhere in the assets because a <c>RecipeDefinition</c> knows its ingredients
    /// and not its phase. <c>TechTreeCatalogTests</c> pins the transcription against the assets on
    /// disk -- every recipe node must name a real recipe asset, every chassis node a real chassis
    /// -- so a tuning pass that renames or drops one fails the suite rather than the chart.
    /// </para>
    ///
    /// <para>
    /// <b>Prerequisites are the readable gate, not the full ingredient list.</b> A node names at
    /// most three, chosen as the structural gate (the chassis whose slot count admits the recipe)
    /// plus the one or two ingredients whose arrival is the actual wait. R17's four inputs are all
    /// real, but four inbound lines per card turns the chart into a wiring diagram and hides the
    /// track it exists to show. The complete ingredient lists are in §5.2 and in the assets.
    /// </para>
    /// </summary>
    public static class TechTreeCatalog
    {
        // ASCII "->" IN EVERY DETAIL LINE, NOT AN ARROW GLYPH. Fourteen of these carried U+2192
        // from the day the chart was written, and TMP's default LiberationSans SDF atlas has no
        // entry for it -- so every recipe node on the Ledger has been drawing a missing-glyph box
        // where its arrow should be. The constraint is recorded in three other places already
        // (StallDiagnostics, GolemStallIndicator, WorkbenchLoopLabels) and this file simply never
        // heard about it; TechTreeCatalogTests now walks every string and refuses anything above
        // U+00FF, so it cannot come back. The middle dot is fine and stays -- it is Latin-1.

        // Chassis nodes name the ASSET name, not a display string: that is what a golem's
        // GolemProgram.chassis reports at runtime and what the .asset file is called on disk.
        public const string ChassisScavenger = "ClockworkScavenger";
        public const string ChassisPresser = "BrassPresser";
        public const string ChassisHauler = "AetherHauler";
        public const string ChassisOverclocker = "MainspringOverclocker";
        public const string ChassisZeppelin = "ZeppelinFreightLoader";

        // Building signal ids. Bare strings for the same reason belt/buffer/node ids are: they
        // cross a system boundary (the panel's collector publishes them, the catalog consumes
        // them) and nothing on either side is a shared enum.
        public const string BuildingHandCrankBench = "HandCrankBench";
        public const string BuildingBoiler = "Boiler";
        public const string BuildingSteamPipe = "SteamPipe";
        public const string BuildingBelt = "Belt";
        public const string BuildingDepot = "Depot";
        public const string BuildingClockTower = "ClockTower";
        public const string BuildingSlagHeap = "SlagHeap";
        public const string BuildingFreightMast = "FreightMast";
        public const string BuildingFloorExpansion = "FloorExpansion";
        public const string BuildingScrapRecycler = "ScrapRecycler";

        private static readonly TechTreePhase[] PhaseTable =
        {
            new TechTreePhase("I · The Cold Workshop", "Hand-gather and hand-crank your way to the first machine"),
            new TechTreePhase("II · The First Machine", "Get one Presser running before the Boiler gauge hits zero"),
            new TechTreePhase("III · Steam & Scale", "Build a coal line that outgrows its own consumption"),
            new TechTreePhase("IV · The Metal Lines", "Two-input metallurgy, and Slag as a disposal obligation"),
            new TechTreePhase("V · The Great Works", "Mechanisms, remote outposts, and the last two chassis"),
            new TechTreePhase("VI · The Clock Tower", "Four rate-scaled stages against a factory of ~96 golems")
        };

        private static readonly TechTreeNode[] NodeTable =
        {
            // ---- I · The Cold Workshop ------------------------------------------------------
            new TechTreeNode(
                "bench.handcrank", "Hand-Crank Bench", "1-input recipes by hand, at 25% speed",
                TechTreeNodeKind.Building, 0, 0,
                TechTreeUnlockSignal.Building, BuildingHandCrankBench,
                new string[0]),
            new TechTreeNode(
                "chassis.scavenger", "Clockwork Scavenger", "2 slots · pure logistics · 12 Scrap",
                TechTreeNodeKind.Chassis, 0, 1,
                TechTreeUnlockSignal.Chassis, ChassisScavenger,
                new string[0], isKeystone: true),
            // NO LONGER PLANNED -- §8's cap is built: bays start at ten slots, upgrade +6 for
            // 40 Scrap + 20 Iron Plate, and GolemConstructionStation refuses past it.
            new TechTreeNode(
                "bays.assembly", "Assembly Bays", "Golem cap 10 · +6 for 40 Scrap + 20 Plate",
                TechTreeNodeKind.Technique, 0, 2,
                TechTreeUnlockSignal.None, null,
                new[] { "chassis.scavenger" }),

            // ---- II · The First Machine -----------------------------------------------------
            new TechTreeNode(
                "r2.reclamation", "R2 Scrap Reclamation", "1 Scrap -> 1 Iron Plate",
                TechTreeNodeKind.Recipe, 1, 0,
                TechTreeUnlockSignal.Item, ItemType.IronPlate,
                new[] { "bench.handcrank" }),
            new TechTreeNode(
                "r8.gearcutting", "R8 Gear Cutting", "2 Iron Plate -> 1 Gear",
                TechTreeNodeKind.Recipe, 1, 1,
                TechTreeUnlockSignal.Item, ItemType.Gear,
                new[] { "r2.reclamation" }),
            new TechTreeNode(
                "r1.coking", "R1 Coking", "1 Coal -> 1 Coke · the throat of the economy",
                TechTreeNodeKind.Recipe, 1, 2,
                TechTreeUnlockSignal.Item, ItemType.Coke,
                new[] { "bench.handcrank" }),
            new TechTreeNode(
                "chassis.presser", "Brass Presser", "3 slots · 1 input · 60 Scrap + 20 Plate + 10 Gear",
                TechTreeNodeKind.Chassis, 1, 3,
                TechTreeUnlockSignal.Chassis, ChassisPresser,
                new[] { "chassis.scavenger", "r2.reclamation", "r8.gearcutting" }, isKeystone: true),
            new TechTreeNode(
                "bldg.boiler", "Boiler", "Powers 8 golems · 6 Coke/min each",
                TechTreeNodeKind.Building, 1, 4,
                TechTreeUnlockSignal.Building, BuildingBoiler,
                new[] { "r2.reclamation", "r1.coking" }),
            new TechTreeNode(
                "bldg.steampipe", "Steam Pipe", "1 Iron Plate · carries steam by adjacency",
                TechTreeNodeKind.Building, 1, 5,
                TechTreeUnlockSignal.Building, BuildingSteamPipe,
                new[] { "bldg.boiler" }),
            new TechTreeNode(
                "bldg.belt", "Conveyor Belt", "1 Scrap · a belt hands off only to a belt",
                TechTreeNodeKind.Building, 1, 6,
                TechTreeUnlockSignal.Building, BuildingBelt,
                new[] { "chassis.scavenger" }),
            new TechTreeNode(
                "bldg.depot", "Depot", "15 Scrap · capacity is per item type",
                TechTreeNodeKind.Building, 1, 7,
                TechTreeUnlockSignal.Building, BuildingDepot,
                new[] { "chassis.scavenger" }),

            // ---- III · Steam & Scale --------------------------------------------------------
            // The only phase whose keystone is not a chassis. §9's phase 3 has no new frame in it
            // at all -- it is spent scaling three simple lines to afford the Hauler -- so the
            // thing that defines the column is the grid the scaling runs on.
            new TechTreeNode(
                "milestone.steamgrid", "The Steam Grid", "Every golem must touch a pipe, or stall",
                TechTreeNodeKind.Technique, 2, 0,
                TechTreeUnlockSignal.None, null,
                new[] { "bldg.steampipe", "r1.coking" }, isKeystone: true),
            new TechTreeNode(
                "milestone.nodecap", "A Second Node Site", "2 extractors per node - scale means distance",
                TechTreeNodeKind.Technique, 2, 1,
                TechTreeUnlockSignal.None, null,
                new[] { "bldg.belt", "milestone.steamgrid" }),
            // NO LONGER PLANNED, AND IT WAS THE LAST ONE. §11 item 15 is built: the workshop's
            // back wall moves north at runtime, painting new plank rows and re-placing the
            // wall run, priced per row and capped -- land is finite and expensive.
            new TechTreeNode(
                "bldg.floorexpansion", "Floor Expansion", "60 Scrap + 30 Iron Plate per 6×6 block",
                TechTreeNodeKind.Building, 2, 2,
                TechTreeUnlockSignal.Building, BuildingFloorExpansion,
                new[] { "r2.reclamation" }),

            // ---- IV · The Metal Lines -------------------------------------------------------
            // A chassis sits in the phase it OPENS, not the one spent affording it: §9 lists the
            // Hauler under phase 3's "newly gated" and phase 4's "newly available", and the same
            // reading puts the Presser in II and the Overclocker in V.
            new TechTreeNode(
                "chassis.hauler", "Aether-Hauler", "4 slots · 2 inputs · 80 Plate + 40 Gear + 30 Coke",
                TechTreeNodeKind.Chassis, 3, 0,
                TechTreeUnlockSignal.Chassis, ChassisHauler,
                new[] { "chassis.presser", "r1.coking", "r8.gearcutting" }, isKeystone: true),
            // R4's signal is Slag, not Iron Plate: R2 also makes Plate, and a signal has to name
            // the good that ONLY this recipe produces or the node lights up in phase II.
            new TechTreeNode(
                "r4.ironsmelting", "R4 Iron Smelting", "2 Scrap + 1 Coke -> 2 Iron Plate + 1 Slag",
                TechTreeNodeKind.Recipe, 3, 1,
                TechTreeUnlockSignal.Item, ItemType.Slag,
                new[] { "chassis.hauler", "r1.coking" }),
            new TechTreeNode(
                "r3.glassmaking", "R3 Glassmaking", "1 Slag -> 1 Glass · the productive Slag sink",
                TechTreeNodeKind.Recipe, 3, 2,
                TechTreeUnlockSignal.Item, ItemType.Glass,
                new[] { "r4.ironsmelting" }),
            // NO LONGER PLANNED -- §5.3(c)'s sink is built: a placeable that voids Slag at
            // 1 Coke per 4, refusing when it has no fuel so the backlog is felt rather
            // than silently absorbed.
            new TechTreeNode(
                "bldg.slagheap", "Slag Heap", "Voids Slag at 1 Coke per 4 · the costed sink",
                TechTreeNodeKind.Building, 3, 3,
                TechTreeUnlockSignal.Building, BuildingSlagHeap,
                new[] { "r4.ironsmelting" }),
            // The heap's counterpart, and the only way to get rid of anything that is not Slag.
            // Sits beside it because they compete for the same Coke: half the disposal rate,
            // plus a Scrap back.
            new TechTreeNode(
                "bldg.scraprecycler", "Scrap Recycler", "Any junk -> Scrap · 1 Coke per 4 points",
                TechTreeNodeKind.Building, 3, 4,
                TechTreeUnlockSignal.Building, BuildingScrapRecycler,
                new[] { "bldg.slagheap" }),
            new TechTreeNode(
                "r5.coppersmelting", "R5 Copper Smelting", "2 Copper Ore + 1 Coke -> 1 Copper Ingot",
                TechTreeNodeKind.Recipe, 3, 5,
                TechTreeUnlockSignal.Item, ItemType.CopperIngot,
                new[] { "chassis.hauler", "r1.coking" }),
            new TechTreeNode(
                "r6.zincsmelting", "R6 Zinc Smelting", "2 Zinc Ore + 1 Coke -> 1 Zinc Ingot",
                TechTreeNodeKind.Recipe, 3, 6,
                TechTreeUnlockSignal.Item, ItemType.ZincIngot,
                new[] { "chassis.hauler", "r1.coking" }),
            new TechTreeNode(
                "r7.brassalloying", "R7 Brass Alloying", "2 Copper + 1 Zinc -> 3 Brass · seven consumers",
                TechTreeNodeKind.Recipe, 3, 7,
                TechTreeUnlockSignal.Item, ItemType.Brass,
                new[] { "r5.coppersmelting", "r6.zincsmelting" }),
            new TechTreeNode(
                "r19.wiredrawing", "R19 Wire Drawing", "1 Copper Ingot -> 3 Copper Wire",
                TechTreeNodeKind.Recipe, 3, 8,
                TechTreeUnlockSignal.Item, ItemType.CopperWire,
                new[] { "r5.coppersmelting" }),
            new TechTreeNode(
                "r9.casingpress", "R9 Casing Press", "4 Iron Plate + 1 Brass -> 1 Casing",
                TechTreeNodeKind.Recipe, 3, 9,
                TechTreeUnlockSignal.Item, ItemType.Casing,
                new[] { "r4.ironsmelting", "r7.brassalloying" }),
            new TechTreeNode(
                "r10.lensgrinding", "R10 Lens Grinding", "2 Glass + 1 Brass -> 1 Lens",
                TechTreeNodeKind.Recipe, 3, 10,
                TechTreeUnlockSignal.Item, ItemType.Lens,
                new[] { "r3.glassmaking", "r7.brassalloying" }),
            new TechTreeNode(
                "r11.mainspringwinding", "R11 Mainspring Winding", "3 Brass + 2 Gear -> 1 Mainspring",
                TechTreeNodeKind.Recipe, 3, 11,
                TechTreeUnlockSignal.Item, ItemType.Mainspring,
                new[] { "r7.brassalloying", "r8.gearcutting" }),
            new TechTreeNode(
                "r12.aethercontainment", "R12 Aether Containment", "1 Aether + 2 Lens -> 1 Aether Cell",
                TechTreeNodeKind.Recipe, 3, 12,
                TechTreeUnlockSignal.Item, ItemType.AetherCell,
                new[] { "r10.lensgrinding" }),
            new TechTreeNode(
                "milestone.carriers", "Carrier Golems", "Chassis slots buy back buffer faces",
                TechTreeNodeKind.Technique, 3, 13,
                TechTreeUnlockSignal.None, null,
                new[] { "chassis.hauler" }),

            // ---- V · The Great Works --------------------------------------------------------
            new TechTreeNode(
                "chassis.overclocker", "Mainspring Overclocker", "5 slots · 3 inputs · 2 Mainspring + 20 Brass + 24 Casing + 12 Gear",
                TechTreeNodeKind.Chassis, 4, 0,
                TechTreeUnlockSignal.Chassis, ChassisOverclocker,
                new[] { "r11.mainspringwinding", "r9.casingpress" }, isKeystone: true),
            // NO LONGER PLANNED -- Repeat is built (progression-design §6's verb: it re-runs the
            // preceding Assemble n more times, Overclocker-only via ChassisDefinition
            // .allowsRepeat). Signal None is right for it: there is nothing separate to observe,
            // because owning an Overclocker IS owning the verb.
            new TechTreeNode(
                "verb.repeat", "Repeat", "Overclocker only · competes for the fifth slot",
                TechTreeNodeKind.Technique, 4, 1,
                TechTreeUnlockSignal.None, null,
                new[] { "chassis.overclocker" }),
            new TechTreeNode(
                "r13.mechanism", "R13 Mechanism Assembly", "1 Mainspring + 3 Gear + 1 Casing",
                TechTreeNodeKind.Recipe, 4, 2,
                TechTreeUnlockSignal.Item, ItemType.Mechanism,
                new[] { "chassis.overclocker", "r11.mainspringwinding", "r9.casingpress" }),
            new TechTreeNode(
                "r15.framesection", "R15 Frame Section", "10 Casing + 6 Plate + 4 Brass",
                TechTreeNodeKind.Recipe, 4, 3,
                TechTreeUnlockSignal.Item, ItemType.FrameSection,
                new[] { "chassis.overclocker", "r9.casingpress" }),
            new TechTreeNode(
                "r16.greatcog", "R16 Great Cog", "4 Mechanism + 6 Brass + 2 Mainspring",
                TechTreeNodeKind.Recipe, 4, 4,
                TechTreeUnlockSignal.Item, ItemType.GreatCog,
                new[] { "r13.mechanism", "r11.mainspringwinding" }),
            new TechTreeNode(
                "r18.aetherconduit", "R18 Aether Conduit", "3 Aether Cell + 4 Brass + 6 Wire",
                TechTreeNodeKind.Recipe, 4, 5,
                TechTreeUnlockSignal.Item, ItemType.AetherConduit,
                new[] { "chassis.overclocker", "r12.aethercontainment", "r19.wiredrawing" }),
            new TechTreeNode(
                "chassis.zeppelin", "Zeppelin Freight Loader", "6 slots · 4 inputs · 6 Mainspring + 8 Lens + 3 Aether Cell + 30 Casing + 40 Brass",
                TechTreeNodeKind.Chassis, 4, 6,
                TechTreeUnlockSignal.Chassis, ChassisZeppelin,
                new[] { "r11.mainspringwinding", "r10.lensgrinding", "r12.aethercontainment" }, isKeystone: true),
            // NO LONGER PLANNED -- §6's Freight Link is built: FreightLaunch empties the golem's
            // output stock onto its bound mast's tile at a flat 24 ticks, Zeppelin-only via
            // ChassisDefinition.allowsFreightLaunch, with the mast a placeable that publishes
            // its own receiving tile.
            new TechTreeNode(
                "verb.freightlink", "Freight Link", "Push to a bound mast, regardless of distance",
                TechTreeNodeKind.Technique, 4, 7,
                TechTreeUnlockSignal.None, null,
                new[] { "chassis.zeppelin" }),
            new TechTreeNode(
                "bldg.freightmast", "Freight Mast", "20 Brass + 10 Casing · one Zeppelin per mast",
                TechTreeNodeKind.Building, 4, 8,
                TechTreeUnlockSignal.Building, BuildingFreightMast,
                new[] { "verb.freightlink" }),
            new TechTreeNode(
                "r14.regulator", "R14 Regulator", "1 Aether Cell + 2 Brass + 1 Gear + 4 Wire",
                TechTreeNodeKind.Recipe, 4, 9,
                TechTreeUnlockSignal.Item, ItemType.Regulator,
                new[] { "chassis.zeppelin", "r12.aethercontainment", "r19.wiredrawing" }),
            new TechTreeNode(
                "r17.chronometercore", "R17 Chronometer Core", "2 Regulator + 2 Mechanism + 2 Lens + 2 Wire",
                TechTreeNodeKind.Recipe, 4, 10,
                TechTreeUnlockSignal.Item, ItemType.ChronometerCore,
                new[] { "r14.regulator", "r13.mechanism", "r10.lensgrinding" }),

            // ---- VI · The Clock Tower -------------------------------------------------------
            new TechTreeNode(
                "bldg.clocktower", "The Clock Tower", "Opens once a Zeppelin flies · its first delivery starts stage 1",
                TechTreeNodeKind.Building, 5, 0,
                TechTreeUnlockSignal.Building, BuildingClockTower,
                new[] { "r15.framesection" }, isKeystone: true),
            new TechTreeNode(
                "tower.stage1", "Stage 1 · Foundation", "Frame Section @ 6/min · ~63 golems",
                TechTreeNodeKind.Milestone, 5, 1,
                TechTreeUnlockSignal.TowerStage, "1",
                new[] { "bldg.clocktower" }),
            new TechTreeNode(
                "tower.stage2", "Stage 2 · The Movement", "Great Cog @ 3 · Frame @ 3 /min · ~72 golems",
                TechTreeNodeKind.Milestone, 5, 2,
                TechTreeUnlockSignal.TowerStage, "2",
                new[] { "tower.stage1", "r16.greatcog" }),
            new TechTreeNode(
                "tower.stage3", "Stage 3 · Aether Illumination", "Conduit @ 3 · Lens @ 24 · Frame @ 2 /min",
                TechTreeNodeKind.Milestone, 5, 3,
                TechTreeUnlockSignal.TowerStage, "3",
                new[] { "tower.stage2", "r18.aetherconduit" }),
            new TechTreeNode(
                "tower.stage4", "Stage 4 · The Chronometer", "Core @ 2 · Cog @ 2 · Conduit @ 1 /min · ~96 golems",
                TechTreeNodeKind.Milestone, 5, 4,
                TechTreeUnlockSignal.TowerStage, "4",
                new[] { "tower.stage3", "r17.chronometercore" }),
            new TechTreeNode(
                "win.metropolis", "The Clockwork Metropolis", "The tower chimes · the save keeps running",
                TechTreeNodeKind.Milestone, 5, 5,
                TechTreeUnlockSignal.TowerStage, "4",
                new[] { "tower.stage4" })
        };

        private static readonly Dictionary<string, TechTreeNode> ById = BuildIndex();

        public static IReadOnlyList<TechTreePhase> Phases => PhaseTable;
        public static IReadOnlyList<TechTreeNode> Nodes => NodeTable;

        public static bool TryGetNode(string id, out TechTreeNode node) =>
            ById.TryGetValue(id ?? string.Empty, out node);

        /// <summary>Rows in the tallest column -- the chart's height in node slots.</summary>
        public static int MaxRowsInAnyPhase()
        {
            int max = 0;
            for (int i = 0; i < NodeTable.Length; i++)
            {
                if (NodeTable[i].Row + 1 > max)
                {
                    max = NodeTable[i].Row + 1;
                }
            }
            return max;
        }

        private static Dictionary<string, TechTreeNode> BuildIndex()
        {
            var index = new Dictionary<string, TechTreeNode>(NodeTable.Length);
            for (int i = 0; i < NodeTable.Length; i++)
            {
                index[NodeTable[i].Id] = NodeTable[i];
            }
            return index;
        }
    }
}
