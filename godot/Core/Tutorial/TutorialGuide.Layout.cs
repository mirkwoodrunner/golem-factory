using System.Collections.Generic;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Tutorial
{
    /// <summary>
    /// Round 3 of the marked-tile plan: the guide lays its tiles out around what the player
    /// actually built, not at fixed offsets from the stalls.
    ///
    /// <para>
    /// Every golem in the guide works the same way: it stands on a free tile with its source
    /// behind it and its target in front, two tiles apart in a straight line. So each chapter's
    /// layout hangs off one building the player COMMITS to -- the first depot, the coal line's
    /// boiler, the smelter's depots, the first belt, the Slag Heap -- and the golem's tile is the
    /// free tile between it and its source. Until that building exists, the default layout (the
    /// one the guide always drew) stands, so a player who follows the marks sees exactly what
    /// they always did. Pipe suggestions keep their hand-drawn default routes for the default
    /// layout and are found by a shortest-path search otherwise.
    /// </para>
    ///
    /// <para>
    /// Computed once per change to the built world (a building placed, removed or turned), and
    /// read many times a frame by Done, Target, Progress and the panel's marks.
    /// </para>
    /// </summary>
    public sealed partial class TutorialGuide
    {
        private sealed class LayoutPlan
        {
            public Vector2Int Golem1, Depot1;
            public Facing Facing1;
            public Vector2Int Presser, PresserDepot;
            public Facing PresserFacing;
            public Vector2Int Coker, Boiler2;
            public Facing CokerFacing;
            public Vector2Int Scav2, Depot3;
            public Facing Scav2Facing;
            public Vector2Int Smelter, SmeltIn, SmeltOut, Carrier, SlagHeap;
            public Facing SmelterFacing, CarrierFacing;
            public Vector2Int Extractor, Unloader, CopperDepot, Zeppelin, Mast;
            public Vector2Int[] Belts;
            public Facing CopperFacing;
            public Vector2Int[] Pipe1, Pipe2, Pipe3, Pipe4, ZeppelinPipe;
            public bool Golem1Steamable;
        }

        private LayoutPlan _plan;
        private bool _planDirty = true;
        private bool _planWired;

        private LayoutPlan Plan
        {
            get
            {
                if (!_planWired && _world.Build != null)
                {
                    _planWired = true;
                    _world.Build.BuildingPlaced += _ => _planDirty = true;
                    _world.Build.BuildingRemoved += _ => _planDirty = true;
                    _world.Build.ConnectedShapesChanged += () => _planDirty = true;
                }
                if (_planDirty || _plan == null)
                {
                    _planDirty = false;
                    _plan = ComputePlan();
                }
                return _plan;
            }
        }

        private static readonly Facing[] Compass = { Facing.North, Facing.East, Facing.South, Facing.West };

        private static IEnumerable<Facing> Prefer(Facing first) => new[] { first }.Concat(Compass.Where(f => f != first));

        private static Facing Opposite(Facing f) => FacingUtility.RotateClockwise(FacingUtility.RotateClockwise(f));

        private static Vector2Int D(Facing f) => FacingUtility.Delta(f);

        private HashSet<Vector2Int> _stallCells;

        private bool IsStallCell(Vector2Int cell)
        {
            _stallCells ??= new HashSet<Vector2Int>(
                (_world.Setup?.nodes ?? new List<World.SandboxSetup.NodeEntry>()).Select(n => new Vector2Int(n.x, n.y)));
            return _stallCells.Contains(cell);
        }

        /// <summary>A tile a golem or a new building can use: on the ground, no building, no stall.</summary>
        private bool Open(Vector2Int cell) =>
            !_world.Grid.IsOccupied(cell) && _world.Build.IsCellBuildable(cell) && !IsStallCell(cell);

        // Tiles an earlier chapter's layout already uses, while the plan is being computed. A later
        // chapter plans around them: chapter 5's carrier once stood on chapter 6's first belt tile.
        private readonly HashSet<Vector2Int> _taken = new HashSet<Vector2Int>();

        /// <summary><see cref="Open"/>, and not already part of an earlier chapter's layout.</summary>
        private bool Usable(Vector2Int cell) => Open(cell) && !_taken.Contains(cell);

        private bool OpenOr(Vector2Int cell, System.Func<PlaceableBuilding, bool> kind)
        {
            if (Open(cell))
            {
                return true;
            }
            PlaceableBuilding there = BuildingOn(cell);
            return there != null && kind(there);
        }

        private bool IsAt(Vector2Int cell, System.Func<PlaceableBuilding, bool> kind)
        {
            PlaceableBuilding there = BuildingOn(cell);
            return there != null && kind(there);
        }

        /// <summary>
        /// The direction a golem standing beside <paramref name="source"/> faces, with the target
        /// two tiles out: the one where a <paramref name="committed"/> building already stands,
        /// else <paramref name="preferred"/> if that line is free, else the first free line.
        /// </summary>
        private Facing Line(Vector2Int source, Facing preferred, System.Func<PlaceableBuilding, bool> committed,
            System.Func<Facing, bool> allowed = null, System.Func<Facing, bool> secondChoice = null)
        {
            var lines = Prefer(preferred).Where(f => (allowed == null || allowed(f))
                && Usable(source + D(f)) && (Usable(source + D(f) * 2) || IsAt(source + D(f) * 2, committed))).ToList();
            foreach (Facing f in lines)
            {
                if (IsAt(source + D(f) * 2, committed))
                {
                    return f;
                }
            }
            if (secondChoice != null)
            {
                foreach (Facing f in lines)
                {
                    if (secondChoice(f))
                    {
                        return f;
                    }
                }
            }
            return lines.Count > 0 ? lines[0] : preferred;
        }

        private LayoutPlan ComputePlan()
        {
            var p = new LayoutPlan();
            _taken.Clear();
            long tick = _world.Clock.CurrentTick;
            Vector2Int scrap = StallCell;
            Vector2Int coal = CoalCell;
            Vector2Int copper = CopperCell;

            // Chapter 1: the first golem between the Scrap stall and its depot. A boiler the
            // player put elsewhere commits too: the line whose golem tile it steams.
            p.Facing1 = Line(scrap, Facing.North, IsDepot,
                secondChoice: f => _world.Steam.Reaches(scrap + D(f), tick));
            p.Golem1 = scrap + D(p.Facing1);
            p.Depot1 = scrap + D(p.Facing1) * 2;
            _taken.UnionWith(new[] { p.Golem1, p.Depot1, BoilerSpot });
            p.Golem1Steamable = Prefer(Facing.North).Any(f => Open(scrap + D(f)) && OpenOr(scrap + D(f) * 2, IsDepot)
                && _world.Steam.Reaches(scrap + D(f), tick));

            // Chapter 2: the Presser on the first depot's far side, carrying the line on.
            p.PresserFacing = Line(p.Depot1, p.Facing1, IsDepot, allowed: f => f != Opposite(p.Facing1));
            p.Presser = p.Depot1 + D(p.PresserFacing);
            p.PresserDepot = p.Depot1 + D(p.PresserFacing) * 2;
            _taken.UnionWith(new[] { p.Presser, p.PresserDepot });

            // Chapter 3: the coking Presser between the Coal stall and the coal line's boiler.
            p.CokerFacing = Line(coal, Facing.North, IsBoiler);
            p.Coker = coal + D(p.CokerFacing);
            p.Boiler2 = coal + D(p.CokerFacing) * 2;
            _taken.UnionWith(new[] { p.Coker, p.Boiler2 });

            // Chapter 4: the second Scavenger on another side of the Scrap stall (east by default),
            // so turning it is the lesson. Never the first golem's side.
            var used = new HashSet<Vector2Int> { p.Depot1, p.PresserDepot };
            p.Scav2Facing = Line(scrap, Facing.East, b => IsDepot(b) && !used.Contains(b.Cell), allowed: f => f != p.Facing1);
            p.Scav2 = scrap + D(p.Scav2Facing);
            p.Depot3 = scrap + D(p.Scav2Facing) * 2;
            used.Add(p.Depot3);
            _taken.UnionWith(new[] { p.Scav2, p.Depot3 });

            // Chapter 5: the smelter between two depots, the carrier beyond the second, the heap
            // beyond that. By default a column two east of the coal line's boiler, facing north.
            Vector2Int defaultSmelter = p.Boiler2 + new Vector2Int(2, 0);
            (Vector2Int smelter, Facing facing) = SmelterLine(defaultSmelter, used);
            p.Smelter = smelter;
            p.SmelterFacing = facing;
            p.SmeltIn = smelter - D(facing);
            p.SmeltOut = smelter + D(facing);
            p.CarrierFacing = Line(p.SmeltOut, facing, b => b.GetPart<PlaceableSlagHeap>() != null,
                allowed: f => f != Opposite(facing));
            p.Carrier = p.SmeltOut + D(p.CarrierFacing);
            p.SlagHeap = p.SmeltOut + D(p.CarrierFacing) * 2;
            _taken.UnionWith(new[] { p.Smelter, p.SmeltIn, p.SmeltOut, p.Carrier, p.SlagHeap });

            // Chapter 6: the copper line runs away from the stall, along the first belt laid.
            // Committed by the first belt laid along a line that fits; else north if it fits, else
            // the first line that does, else north regardless (the notes then name what's in the way).
            p.CopperFacing = Compass.Where(f => CopperLineFits(copper, f))
                .OrderBy(f => _world.Belts.TryGetBelt(copper + D(f) * 2, out PlacedBelt belt) && belt.Facing == f ? 0 : 1)
                .ThenBy(f => f == Facing.North ? 0 : 1)
                .DefaultIfEmpty(Facing.North).First();
            Vector2Int run = D(p.CopperFacing);
            p.Extractor = copper + run;
            p.Belts = new[] { copper + run * 2, copper + run * 3, copper + run * 4 };
            p.Unloader = copper + run * 5;
            p.CopperDepot = copper + run * 6;

            // Chapter 9: the Zeppelin past the labelled depot, the depot behind it.
            p.Zeppelin = p.CopperDepot + run;
            p.Mast = p.Zeppelin + new Vector2Int(4, 3);

            // Pipe routes last, kept off every tile the layout uses.
            var reserved = new HashSet<Vector2Int>
            {
                p.Golem1, p.Depot1, p.Presser, p.PresserDepot, p.Coker, p.Boiler2, p.Scav2, p.Depot3,
                p.Smelter, p.SmeltIn, p.SmeltOut, p.Carrier, p.SlagHeap, p.Extractor, p.Unloader,
                p.CopperDepot, p.Zeppelin, p.Mast, BoilerSpot,
            };
            reserved.UnionWith(p.Belts);

            bool defaultCh1 = p.Facing1 == Facing.North && p.PresserFacing == Facing.North;
            p.Pipe1 = HandRoute(defaultCh1, new[] { scrap + new Vector2Int(1, 2), scrap + new Vector2Int(1, 3) })
                ?? RouteTo(AllNetworks(), new[] { p.Presser }, reserved);

            p.Pipe2 = HandRoute(p.CokerFacing == Facing.North && p.Facing1 == Facing.North, DefaultPipe2(p))
                ?? CokerRoute(p, reserved);

            bool defaultCh5 = p.CokerFacing == Facing.North && p.SmelterFacing == Facing.North
                && p.Smelter == defaultSmelter && p.CarrierFacing == Facing.North;
            p.Pipe3 = HandRoute(defaultCh5, new[]
                {
                    p.Smelter + new Vector2Int(-1, 0), p.Smelter + new Vector2Int(-1, 1), p.Smelter + new Vector2Int(-1, 2),
                })
                ?? RouteTo(AllNetworks(), new[] { p.Smelter, p.Carrier }, reserved);

            bool defaultCh6 = defaultCh5 && p.CopperFacing == Facing.North;
            p.Pipe4 = HandRoute(defaultCh6, DefaultPipe4(p))
                ?? RouteTo(AllNetworks(), new[] { p.Extractor, p.Unloader }, reserved);

            p.ZeppelinPipe = HandRoute(defaultCh6, new[] { p.Zeppelin + new Vector2Int(-1, 0) })
                ?? RouteTo(AllNetworks(), new[] { p.Zeppelin }, reserved);
            return p;
        }

        /// <summary>
        /// Whether the copper line fits running <paramref name="f"/> from the stall: the extractor's,
        /// unloader's, depot's and Zeppelin's tiles free (or already holding their building), and the
        /// three belt tiles free or belts. Without it a belt laid toward the next stall marked the
        /// rest of the line on top of that stall.
        /// </summary>
        private bool CopperLineFits(Vector2Int copper, Facing f)
        {
            Vector2Int run = D(f);
            for (int i = 2; i <= 4; i++)
            {
                Vector2Int cell = copper + run * i;
                if (!Usable(cell) && !IsAt(cell, b => b.GetPart<PlaceableBelt>() != null))
                {
                    return false;
                }
            }
            return Usable(copper + run) && Usable(copper + run * 5)
                && (Usable(copper + run * 6) || IsAt(copper + run * 6, IsDepot)) && Usable(copper + run * 7);
        }

        // --- Chapter 5's pair of depots ---------------------------------------------------------

        private (Vector2Int smelter, Facing facing) SmelterLine(Vector2Int defaultSmelter, HashSet<Vector2Int> used)
        {
            // Two new depots two apart in a straight line, the tile between them free: that is a
            // smelter's place, whichever way round. The default column wins a tie.
            var depots = Built.Where(b => IsDepot(b) && !used.Contains(b.Cell)).Select(b => b.Cell).ToList();
            var depotSet = new HashSet<Vector2Int>(depots);
            (Vector2Int, Facing)? best = null;
            int bestScore = int.MaxValue;
            foreach (Vector2Int a in depots)
            {
                foreach (Facing f in Compass)
                {
                    Vector2Int middle = a + D(f);
                    if (!depotSet.Contains(a + D(f) * 2) || !Usable(middle))
                    {
                        continue;
                    }
                    // Which end is the smelter's input: the one that leaves the carrier room.
                    Vector2Int beyond = a + D(f) * 3;
                    Vector2Int off = middle - defaultSmelter;
                    int score = (off.x * off.x + off.y * off.y) * 4
                        + (f == Facing.North ? 0 : 1) + (Usable(beyond) ? 0 : 2);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = (middle, f);
                    }
                }
            }
            return best ?? (defaultSmelter, Facing.North);
        }

        // --- Pipe routes ----------------------------------------------------------------------

        /// <summary>The hand-drawn default route, while the layout is the default and the route is clear.</summary>
        private Vector2Int[] HandRoute(bool layoutIsDefault, Vector2Int[] route) =>
            layoutIsDefault && route.All(c => Open(c) || _world.Steam.HasPipe(c)) ? route : null;

        private Vector2Int[] DefaultPipe2(LayoutPlan p)
        {
            var spots = new List<Vector2Int>();
            for (int x = BoilerSpot.x + 1; x < p.Coker.x; x++)
            {
                spots.Add(new Vector2Int(x, BoilerSpot.y));
            }
            spots.Add(new Vector2Int(p.Coker.x - 1, p.Boiler2.y));
            return spots.ToArray();
        }

        private Vector2Int[] DefaultPipe4(LayoutPlan p)
        {
            var spots = new List<Vector2Int>();
            int west = CopperCell.x - 1;
            for (int y = p.Extractor.y; y <= p.Unloader.y + 1; y++)
            {
                spots.Add(new Vector2Int(west, y));
            }
            Vector2Int top = p.Smelter + new Vector2Int(-1, 2);
            int row = p.Unloader.y + 1;
            for (int x = west - 1; x >= top.x; x--)
            {
                spots.Add(new Vector2Int(x, row));
            }
            for (int y = row - 1; y > top.y; y--)
            {
                spots.Add(new Vector2Int(top.x, y));
            }
            return spots.ToArray();
        }

        /// <summary>Every boiler cell and every pipe cell joined to a boiler: where steam starts.</summary>
        private HashSet<Vector2Int> AllNetworks() => NetworksOf(_world.Steam.BoilersInOrder.Select(b => b.Cell));

        private HashSet<Vector2Int> NetworksOf(IEnumerable<Vector2Int> boilers)
        {
            var pipes = new HashSet<Vector2Int>(_world.Steam.PipeCells);
            var cells = new HashSet<Vector2Int>();
            var connected = new HashSet<Vector2Int>();
            foreach (Vector2Int boiler in boilers)
            {
                cells.Add(boiler);
                SteamPipeRules.CollectConnectedPipes(boiler, pipes, connected);
                cells.UnionWith(connected);
            }
            return cells;
        }

        /// <summary>
        /// The pipes to lay so steam reaches every one of <paramref name="golemTiles"/>, in order,
        /// from <paramref name="network"/>: the shortest run of open tiles to a tile beside each.
        /// Empty when steam already reaches them all; never through a tile the layout uses.
        /// </summary>
        private Vector2Int[] RouteTo(HashSet<Vector2Int> network, IEnumerable<Vector2Int> golemTiles, HashSet<Vector2Int> reserved)
        {
            var route = new List<Vector2Int>();
            var grown = new HashSet<Vector2Int>(network);
            foreach (Vector2Int golem in golemTiles)
            {
                if (Neighbours(golem).Any(grown.Contains))
                {
                    continue; // a boiler or a joined pipe is already beside it
                }
                List<Vector2Int> leg = Shortest(grown, cell => Neighbours(golem).Contains(cell), reserved);
                if (leg == null)
                {
                    continue; // nothing reachable: the marks stay off, and the step's note says why
                }
                route.AddRange(leg);
                grown.UnionWith(leg);
            }
            return route.ToArray();
        }

        /// <summary>Chapter 3's route: from the FIRST boiler's run to the coker's side, then on to touch the new boiler.</summary>
        private Vector2Int[] CokerRoute(LayoutPlan p, HashSet<Vector2Int> reserved)
        {
            Vector2Int? first = BoilerBuildings.Where(b => b.Cell != p.Boiler2)
                .OrderBy(b => b.Cell == BoilerSpot ? 0 : 1).Select(b => (Vector2Int?)b.Cell).FirstOrDefault();
            if (first == null)
            {
                return new Vector2Int[0];
            }
            HashSet<Vector2Int> network = NetworksOf(new[] { first.Value });
            var route = new List<Vector2Int>(RouteTo(network, new[] { p.Coker }, reserved));
            network.UnionWith(route);
            if (!Neighbours(p.Boiler2).Any(c => network.Contains(c) && c != first.Value))
            {
                List<Vector2Int> join = Shortest(network, cell => Neighbours(p.Boiler2).Contains(cell), reserved);
                if (join != null)
                {
                    route.AddRange(join);
                }
            }
            return route.ToArray();
        }

        private static IEnumerable<Vector2Int> Neighbours(Vector2Int cell) => SteamPipeRules.OrthogonalOffsets.Select(o => cell + o);

        /// <summary>Breadth-first over open, unreserved tiles from beside <paramref name="from"/> to the first goal tile.</summary>
        private List<Vector2Int> Shortest(HashSet<Vector2Int> from, System.Func<Vector2Int, bool> goal, HashSet<Vector2Int> reserved)
        {
            var parent = new Dictionary<Vector2Int, Vector2Int?>();
            var frontier = new Queue<Vector2Int>();
            foreach (Vector2Int seed in from.OrderBy(c => c.x).ThenBy(c => c.y))
            {
                foreach (Vector2Int n in Neighbours(seed))
                {
                    if (!parent.ContainsKey(n) && !from.Contains(n) && Open(n) && !reserved.Contains(n))
                    {
                        parent[n] = null;
                        frontier.Enqueue(n);
                    }
                }
            }
            while (frontier.Count > 0 && parent.Count < 6000)
            {
                Vector2Int cell = frontier.Dequeue();
                if (goal(cell))
                {
                    var path = new List<Vector2Int>();
                    for (Vector2Int? at = cell; at != null; at = parent[at.Value])
                    {
                        path.Add(at.Value);
                    }
                    path.Reverse();
                    return path;
                }
                foreach (Vector2Int n in Neighbours(cell))
                {
                    if (!parent.ContainsKey(n) && !from.Contains(n) && Open(n) && !reserved.Contains(n))
                    {
                        parent[n] = cell;
                        frontier.Enqueue(n);
                    }
                }
            }
            return null;
        }
    }
}
