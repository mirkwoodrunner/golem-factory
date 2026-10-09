using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.World;

namespace GolemFactory.Tutorial
{
    /// <summary>
    /// The playtest kit (G10, at the user's call: late chapters need hours of play to reach, so a
    /// playtest-only key fast-forwards). It does not skip the guide's steps: it PERFORMS them --
    /// grants a step's goods and does what the player would, with the same verbs (place on the
    /// marked tile, build at the station, program, set to work) -- so the world after a
    /// fast-forward is the world a player who did the chapter would have, and the next chapter
    /// starts from real ground. The report records every use.
    /// </summary>
    public sealed partial class TutorialGuide
    {
        /// <summary>The chapter each step belongs to, from the id that opens it.</summary>
        private static readonly (string firstStepId, int chapter, string title)[] ChapterStarts =
        {
            ("scrap", 1, "The first golem"),
            ("gears", 2, "The first Presser"),
            ("coking-card", 3, "The Coke line"),
            ("patent", 4, "Copies, turning and stalls"),
        };

        /// <summary>The chapter the current step belongs to, and its title.</summary>
        public int ChapterOf(int stepIndex)
        {
            int chapter = 1;
            for (int i = 0; i <= Math.Min(stepIndex, _steps.Count - 1); i++)
            {
                foreach ((string id, int number, string _) in ChapterStarts)
                {
                    if (_steps[i].Id == id)
                    {
                        chapter = number;
                    }
                }
            }
            return chapter;
        }

        public string ChapterTitle(int chapter) =>
            ChapterStarts.Where(c => c.chapter == chapter).Select(c => c.title).FirstOrDefault() ?? "";

        public int ChapterCount => ChapterStarts.Length;

        private int _kitChapter;
        private int _kitPerformedIndex = -1;
        private float _kitStartedAt;

        /// <summary>Whether the kit is fast-forwarding right now.</summary>
        public bool KitRunning => _kitChapter > 0;

        /// <summary>
        /// The playtest kit: finish the current chapter by performing its remaining steps. Steps
        /// that take time (a golem's first cycle) run on the world's own clock; the kit waits.
        /// </summary>
        public bool StartKit()
        {
            if (IsFinished || KitRunning)
            {
                return false;
            }
            _kitChapter = ChapterOf(Index);
            _kitPerformedIndex = -1;
            _kitStartedAt = Now;
            Playtest?.RecordKit($"fast-forwarded chapter {_kitChapter}, {ChapterTitle(_kitChapter)}, from '{Current.Title}'", Now);
            Version++;
            return true;
        }

        /// <summary>Called from <see cref="Update"/>: performs the current step once, then waits for it.</summary>
        private void KitTick()
        {
            if (!KitRunning)
            {
                return;
            }
            if (IsFinished || ChapterOf(Index) != _kitChapter || Current.Id == "done")
            {
                _kitChapter = 0;
                Version++;
                return;
            }
            if (_kitPerformedIndex != Index)
            {
                _kitPerformedIndex = Index;
                if (Performs.TryGetValue(Current.Id, out Action perform))
                {
                    perform();
                }
            }
            // A step the world cannot finish (a golem stuck for a reason the kit did not foresee)
            // must not leave the kit spinning forever.
            if (Now - _kitStartedAt > 120f)
            {
                Playtest?.RecordKit($"gave up on '{Current.Title}' after 2 minutes", Now);
                _kitChapter = 0;
                Version++;
            }
        }

        // --- What each step does when the kit does it for the player ----------------------------

        /// <summary>Whether the kit knows how to do <paramref name="stepId"/>.</summary>
        public bool HasKitAction(string stepId) => Performs.ContainsKey(stepId);

        private Dictionary<string, Action> _performs;

