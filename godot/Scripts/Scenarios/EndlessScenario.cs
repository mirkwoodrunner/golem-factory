using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Diagnostics;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Menus;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Supply;
using GolemFactory.UI;
using GolemFactory.World;
using GolemFactory.Nodes;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// Endless play (docs/endless-design.md), with real keys and clicks:
    /// <list type="number">
    ///   <item>Before the tower: the bays at 100 say "more once the Clock Tower stands" with no
    ///   button, and the pack stops at 250.</item>
    ///   <item>The tower complete: the Assembly Line tab's bay row takes two Upgrade clicks to the
    ///   golem cap of 112, then says so; the Pack row's Upgrade makes the pack 300.</item>
    ///   <item>A Coal cart at rung 3: I opens its panel, and a click buys rung 4.</item>
    ///   <item>The authored contracts done: E at the City Office shows the first generated
    ///   contract (docs/endless-design.md §1.1), and a click takes it.</item>
    ///   <item>The civic works' site is open; the HUD follows the Tramway; its first stage's goods
    ///   delivered to the site build it, and the panel moves on to the next.</item>
    /// </list>
    /// Writes only <c>user://scenario-saves</c> and <c>user://scenario-settings.cfg</c>.
    /// </summary>
    public sealed class EndlessScenario : StepScenario
    {
        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private PlayerInteractor Hands => Sandbox.Interactor;
        private ManagementScreen Management => Tree.Root.FindChild("Management", true, false) as ManagementScreen;
        private SupplyScreen Carts => SupplyScreen.Find(Runner);

        private Button RowButton(string row) =>
            Management.AssemblyLine.RowList.FindChild(row, true, false)?.FindChild("Upgrade", true, false) as Button;

        private Label RowText(string row) =>
            Management.AssemblyLine.RowList.FindChild(row, true, false)?.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault();

        private CartSpot _coal;

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            Do("no saves", () => { ScenarioSaves.Clear(); return null; });
            Wait(0.5);
            Do("answer the playtest's first question", () =>
            {
                var playtest = Tree.Root.FindChild("Playtest", true, false) as PlaytestNode;
                if (playtest?.Card.Visible == true)
                {
                    Click(playtest.Options.GetChildren().OfType<Button>().First());
                }
                return null;
            });
            Do("a campaign's endgame: bays at 100, the pack at 250, tower goods in the pack", () =>
            {
                if ((Tree.Root.FindChild("Playtest", true, false) as PlaytestNode)?.Card.Visible == true)
                {
                    return "the card is still up";
                }
                Sandbox.Tutorial?.Dismiss();
                Sandbox.Clock.Play();
                Sandbox.AssemblyBay.RestoreTier(AssemblyBayStructure.TierFor(100));
                Sandbox.PackUpgrade.Restore(3);
                foreach (string good in new[] { ItemType.Mechanism, ItemType.Regulator, ItemType.GreatCog, ItemType.Casing, ItemType.ChronometerCore })
                {
                    Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, good, 100);
                }
                Sandbox.TechTree.Poll();
                return Sandbox.EndlessOpen ? "endless play is open before the tower" : null;
            });
            PlanBeforeTheTower();
            PlanTheBays();
            PlanTheCart();
            PlanGeneratedContracts();
            PlanCivicWorks();
        }

        protected override void Cleanup() => ScenarioSaves.Clear();

        private void PlanBeforeTheTower()
        {
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("click Assembly Line", () => { Click(Management.TabButtons[ManagementTab.AssemblyLine]); return null; });
            Do("the bays wait for the tower, with no button", () =>
            {
                string text = RowText("BayRow")?.Text ?? "";
                if (!text.Contains("more once the Clock Tower stands") || RowButton("BayRow") != null)
                {
                    return $"bay row '{text}', button {RowButton("BayRow") != null}";
                }
                return RowButton("PackRow") == null ? null : "the pack offers a rung before the tower";
            });
            Do("Escape", () => { Tap(Key.Escape); return null; });
        }

        private void PlanTheBays()
        {
            Do("the Clock Tower completes", () =>
            {
                Sandbox.ClockTower.Site.RestoreProgress(Sandbox.ClockTower.Site.StageCount, 0L, true);
                return Sandbox.EndlessOpen ? null : "endless play didn't open";
            });
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("click Assembly Line", () => { Click(Management.TabButtons[ManagementTab.AssemblyLine]); return null; });
            Wait(0.1); // the rows are rebuilt; a button has no size until the frame after
            Do("click Upgrade on the bay row", () =>
            {
                Button upgrade = RowButton("BayRow");
                if (upgrade == null || upgrade.Disabled)
                {
                    return $"no live Upgrade on the bay row: '{RowText("BayRow")?.Text}', open {Management.IsOpen}, rows {string.Join(",", Management.AssemblyLine.RowList.GetChildren().Select(c => c.Name.ToString()))}";
                }
                Click(upgrade);
                return null;
            });
            Until("106 slots", 1, () => Sandbox.AssemblyBay.MaxGolemSlots == 106 ? null : $"{Sandbox.AssemblyBay.MaxGolemSlots} ({Sandbox.AssemblyLineBoard.Status})");
            Wait(0.1); // the row is rebuilt; its button has no size until the frame after
            Do("click Upgrade again", () =>
            {
                Button upgrade = RowButton("BayRow");
                if (upgrade == null || upgrade.Disabled)
                {
                    return "no live Upgrade on the bay row";
                }
                Click(upgrade);
                return null;
            });
            Until("112: the golem cap, said so", 1, () =>
            {
                string text = RowText("BayRow")?.Text ?? "";
                return Sandbox.AssemblyBay.MaxGolemSlots == 112 && text.Contains("the most the city allows") && RowButton("BayRow") == null
                    ? null : $"{Sandbox.AssemblyBay.MaxGolemSlots}, '{text}'";
            });
            Do("click Upgrade on the Pack row", () =>
            {
                Button upgrade = RowButton("PackRow");
                if (upgrade == null || upgrade.Disabled)
                {
                    return $"no live Upgrade on the Pack row: '{RowText("PackRow")?.Text}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "endless-rungs");
                Click(upgrade);
                return null;
            });
            Until("the pack carries 300", 1, () => Sandbox.Wallet.Pack.CapacityPerType == 300 ? null : $"{Sandbox.Wallet.Pack.CapacityPerType} ({Sandbox.AssemblyLineBoard.Status})");
            Do("Escape", () => { Tap(Key.Escape); return null; });
        }

        private void PlanTheCart()
        {
            Do("a Coal cart at rung 3, and the player beside it", () =>
            {
                CartSpot spot = Sandbox.Supply.Spots.First(s => s.IsEmpty);
                SupplyResult added = Sandbox.GrantCart(spot.Cell, ItemType.Coal);
                if (!added.Done)
                {
                    return added.Message;
                }
                for (int i = 0; i < 3; i++)
                {
                    Sandbox.Supply.GiveRung(spot, rate: true);
                }
                _coal = spot;
                Hands.Teleport(new CoreVector3(spot.Cell.x, spot.Cell.y + 1, 0f));
                return null;
            });
            Wait(0.3);
            Do("I opens its panel", () => { Tap(Key.I); return null; });
            Do("click Rate: rung 4, in tower goods", () =>
            {
                if (!Carts.IsOpen || Carts.Spot != _coal)
                {
                    return $"panel open {Carts.IsOpen}";
                }
                Button rate = Carts.Button(SupplyAction.Rate);
                SupplyRow row = Carts.Rows.First(r => r.row.Action == SupplyAction.Rate).row;
                if (!row.Enabled || !row.Price.Contains("Regulator"))
                {
                    return $"Rate row '{row.Detail}' '{row.Price}' enabled {row.Enabled}";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "endless-cart");
                Click(rate);
                return null;
            });
            Do("the cart climbed past the campaign's top", () =>
            {
                if (_coal.Cart.RateRung != 4)
                {
                    return $"rung {_coal.Cart.RateRung}: {Carts.Status}";
                }
                Tap(Key.Escape);
                Log($"bays 100 -> 112 (the golem cap) and the pack 250 -> 300 through the Assembly Line tab, a Coal cart to rung 4 ({_coal.Cart.RatePerMinute} a minute) through its panel");
                return null;
            });
        }

        private void PlanGeneratedContracts()
        {
            Do("every authored contract done; the player at the office's door", () =>
            {
                foreach (string good in ItemTiers.CanonicalOrder)
                {
                    Sandbox.TechTree.Ledger.RecordItem(good);
                }
                Sandbox.Contracts.Restore(new Save.ContractsEntry { done = Sandbox.Contracts.Sequence.Select(c => c.name).ToList() });
                Hands.Teleport(new CoreVector3(TownSquare.OfficeCell.x, TownSquare.OfficeCell.y - 1, 0f));
                return null;
            });
            Until("the first generated contract posts", 2, () =>
                Sandbox.Contracts.Current?.name == Contracts.ContractGenerator.NamePrefix + "1" ? null : $"current {Sandbox.Contracts.Current?.name}");
            Wait(0.3);
            Do("E opens its notice", () => { Tap(Key.E); return null; });
            Do("who asks, and what, from the formula", () =>
            {
                ContractScreen notice = ContractScreen.Find(Runner);
                Contracts.ContractDefinition c = Sandbox.Contracts.Current;
                if (!notice.IsOpen || notice.Title != c.requester || notice.Words != c.words)
                {
                    return $"open {notice.IsOpen}, '{notice.Title}', '{notice.Words}'";
                }
                string id = notice.DrawnText.Select(PlayerText.RawIdIn).FirstOrDefault(t => t != null);
                if (id != null)
                {
                    return "an id on the notice: " + id;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "endless-contract");
                Click(notice.Button(Contracts.ContractAction.Accept));
                return null;
            });
            Do("taken", () =>
            {
                if (Sandbox.Contracts.State != Contracts.ContractState.Accepted)
                {
                    return "state " + Sandbox.Contracts.State;
                }
                Tap(Key.Escape);
                Log($"the first generated contract ({Sandbox.Contracts.Current.requester}: {Contracts.ContractText.Goods(Sandbox.Contracts.Current.asks)}) posted after the authored ones and was taken at the office");
                return null;
            });
            Do("the last stage completes, as it does in play", () =>
            {
                ClockTower.ClockTowerSite tower = Sandbox.ClockTower.Site;
                EventBus.Publish(new ClockTowerStageCompletedEvent(tower.StageCount, "The Chronometer", true, Sandbox.Clock.CurrentTick));
                return null;
            });
            Until("its banner, the end screen, points at the office", 15, () =>
            {
                var hud = Tree.Root.FindChild("Hud", true, false) as HudOverlay;
                if (hud.BannerNext != Contracts.ContractText.EndlessInvitation)
                {
                    return $"banner '{hud.BannerText}' / '{hud.BannerNext}'";
                }
                return null;
            });
            Do("look at it", () => { ScenarioCapture.Shot(Runner.GetViewport(), "endless-won"); return null; });
            Until("the banner has gone", 20, () =>
                (Tree.Root.FindChild("Hud", true, false) as HudOverlay).BannerText == "" ? null : "still showing");
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("click Overview", () => { Click(Management.TabButtons[ManagementTab.Overview]); return null; });
            Until("the running totals, over the whole game", 1, () =>
            {
                OverviewTab overview = Management.Overview;
                string expected = Sandbox.RunTotalsLine;
                if (overview == null || overview.Totals != expected || !expected.Contains(Sandbox.Contracts.Done.Count + " contracts done"))
                {
                    return $"totals '{overview?.Totals}', expected '{expected}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "endless-overview");
                Tap(Key.Escape);
                return null;
            });
        }

        private void PlanCivicWorks()
        {
            HudOverlay Hud() => Tree.Root.FindChild("Hud", true, false) as HudOverlay;
            Do("at the civic works site", () =>
            {
                Hands.Teleport(new CoreVector3(TownSquare.CivicCentre.x, TownSquare.CivicCentre.y - 3, 0f));
                return null;
            });
            Until("open, and the HUD follows the Tramway", 2, () =>
            {
                var layer = Tree.GetFirstNodeInGroup(BuildingsLayer.GroupName) as BuildingsLayer;
                if (layer?.Civic == null || layer.Civic.SpritePath != CivicSiteView.OpenSprite)
                {
                    return "drawn as " + layer?.Civic?.SpritePath;
                }
                return Hud().TowerHeadline.StartsWith("The Tramway 1/2 - ") ? null : $"HUD '{Hud().TowerHeadline}'";
            });
            Do("the stage nearly built; its goods delivered to the site, as a golem pushes", () =>
            {
                ClockTower.ClockTowerSite site = Sandbox.Civic.Site;
                site.RestoreProgress(0, site.ActiveStage.RequiredProgressUnits() - 1, false);
                if (!Sandbox.Endpoints.TryGetEndpoint(TownSquare.CivicCentre, out IItemEndpoint endpoint))
                {
                    return "no endpoint on the site";
                }
                foreach (ClockTower.StageDemand demand in site.ActiveStage.demands)
                {
                    for (int i = 0; i < demand.ratePerMinute; i++)
                    {
                        if (!endpoint.TryGive(new Belts.ItemStack { ItemType = demand.itemType }))
                        {
                            return "the site refused " + demand.itemType;
                        }
                    }
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "endless-civic");
                return null;
            });
            Until("the Rails built: the panel moves on to the Trams", 3, () =>
            {
                if (Sandbox.Civic.StagesBuilt != 1)
                {
                    return $"stages built {Sandbox.Civic.StagesBuilt}";
                }
                if (!Hud().TowerHeadline.StartsWith("The Tramway 2/2 - "))
                {
                    return $"HUD '{Hud().TowerHeadline}'";
                }
                if (!Sandbox.RunTotalsLine.Contains(" · 1 civic stage built")) // golems sold follow it
                {
                    return "totals '" + Sandbox.RunTotalsLine + "'";
                }
                Log("the civic works site opened with the tower; the Tramway's first stage was built from deliveries to it, and the HUD moved on to its second");
                return null;
            });
        }
    }
}
