using System.Linq;
using Godot;
using GolemFactory.Economy;
using GolemFactory.Menus;
using GolemFactory.Player;
using GolemFactory.Supply;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The street's supply carts (docs/supply-design.md), with real keys and clicks:
    /// <list type="number">
    ///   <item>The guide (§6): held E takes Scrap at the starting Scrap cart; the guide asks for a
    ///   Coal cart and marks its spot; walking there with D, E and a click add it, and E takes 5
    ///   Coal as it fills, which moves the guide on to Coke.</item>
    ///   <item>Walking with S and A to the spot at x = −20: the prompt offers to add a cart, and E
    ///   opens the cart panel on it, listing the carts the Ledger allows.</item>
    ///   <item>A click adds a Coal cart: the price leaves the pack, the rope becomes the Coal
    ///   cart on the street, and it fills by itself on the clock.</item>
    ///   <item>E at the cart takes Coal; I opens its panel; a click buys a Rate rung.</item>
    ///   <item>Saved to Slot 1, removed (half its price back, the rope again), and loaded from
    ///   Slot 1 with two clicks: the cart comes back at its rung.</item>
    /// </list>
    /// Writes only <c>user://scenario-saves</c> and <c>user://scenario-settings.cfg</c>.
    /// </summary>
    public sealed class SupplyScenario : StepScenario
    {
        private static readonly CoreVector2Int SpotCell = new CoreVector2Int(-20, -16);

        private CoreVector2Int ScrapHome => Sandbox.Setup.HomeSpot(ItemType.Scrap).Value;
        private CoreVector2Int CoalHome => Sandbox.Setup.HomeSpot(ItemType.Coal).Value;
        private GolemFactory.Tutorial.TutorialGuide Guide => Sandbox.Tutorial;
        private CartLadder CoalLadder => Sandbox.Supply.TryGetLadder(ItemType.Coal, out CartLadder l) ? l : null;
        private int Line(System.Collections.Generic.IEnumerable<GolemFactory.PunchCards.RecipeIngredient> price, string item) =>
            price.Where(p => p.itemType == item).Sum(p => p.quantity);

        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private PlayerInteractor Hands => Sandbox.Interactor;
        private SupplyScreen Panel => SupplyScreen.Find(Runner);
        private ManagementScreen Management => Tree.Root.FindChild("Management", true, false) as ManagementScreen;
        private CartSpot Spot => Sandbox.Supply.SpotAt(SpotCell);
        private CartSpotView View => (Tree.GetFirstNodeInGroup(SandboxNode.GroupName) as SandboxNode)?.SpotViews.Single(v => v.Spot == Spot);
        private int Pack(string item) => Sandbox.Wallet.Pack.GetQuantity(item);

        private int _scrap;
        private int _coal;
        private int _plate;

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            Do("no saves", () => { ScenarioSaves.Clear(); return null; });
            Wait(0.5);
            PlanGuide();
            Do("goods for a cart and a rung", () =>
            {
                foreach ((string item, int n) in new[] { (ItemType.Scrap, 100), (ItemType.IronPlate, 40), (ItemType.Gear, 10) })
                {
                    Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, item, n);
                }
                Sandbox.Clock.Play();
                if (View == null || View.Shown != SupplyArt.SpotSprite)
                {
                    return "the spot isn't drawn as a rope: " + View?.Shown;
                }
                return null;
            });
            PlanWalk();
            PlanAdd();
            PlanCart();
            PlanSaveRemoveLoad();
        }

        protected override void Cleanup() => ScenarioSaves.Clear();

        // --- 0. The guide's Coal cart --------------------------------------------------------------

        private void PlanGuide()
        {
            // Playtest mode asks its first question before anything else; a card up is modal.
            Do("answer the playtest's first question", () =>
            {
                var playtest = Tree.Root.FindChild("Playtest", true, false) as PlaytestNode;
                if (playtest?.Card.Visible == true)
                {
                    Click(playtest.Options.GetChildren().OfType<Button>().First());
                }
                return null;
            });
            Do("the card is down", () =>
                (Tree.Root.FindChild("Playtest", true, false) as PlaytestNode)?.Card.Visible == true ? "the card is still up" : null);
            Do("the guide asks for Scrap, at the starting Scrap cart", () =>
            {
                Sandbox.Clock.Play();
                if (Guide?.Current?.Id != "scrap" || Guide.TargetCell != ScrapHome)
                {
                    return $"guide on '{Guide?.Current?.Id}', pointing at {Guide?.TargetCell}";
                }
                SupplyCart scrap = Sandbox.Supply.SpotAt(ScrapHome).Cart;
                return scrap?.ItemType == ItemType.Scrap && scrap.Stock == scrap.Capacity ? null : "no full Scrap cart at the start";
            });
            Do("S down to the street", () => { Press(Key.S, true); return null; });
            Until("on the street", 10, () => Hands.Position.y <= ScrapHome.y + 1.2f ? null : $"at y {Hands.Position.y:0.0}");
            Do("A to the Scrap cart", () => { Press(Key.S, false); Press(Key.A, true); return null; });
            Until("at the Scrap cart", 10, () => Hands.Position.x <= ScrapHome.x + 0.3f ? null : $"at x {Hands.Position.x:0.0}");
            Do("stop, and hold E", () =>
            {
                Press(Key.A, false);
                if (Hands.CurrentPick.Kind != InteractionKind.Harvest || !Hands.CurrentPrompt.Contains("Scrap cart"))
                {
                    return $"pick {Hands.CurrentPick.Kind}, prompt '{Hands.CurrentPrompt}'";
                }
                Press(Key.E, true);
                return null;
            });
            Until("the guide moves on to the Coal cart", 20, () =>
                Guide.Current?.Id == "coal" ? null : $"'{Guide.Current?.Id}', Scrap {Pack(ItemType.Scrap)}, prompt '{Hands.CurrentPrompt}'");
            Do("let go: the step marks the Coal spot and names the cart's price", () =>
            {
                Press(Key.E, false);
                int price = Line(CoalLadder.AddPrice, ItemType.Scrap);
                if (Guide.Current.Spot != CoalHome || !Guide.Current.Body.Contains($"add a Coal cart ({price} Scrap)"))
                {
                    return $"marks {Guide.Current.Spot}, reads '{Guide.Current.Body}'";
                }
                // The Scrap the cart's price wants beyond the step's 20, as a player would hold E for it.
                Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, ItemType.Scrap, price);
                return null;
            });
            Wait(0.5); // the Ledger catches up with the Scrap
            Do("D to the marked spot", () => { Press(Key.D, true); return null; });
            Until("at the Coal spot", 10, () => Hands.Position.x >= CoalHome.x - 0.3f ? null : $"at x {Hands.Position.x:0.0}");
            Do("stop: the guide points here", () =>
            {
                Press(Key.D, false);
                ScenarioCapture.Shot(Runner.GetViewport(), "supply-guide");
                return Guide.TargetCell == CoalHome && Hands.CurrentPick.Kind == InteractionKind.Supply
                    ? null : $"guide at {Guide.TargetCell}, pick {Hands.CurrentPick.Kind}";
            });
            Do("E opens the panel", () => { Tap(Key.E); return null; });
            Do("click Coal cart", () =>
            {
                if (!Panel.IsOpen || Panel.Spot?.Cell != CoalHome)
                {
                    return $"panel open {Panel.IsOpen} on {Panel.Spot?.Cell}";
                }
                Click(Panel.Button(SupplyAction.Add, ItemType.Coal));
                return null;
            });
            Do("a Coal cart on the marked spot; Escape", () =>
            {
                if (Sandbox.Supply.SpotAt(CoalHome).Cart?.ItemType != ItemType.Coal)
                {
                    return "no Coal cart: " + Panel.Status;
                }
                Tap(Key.Escape);
                return null;
            });
            Do("the mark is gone", () => Guide.Current.Id == "coal" && Guide.Current.Spot == null && Guide.Current.ProgressText(Sandbox) == "Coal  0 / 5"
                ? null : $"'{Guide.Current.Id}', marks {Guide.Current.Spot}, '{Guide.Current.ProgressText(Sandbox)}'");
            for (int i = 0; i < 5; i++)
            {
                Until($"Coal {i + 1} has trickled in", 10, () => Sandbox.Supply.SpotAt(CoalHome).Cart.Stock >= 1 ? null : "the cart is empty");
                Do($"E takes Coal {i + 1}", () => { Tap(Key.E); return null; });
            }
            Do("five Coal: the guide moves on to Coke", () =>
            {
                if (Guide.Current?.Id != "coke")
                {
                    return $"'{Guide.Current?.Id}', Coal {Pack(ItemType.Coal)}";
                }
                Log($"the guide's Coal cart: held E took Scrap at the starting cart, the mark led to {CoalHome}, E and a click added the cart, E took 5 Coal as it filled");
                return null;
            });
        }

        // --- 1. To the spot ---------------------------------------------------------------------

        private void PlanWalk()
        {
            Do("S down to the street", () => { Press(Key.S, true); return null; });
            Until("on the stall row's front", 10, () => Hands.Position.y <= SpotCell.y + 1.2f ? null : $"at y {Hands.Position.y:0.0}");
            Do("A along the street", () => { Press(Key.S, false); Press(Key.A, true); return null; });
            Until("at the spot", 12, () => Hands.Position.x <= SpotCell.x + 0.3f ? null : $"at x {Hands.Position.x:0.0}");
            Do("stop", () => { Press(Key.A, false); return null; });
            Wait(0.3);
            Do("the prompt offers a cart, and E opens the panel", () =>
            {
                if (Hands.CurrentPick.Kind != InteractionKind.Supply || !Hands.CurrentPrompt.StartsWith("[E]  Add a cart at this spot"))
                {
                    return $"pick {Hands.CurrentPick.Kind}, prompt '{Hands.CurrentPrompt}'";
                }
                Tap(Key.E);
                return null;
            });
        }

        // --- 2. Adding a cart -------------------------------------------------------------------

        private void PlanAdd()
        {
            Do("the panel lists the carts", () =>
            {
                if (!Panel.IsOpen || Panel.Spot != Spot || Panel.Title != "Cart spot")
                {
                    return $"open {Panel.IsOpen}, '{Panel.Title}'";
                }
                // Every cart whose price the Ledger has seen (the pack holds the Rate rung's Plate
                // and Gear, so Copper Ore's cart is offered too), in the street's order.
                string[] offered = Panel.Rows.Select(r => r.row.Label).ToArray();
                string[] expected = Sandbox.Supply.Offers().Select(l => SupplyText.CartName(l.ItemType)).ToArray();
                if (!offered.SequenceEqual(expected) || !offered.Contains("Coal cart"))
                {
                    return "offers " + string.Join(", ", offered) + "; the street offers " + string.Join(", ", expected);
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "supply-spot");
                return null;
            });
            Do("click Coal cart", () =>
            {
                _scrap = Pack(ItemType.Scrap);
                Click(Panel.Button(SupplyAction.Add, ItemType.Coal));
                return null;
            });
            Do("a Coal cart, for its price", () =>
            {
                if (Spot.Cart?.ItemType != ItemType.Coal)
                {
                    return "no Coal cart: " + Panel.Status;
                }
                if (Pack(ItemType.Scrap) != _scrap - Line(CoalLadder.AddPrice, ItemType.Scrap))
                {
                    return $"Scrap {_scrap} -> {Pack(ItemType.Scrap)}";
                }
                if (Panel.Title != "Coal cart" || Panel.Status != "Added a Coal cart.")
                {
                    return $"'{Panel.Title}', '{Panel.Status}'";
                }
                Tap(Key.Escape);
                return null;
            });
            Do("drawn as the Coal cart", () => View.Shown == "stall_coal" && !Panel.IsOpen ? null : $"shows {View.Shown}, panel open {Panel.IsOpen}");
            Until("it fills by itself", 10, () => Spot.Cart.Stock >= 2 ? null : $"holds {Spot.Cart.Stock}");
            Do("the prompt is the cart's", () =>
            {
                string prompt = Hands.CurrentPrompt;
                if (Hands.CurrentPick.Kind != InteractionKind.Harvest || !prompt.Contains("Coal cart") || !prompt.Contains("+30 a minute") || !prompt.Contains("[I] open"))
                {
                    return $"pick {Hands.CurrentPick.Kind}, prompt '{prompt}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "supply-street");
                _coal = Pack(ItemType.Coal);
                Tap(Key.E);
                return null;
            });
            Do("E took Coal from it", () => Pack(ItemType.Coal) == _coal + 1 ? null : $"Coal {_coal} -> {Pack(ItemType.Coal)}");
        }

        // --- 3. The cart's panel ----------------------------------------------------------------

        private void PlanCart()
        {
            Do("I opens its panel", () => { Tap(Key.I); return null; });
            Do("click Rate", () =>
            {
                if (!Panel.IsOpen || Panel.Title != "Coal cart" || !Panel.Holding.EndsWith("+30 a minute"))
                {
                    return $"open {Panel.IsOpen}, '{Panel.Title}', '{Panel.Holding}'";
                }
                _plate = Pack(ItemType.IronPlate);
                Click(Panel.Button(SupplyAction.Rate));
                return null;
            });
            Do("60 a minute, for its price", () =>
            {
                if (Spot.Cart.RatePerMinute != 60 || Pack(ItemType.IronPlate) != _plate - Line(CoalLadder.RateCosts[0], ItemType.IronPlate))
                {
                    return $"rate {Spot.Cart.RatePerMinute}, Plate {_plate} -> {Pack(ItemType.IronPlate)}: {Panel.Status}";
                }
                if (!Panel.Holding.EndsWith("+60 a minute") || Panel.Status != "Coal cart: 60 a minute.")
                {
                    return $"'{Panel.Holding}', '{Panel.Status}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "supply-cart");
                Tap(Key.Escape);
                Log("a Coal cart added at a spot, filling at its rate, taken from with E, upgraded through I");
                return null;
            });
        }

        // --- 4. Saved, removed, loaded -----------------------------------------------------------

        private void PlanSaveRemoveLoad()
        {
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("click Save / Load", () => { Click(Management.TabButtons[ManagementTab.SaveLoad]); return null; });
            Do("click Save on Slot 1", () => { Click(Management.Slots.Button("slot-1", SlotAction.Save)); return null; });
            Do("saved", () =>
            {
                if (Management.SlotBoard.Row("slot-1")?.Empty != false)
                {
                    return "Slot 1 is empty";
                }
                Tap(Key.Escape);
                return null;
            });
            Do("I at the cart", () => { Tap(Key.I); return null; });
            Do("click Remove", () =>
            {
                _scrap = Pack(ItemType.Scrap);
                _coal = Pack(ItemType.Coal);
                Click(Panel.Button(SupplyAction.Remove));
                return null;
            });
            Do("half back, and the rope again", () =>
            {
                if (!Spot.IsEmpty || Pack(ItemType.Scrap) != _scrap + Line(CoalLadder.AddPrice, ItemType.Scrap) / 2 || Pack(ItemType.Coal) <= _coal)
                {
                    return $"empty {Spot.IsEmpty}, Scrap {_scrap} -> {Pack(ItemType.Scrap)}, Coal {_coal} -> {Pack(ItemType.Coal)}: {Panel.Status}";
                }
                if (Panel.Title != "Cart spot")
                {
                    return "the panel says " + Panel.Title;
                }
                Tap(Key.Escape);
                return null;
            });
            Do("the rope is drawn", () => View.Shown == SupplyArt.SpotSprite ? null : "shows " + View.Shown);
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("click Save / Load", () => { Click(Management.TabButtons[ManagementTab.SaveLoad]); return null; });
            Do("click Load on Slot 1", () => { Click(Management.Slots.Button("slot-1", SlotAction.Load)); return null; });
            Do("click Load now", () => { Click(Management.Slots.Button("slot-1", SlotAction.Load)); return null; });
            Until("loaded", 2, () => Spot.Cart != null ? null : "no cart yet");
            Do("the cart came back as saved", () =>
            {
                if (Spot.Cart.ItemType != ItemType.Coal || Spot.Cart.RateRung != 1)
                {
                    return $"{Spot.Cart.ItemType} at rung {Spot.Cart.RateRung}";
                }
                if (Sandbox.Markers.Count(m => m.NodeId == Spot.NodeId) != 1)
                {
                    return "its marker is missing or doubled";
                }
                Tap(Key.Escape);
                return null;
            });
            Do("drawn as the cart again", () =>
            {
                if (View.Shown != "stall_coal")
                {
                    return "shows " + View.Shown;
                }
                Log("saved, removed for half its price, loaded back at its rung");
                return null;
            });
        }
    }
}
