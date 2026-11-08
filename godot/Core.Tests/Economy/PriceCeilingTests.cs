using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.ClockTower;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Steam;
using GolemFactory.Tutorial;
using NUnit.Framework;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// docs/tuning-data-spec.md §15.3: the ceiling on a price is the pack reachable when it
    /// opens (invariants 1 and 2), plus the data checks the spec adds -- the Lens at 3 Glass and
    /// stage 3's Slag balance (invariant 4), claim prices on their formula (invariant 8), and
    /// one coker for one full boiler (invariant 7). Shares <see cref="PriceList"/> with
    /// <c>PriceReachTests</c>.
    /// </summary>
    public class PriceCeilingTests
    {
        // --- Invariant 2: what the guide's path pays -------------------------------------------

        /// <summary>The chassis a step has the player build, by step id: the kit's, and chapter 10's.</summary>
        private static readonly Dictionary<string, string> ChassisBuilt = new Dictionary<string, string>(PriceList.KitBuilds)
        {
            // Chapter 10's text: "build a Mainspring Overclocker at the station" (two for stage 1).
            { "frame-section", "MainspringOverclocker" },
        };

        /// <summary>The carts and rungs a step has the player buy, by step id (supply-design §6).</summary>
        private static readonly Dictionary<string, string> CartBought = new Dictionary<string, string>
        {
            { "coal", "cart Coal" }, { "coal-order", "cart Coal rate 1" }, { "copper", "cart CopperOre" },
            { "zinc-ingot", "cart ZincOre" }, { "aether-cell", "cart Aether" },
        };

        /// <summary>The Assembly Line tab's purchases a step makes, by step id.</summary>
        private static readonly Dictionary<string, string> Purchased = new Dictionary<string, string>
        {
            { "expand", "floor expansion 1" }, { "bays", "assembly bays" }, { "pack", "pack rung 1" },
        };

        /// <summary>Every price a guide step's kit pays: (step index, what, price).</summary>
        private static List<(int step, string what, IReadOnlyList<RecipeIngredient> price)> GuidePath(PriceList list)
        {
            var path = new List<(int, string, IReadOnlyList<RecipeIngredient>)>();
            IReadOnlyList<TutorialStep> steps = list.World.Tutorial.Steps;
            for (int s = 0; s < steps.Count; s++)
            {
                TutorialStep step = steps[s];
                if (step.MenuKey != null)
                {
                    path.Add((s, "placeable " + step.MenuKey, list.Find("placeable " + step.MenuKey).Lines));
                }
                if (step.Card != null)
                {
                    DraftableCardDefinition card = list.CardFor(step);
                    path.Add((s, "card " + card.name, PriceList.ClaimPrice(card)));
                }
                if (ChassisBuilt.TryGetValue(step.Id, out string chassis))
                {
                    path.Add((s, "chassis " + chassis, list.Find("chassis " + chassis).Lines));
                }
                if (CartBought.TryGetValue(step.Id, out string cart))
                {
                    path.Add((s, cart, list.Find(cart).Lines));
                }
                if (Purchased.TryGetValue(step.Id, out string bought))
                {
                    path.Add((s, bought, list.Find(bought).Lines));
                }
            }
            return path;
        }

        [Test]
        public void GuidePathPrices_AskNoMoreThanTheTaughtRung()
        {
            PriceList list = PriceList.Play();
            int baseCap = list.World.Setup.packCapacityPerType;
            int taught = list.World.Setup.packUpgrades[0].capacityPerType;
            foreach ((int step, string what, IReadOnlyList<RecipeIngredient> price) in GuidePath(list))
            {
                int chapter = list.ChapterAt(step);
                int ceiling = chapter <= 4 ? baseCap : taught;
                foreach ((string good, int sum) in PriceList.Summed(price))
                {
                    Assert.LessOrEqual(sum, ceiling,
                        $"{what}, paid on '{list.StepAt(step)}' (chapter {chapter}), asks {sum} {good}: the guide's path stops at {ceiling}");
                }
            }
        }

        [Test]
        public void EveryRung_HasAPriceThatNeedsIt()
        {
            PriceList list = PriceList.Play();
            IReadOnlyList<PackUpgradeRung> rungs = list.Rungs;
            // The campaign's rungs. Endless play's (300, 350) are needed by its generated
            // contracts, filled by hand from the pack: GeneratedContractTests pins that.
            for (int k = 0; k < rungs.Count && !rungs[k].endless; k++)
            {
                int below = k == 0 ? list.World.Setup.packCapacityPerType : rungs[k - 1].capacityPerType;
                int cap = rungs[k].capacityPerType;
                int rungOpens = list.Opens(rungs[k].cost);
                List<string> needing = list.Prices
                    .Where(p => !p.What.StartsWith("pack rung"))
                    .Where(p => list.Opens(p.Lines) >= rungOpens)
                    .Where(p => PriceList.Summed(p.Lines).Any(l => l.sum > below && l.sum <= cap))
                    .Select(p => p.What).ToList();
                TestContext.Out.WriteLine($"rung {k + 1} ({cap}): {string.Join(", ", needing)}");
                Assert.IsNotEmpty(needing, $"rung {k + 1} ({below} → {cap}) has nothing to buy that needs it");
            }
        }

        /// <summary>
        /// Every price that opens in chapters 1-4, as tuning-data-spec §8 and §4.3 set them (the
        /// Presser at its new price, the carts at supply-design §3's). Nothing there is raised
        /// for the pack's rungs (invariant 2): a later raise must edit this table and say why.
        /// </summary>
        private static readonly Dictionary<string, string> ChaptersOneToFour = new Dictionary<string, string>
        {
            { "assembly bays", "Scrap:40 IronPlate:20" },
            { "bench R1_Coking", "Coal:1" },
            { "bench R2_ScrapReclamation", "Scrap:1" },
            { "bench R8_GearCutting", "IronPlate:2" },
            { "boiler hand-load", "Coke:20" },
            { "card Card_AetherHauler", "IronPlate:5 Gear:2 Coke:7" }, // follows its chassis
            { "card Card_BrassPresser", "Scrap:15 IronPlate:2 Gear:1" },
            { "card Card_ClockworkScavenger", "Scrap:3" },
            { "card Card_ExtractScrap", "" },
            { "card Card_FreightLaunch", "" },
            { "card Card_HaulScrap", "" },
            { "card Card_PushOutput", "" },
            { "card Card_R1_Coking", "Coal:4" },
            { "card Card_R2_ScrapReclamation", "Scrap:4" },
            { "card Card_R4_IronSmelting", "Scrap:8 Coke:4" },
            { "card Card_R8_GearCutting", "IronPlate:8" },
            { "card Card_RepeatAssembly", "" }, // free; claimable only once a Mainspring is made (§9)
            // Lowered, not raised (user, 2026-10-20): chapter 8's floor was 35:39 of its 30:00
            // ceiling at 80 Iron Plate + 40 Gear, one Hauler for each two-input good (§18).
            { "chassis AetherHauler", "IronPlate:20 Gear:10 Coke:30" },
            { "chassis BrassPresser", "Scrap:60 IronPlate:10 Gear:5" }, // §12: was 20 Iron Plate + 10 Gear
            { "chassis ClockworkScavenger", "Scrap:12" },
            { "floor expansion 1", "Scrap:80 IronPlate:40" },
            { "floor expansion 2", "Scrap:120 IronPlate:60 Gear:30" }, // pack rung 1 opens with it, at "gears"
            { "pack rung 1", "IronPlate:60 Gear:30" },
            { "placeable BeltPrefab", "Scrap:1" },
            { "placeable BeltSplitterPrefab", "Scrap:4" },
            { "placeable BoilerPrefab", "Scrap:30 IronPlate:10" },
            { "placeable DepotPrefab", "Scrap:15" },
            { "placeable HandCrankBenchPrefab", "" },
            { "placeable ScrapRecyclerPrefab", "Scrap:20 IronPlate:10" },
            { "placeable SlagHeapPrefab", "Scrap:20 IronPlate:10" },
            { "placeable SteamPipePrefab", "IronPlate:1" },
            // docs/supply-design.md §3: the carts and rungs that open by chapter 4. Copper Ore's
            // cart opens with its price's goods, before chapter 6 asks for it (user, 2026-11-06).
            { "cart Coal", "Scrap:80" },
            // The Coal rung chapter 3 teaches, lowered so chapter 3 fits its ceiling (§6).
            { "cart Coal capacity 1", "IronPlate:5 Gear:2" },
            { "cart Coal rate 1", "IronPlate:10 Gear:5" },
            { "cart CopperOre", "IronPlate:60 Gear:20" },
            { "cart Scrap", "Scrap:60" },
            { "cart Scrap capacity 1", "IronPlate:20 Gear:5" },
            { "cart Scrap rate 1", "IronPlate:40 Gear:10" },
        };

        private static string Text(IReadOnlyList<RecipeIngredient> price) =>
            string.Join(" ", price.Select(c => c.itemType + ":" + c.quantity));

        [Test]
        public void NothingInChaptersOneToFour_WasRaised()
        {
            PriceList list = PriceList.Play();
            var opened = new Dictionary<string, string>();
            foreach (PriceList.Price price in list.Prices)
            {
                int opens = list.Opens(price.Lines);
                if (opens < list.Seen.Count && list.ChapterAt(opens) <= 4)
                {
                    opened[price.What] = Text(price.Lines);
                }
            }
            TestContext.Out.WriteLine(string.Join("\n", opened.OrderBy(p => p.Key).Select(p => $"{{ \"{p.Key}\", \"{p.Value}\" }},")));
            CollectionAssert.AreEquivalent(ChaptersOneToFour, opened);
        }

        // --- Invariant 4: the Lens, and stage 3 ---------------------------------------------------

        [Test]
        public void TheLensCostsThreeGlass()
        {
            RecipeDefinition r10 = AuthoredData.Load().Recipes["R10_LensGrinding"];
            CollectionAssert.AreEquivalent(new[] { (ItemType.Glass, 3), (ItemType.Brass, 1) },
                r10.inputs.Select(i => (i.itemType, i.quantity)));
            Assert.AreEqual(ItemType.Lens, r10.outputItemType);
            Assert.AreEqual(1, r10.outputQuantity);
        }

        [Test]
        public void StageThreeStillBalances()
        {
            // §11's arithmetic from the data: Slag the stage's Frame Sections make (Iron Plate by
            // R4, which makes Slag with it) against the Glass its Lenses take, both at 1× demand.
            DefinitionSet defs = AuthoredData.Load();
            ClockTowerStageDefinition stage = defs.ClockTowerStages.Values.Single(s => s.stageNumber == 3);
            int Demand(string good) => stage.demands.Where(d => d.itemType == good).Sum(d => d.ratePerMinute);
            int Input(string recipe, string good) => defs.Recipes[recipe].inputs.Where(i => i.itemType == good).Sum(i => i.quantity);

            double lens = Demand(ItemType.Lens)
                + Input("R12_AetherContainment", ItemType.Lens) * Input("R18_AetherConduit", ItemType.AetherCell) * Demand(ItemType.AetherConduit);
            double slagNeeded = lens * Input("R10_LensGrinding", ItemType.Glass) / defs.Recipes["R3_Glassmaking"].outputQuantity;
            double platePerFrame = Input("R15_FrameSection", ItemType.Casing) * Input("R9_CasingPress", ItemType.IronPlate)
                + Input("R15_FrameSection", ItemType.IronPlate);
            RecipeDefinition r4 = defs.Recipes["R4_IronSmelting"];
            double slagMade = Demand(ItemType.FrameSection) * platePerFrame * r4.byproductQuantity / r4.outputQuantity;
            double balance = slagMade - slagNeeded;
            TestContext.Out.WriteLine($"stage 3: {lens} Lens/min need {slagNeeded} Slag; {slagMade} made; balance {balance}");
            Assert.AreEqual(-38, balance, 10, "invariant 4: within ±10 Slag/min of the deficit at 2 Glass a Lens");
        }

        // --- Invariant 8: claim prices follow their formula --------------------------------------

        [Test]
        public void ClaimPricesFollowTheirFormula()
        {
            DefinitionSet defs = AuthoredData.Load();
            foreach (DraftableCardDefinition card in defs.Decks.Values.SelectMany(d => d.Cards.Concat(d.OpeningHand)).Where(c => c != null).Distinct())
            {
                IEnumerable<(string, int)> expected;
                if (card.chassis != null)
                {
                    expected = card.chassis.cost.Select(c => (c.itemType, Math.Max(1, c.quantity / 4)));
                }
                else if (card.appendage?.recipe != null)
                {
                    expected = card.appendage.recipe.inputs.Select(i => (i.itemType, 4 * i.quantity));
                }
                else
                {
                    expected = Enumerable.Empty<(string, int)>();
                }
                CollectionAssert.AreEquivalent(expected, (card.claimCost ?? new List<RecipeIngredient>()).Select(c => (c.itemType, c.quantity)), card.name);
            }
        }

        // --- Invariant 7: one coker feeds one full boiler ----------------------------------------

        [Test]
        public void OneCokerFeedsAFullBoiler()
        {
            // The guide's coker: Extract(1), Assemble Coking, Push its 1 Coke (2 + units ticks).
            RecipeDefinition r1 = AuthoredData.Load().Recipes["R1_Coking"];
            int cycle = StepDurationRules.ExtractFromNode(1) + r1.durationTicks + (2 + r1.outputQuantity);
            double cokePerMinute = 600.0 * r1.outputQuantity / cycle;
            double cokePerGolem = 600.0 / SteamNetwork.TicksPerCokePerPoweredGolem;
            TestContext.Out.WriteLine($"coker {cokePerMinute:0.0} Coke/min; a full boiler burns {SteamNetwork.MaxGolemsPerBoiler * cokePerGolem}");
            Assert.GreaterOrEqual(cokePerMinute, SteamNetwork.MaxGolemsPerBoiler * cokePerGolem);
        }
    }
}
