using System.Collections.Generic;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Overview
{
    /// <summary>What a mark on the overview's map stands for: it picks the mark's shape and colour.</summary>
    public enum MapMarkKind
    {
        Belt,
        Crate,
        Steam,
        Machine,
        Station,
        Stall,
        Tower,
        Golem,
        Player,
    }

    /// <summary>A block of cells: its south-west cell and its size.</summary>
    public readonly struct MapRect
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;

        public MapRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public bool Contains(Vector2Int cell) => cell.x >= X && cell.x < X + Width && cell.y >= Y && cell.y < Y + Height;
    }

    /// <summary>One thing on the map: a cell (the south-west one, for the tower's 3 x 3), and what it says when pointed at.</summary>
    public readonly struct MapMark
    {
        public readonly MapMarkKind Kind;
        public readonly Vector2Int Cell;
        public readonly int Size;
        public readonly Facing Facing;
        public readonly GolemMood Mood;

        /// <summary>A golem's id, for the camera; null for anything else. Never shown.</summary>
        public readonly string GolemId;

        /// <summary>
        /// Its name in player words ("Scavenger #2", "Crate (3,4)"). What the pointer shows is
        /// <see cref="FactoryMap.Describe"/>, which adds a golem's state.
        /// </summary>
        public readonly string Label;

        public MapMark(MapMarkKind kind, Vector2Int cell, string label, int size = 1, Facing facing = Facing.North,
            GolemMood mood = GolemMood.Working, string golemId = null)
        {
            Kind = kind;
            Cell = cell;
            Label = label;
            Size = size;
            Facing = facing;
            Mood = mood;
            GolemId = golemId;
        }

        /// <summary>A golem the player must see to: red or blue. The map rings it, so it isn't colour alone.</summary>
        public bool Stopped => Kind == MapMarkKind.Golem && GolemMoodRules.IsStopped(Mood);

        public bool Covers(Vector2Int cell) =>
            cell.x >= Cell.x && cell.x < Cell.x + Size && cell.y >= Cell.y && cell.y < Cell.y + Size;
    }

    /// <summary>
    /// The factory at a glance (docs/menus-design.md §8): the workshop's floor and the street
    /// in front of it, everything the player built, the stalls, the tower, every golem by its
    /// mood, and the player. Composed fresh from the world whenever the map is drawn; it holds
    /// nothing of its own.
    /// </summary>
    public sealed class FactoryMap
    {
        /// <summary>The workshop's floor, west/south/east/north inclusive.</summary>
        public MapRect Workshop { get; private set; }

        /// <summary>The street and the town square below it.</summary>
        public MapRect Street { get; private set; }

        /// <summary>Every cell the map must show.</summary>
        public MapRect Extent { get; private set; }

        public IReadOnlyList<MapMark> Marks => _marks;

        /// <summary>"8 golems · 6 working · 1 waiting · 1 stopped".</summary>
        public string Summary { get; private set; }

        private readonly List<MapMark> _marks = new List<MapMark>();
        private SandboxWorld _world;

        public static FactoryMap Compose(SandboxWorld world)
        {
            var map = new FactoryMap { _world = world };
            int half = world.Bounds.HalfExtent;
            int north = world.Bounds.NorthExtent;
            map.Workshop = new MapRect(-half, -half, half * 2 + 1, north + half + 1);
            int streetBottom = TownSquare.Bottom;
            map.Street = new MapRect(-FloorLayout.StreetHalfExtent, streetBottom, FloorLayout.StreetHalfExtent * 2 + 1, -half - streetBottom);
            int west = -FloorLayout.StreetHalfExtent;
            map.Extent = new MapRect(west, streetBottom, -west * 2 + 1, north - streetBottom + 1);

            TileNames names = world.TileNames;
            var cells = new GridCoordinateConverter(new Vector2(1f, 1f));
            foreach (ResourceNodeMarker marker in world.Markers)
            {
                Vector2Int cell = cells.WorldToCell(marker.Position);
                map._marks.Add(new MapMark(MapMarkKind.Stall, cell, names.Name(cell)));
            }
            map._marks.Add(new MapMark(MapMarkKind.Tower, TownSquare.TowerOrigin, "the Clock Tower", TownSquare.TowerSize));
            foreach (PlaceableBuilding building in world.AuthoredBuildings.Concat(world.Build.Buildings))
            {
                if (building == null || building.IsRemoved || building.IsFixture)
                {
                    continue;
                }
                map._marks.Add(new MapMark(KindOf(building), building.Cell, BuildingName(building, names), facing: building.Facing));
            }

            int working = 0, waiting = 0, stopped = 0;
            foreach (GolemEntity golem in world.Golems)
            {
                if (golem == null || golem.IsRemoved)
                {
                    continue;
                }
                GolemMood mood = golem.Mood;
                if (GolemMoodRules.IsStopped(mood))
                {
                    stopped++;
                }
                else if (mood == GolemMood.Working || mood == GolemMood.Straining)
                {
                    working++;
                }
                else
                {
                    waiting++;
                }
                map._marks.Add(new MapMark(MapMarkKind.Golem, golem.Cell, GolemNames.Short(golem), mood: mood, golemId: golem.GolemId));
            }
            map._marks.Add(new MapMark(MapMarkKind.Player, world.Interactor.PlayerCell, "You"));

            int golems = working + waiting + stopped;
            map.Summary = golems == 0
                ? "No golems yet"
                : $"{golems} golem{(golems == 1 ? "" : "s")} · {working} working · {waiting} idle or waiting · {stopped} stopped";
            return map;
        }

        /// <summary>
        /// What is on <paramref name="cell"/>, the one the pointer is over: the player first, then
        /// a golem, then what it stands on.
        /// </summary>
        public MapMark? MarkAt(Vector2Int cell)
        {
            foreach (MapMarkKind kind in new[] { MapMarkKind.Player, MapMarkKind.Golem })
            {
                foreach (MapMark mark in _marks)
                {
                    if (mark.Kind == kind && mark.Covers(cell))
                    {
                        return mark;
                    }
                }
            }
            foreach (MapMark mark in _marks)
            {
                if (mark.Covers(cell))
                {
                    return mark;
                }
            }
            return null;
        }

        public static MapMarkKind KindOf(PlaceableBuilding building)
        {
            switch (building.PrefabKey)
            {
                case "BeltPrefab":
                case "BeltSplitterPrefab":
                    return MapMarkKind.Belt;
                case "DepotPrefab":
                case "FreightMastPrefab":
                case "PneumaticPostPrefab":
                    return MapMarkKind.Crate;
                case "BoilerPrefab":
                case "SteamPipePrefab":
                    return MapMarkKind.Steam;
                case "GolemConstructionStationPrefab":
                case "HandCrankBenchPrefab":
                    return MapMarkKind.Station;
                default:
                    // An authored station or bench has no prefab key: it is named for its part.
                    return building.GetPart<GolemConstructionStation>() != null ? MapMarkKind.Station : MapMarkKind.Machine;
            }
        }

        private static string BuildingName(PlaceableBuilding building, TileNames names)
        {
            string name = names.Name(building.Cell);
            if (!name.StartsWith("the floor"))
            {
                return name;
            }
            // Not an endpoint (a station, a bench, a pipe): named from what the build menu calls it.
            string label = BuildMenuLabels.NameFor(string.IsNullOrEmpty(building.PrefabKey) ? building.name : building.PrefabKey);
            return label + " " + TileNames.Cell(building.Cell);
        }

        /// <summary>
        /// What the pointer says over <paramref name="mark"/>: a golem's name and its hover card's
        /// state ("Scavenger #2 · Waiting for Scrap"), anything else its name. Asked for the one
        /// mark under the pointer only: a hover card finds a waiting golem's feeders, which is
        /// never done for every golem (docs/onboarding-feedback-design.md §17.2).
        /// </summary>
        public string Describe(MapMark mark)
        {
            if (mark.Kind != MapMarkKind.Golem || _world == null)
            {
                return mark.Label;
            }
            GolemEntity golem = _world.Golems.FirstOrDefault(g => g != null && !g.IsRemoved && g.GolemId == mark.GolemId);
            if (golem == null)
            {
                return mark.Label;
            }
            string state = _world.HoverCard(golem, golem.Mood).State;
            return string.IsNullOrEmpty(state) ? mark.Label : mark.Label + " · " + state;
        }
    }
}
