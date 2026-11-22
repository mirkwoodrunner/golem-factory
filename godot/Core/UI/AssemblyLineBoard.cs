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

        /// <summary>A card the player can claim now, with its price and a Claim button.</summary>
        Slot,

        /// <summary>Nothing to claim: says how more unlock.</summary>
        EmptySlot,
        WaitingHeader,
        Waiting,
        More,
        Pack,

        /// <summary>The Docks' floor (docs/region-design.md §1). Appended.</summary>
        Docks,

        /// <summary>
        /// Where cards are claimed: the Ledger's plaques, with how many are ready (2026-11-21).
        /// Appended. The tab lists no cards of its own any more.
        /// </summary>
        Cards,
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
        /// "Extend" (the floor, the Docks), so a click dispatches on <see cref="Kind"/>.
        /// </summary>
        public string ButtonLabel;

        /// <summary>Whether the row reads as active (bright) rather than dim.</summary>
        public bool Bright;

        /// <summary>The card a Slot row claims, or null.</summary>
        public DraftableCardDefinition Card;

        /// <summary>
        /// Whether the row also offers its purchase for a city voucher (a second button, "Voucher"):
        /// the player holds one and the row could be bought (2026-11-19).
        /// </summary>
        public bool VoucherButton;
    }

    /// <summary>
    /// The Workshop tab (it was the Assembly Line tab): Unity's AssemblyLinePanel with its UGUI
    /// taken out (G8). The wallet line, the golem cap's row (how many stand, of how many), the
    /// floor-expansion row (the only place to buy more workshop), the Docks' and the pack's rows,
    /// and one line saying how many cards the Ledger has ready to claim.
    ///
    /// <para>
    /// CARDS ARE CLAIMED IN THE LEDGER (2026-11-21, the user's call). With the draft row gone
    /// (2026-11-19) the tab's card list was a second copy of the Ledger's plaques, and the Ledger
    /// is the one that shows what a card needs and leads to. The claim itself still lives here
    /// (<see cref="Claim"/>, <see cref="ClaimWithVoucher"/>): the Ledger's pane calls it.
    /// </para>
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

        /// <summary>The cards row when nothing can be claimed.</summary>
        public const string NothingToClaim = "Cards: nothing to claim right now. Make a new good to unlock more.";

        /// <summary>The cards row when some can: "Cards: 3 ready to claim in the Ledger".</summary>
        public static string ReadyToClaim(int count) =>
            "Cards: " + count + (count == 1 ? " is" : " are") + " ready to claim on the Ledger tab";

        private readonly AssemblyLineState _line;
        private readonly StorageBufferRegistry _buffers;
        private readonly string _wallet;
        private readonly string _userId;
        private readonly string _post;
        private AssemblyBayStructure _bay;
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

        public void ConfigureBays(AssemblyBayStructure bay) => _bay = bay;

        public void ConfigureFloorExpansion(FloorExpansionService floor) => _floor = floor;

        /// <summary>The Docks' floor rows, shown once the Docks are open.</summary>
        public void ConfigureDocks(DocksExpansionService docks) => _docks = docks;

        private DocksExpansionService _docks;

        public bool ExtendDocks()
        {
            if (_docks == null)
            {
                Status = "No Docks in this scene.";
                return false;
            }
            bool bought = _docks.TryPurchase();
            Status = bought ? "" : _docks.LastStatusMessage;
            return bought;
        }

        public void ConfigurePackUpgrade(PackUpgradeService pack) => _pack = pack;

        /// <summary>The city's vouchers, spent by each row's Voucher button; null offers none.</summary>
        public void ConfigureVouchers(Contracts.CityVouchers vouchers) => _vouchers = vouchers;

        private Contracts.CityVouchers _vouchers;

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
            int post = _post == null ? 0 : _buffers?.GetQuantity(_post, ItemType.Scrap) ?? 0;
            if (_post != null && (post > 0 || (_postShown?.Invoke() ?? false)))
            {
                line += "  ·  Post: " + post + " Scrap";
            }
            return _vouchers?.Any == true ? line + "  ·  " + Contracts.CityVouchers.Describe(_vouchers.Count) + " to spend" : line;
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
            // a golem, so a player who cannot build is looking for this line. Nothing to buy: the
            // cap is fixed (2026-11-19).
            if (_bay != null)
            {
                rows.Add(new AssemblyLineRow
                {
                    Kind = AssemblyLineRowKind.Bay,
                    Text = AssemblyBayRowPolicy.FormatOccupancy(_bay.OccupiedSlots, _bay.MaxGolemSlots)
                           + "  ·  the most the workshop holds",
                    Bright = !_bay.HasFreeSlot,
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

            if (_docks?.Bounds != null && _docks.Bounds.DocksOpen)
            {
                string label = "Docks " + (Docks.East - Docks.West + 1) + "x" + (_docks.Bounds.DocksNorth - Docks.Bottom + 1);
                rows.Add(_docks.CanExpand
                    ? PricedRow(AssemblyLineRowKind.Docks, label + "  ·  +" + _docks.RowsPerPurchase + " rows north",
                        _docks.NextCost(), _docks.CanAfford(), "Extend")
                    : new AssemblyLineRow { Kind = AssemblyLineRowKind.Docks, Text = label + "  ·  fully extended" });
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

            // The cards are the Ledger's (2026-11-21): one line saying how many wait there.
            int offered = _line.Offers.Count;
            rows.Add(new AssemblyLineRow
            {
                Kind = AssemblyLineRowKind.Cards,
                Text = offered == 0 ? NothingToClaim : ReadyToClaim(offered),
                Bright = offered > 0,
            });

            // A voucher buys any row's next step that goods could buy now (2026-11-19).
            if (_vouchers?.Any == true)
            {
                foreach (AssemblyLineRow row in rows)
                {
                    row.VoucherButton = row.ButtonLabel != null && !row.CostText.StartsWith("Requires")
                        && (row.Kind == AssemblyLineRowKind.Floor
                            || row.Kind == AssemblyLineRowKind.Docks || row.Kind == AssemblyLineRowKind.Pack);
                }
            }
            return rows;
        }

        /// <summary>A card was claimed: the latch.</summary>
        public event Action Claimed;

        /// <summary>The guide's wanted card, which the Ledger opens on; null wants none.</summary>
        public void ConfigureWanted(Func<DraftableCardDefinition, bool> wanted) => _wanted = wanted;

        private Func<DraftableCardDefinition, bool> _wanted;

        /// <summary>Whether the guide's current step asks for <paramref name="card"/>.</summary>
        public bool IsWanted(DraftableCardDefinition card) => card != null && _wanted?.Invoke(card) == true;

        /// <summary>Whether the player holds a city voucher to spend.</summary>
        public bool HasVoucher => _vouchers?.Any == true;

        /// <summary>A Ledger plaque's Claim: <paramref name="card"/> at its price.</summary>
        public bool Claim(DraftableCardDefinition card)
        {
            if (card == null || _line == null)
            {
                Status = "Nothing to claim.";
                return false;
            }
            string name = WorkbenchDiagnostics.Humanize(card.DisplayName);
            if (_line.IsClaimed(card))
            {
                Status = $"{name} is already yours.";
                return false;
            }
            IReadOnlyList<RecipeIngredient> price = AssemblyLineState.PriceOf(card);
            if (_line.TryClaim(card, _userId, _buffers, _wallet, _post))
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

        /// <summary>A row's Voucher button: its next step for one city voucher instead of goods.</summary>
        public bool PayWithVoucher(AssemblyLineRow row)
        {
            if (row == null || _vouchers?.Any != true)
            {
                Status = "No vouchers to spend: the City Office pays them for contracts and golem orders.";
                return false;
            }
            bool done;
            switch (row.Kind)
            {
                case AssemblyLineRowKind.Slot:
                    return ClaimWithVoucher(row.Card);
                case AssemblyLineRowKind.Floor:
                    done = _floor != null && _vouchers.TrySpend(() => _floor.TryPurchaseExpansion(free: true));
                    Status = done ? "Extended with a voucher." : _floor?.LastStatusMessage ?? "";
                    return done;
                case AssemblyLineRowKind.Docks:
                    done = _docks != null && _vouchers.TrySpend(() => _docks.TryPurchase(free: true));
                    Status = done ? "Extended with a voucher." : _docks?.LastStatusMessage ?? "";
                    return done;
                case AssemblyLineRowKind.Pack:
                    done = _pack != null && _vouchers.TrySpend(() => _pack.TryUpgrade(free: true));
                    Status = done ? "Upgraded with a voucher." : _pack?.LastStatusMessage ?? "";
                    return done;
                default:
                    return false;
            }
        }

        /// <summary>A Ledger plaque's Voucher: <paramref name="card"/> for one city voucher instead of goods.</summary>
        public bool ClaimWithVoucher(DraftableCardDefinition card)
        {
            if (_vouchers?.Any != true)
            {
                Status = "No vouchers to spend: the City Office pays them for contracts and golem orders.";
                return false;
            }
            bool done = card != null && _line != null && _vouchers.TrySpend(() => _line.TryClaimFree(card, _userId));
            Status = done ? $"Claimed {WorkbenchDiagnostics.Humanize(card.DisplayName)} with a voucher." : "That card can't be claimed now.";
            if (done)
            {
                Claimed?.Invoke();
            }
            return done;
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

        /// <summary>Whether the wallet can pay for <paramref name="card"/> now.</summary>
        public bool CanAffordCard(DraftableCardDefinition card) => card != null && CanAfford(AssemblyLineState.PriceOf(card));

        // What the charge (PlayerWallet.TryCharge) would take: Creative's waiver included.
        private bool CanAfford(IReadOnlyList<RecipeIngredient> price) =>
            _buffers == null ? ConstructionCostPolicy.CanAfford(_ => 0, price) : new PlayerWallet(_buffers, _wallet, _post).CanAfford(price);
    }
}
