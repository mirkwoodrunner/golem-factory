using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Progression;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.UI
{
    public enum AssemblyLineRowKind
    {
        Message,
        Wallet,
        Bay,
        Floor,
        Slot,
        EmptySlot,
        WaitingHeader,
        Waiting,
        More,
        Pack,
    }

    /// <summary>One row of the Assembly Line tab, decided; the screen only lays it out.</summary>
    public sealed class AssemblyLineRow
    {
        public AssemblyLineRowKind Kind;
        public string Text = "";

        /// <summary>The right-hand column: a price, or "needs ...".</summary>
        public string CostText = "";

        public bool Affordable;

        /// <summary>
        /// "Claim", "Upgrade" or "Extend" for rows with a button; null otherwise. Two rows say
        /// "Upgrade" (bays, pack), so a click dispatches on <see cref="Kind"/>.
        /// </summary>
        public string ButtonLabel;

        /// <summary>Whether the row reads as active (bright) rather than dim.</summary>
        public bool Bright;

        public int SlotIndex = -1;
    }

    /// <summary>
    /// The Assembly Line tab: Unity's AssemblyLinePanel with its UGUI taken out (G8). The wallet
    /// line, the assembly-bay row (occupancy and its upgrade), the floor-expansion row (the only
    /// place to buy more workshop), one row per draft slot with its WHOLE price and a Claim, and
    /// the cards still waiting on prerequisites with what each needs.
    ///
    /// <para>
    /// THE WALLET IS THE STOCKPILE. Unity's panel charged claims from <c>ScrapBuffer</c> -- a
    /// setting from M9, before the economy moved every good into <c>FactoryStockpile</c>, never
    /// revisited, so in Unity's Sandbox a Scrap-priced card read its price against a buffer the
    /// player has no way to fill. The bay and floor rows already paid from the stockpile.
    /// </para>
    /// </summary>
    public sealed class AssemblyLineBoard
    {
        public const string UnavailableMessage = "Assembly line unavailable: no line state wired.";
        public const int MaxWaitingRowsShown = 4;

        private readonly AssemblyLineState _line;
        private readonly StorageBufferRegistry _buffers;
        private readonly string _wallet;
        private readonly string _userId;
        private readonly string _post;
        private AssemblyBayStructure _bay;
        private string _stockpile;
        private FloorExpansionService _floor;
        private PackUpgradeService _pack;
        private Func<string, bool> _seen;
        private Func<bool> _postShown;

        /// <param name="postBufferId">The Pneumatic Post's store, spent after the pack (crates-design §10); null for none.</param>
        public AssemblyLineBoard(AssemblyLineState line, StorageBufferRegistry buffers, string walletBufferId,
            string userId = PlaceableBuilding.LocalPlayerOwnerId, string postBufferId = null)
        {
            _line = line;
            _buffers = buffers;
            _wallet = walletBufferId;
            _userId = userId;
            _post = postBufferId;
        }

        public void ConfigureBays(AssemblyBayStructure bay, string stockpileBufferId)
        {
            _bay = bay;
            _stockpile = stockpileBufferId;
        }

        public void ConfigureFloorExpansion(FloorExpansionService floor) => _floor = floor;

        public void ConfigurePackUpgrade(PackUpgradeService pack) => _pack = pack;

        /// <summary>
        /// Whether the factory has made a good (the Ledger). An Extend or Pack price naming one it
        /// hasn't reads "Requires: Brass", greyed (docs/pack-upgrades-design.md §0.11). Unset, all are seen.
        /// </summary>
        public void ConfigureSeen(Func<string, bool> seen) => _seen = seen;

        /// <summary>
        /// Whether a Pneumatic Post has been built (the Ledger), so the header names its Scrap
        /// beside the pack's: a claim spends the pack, then the Post. A Post store still holding
        /// Scrap is named whatever this says, since the wallet still spends it.
        /// </summary>
        public void ConfigurePostShown(Func<bool> postShown) => _postShown = postShown;

        /// <summary>
        /// The header: "Pack: 100 Scrap", and "  ·  Post: 40 Scrap" once a Post exists. Never a
        /// buffer id (it used to read "Wallet: 100 Scrap (FactoryStockpile)").
        /// </summary>
        public string WalletLine()
        {
            int pack = _buffers?.GetQuantity(_wallet, ItemType.Scrap) ?? 0;
            string line = "Pack: " + pack + " Scrap";
            if (_post == null)
            {
                return line;
            }
            int post = _buffers?.GetQuantity(_post, ItemType.Scrap) ?? 0;
            return post > 0 || (_postShown?.Invoke() ?? false) ? line + "  ·  Post: " + post + " Scrap" : line;
        }

        /// <summary>The status line under the rows: what the last action did, or why it could not.</summary>
        public string Status { get; private set; } = "";

        public List<AssemblyLineRow> Rows()
        {
            var rows = new List<AssemblyLineRow>();
            if (_line == null)
            {
                rows.Add(new AssemblyLineRow { Kind = AssemblyLineRowKind.Message, Text = UnavailableMessage });
                return rows;
            }

            rows.Add(new AssemblyLineRow { Kind = AssemblyLineRowKind.Wallet, Text = WalletLine(), Bright = true });

            // FIRST, above the cards: the cap decides whether a claimed card can ever be put on
            // a golem, so a player who cannot build is looking for this line.
            if (_bay != null)
            {
                IReadOnlyList<RecipeIngredient> cost = _bay.UpgradeCost;
                rows.Add(new AssemblyLineRow
                {
                    Kind = AssemblyLineRowKind.Bay,
                    Text = AssemblyBayRowPolicy.FormatOccupancy(_bay.OccupiedSlots, _bay.MaxGolemSlots)
                           + "  ·  " + AssemblyBayRowPolicy.FormatUpgradeEffect(AssemblyBayStructure.SlotsPerUpgrade),
                    Bright = !_bay.HasFreeSlot,
                    CostText = AssemblyBayRowPolicy.FormatCost(cost),
                    Affordable = AssemblyBayRowPolicy.CanAfford(StockpileOf, cost),
                    ButtonLabel = "Upgrade",
                });
            }

            if (_floor?.Bounds != null)
            {
                FloorBounds bounds = _floor.Bounds;
                string label = "Workshop " + (bounds.HalfExtent * 2 + 1) + "x" + (bounds.NorthExtent + bounds.HalfExtent + 1);
                if (!bounds.CanExpand)
                {
                    // Land is finite (§11): when it runs out the row says so, rather than
                    // offering a button that always refuses.
                    rows.Add(new AssemblyLineRow { Kind = AssemblyLineRowKind.Floor, Text = label + "  ·  fully extended" });
                }
                else
                {
                    rows.Add(PricedRow(AssemblyLineRowKind.Floor, label + "  ·  +" + _floor.RowsPerPurchase + " rows north",
                        _floor.NextCost(), _floor.CanAfford(), "Extend"));
                }
            }

            if (_pack != null && _pack.CapacityPerType > 0)
            {
                string label = "Pack  ·  carries " + _pack.CapacityPerType + " of each good";
                if (_pack.IsMaxed)
                {
                    rows.Add(new AssemblyLineRow { Kind = AssemblyLineRowKind.Pack, Text = label });
                }
                else
                {
                    rows.Add(PricedRow(AssemblyLineRowKind.Pack, label + "  ·  +" + (_pack.NextCapacity - _pack.CapacityPerType),
                        _pack.NextCost(), _pack.CanAfford(), "Upgrade"));
                }
            }

            for (int i = 0; i < _line.SlotCount; i++)
            {
                DraftableCardDefinition card = _line.GetCard(i);
                if (card == null)
                {
                    rows.Add(new AssemblyLineRow { Kind = AssemblyLineRowKind.EmptySlot, Text = "Slot " + (i + 1) + " -- empty", SlotIndex = i });
                    continue;
                }
                // THE WHOLE PRICE, not its Scrap component: R4 Iron Smelting costs Scrap AND
                // Coke, and once advertised only the Scrap, lit Claim off a Scrap-only check,
                // and refused the sale.
                IReadOnlyList<RecipeIngredient> price = _line.GetCurrentCostBundle(i);
                rows.Add(new AssemblyLineRow
                {
                    Kind = AssemblyLineRowKind.Slot,
                    Text = WorkbenchDiagnostics.Humanize(card.DisplayName),
                    Bright = true,
                    CostText = RecipeLedger.FormatBundle(price),
                    Affordable = CanAfford(price),
                    ButtonLabel = "Claim",
                    SlotIndex = i,
                });
            }

            IReadOnlyList<DraftableCardDefinition> waiting = _line.WaitingCards;
            if (waiting != null && waiting.Count > 0)
            {
                rows.Add(new AssemblyLineRow { Kind = AssemblyLineRowKind.WaitingHeader, Text = "Waiting on prerequisites: " + waiting.Count });
                int shown = 0;
                for (int i = 0; i < waiting.Count && shown < MaxWaitingRowsShown; i++)
                {
                    if (waiting[i] == null)
                    {
                        continue;
                    }
                    rows.Add(new AssemblyLineRow
                    {
                        Kind = AssemblyLineRowKind.Waiting,
                        Text = WorkbenchDiagnostics.Humanize(waiting[i].DisplayName),
                        CostText = "needs " + WorkbenchDiagnostics.Humanize(_line.DescribeMissingPrerequisites(waiting[i])),
                    });
                    shown++;
                }
                if (waiting.Count > shown)
                {
                    rows.Add(new AssemblyLineRow { Kind = AssemblyLineRowKind.More, Text = "... and " + (waiting.Count - shown) + " more further up the tree" });
                }
            }
            return rows;
        }

        /// <summary>A card was claimed: the latch.</summary>
        public event Action Claimed;

        /// <summary>The Claim button on slot <paramref name="slotIndex"/>.</summary>
        public bool Claim(int slotIndex)
        {
            DraftableCardDefinition card = _line?.GetCard(slotIndex);
            if (card == null)
            {
                Status = "That slot is empty.";
                return false;
            }
            string name = WorkbenchDiagnostics.Humanize(card.DisplayName);
            IReadOnlyList<RecipeIngredient> price = _line.GetCurrentCostBundle(slotIndex);
            if (_line.TryClaimSlot(slotIndex, _userId, _buffers, _wallet, _post))
            {
                Status = $"Claimed {name}.";
                Claimed?.Invoke();
                return true;
            }
            // Two ways to be refused, needing different answers: go and make something, or go
            // and afford something.
            string missing = _line.DescribeMissingPrerequisites(card);
            Status = string.IsNullOrEmpty(missing)
                ? $"Cannot afford {name}: it costs {RecipeLedger.FormatBundle(price)}."
                : $"{name} is still locked. Needs: {WorkbenchDiagnostics.Humanize(missing)}.";
            return false;
        }

        public bool UpgradeBays()
        {
            if (_bay == null)
            {
                Status = "No assembly bays in this scene.";
                return false;
            }
            IReadOnlyList<RecipeIngredient> cost = _bay.UpgradeCost;
            bool upgraded = _buffers != null && _bay.TryUpgrade(_buffers, _stockpile, _post);
            Status = AssemblyBayRowPolicy.DescribeResult(upgraded, StockpileOf, cost, AssemblyBayStructure.SlotsPerUpgrade,
                new PlayerWallet(_buffers, _stockpile, _post).PackCapacity);
            return upgraded;
        }

        public bool ExtendFloor()
        {
            if (_floor == null)
            {
                Status = "No expandable floor in this scene.";
                return false;
            }
            bool expanded = _floor.TryPurchaseExpansion();
            Status = expanded ? "" : _floor.LastStatusMessage;
            return expanded;
        }

        public bool UpgradePack()
        {
            if (_pack == null)
            {
                Status = "No pack upgrades in this scene.";
                return false;
            }
            bool upgraded = _pack.TryUpgrade();
            Status = upgraded ? "" : _pack.LastStatusMessage;
            return upgraded;
        }

        /// <summary>
        /// The Floor and Pack rows: a price, or "Requires: Brass", greyed, when it names goods the
        /// factory has never made.
        /// </summary>
        private AssemblyLineRow PricedRow(AssemblyLineRowKind kind, string text, IReadOnlyList<RecipeIngredient> price, bool canAfford, string button)
        {
            List<string> unseen = _seen == null || price == null
                ? new List<string>()
                : price.Where(p => p.quantity > 0 && !_seen(p.itemType)).Select(p => ConstructionCostPolicy.DisplayName(p.itemType)).ToList();
            bool locked = unseen.Count > 0;
            return new AssemblyLineRow
            {
                Kind = kind,
                Text = text,
                Bright = true,
                CostText = locked ? "Requires: " + string.Join(", ", unseen) : ConstructionCostPolicy.FormatCost(price),
                Affordable = !locked && canAfford,
                ButtonLabel = button,
            };
        }

        // The pack plus the Post: what the bay's charge (PlayerWallet.TryCharge) can spend.
        private int StockpileOf(string itemType) => _buffers == null ? 0 : new PlayerWallet(_buffers, _stockpile, _post).Quantity(itemType);

        private bool CanAfford(IReadOnlyList<RecipeIngredient> price) =>
            ConstructionCostPolicy.CanAfford(item => _buffers == null ? 0 : new PlayerWallet(_buffers, _wallet, _post).Quantity(item), price);
    }
}
