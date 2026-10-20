using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Diagnostics
{
    /// <summary>What <see cref="EndgameLoad.Build"/> builds: the counts of the endgame profile.</summary>
    public sealed class EndgameLoadSpec
    {
        public int Golems = 96;
        public int BeltItems = 500;
        public int Boilers = 12;
    }

    /// <summary>
    /// The endgame factory (docs/performance-report-template.md §2), built into a composed
    /// <see cref="SandboxWorld"/> through the player's verbs: floor bought, pack and bays
    /// upgraded, every building placed through build mode and every golem built at the starter
    /// station and programmed through <see cref="GolemProgramWriter"/>, each price granted first
    /// as the playtest kit's <c>PlaceGranted</c> does. Engine-free, so the <c>perf</c> scenario
    /// and the Core benchmark profile the same factory.
    ///
    /// <para>
    /// <b>The layout.</b> The street: eight Scavengers extract from eight stalls (y −16) into
    /// crates in front (y −14), on one boiler. The workshop, y 5–19: nine rows of golems facing
    /// east, crate between each pair (x −11..9), so each golem's target crate is the next one's
    /// source: a chain, ordered so a golem's feeder can usually push the good it wants, as a
    /// real factory's lines do. A pipe row under every second golem row, one pipe column (x 11)
    /// joining them, and eleven boilers on it: 88 golems. Belts: four closed loops on the
    /// street's south rows and the town square, 500 items riding them.
    /// </para>
    ///
    /// <para>
    /// <b>Not Creative Mode.</b> The spec asked for it so the stalls never empty, but Creative
    /// Mode is chosen when the world is composed (<c>sandbox.json</c>), and the perf scenario runs
    /// the real one. Creative Mode does nothing else (it leaves steam burn and crates alone:
    /// <c>SandboxSetup.RegisterNodes</c> and the market are its only readers), so the load gives
    /// each stall it uses a truckload of a million instead: the same effect.
    /// </para>
    /// </summary>
    public static class EndgameLoad
    {
        /// <summary>The chain rows, west to east; tokens are <see cref="Roles"/>' keys.</summary>
        public static readonly string[][] Rows =
        {
            new[] { "LC", "C", "U", "C", "U", "C", "U", "C", "U", "Z" },
            new[] { "LC", "C", "U", "C", "U", "C", "U", "C", "U", "Z" },
            new[] { "LC", "C", "UH", "C", "UH", "C", "UH", "C", "LC", "OC" },
            new[] { "LC", "C", "UH", "C", "UH", "C", "LC", "OC", "U", "Z" },
            new[] { "LS", "SI", "G", "MS", "MX", "GC", "LS", "SI", "K", "FS" },
            new[] { "LS", "SI", "GL", "LN", "AC", "CD", "LS", "SI", "G", "MS" },
            new[] { "LC", "C", "SC", "B", "K", "FS", "C", "SZ", "C", "U" },
            new[] { "C", "SC", "W", "C", "SZ", "C", "SC", "B", "LN", "AC" },
            new[] { "C", "SC", "W", "C", "SZ", "SZ", "GL", "MX" },
        };

        /// <summary>The roles of the spec's §2 table, and the token each chain golem is.</summary>
        public const string Extractor = "Extractor", Coker = "Coker", Loader = "Loader", Smelter = "Smelter",
            BrassAndWire = "BrassAndWire", Component = "Component", TierFourFive = "TierFourFive",
            Carrier = "Carrier", Zeppelin = "Zeppelin";

        private const string Scav = "ClockworkScavenger", Press = "BrassPresser", Haul = "AetherHauler",
            Over = "MainspringOverclocker", Zep = "ZeppelinFreightLoader";

        /// <summary>A golem in a chain: its role, chassis, hauls (good, batch; null good = untyped) and recipe.</summary>
        public sealed class RoleDef
        {
            public string Role;
            public string Chassis;
            public (string good, int batch)[] Hauls;
            public string Assemble;
            public int Repeat;
            public bool Launch;
        }

        private static RoleDef R(string role, string chassis, string assemble, params (string, int)[] hauls) =>
            new RoleDef { Role = role, Chassis = chassis, Assemble = assemble, Hauls = hauls };

        public static readonly IReadOnlyDictionary<string, RoleDef> Roles = new Dictionary<string, RoleDef>
        {
            ["C"] = R(Coker, Press, "AssembleCoking", (ItemType.Coal, 1)),
            ["OC"] = new RoleDef { Role = TierFourFive, Chassis = Over, Assemble = "AssembleCoking", Repeat = 3, Hauls = new[] { (ItemType.Coal, 4) } },
            ["LC"] = R(Loader, Scav, null, (ItemType.Coal, 4)),
            ["LS"] = R(Loader, Scav, null, (ItemType.Scrap, 4)),
            ["U"] = R(Carrier, Scav, null, ((string)null, 4)),          // hauls what the golem before it pushes
            ["UH"] = R(Carrier, Haul, null, ((string)null, 2), ((string)null, 2), ((string)null, 2)),
            ["SI"] = R(Smelter, Haul, "AssembleIronSmelting", (ItemType.Scrap, 2), (ItemType.Coke, 1)),
            ["SC"] = R(Smelter, Haul, "AssembleCopperSmelting", (ItemType.Coke, 1), (ItemType.CopperOre, 2)),
            ["SZ"] = R(Smelter, Haul, "AssembleZincSmelting", (ItemType.Coke, 1), (ItemType.ZincOre, 2)),
            ["B"] = R(BrassAndWire, Haul, "AssembleBrassAlloying", (ItemType.CopperIngot, 2), (ItemType.ZincIngot, 1)),
            ["W"] = R(BrassAndWire, Press, "AssembleWireDrawing", (ItemType.CopperIngot, 1)),
            ["G"] = R(Component, Press, "AssembleGearCutting", (ItemType.IronPlate, 2)),
            ["K"] = R(Component, Haul, "AssembleCasingPress", (ItemType.Brass, 1), (ItemType.IronPlate, 4)),
            ["GL"] = R(Component, Press, "AssembleGlassmaking", (ItemType.Slag, 1)),
            ["LN"] = R(Component, Haul, "AssembleLensGrinding", (ItemType.Brass, 1), (ItemType.Glass, 3)),
            ["MS"] = R(Component, Haul, "AssembleMainspringWinding", (ItemType.Gear, 2), (ItemType.Brass, 3)),
            ["AC"] = R(Component, Haul, "AssembleAetherContainment", (ItemType.Lens, 2), (ItemType.Aether, 1)),
            ["MX"] = R(TierFourFive, Over, "AssembleMechanismAssembly", (ItemType.Mainspring, 1), (ItemType.Gear, 3), (ItemType.Casing, 1)),
            ["FS"] = R(TierFourFive, Over, "AssembleFrameSection", (ItemType.Casing, 10), (ItemType.IronPlate, 6), (ItemType.Brass, 4)),
            ["GC"] = R(TierFourFive, Over, "AssembleGreatCog", (ItemType.Mechanism, 4), (ItemType.Brass, 6), (ItemType.Mainspring, 2)),
            ["CD"] = R(TierFourFive, Over, "AssembleAetherConduit", (ItemType.AetherCell, 3), (ItemType.Brass, 4), (ItemType.CopperWire, 6)),
            ["Z"] = new RoleDef { Role = Zeppelin, Chassis = Zep, Launch = true, Hauls = new[] { (ItemType.Coke, 4) } },
        };

        /// <summary>The street's stalls, west to east, an extractor each.</summary>
        public static readonly int[] StallColumns = { -16, -12, -8, -4, 0, 4, 8, 12 };

        public const int StallRow = -16;
        public const int FirstBand = 5;
        public const int ChainWest = -11;
        public const int PipeColumn = 11;
        /// <summary>What a refill leaves of each good a crate's taker hauls: half the crate, so the pusher behind has room.</summary>
        public const int CrateFill = 25;

        public static EndgameLoadResult Build(SandboxWorld world, EndgameLoadSpec spec = null)
        {
            spec = spec ?? new EndgameLoadSpec();
            var load = new EndgameLoadResult(world);

            // The endgame's guide is finished, so it asks nothing of the world each frame.
            world.Tutorial?.Restore(GolemFactory.Tutorial.TutorialGuide.FinishedStepId, 0, true);

            // The room, the pack and the bays a 96-golem factory has bought.
            while (!world.PackUpgrade.IsMaxed)
            {
                Grant(world, world.PackUpgrade.NextCost());
                Require(world.PackUpgrade.TryUpgrade(), "pack upgrade: " + world.PackUpgrade.LastStatusMessage);
            }
            while (world.Bounds.CanExpand)
            {
                Grant(world, world.FloorExpansion.NextCost());
                Require(world.FloorExpansion.TryPurchaseExpansion(), "floor: " + world.FloorExpansion.LastStatusMessage);
            }
            while (world.AssemblyBay.MaxGolemSlots < spec.Golems + world.AssemblyBay.OccupiedSlots)
            {
                Grant(world, world.AssemblyBay.UpgradeCost);
                Require(world.AssemblyBay.TryUpgrade(world.Buffers, world.StockpileBufferId, SandboxWorld.PostBufferId), "bay upgrade");
            }

            BuildStreet(world, load);
            BuildWorkshop(world, load, spec.Golems - StallColumns.Length);
            BuildBelts(world, load, spec.BeltItems);

            Require(load.Boilers.Count == spec.Boilers, $"{load.Boilers.Count} boilers, wanted {spec.Boilers}");
            Require(load.Golems.Count == spec.Golems, $"{load.Golems.Count} golems, wanted {spec.Golems}");
            load.Refill();
            world.Clock.Play();
            return load;
        }

        // --- The street: eight extractors ------------------------------------------------------

        private static void BuildStreet(SandboxWorld world, EndgameLoadResult load)
        {
            const int golemRow = StallRow + 1, crateRow = StallRow + 2, mainRow = StallRow + 3;
            Place(world, load, "BoilerPrefab", new Vector2Int(-18, mainRow), Facing.North);
            for (int x = -17; x <= 17; x++)
            {
                Place(world, load, "SteamPipePrefab", new Vector2Int(x, mainRow), Facing.North);
            }
            foreach (int x in StallColumns)
            {
                ResourceNodeMarker stall = world.Markers.First(m => (int)Math.Round(m.Position.x) == x && (int)Math.Round(m.Position.y) == StallRow);
                world.Nodes.TryGetNode(stall.NodeId, out ResourceNode node);
                node?.Deliver(1000000);

                Place(world, load, "SteamPipePrefab", new Vector2Int(x + 1, golemRow), Facing.North);
                Place(world, load, "SteamPipePrefab", new Vector2Int(x + 1, crateRow), Facing.North);
                ICrate target = Place(world, load, "DepotPrefab", new Vector2Int(x, crateRow), Facing.North).GetPart<ICrate>();
                load.OutputOnly.Add(target);

                GolemEntity golem = Construct(world, Scav);
                Program(world, golem, "AlwaysOnCore", new[] { ("ExtractScrap", (string)null, 4), ("PushOutput", null, 1) });
                golem.SetPlacement(new Vector2Int(x, golemRow), Facing.North);
                load.Add(Extractor, golem);
            }
        }

        // --- The workshop: nine chain rows on one steam network ---------------------------------

        private static void BuildWorkshop(SandboxWorld world, EndgameLoadResult load, int golems)
        {
            var golemRows = new List<int>();
            int built = 0;
            for (int band = 0; golemRows.Count < Rows.Length; band++)
            {
                int y = FirstBand + 3 * band;
                golemRows.Add(y);
                if (golemRows.Count < Rows.Length)
                {
                    golemRows.Add(y + 2);
                }
                for (int x = ChainWest; x <= PipeColumn; x++)
                {
                    Place(world, load, "SteamPipePrefab", new Vector2Int(x, y + 1), Facing.North);
                }
            }
            int topPipe = FirstBand + 3 * ((Rows.Length - 1) / 2) + 1;
            for (int y = FirstBand + 2; y < topPipe; y++)
            {
                if ((y - FirstBand) % 3 != 1)
                {
                    Place(world, load, "SteamPipePrefab", new Vector2Int(PipeColumn, y), Facing.North);
                }
            }

            // Eleven boilers beside the column: one at the east end of every golem row, and two
            // past the top pipe row.
            var boilers = golemRows.Select(y => new Vector2Int(PipeColumn - 1, y)).ToList();
            boilers.Add(new Vector2Int(PipeColumn - 1, topPipe + 1));
            boilers.Add(new Vector2Int(PipeColumn, topPipe + 1));
            foreach (Vector2Int cell in boilers.Take((golems + SteamNetwork.MaxGolemsPerBoiler - 1) / SteamNetwork.MaxGolemsPerBoiler))
            {
                Place(world, load, "BoilerPrefab", cell, Facing.North);
            }

            for (int r = 0; r < Rows.Length; r++)
            {
                int y = golemRows[r];
                string[] row = Rows[r];
                for (int i = 0; i < row.Length && built < golems; i++, built++)
                {
                    RoleDef role = Roles[row[i]];
                    int x = ChainWest + 1 + 2 * i;
                    var source = new Vector2Int(x - 1, y);
                    var target = new Vector2Int(x + 1, y);

                    PlaceableBuilding sourceCrate = load.BuildingOn(source) ?? Place(world, load, "DepotPrefab", source, Facing.North);
                    load.Feed(sourceCrate.GetPart<ICrate>(), role.Hauls.Select(h => h.good ?? PreviousPush(row, i)));
                    if (role.Launch)
                    {
                        load.OutputOnly.Add(Place(world, load, "FreightMastPrefab", target, Facing.North).GetPart<ICrate>());
                    }
                    else if (load.BuildingOn(target) == null)
                    {
                        PlaceableBuilding crate = Place(world, load, "DepotPrefab", target, Facing.North);
                        if (i == row.Length - 1)
                        {
                            load.OutputOnly.Add(crate.GetPart<ICrate>());
                        }
                    }

                    GolemEntity golem = Construct(world, role.Chassis);
                    golem.SetPlacement(new Vector2Int(x, y), Facing.East);
                    Program(world, golem, "AlwaysOnCore", Steps(role, PreviousPush(row, i)));
                    if (role.Launch)
                    {
                        golem.BindToNearestMast();
                    }
                    load.Add(role.Role, golem);
                }
            }
        }

        /// <summary>The good the golem before position <paramref name="i"/> pushes, for an untyped carrier's crate.</summary>
        private static string PreviousPush(string[] row, int i)
        {
            if (i == 0)
            {
                return ItemType.Scrap;
            }
            RoleDef before = Roles[row[i - 1]];
            if (before.Assemble != null)
            {
                return AssembleOutputs.TryGetValue(before.Assemble, out string output) ? output : ItemType.Scrap;
            }
            return before.Hauls[0].good ?? PreviousPush(row, i - 1);
        }

        private static readonly Dictionary<string, string> AssembleOutputs = new Dictionary<string, string>
        {
            ["AssembleCoking"] = ItemType.Coke,
            ["AssembleIronSmelting"] = ItemType.IronPlate,
            ["AssembleCopperSmelting"] = ItemType.CopperIngot,
            ["AssembleZincSmelting"] = ItemType.ZincIngot,
        };

        /// <summary>A carrier hauls what the golem before it pushes (<paramref name="carried"/>): an empty Haul good means Scrap.</summary>
        private static (string card, string good, int quantity)[] Steps(RoleDef role, string carried)
        {
            var steps = role.Hauls.Select(h => ("HaulScrap", h.good ?? carried, h.batch)).ToList();
            if (role.Assemble != null)
            {
                steps.Add((role.Assemble, null, 1));
            }
            if (role.Repeat > 0)
            {
                steps.Add(("RepeatAssembly", null, role.Repeat));
            }
            steps.Add((role.Launch ? "FreightLaunch" : "PushOutput", null, 1));
            return steps.ToArray();
        }

        // --- Belts: closed loops -------------------------------------------------------------

        /// <summary>The loops: two on the street's south rows, two across the town square, clear of the tower.</summary>
        public static readonly (int west, int east, int south)[] Loops =
        {
            (-17, 17, -18), (-17, 17, -20), (-7, 7, TownSquare.Top - 1), (-7, 7, TownSquare.Bottom),
        };

        private static void BuildBelts(SandboxWorld world, EndgameLoadResult load, int items)
        {
            foreach ((int west, int east, int south) in Loops)
            {
                int north = south + 1;
                // North row runs east, south row runs west; the ends turn the corner.
                for (int x = west; x <= east; x++)
                {
                    PlaceBelt(world, load, new Vector2Int(x, north), x == east ? Facing.South : Facing.East);
                    PlaceBelt(world, load, new Vector2Int(x, south), x == west ? Facing.North : Facing.West);
                }
            }

            // Filled straight onto the lanes, spaced a step apart, as evenly as the count allows.
            List<BeltSegment> segments = load.BeltCells
                .Select(c => world.Belts.TryGetBelt(c, out PlacedBelt belt) ? belt.Segment : null)
                .Where(s => s != null).ToList();
            string[] goods = { ItemType.Scrap, ItemType.IronPlate, ItemType.Coal, ItemType.Coke };
            for (int n = 0; n < items; n++)
            {
                BeltSegment segment = segments[n % segments.Count];
                segment.Advance(1f);
                Require(segment.TryEnqueue(new ItemStack { ItemType = goods[n % goods.Length] }), "a belt lane is full");
            }
        }

        private static void PlaceBelt(SandboxWorld world, EndgameLoadResult load, Vector2Int cell, Facing facing)
        {
            Place(world, load, "BeltPrefab", cell, facing);
            load.BeltCells.Add(cell);
        }

        // --- The player's verbs, paid for -------------------------------------------------------

        private static PlaceableBuilding Place(SandboxWorld world, EndgameLoadResult load, string key, Vector2Int cell, Facing facing)
        {
            PlaceableBuilding prefab = world.Placeables.First(p => p.Key == key).Prefab;
            Grant(world, prefab.Cost);
            world.Build.SetActivePrefab(prefab);
            while (world.Build.PlacementFacing != facing)
            {
                world.Build.RotatePlacement();
            }
            world.Build.PlaceOrRemove(cell);
            string status = world.Build.LastStatusMessage;
            world.Build.CancelPlacement();
            PlaceableBuilding placed = world.Build.Buildings.FirstOrDefault(b => !b.IsRemoved && b.Cell == cell && b.PrefabKey == key);
            Require(placed != null, $"{key} at {cell}: {status}");
            load.Buildings[cell] = placed;
            PlaceableBoiler boiler = placed.GetPart<PlaceableBoiler>();
            if (boiler?.Boiler != null)
            {
                load.Boilers.Add(boiler);
            }
            return placed;
        }

        private static GolemEntity Construct(SandboxWorld world, string chassisName)
        {
            ChassisDefinition chassis = world.Definitions.Chassis[chassisName];
            Grant(world, chassis.cost);
            Require(world.StarterStation.TryConstructGolem(chassis, out GolemEntity golem),
                $"build {chassisName}: {world.StarterStation.LastRefusalReason}");
            return golem;
        }

        private static void Program(SandboxWorld world, GolemEntity golem, string core, (string card, string good, int quantity)[] steps)
        {
            Require(GolemProgramWriter.TryWrite(golem.Program, null, world.Definitions.LogicCores[core],
                steps.Select(s => world.Definitions.Appendages[s.card]).ToList(),
                steps.Select(s => s.good ?? "").ToList(),
                steps.Select(s => s.quantity).ToList(), out string refusal),
                $"{golem.GolemId}: {refusal}");
        }

        /// <summary>Puts a price into the pack, and what the pack can't hold into the Post, which the wallet spends next.</summary>
        private static void Grant(SandboxWorld world, IEnumerable<RecipeIngredient> cost)
        {
            foreach (RecipeIngredient line in cost ?? Enumerable.Empty<RecipeIngredient>())
            {
                int put = world.Buffers.Deposit(world.StockpileBufferId, line.itemType, line.quantity);
                if (put < line.quantity)
                {
                    world.Buffers.Deposit(SandboxWorld.PostBufferId, line.itemType, line.quantity - put);
                }
            }
        }

        private static void Require(bool condition, string what)
        {
            if (!condition)
            {
                throw new InvalidOperationException("EndgameLoad: " + what);
            }
        }
    }

    /// <summary>The built load: its golems by role, its crates, belts and boilers, and the two states the profile drives.</summary>
    public sealed class EndgameLoadResult
    {
        private readonly SandboxWorld _world;
        private readonly Dictionary<ICrate, HashSet<string>> _fills = new Dictionary<ICrate, HashSet<string>>();

        internal EndgameLoadResult(SandboxWorld world) => _world = world;

        public List<GolemEntity> Golems { get; } = new List<GolemEntity>();
        public Dictionary<string, List<GolemEntity>> ByRole { get; } = new Dictionary<string, List<GolemEntity>>();
        public List<Vector2Int> BeltCells { get; } = new List<Vector2Int>();
        public List<PlaceableBoiler> Boilers { get; } = new List<PlaceableBoiler>();
        internal Dictionary<Vector2Int, PlaceableBuilding> Buildings { get; } = new Dictionary<Vector2Int, PlaceableBuilding>();

        /// <summary>Crates something only pushes into (the ends of the lines, the masts): emptied on a refill.</summary>
        public List<ICrate> OutputOnly { get; } = new List<ICrate>();

        /// <summary>The crates golems take from, and the goods each is kept stocked with.</summary>
        public IEnumerable<ICrate> SourceCrates => _fills.Keys;

        /// <summary>How often <see cref="Maintain"/> refills (or re-empties), in ticks: 2 s.</summary>
        public const int MaintainEveryTicks = 20;

        private long _maintainedTick = -MaintainEveryTicks;

        /// <summary>
        /// Called once a frame by whatever drives the load: every <see cref="MaintainEveryTicks"/>
        /// of game time it refills a flowing factory, or re-empties a starved one. Timed as the
        /// sampler's <c>load.keep</c> section, so a profile can tell it from the game's own cost.
        /// </summary>
        public void Maintain()
        {
            long tick = _world.Clock.CurrentTick;
            if (tick - _maintainedTick < MaintainEveryTicks)
            {
                return;
            }
            _maintainedTick = tick;
            using (_world.Perf.Measure(KeepSection))
            {
                if (Starved)
                {
                    KeepStarved();
                }
                else
                {
                    Refill();
                }
            }
        }

        public const string KeepSection = "load.keep";

        /// <summary>True between <see cref="Starve"/> and the next <see cref="Refill"/>.</summary>
        public bool Starved { get; private set; }

        internal PlaceableBuilding BuildingOn(Vector2Int cell) => Buildings.TryGetValue(cell, out PlaceableBuilding b) ? b : null;

        internal void Add(string role, GolemEntity golem)
        {
            Golems.Add(golem);
            if (!ByRole.TryGetValue(role, out List<GolemEntity> list))
            {
                ByRole[role] = list = new List<GolemEntity>();
            }
            list.Add(golem);
        }

        internal void Feed(ICrate crate, IEnumerable<string> goods)
        {
            if (crate == null)
            {
                return;
            }
            if (!_fills.TryGetValue(crate, out HashSet<string> set))
            {
                _fills[crate] = set = new HashSet<string>();
            }
            set.UnionWith(goods);
        }

        /// <summary>Every item riding a belt.</summary>
        public int BeltItems => BeltCells.Sum(c => _world.Belts.TryGetBelt(c, out PlacedBelt belt) ? belt.Segment.Items.Count : 0);

        /// <summary>Golems stalled now.</summary>
        public int Stalled => Golems.Count(g => g.Program.State == GolemState.Stalled);

        /// <summary>Golems the steam network powers on the current tick.</summary>
        public int Powered => Golems.Count(g => _world.Steam.IsPowered(g.GolemId, _world.Clock.CurrentTick));

        /// <summary>
        /// Keeps the factory steady: every source crate holds <see cref="EndgameLoad.CrateFill"/> of
        /// each good its taker hauls and nothing else, every end-of-line crate and mast is emptied,
        /// and every boiler is full. What a player's upstream lines would do, without them.
        /// </summary>
        public void Refill()
        {
            Starved = false;
            foreach (KeyValuePair<ICrate, HashSet<string>> fill in _fills)
            {
                StorageBuffer buffer = fill.Key.Buffer;
                foreach (string good in buffer.Quantities.Keys.ToList())
                {
                    if (!fill.Value.Contains(good))
                    {
                        buffer.TryWithdraw(good, buffer.GetQuantity(good));
                    }
                }
                foreach (string good in fill.Value)
                {
                    int held = buffer.GetQuantity(good);
                    if (held < EndgameLoad.CrateFill)
                    {
                        buffer.Deposit(good, EndgameLoad.CrateFill - held);
                    }
                    else if (held > EndgameLoad.CrateFill)
                    {
                        buffer.TryWithdraw(good, held - EndgameLoad.CrateFill);
                    }
                }
            }
            EmptyOutputs();
            foreach (PlaceableBoiler boiler in Boilers)
            {
                boiler.Boiler.AddCoke(boiler.Boiler.CokeRoom);
            }
        }

        /// <summary>
        /// The <c>starved</c> phase: every crate emptied (and kept empty by <see cref="Maintain"/>),
        /// so the lines run out what they hold and stall on their empty takes. Boilers stay full,
        /// so they stall waiting, not for steam.
        /// </summary>
        public void Starve()
        {
            Starved = true;
            foreach (ICrate crate in _fills.Keys)
            {
                Empty(crate.Buffer);
            }
            EmptyOutputs();
        }

        /// <summary>Keeps a starved factory starved: what golems pushed downstream is cleared again.</summary>
        public void KeepStarved()
        {
            foreach (ICrate crate in _fills.Keys)
            {
                Empty(crate.Buffer);
            }
            EmptyOutputs();
        }

        private void EmptyOutputs()
        {
            foreach (ICrate crate in OutputOnly)
            {
                Empty(crate.Buffer);
            }
        }

        private static void Empty(StorageBuffer buffer)
        {
            foreach (string good in buffer.Quantities.Keys.ToList())
            {
                buffer.TryWithdraw(good, buffer.GetQuantity(good));
            }
        }
    }
}
