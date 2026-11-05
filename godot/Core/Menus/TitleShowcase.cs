using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Golems;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Menus
{
    /// <summary>
    /// The workshop behind the title screen (docs/menus-design.md §6): a live factory the player
    /// watches but cannot touch. It is built the way the playtest kit builds a chapter -- the
    /// guide's own steps performed with the player's verbs -- so it is exactly the workshop a
    /// player has after <see cref="Chapters"/> chapters, and it changes whenever the guide does.
    /// Then <see cref="Keep"/> keeps it running: the script orders the stalls' truckloads, stokes
    /// the boilers and empties full crates, as a player would, so no golem ever stops while the
    /// title is up. The world is thrown away when a game starts; nothing here is ever saved.
    /// </summary>
    public sealed class TitleShowcase
    {
        /// <summary>Chapters 1-6: a Scavenger line, the Pressers, the Coke line, the Slag Heap, a belt.</summary>
        public const int Chapters = 6;

        /// <summary>How often <see cref="Keep"/> looks at the stocks, in seconds of game time.</summary>
        public const float KeepEverySeconds = 1f;

        /// <summary>A stall is restocked below this many goods; a truckload's worth arrives.</summary>
        public const int StallLow = 20;

        /// <summary>A boiler is stoked to full below this much Coke.</summary>
        public const int BoilerLow = SteamBoiler.CokeCapacity / 2;

        /// <summary>A crate holding more than this of a good is emptied down to <see cref="CrateKeep"/>.</summary>
        public const int CrateHigh = 35;
        public const int CrateKeep = 25;

        /// <summary>Frames of 1/30 s each chapter may take before staging gives up on it.</summary>
        private const int FramesPerChapter = 3600;
        private const float FrameSeconds = 1f / 30f;

        private readonly SandboxWorld _world;
        private float _sinceKeep;

        private TitleShowcase(SandboxWorld world) => _world = world;

        /// <summary>The chapter the guide reached; <see cref="Chapters"/> + 1 when staging finished.</summary>
        public int ChapterReached { get; private set; }

        /// <summary>The middle of everything staged, in cells: where the camera looks.</summary>
        public Vector2 Centre { get; private set; }

        /// <summary>The staged factory's width and height, in cells.</summary>
        public Vector2 Extent { get; private set; }

        /// <summary>
        /// Plays the guide's first <paramref name="chapters"/> chapters through the playtest kit on
        /// <paramref name="world"/>'s own clock, as fast as the machine allows (about a third of a
        /// second). The world must be fresh, with its guide, and its clock playing.
        /// </summary>
        public static TitleShowcase Stage(SandboxWorld world, int chapters = Chapters)
        {
            if (world?.Tutorial == null)
            {
                throw new InvalidOperationException("the showcase is built by the guide's kit; this world has no guide");
            }
            var showcase = new TitleShowcase(world);
            // Built in one frame, so quietly, as a load is: no landing, dust, sound or popup.
            world.Quietly(() =>
            {
                for (int chapter = 0; chapter < chapters; chapter++)
                {
                    if (!world.Tutorial.StartKit())
                    {
                        break;
                    }
                    for (int frame = 0; frame < FramesPerChapter && world.Tutorial.KitRunning; frame++)
                    {
                        world.Advance(FrameSeconds);
                    }
                }
                return 0;
            });
            showcase.ChapterReached = world.Tutorial.ChapterOf(world.Tutorial.Index);
            showcase.Frame();
            return showcase;
        }

        /// <summary>Called every frame: keeps the factory stocked, once a <see cref="KeepEverySeconds"/>.</summary>
        public void Keep(float deltaSeconds)
        {
            _sinceKeep += deltaSeconds;
            if (_sinceKeep < KeepEverySeconds)
            {
                return;
            }
            _sinceKeep = 0f;
            KeepStocked();
        }

        /// <summary>
        /// Restocks low stalls, stokes low boilers, empties full crates, and tops up the crate
        /// behind every Haul with its good, at once. Ahead of need: waiting for a golem to stop
        /// first showed it red until the next look.
        /// </summary>
        public void KeepStocked()
        {
            foreach (GolemEntity golem in _world.Golems)
            {
                if (golem == null || golem.IsRemoved)
                {
                    continue;
                }
                ICrate source = CrateAt(golem.SourceCell);
                if (source?.Buffer == null || source.CrateId == SandboxWorld.PostBufferId)
                {
                    continue;
                }
                for (int i = 0; i < golem.Program.appendages.Count; i++)
                {
                    string good = golem.Program.GetItemTypeAt(i);
                    if (golem.Program.appendages[i]?.actionType != PunchCards.AppendageActionType.Haul || string.IsNullOrEmpty(good))
                    {
                        continue;
                    }
                    int missing = CrateKeep - source.Buffer.GetQuantity(good);
                    if (missing > 0)
                    {
                        source.Buffer.Deposit(good, missing);
                    }
                }
            }

            foreach (ResourceNode node in _world.Nodes.Nodes)
            {
                if (node.RemainingQuantity != ResourceNode.Infinite && node.RemainingQuantity < StallLow)
                {
                    int truckload = _world.Market != null && _world.Market.TryGetOffer(node.NodeId, out Economy.MarketOffer offer)
                        ? offer.TruckloadSize
                        : StallLow * 2;
                    node.Deliver(truckload);
                }
            }
            foreach (PlaceableBuilding building in _world.Build.Buildings)
            {
                if (building.IsRemoved)
                {
                    continue;
                }
                SteamBoiler boiler = building.GetPart<PlaceableBoiler>()?.Boiler;
                if (boiler != null && boiler.CokeStock < BoilerLow)
                {
                    boiler.AddCoke(boiler.CokeRoom);
                }
                ICrate crate = building.GetPart<ICrate>();
                if (crate?.Buffer == null || crate.CrateId == SandboxWorld.PostBufferId)
                {
                    continue;
                }
                foreach (KeyValuePair<string, int> held in crate.Buffer.Quantities.ToList())
                {
                    if (held.Value > CrateHigh)
                    {
                        crate.Buffer.TryWithdraw(held.Key, held.Value - CrateKeep);
                    }
                }
            }
        }

        private ICrate CrateAt(Vector2Int cell) =>
            _world.Build.Buildings.FirstOrDefault(b => !b.IsRemoved && b.Cell == cell)?.GetPart<ICrate>();

        /// <summary>The box around every staged building and golem.</summary>
        private void Frame()
        {
            var cells = _world.Build.Buildings.Where(b => !b.IsRemoved).Select(b => b.Cell)
                .Concat(_world.Golems.Where(g => g != null && !g.IsRemoved).Select(g => g.Cell))
                .ToList();
            if (cells.Count == 0)
            {
                return;
            }
            int minX = cells.Min(c => c.x), maxX = cells.Max(c => c.x);
            int minY = cells.Min(c => c.y), maxY = cells.Max(c => c.y);
            Centre = new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
            Extent = new Vector2(maxX - minX + 1, maxY - minY + 1);
        }
    }
}