        private Dictionary<string, Action> Performs => _performs ??= new Dictionary<string, Action>
        {
            ["scrap"] = () => Grant(ItemType.Scrap, 20),
            ["coal"] = () => Grant(ItemType.Coal, 5),
            ["coke"] = () => Grant(ItemType.Coke, 5),
            ["iron"] = () => Grant(ItemType.IronPlate, 10),
            ["boiler"] = () => PlaceGranted("BoilerPrefab", BoilerSpot, Facing.North),
            ["fuel"] = () =>
            {
                PlaceableBoiler boiler = Boilers.FirstOrDefault();
                Grant(ItemType.Coke, 20);
                if (boiler != null)
                {
                    _world.Interactor.TryRefuelBoiler(boiler);
                }
            },
            ["golem"] = () => BuildGolem("ClockworkScavenger"),
            ["program"] = () => Program(LiveGolems.FirstOrDefault(g => g.Program?.chassis?.name == "ClockworkScavenger"),
                "ExtractScrap", "PushOutput"),
            ["depot"] = () => PlaceGranted("DepotPrefab", DepotSpot, Facing.North),
            ["work"] = () => LiveGolems.FirstOrDefault(IsProgrammed)?.SetPlacement(GolemSpot, Facing.North),

            ["gears"] = () => Grant(ItemType.Gear, 10),
            ["claim"] = () => Claim("AssembleScrapReclamation"),
            ["presser"] = () => BuildGolem("BrassPresser"),
            ["pipe"] = () =>
            {
                foreach (Vector2Int cell in PipeSpots)
                {
                    PlaceGranted("SteamPipePrefab", cell, Facing.North);
                }
            },
            ["depot2"] = () => PlaceGranted("DepotPrefab", PresserDepotSpot, Facing.North),
            ["program-presser"] = () => Program(Pressers.FirstOrDefault(),
                "HaulScrap", "AssembleScrapReclamation", "PushOutput"),
            ["work-presser"] = () => Pressers.FirstOrDefault(IsIronPresser)?.SetPlacement(PresserSpot, Facing.North),

            ["coking-card"] = () => Claim("AssembleCoking"),
            ["presser2"] = () => BuildGolem("BrassPresser"),
            ["boiler2"] = () => PlaceGranted("BoilerPrefab", Boiler2Spot, Facing.North),
            ["pipes2"] = () =>
            {
                foreach (Vector2Int cell in Pipe2Spots)
                {
                    PlaceGranted("SteamPipePrefab", cell, Facing.North);
                }
            },
            ["coal-order"] = () =>
            {
                if (_world.Nodes.TryGetNode("CoalNode", out ResourceNode node))
                {
                    node.Deliver(40); // the truckload, without the wait
                }
            },
            ["program-coker"] = () => Program(Pressers.FirstOrDefault(g => !IsIronPresser(g)),
                "ExtractScrap", "AssembleCoking", "PushOutput"),
            ["work-coker"] = () => Pressers.FirstOrDefault(IsCoker)?.SetPlacement(CokerSpot, Facing.North),

            ["patent"] = () =>
            {
                GolemEntity first = Scavengers.FirstOrDefault(IsProgrammed);
                if (first != null)
                {
                    _world.Patents.TryPatent(new Blueprints.Blueprint(
                        "BP-KIT", _world.Setup?.assemblyLine?.claimUserId ?? "LocalPlayer",
                        first.Program.chassis, first.Program.logicCore, first.Program.appendages.ToList()));
                }
            },
            ["scav2"] = () => BuildGolem("ClockworkScavenger"),
            ["stamp"] = () => Program(SecondScavenger, "ExtractScrap", "PushOutput"),
            ["depot3"] = () => PlaceGranted("DepotPrefab", Depot3Spot, Facing.North),
            ["turn"] = () => SecondScavenger?.SetPlacement(Scav2Spot, Facing.East),
            ["stall"] = () => SecondScavenger?.SetPlacement(Scav2Spot, Facing.North),
            ["unstall"] = () => SecondScavenger?.SetPlacement(Scav2Spot, Facing.East),
        };

        private void Grant(string item, int quantity) => _world.Buffers.Deposit(_world.StockpileBufferId, item, quantity);

        private void GrantCost(IEnumerable<PunchCards.RecipeIngredient> cost)
        {
            foreach (PunchCards.RecipeIngredient c in cost ?? Enumerable.Empty<PunchCards.RecipeIngredient>())
            {
                Grant(c.itemType, c.quantity);
            }
        }

        /// <summary>Places a building as the player would -- through build mode -- having paid for it.</summary>
        private void PlaceGranted(string key, Vector2Int cell, Facing facing)
        {
            if (Built.Any(b => b.Cell == cell))
            {
                return;
            }
            PlaceableBuilding prefab = _world.Placeables.FirstOrDefault(p => p.Key == key)?.Prefab;
            if (prefab == null)
            {
                return;
            }
            GrantCost(prefab.Cost);
            _world.Build.SetActivePrefab(prefab);
            while (_world.Build.PlacementFacing != facing)
            {
                _world.Build.RotatePlacement();
            }
            _world.Build.PlaceOrRemove(cell);
            _world.Build.CancelPlacement();
        }

        private void BuildGolem(string chassisName)
        {
            if (!_world.Definitions.Chassis.TryGetValue(chassisName, out PunchCards.ChassisDefinition chassis))
            {
                return;
            }
            GrantCost(chassis.cost);
            _world.StarterStation?.TryConstructGolem(chassis, out _);
        }

        private void Program(GolemEntity golem, params string[] cards)
        {
            if (golem == null)
            {
                return;
            }
            golem.Program.logicCore = _world.Definitions.LogicCores["AlwaysOnCore"];
            while (golem.Program.appendages.Count > 0)
            {
                golem.Program.RemoveAppendageAt(0);
            }
            foreach (string card in cards)
            {
                golem.Program.TryAddAppendage(_world.Definitions.Appendages[card]);
            }
        }

        /// <summary>Claims the card with this appendage: from its slot if offered, else granted.</summary>
        private void Claim(string appendageName)
        {
            AssemblyLineState line = _world.AssemblyLine;
            string user = _world.Setup?.assemblyLine?.claimUserId;
            if (line == null || user == null)
            {
                return;
            }
            for (int i = 0; i < line.SlotCount; i++)
            {
                if (line.GetCard(i)?.appendage?.name == appendageName)
                {
                    GrantCost(line.GetCurrentCostBundle(i));
                    _world.AssemblyLineBoard.Claim(i);
                    return;
                }
            }
            string deck = _world.Setup.assemblyLine.deck;
            DraftableCardDefinition card = _world.Definitions.Decks.TryGetValue(deck, out DraftableCardCatalog catalog)
                ? catalog.Cards.FirstOrDefault(c => c.appendage?.name == appendageName)
                : null;
            if (card != null)
            {
                line.GrantClaim(user, card);
            }
        }
    }
}
