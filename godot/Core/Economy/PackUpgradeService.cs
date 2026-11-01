using System;
using System.Collections.Generic;
using GolemFactory.PunchCards;

namespace GolemFactory.Economy
{
    /// <summary>One rung of the pack's cap ladder (docs/pack-upgrades-design.md §3): the cap it sets, and its price.</summary>
    public sealed class PackUpgradeRung
    {
        public int capacityPerType;
        public List<RecipeIngredient> cost = new List<RecipeIngredient>();
    }

    /// <summary>
    /// Pack upgrades (docs/pack-upgrades-design.md): the pack's per-good cap, raised one rung at
    /// a time with goods from the wallet. Caps only grow in play (§0.2): an upgrade never sets
    /// the pack below the cap it has, so an uncapped pack stays uncapped. A load puts back the
    /// saved rung exactly, smaller or not, and clamps nothing: above-cap goods stay, spendable
    /// but not addable (§0.3).
    /// </summary>
    public sealed class PackUpgradeService
    {
        private StorageBufferRegistry _buffers;
        private string _packId;
        private string _postId;
        private int _baseCapacity = StorageBuffer.Unlimited;
        private IReadOnlyList<PackUpgradeRung> _rungs = new PackUpgradeRung[0];

        /// <summary>Rungs bought: 0 is the base cap.</summary>
        public int Tier { get; private set; }

        public string LastStatusMessage { get; private set; } = "";

        public IReadOnlyList<PackUpgradeRung> Rungs => _rungs;

        /// <param name="baseCapacity">The pack's cap with no rung bought (sandbox.json's packCapacityPerType).</param>
        public void Configure(StorageBufferRegistry buffers, string packId, string postId, int baseCapacity, IReadOnlyList<PackUpgradeRung> rungs)
        {
            _buffers = buffers;
            _packId = packId;
            _postId = postId;
            _baseCapacity = baseCapacity;
            _rungs = rungs ?? new PackUpgradeRung[0];
        }

        public bool IsMaxed => Tier >= _rungs.Count;

        /// <summary>The pack's live cap, or <see cref="StorageBuffer.Unlimited"/>.</summary>
        public int CapacityPerType => Pack?.CapacityPerType ?? StorageBuffer.Unlimited;

        /// <summary>The cap the next rung sets; the current cap at the top.</summary>
        public int NextCapacity => IsMaxed ? CapacityPerType : _rungs[Tier].capacityPerType;

        public IReadOnlyList<RecipeIngredient> NextCost() => IsMaxed ? new RecipeIngredient[0] : _rungs[Tier].cost;

        public bool CanAfford() => !IsMaxed && Wallet != null && UI.ConstructionCostPolicy.CanAfford(Wallet.Quantity, NextCost());

        /// <summary>The Pack row's Upgrade: charges the next rung through the wallet, then raises the cap.</summary>
        public bool TryUpgrade()
        {
            LastStatusMessage = "";
            if (Wallet == null)
            {
                LastStatusMessage = "No pack in this scene.";
                return false;
            }
            if (IsMaxed)
            {
                LastStatusMessage = "Your pack is as big as it gets.";
                return false;
            }
            IReadOnlyList<RecipeIngredient> cost = NextCost();
            if (!Wallet.TryCharge(cost))
            {
                LastStatusMessage = UI.ConstructionCostPolicy.FormatShortfall(Wallet, cost) + " to carry " + NextCapacity + " of each good.";
                return false;
            }
            Tier++;
            if (CapacityPerType != StorageBuffer.Unlimited)
            {
                _buffers.SetCapacity(_packId, Math.Max(CapacityPerType, CapAt(Tier)));
            }
            return true;
        }

        /// <summary>A load: the saved rung, and its cap. Never charges, never clamps the pack's contents.</summary>
        public void Restore(int tier)
        {
            Tier = Math.Max(0, Math.Min(tier, _rungs.Count));
            if (CapacityPerType != StorageBuffer.Unlimited)
            {
                _buffers.SetCapacity(_packId, CapAt(Tier));
            }
        }

        // The cap with this many rungs bought.
        private int CapAt(int tier) => tier == 0 ? _baseCapacity : _rungs[tier - 1].capacityPerType;

        private StorageBuffer Pack => _buffers == null || string.IsNullOrEmpty(_packId) ? null : _buffers.GetOrCreate(_packId);

        private PlayerWallet Wallet => Pack == null ? null : new PlayerWallet(_buffers, _packId, _postId);
    }
}
