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
    /// The street's supply carts (docs/supply-design.md), step 1, with real keys and clicks:
    /// <list type="number">
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
            Do("goods for a cart and a rung", () =>
            {
                Sandbox.TechTree.Ledger.RecordItem(ItemType.Coal);
                Sandbox.TechTree.Ledger.RecordItem(ItemType.Scrap);
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
                string[] offered = Panel.Rows.Select(r => r.row.Label).ToArray();
                if (!offered.SequenceEqual(new[] { "Scrap cart", "Coal cart" }))
                {
                    return "offers " + string.Join(", ", offered);
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
                if (Pack(ItemType.Scrap) != _scrap - 80)
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
                if (Spot.Cart.RatePerMinute != 60 || Pack(ItemType.IronPlate) != _plate - 40)
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
                if (!Spot.IsEmpty || Pack(ItemType.Scrap) != _scrap + 40 || Pack(ItemType.Coal) <= _coal)
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
