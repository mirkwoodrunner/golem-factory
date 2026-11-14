using System;
using System.Collections.Generic;
using GolemFactory.Belts;
using GolemFactory.Compat;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Diagnostics
{
    /// <summary>
    /// The simulation at several times the size the game allows: production lines laid side by
    /// side on an open grid, eight golems each, built from the authored cards and recipes and
    /// wired as <c>GolemConstructionStation</c> wires a golem. It is the tick and nothing else
    /// (clock, belts, steam, endpoints, golems), with no floor, wallet, guide or screen, so a
    /// test can time and count the tick alone and compare two runs state for state.
    /// <see cref="EndgameLoad"/> is the other half: the whole composed world at the size the
    /// game caps.
    ///
    /// <para>
    /// <b>A line</b> (west to east, on row 3k, its pipes on the row above): a Coal seam, an
    /// extractor, a crate, a coker, a crate, and a stoker who feeds the line's own boiler; then
    /// a Scrap seam, an extractor, a crate, a reclaimer (Scrap to Iron Plate), a crate, a gear
    /// cutter, a crate, a carrier onto a belt run, and a last golem taking gears off the belt
    /// into a sink that counts them. Each line keeps itself in Coke, so it runs for as long as
    /// it is ticked, and backs up and stalls where its slowest machine makes it.
    /// </para>
    ///
    /// <para>
    /// <b>Lines differ</b> (batch sizes, belt length, the boiler's first Coke, an interval
    /// trigger on every third line, an Overclocker repeating on every fourth) so the factory
    /// is not one line in step with itself. <b>Lines are independent</b>: nothing joins one to
    /// the next, so the order they are built in must not change what any of them does, and
    /// <see cref="Checksum"/> reads them in line order to let a test check exactly that.
    /// </para>
    /// </summary>
    public sealed class StressLoad : IDisposable
    {
        public const int GolemsPerLine = 8;
        public const int CrateCapacity = 50;
        public const int LinePitch = 3;

        private readonly List<GolemEntity> _golems = new List<GolemEntity>();
        private readonly Line[] _lines;
        private readonly TickAdapter _steamTick;

        public SimulationClock Clock { get; } = new SimulationClock();
        public ConveyorSystem Conveyor { get; } = new ConveyorSystem();
        public BeltNetwork Belts { get; } = new BeltNetwork();
        public SpatialEndpointRegistry Endpoints { get; } = new SpatialEndpointRegistry();
        public StorageBufferRegistry Buffers { get; } = new StorageBufferRegistry();
        public ResourceNodeRegistry Nodes { get; } = new ResourceNodeRegistry();
        public SteamNetwork Steam { get; } = new SteamNetwork();
        public NodeExtractorRegistry ExtractorCap { get; } = new NodeExtractorRegistry();

        /// <summary>Every golem, line by line, west to east within a line.</summary>
        public IReadOnlyList<GolemEntity> Golems => _golems;

        public int LineCount => _lines.Length;

        /// <summary>Everything standing on a cell: endpoints (seams, crates, belts, boilers, sinks), pipes and golems.</summary>
        public int GridEntities => Endpoints.Count + Steam.PipeCount + _golems.Count;

        private sealed class Line
        {
            public GolemEntity[] Golems;
            public StorageBuffer[] Crates;
            public SteamBoiler Boiler;
            public PlacedBelt[] BeltRun;
            public SinkEndpoint Sink;
        }

        /// <summary>The end of a line: takes whatever it is given and counts it.</summary>
        private sealed class SinkEndpoint : IItemEndpoint
        {
            public long Taken;

            public string DisplayName => "Sink";
            public bool TryTake(out ItemStack item) { item = default; return false; }
            public string PeekAvailableType() => null;
            public bool TryTake(string itemType, int quantity, out int taken) { taken = 0; return false; }
            public bool CanGive() => true;
            public bool CanGive(string itemType) => true;
            public bool TryGive(ItemStack item) { Taken++; return true; }
        }

        private sealed class TickAdapter : ITickable
        {
            private readonly SteamNetwork _steam;
            public TickAdapter(SteamNetwork steam) => _steam = steam;
            public void Tick(long tick) => _steam.Tick(tick);
        }

        private StressLoad(int lines)
        {
            _lines = new Line[lines];
            Belts.Configure(Conveyor, Endpoints, 4);
            // The Sandbox's order: belts, then steam, then each golem as it is built.
            Clock.Register(Conveyor);
            _steamTick = new TickAdapter(Steam);
            Clock.Register(_steamTick);
        }

        /// <summary>
        /// Builds <paramref name="lines"/> lines and starts the clock.
        /// </summary>
        /// <param name="reverseBuildOrder">
        /// Builds the last line first. The factory is the same; only the order things were
        /// registered in differs, which a deterministic simulation must not show.
        /// </param>
        public static StressLoad Build(DefinitionSet definitions, int lines, bool reverseBuildOrder = false)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }
            if (lines < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(lines));
            }

            var load = new StressLoad(lines);
            for (int i = 0; i < lines; i++)
            {
                int k = reverseBuildOrder ? lines - 1 - i : i;
                load._lines[k] = load.BuildLine(definitions, k);
            }
            for (int k = 0; k < lines; k++)
            {
                load._golems.AddRange(load._lines[k].Golems);
            }
            load.Clock.Play();
            return load;
        }

        private Line BuildLine(DefinitionSet definitions, int k)
        {
            int y = k * LinePitch;
            int beltLength = 4 + k % 4;
            int beltWest = 16;
            int takerX = beltWest + beltLength;
            var line = new Line { Golems = new GolemEntity[GolemsPerLine], Crates = new StorageBuffer[5] };

            ChassisDefinition scavenger = definitions.Chassis["ClockworkScavenger"];
            ChassisDefinition presser = definitions.Chassis["BrassPresser"];
            ChassisDefinition overclocker = definitions.Chassis["MainspringOverclocker"];
            LogicCoreDefinition always = definitions.LogicCores["AlwaysOnCore"];
            LogicCoreDefinition interval = definitions.LogicCores["IntervalCore10"];

            // --- The fuel half: Coal to Coke to this line's own boiler.
            Seam(k, "Coal", new Vector2Int(0, y), ItemType.Coal);
            line.Crates[0] = Crate(k, 0, new Vector2Int(2, y));
            line.Crates[1] = Crate(k, 1, new Vector2Int(4, y));
            line.Boiler = Steam.RegisterBoiler("Boiler " + k, new Vector2Int(6, y), 60 + k * 7 % 120);
            Endpoints.Register(line.Boiler.Cell, new BoilerFuelEndpoint(line.Boiler));
            for (int x = 1; x <= takerX; x++)
            {
                Steam.AddPipe(new Vector2Int(x, y + 1));
            }

            // --- The goods half: Scrap to Iron Plate to Gears, down a belt, into the sink.
            Seam(k, "Scrap", new Vector2Int(8, y), ItemType.Scrap);
            line.Crates[2] = Crate(k, 2, new Vector2Int(10, y));
            line.Crates[3] = Crate(k, 3, new Vector2Int(12, y));
            line.Crates[4] = Crate(k, 4, new Vector2Int(14, y));
            line.BeltRun = new PlacedBelt[beltLength];
            for (int b = 0; b < beltLength; b++)
            {
                if (!Belts.TryPlace(new Vector2Int(beltWest + b, y), Facing.East, out line.BeltRun[b]))
                {
                    throw new InvalidOperationException("StressLoad: no belt at " + new Vector2Int(beltWest + b, y));
                }
            }
            line.Sink = new SinkEndpoint();
            Endpoints.Register(new Vector2Int(takerX + 1, y), line.Sink);

            // --- The golems, west to east. Every one faces east: seam or crate behind, crate,
            // boiler, belt or sink in front.
            line.Golems[0] = Golem(definitions, k, 0, 1, y, scavenger, always,
                ("ExtractScrap", null, 4), ("PushOutput", null, 1));
            line.Golems[1] = Golem(definitions, k, 1, 3, y, presser, always,
                ("HaulScrap", ItemType.Coal, 1), ("AssembleCoking", null, 1), ("PushOutput", null, 1));
            line.Golems[2] = Golem(definitions, k, 2, 5, y, scavenger, always,
                ("HaulScrap", ItemType.Coke, 4), ("PushOutput", null, 1));
            line.Golems[3] = Golem(definitions, k, 3, 9, y, scavenger, k % 3 == 2 ? interval : always,
                ("ExtractScrap", null, 4), ("PushOutput", null, 1));
            line.Golems[4] = k % 4 == 3
                ? Golem(definitions, k, 4, 11, y, overclocker, always,
                    ("HaulScrap", ItemType.Scrap, 3), ("AssembleScrapReclamation", null, 1),
                    ("RepeatAssembly", null, 2), ("PushOutput", null, 1))
                : Golem(definitions, k, 4, 11, y, presser, always,
                    ("HaulScrap", ItemType.Scrap, 1), ("AssembleScrapReclamation", null, 1), ("PushOutput", null, 1));
            line.Golems[5] = Golem(definitions, k, 5, 13, y, presser, always,
                ("HaulScrap", ItemType.IronPlate, 2), ("AssembleGearCutting", null, 1), ("PushOutput", null, 1));
            line.Golems[6] = Golem(definitions, k, 6, 15, y, scavenger, always,
                ("HaulScrap", ItemType.Gear, 2 + k % 3), ("PushOutput", null, 1));
            line.Golems[7] = Golem(definitions, k, 7, takerX, y, scavenger, always,
                ("HaulScrap", ItemType.Gear, 1 + k % 4), ("PushOutput", null, 1));
            return line;
        }

        private void Seam(int k, string name, Vector2Int cell, string good)
        {
            var node = new ResourceNode("Seam " + k + " " + name, good);
            Nodes.Register(node);
            Endpoints.Register(cell, new ResourceNodeEndpoint(node));
        }

        private StorageBuffer Crate(int k, int index, Vector2Int cell)
        {
            StorageBuffer buffer = Buffers.SetCapacity("Crate " + k + "." + index, CrateCapacity);
            Endpoints.Register(cell, new StorageBufferEndpoint(buffer));
            return buffer;
        }

        /// <summary>A golem, wired in the order <c>GolemConstructionStation.SpawnGolem</c> wires one, then programmed.</summary>
        private GolemEntity Golem(DefinitionSet definitions, int k, int role, int x, int y,
            ChassisDefinition chassis, LogicCoreDefinition core, params (string card, string good, int quantity)[] steps)
        {
            var golem = new GolemEntity();
            golem.Configure("Stress " + k + "." + role, Conveyor);
            golem.ConfigureEconomy(Nodes, Buffers);
            golem.Program.TryAssignChassis(chassis);
            golem.MarkRuntimeSpawned();
            golem.ConfigureSpatial(Endpoints, new Vector2Int(x, y), Facing.East);
            golem.ConfigureSteam(Steam);
            golem.ConfigureNodeExtractorCap(ExtractorCap);
            golem.Attach();
            Clock.Register(golem);

            var cards = new List<AppendageActionDefinition>(steps.Length);
            var goods = new List<string>(steps.Length);
            var quantities = new List<int>(steps.Length);
            foreach ((string card, string good, int quantity) in steps)
            {
                cards.Add(definitions.Appendages[card]);
                goods.Add(good ?? "");
                quantities.Add(quantity);
            }
            if (!GolemProgramWriter.TryWrite(golem.Program, null, core, cards, goods, quantities, out string refusal))
            {
                throw new InvalidOperationException("StressLoad: " + golem.GolemId + ": " + refusal);
            }
            return golem;
        }

        // --- What a run reads ----------------------------------------------------------------

        /// <summary>Gears every line's sink has taken.</summary>
        public long Delivered
        {
            get
            {
                long total = 0;
                for (int k = 0; k < _lines.Length; k++)
                {
                    total += _lines[k].Sink.Taken;
                }
                return total;
            }
        }

        /// <summary>Golems stalled now.</summary>
        public int Stalled
        {
            get
            {
                int stalled = 0;
                for (int i = 0; i < _golems.Count; i++)
                {
                    stalled += _golems[i].Program.State == GolemState.Stalled ? 1 : 0;
                }
                return stalled;
            }
        }

        /// <summary>Items riding the belts.</summary>
        public int BeltItems
        {
            get
            {
                int items = 0;
                for (int k = 0; k < _lines.Length; k++)
                {
                    foreach (PlacedBelt belt in _lines[k].BeltRun)
                    {
                        items += belt.Segment.Items.Count;
                    }
                }
                return items;
            }
        }

        /// <summary>
        /// The whole state as one number: the tick, and for every line in line order its golems
        /// (step, progress, state, stall, both stocks), crates, boiler, belt items and sink.
        /// Two runs that agree here on every tick ran the same simulation. Names are hashed by
        /// their characters, never by <c>string.GetHashCode</c>, which differs between runs, so
        /// the number can be compared across processes and machines. Allocates nothing.
        /// </summary>
        public ulong Checksum()
        {
            ulong hash = 14695981039346656037UL;
            hash = Mix(hash, Clock.CurrentTick);
            for (int k = 0; k < _lines.Length; k++)
            {
                Line line = _lines[k];
                for (int g = 0; g < line.Golems.Length; g++)
                {
                    GolemEntity golem = line.Golems[g];
                    GolemProgram program = golem.Program;
                    hash = Mix(hash, program.CurrentStepIndex);
                    hash = Mix(hash, program.StepProgressTicks);
                    hash = Mix(hash, (int)program.State);
                    hash = Mix(hash, (int)golem.StallReason);
                    hash = Mix(hash, golem.StallShortfall);
                    hash = Mix(hash, golem.StepDurationTicks);
                    hash = Mix(hash, golem.Inventory.Input);
                    hash = Mix(hash, golem.Inventory.Output);
                }
                for (int c = 0; c < line.Crates.Length; c++)
                {
                    for (int t = 0; t < Goods.Length; t++)
                    {
                        hash = Mix(hash, line.Crates[c].GetQuantity(Goods[t]));
                    }
                }
                hash = Mix(hash, line.Boiler.CokeStock);
                hash = Mix(hash, line.Boiler.BurnAccumulator);
                hash = Mix(hash, line.Boiler.PoweredGolemCount);
                for (int b = 0; b < line.BeltRun.Length; b++)
                {
                    IReadOnlyList<ItemStack> items = line.BeltRun[b].Segment.Items;
                    hash = Mix(hash, items.Count);
                    for (int i = 0; i < items.Count; i++)
                    {
                        hash = Mix(hash, items[i].ItemType);
                        hash = Mix(hash, BitConverter.SingleToInt32Bits(items[i].Progress));
                    }
                }
                hash = Mix(hash, line.Sink.Taken);
            }
            return hash;
        }

        // Every good a line handles, in a fixed order, so a crate is read the same way each time.
        private static readonly string[] Goods =
        {
            ItemType.Coal, ItemType.Coke, ItemType.Scrap, ItemType.IronPlate, ItemType.Gear,
        };

        // FNV-1a, a byte at a time.
        private static ulong Mix(ulong hash, long value)
        {
            for (int i = 0; i < 8; i++)
            {
                hash = (hash ^ (byte)(value >> (i * 8))) * 1099511628211UL;
            }
            return hash;
        }

        private static ulong Mix(ulong hash, string text)
        {
            if (text == null)
            {
                return Mix(hash, -1L);
            }
            for (int i = 0; i < text.Length; i++)
            {
                hash = (hash ^ text[i]) * 1099511628211UL;
            }
            return Mix(hash, text.Length);
        }

        private static ulong Mix(ulong hash, GolemInventory.Stock stock)
        {
            IReadOnlyList<string> types = stock.TypesInOrder;
            hash = Mix(hash, types.Count);
            for (int i = 0; i < types.Count; i++)
            {
                hash = Mix(hash, types[i]);
                hash = Mix(hash, stock.Get(types[i]));
            }
            return hash;
        }

        /// <summary>
        /// Takes every golem off the static event bus and the clock. A load left undisposed
        /// goes on hearing the next world's golems.
        /// </summary>
        public void Dispose()
        {
            for (int i = 0; i < _golems.Count; i++)
            {
                Clock.Unregister(_golems[i]);
                _golems[i].Remove();
            }
            Clock.Unregister(_steamTick);
            Clock.Unregister(Conveyor);
            Clock.Pause();
        }
    }
}
