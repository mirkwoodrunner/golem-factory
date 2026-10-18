using System.Collections.Generic;
using GolemFactory.PunchCards;

namespace GolemFactory.Economy
{
    /// <summary>
    /// What the player pays with (docs/crates-design.md §4.4): the pack, then the Pneumatic Post
    /// store once it exists (§10). Every cost in the game is charged through
    /// <see cref="TryCharge"/>, so the order the two are spent in cannot drift between call sites,
    /// and nothing ever charges a crate (§0.5).
    ///
    /// <para>
    /// Stateless over the registry: two wallets over the same buffers are the same wallet, which
    /// is why a part wired with a <c>(registry, bufferId)</c> pair can build its own. Without a
    /// Post (<c>postId</c> null) it is the pack alone, which is all of Phase 1's steps 1 to 5.
    /// </para>
    /// </summary>
    public sealed class PlayerWallet
    {
        private readonly StorageBufferRegistry _buffers;
        private readonly string _postId;

        public PlayerWallet(StorageBufferRegistry buffers, string packId, string postId = null)
        {
            _buffers = buffers;
            PackId = packId;
            _postId = string.IsNullOrEmpty(postId) ? null : postId;
        }

        public string PackId { get; }

        /// <summary>The pack's buffer: what harvesting and collecting fill, the player's hands.</summary>
        public StorageBuffer Pack => _buffers?.GetOrCreate(PackId);

        /// <summary>The pack plus the Post. Every affordability check and shortfall line reads this.</summary>
        public int Quantity(string itemType) =>
            _buffers == null ? 0 : _buffers.GetQuantity(PackId, itemType) + (_postId == null ? 0 : _buffers.GetQuantity(_postId, itemType));

        /// <summary>Room for a refund: the pack's, then the Post's. Saturates rather than overflowing.</summary>
        public int RoomFor(string itemType)
        {
            if (_buffers == null)
            {
                return 0;
            }
            long room = (long)_buffers.RoomFor(PackId, itemType) + (_postId == null ? 0 : _buffers.RoomFor(_postId, itemType));
            return room > int.MaxValue ? int.MaxValue : (int)room;
        }

        /// <summary>
        /// Charges a price atomically: every good is affordable or nothing is taken. Each good
        /// comes from the pack first, the rest from the Post. A null or empty bundle, and a
        /// non-positive quantity, are free (the rule <see cref="StorageBufferRegistry.TryWithdrawBundle"/>
        /// keeps for a zero cost).
        /// </summary>
        public bool TryCharge(IReadOnlyList<RecipeIngredient> bundle)
        {
            if (bundle == null || bundle.Count == 0)
            {
                return true;
            }
            if (_buffers == null)
            {
                return false;
            }

            // Checked whole before a unit moves, with a good named twice counted twice, so a
            // refusal takes nothing and needs no unwind.
            var need = new Dictionary<string, int>();
            foreach (RecipeIngredient entry in bundle)
            {
                if (entry.quantity > 0 && !string.IsNullOrEmpty(entry.itemType))
                {
                    need.TryGetValue(entry.itemType, out int sum);
                    need[entry.itemType] = sum + entry.quantity;
                }
            }
            foreach (KeyValuePair<string, int> pair in need)
            {
                if (Quantity(pair.Key) < pair.Value)
                {
                    return false;
                }
            }

            foreach (KeyValuePair<string, int> pair in need)
            {
                int fromPack = System.Math.Min(pair.Value, _buffers.GetQuantity(PackId, pair.Key));
                if (fromPack > 0)
                {
                    _buffers.TryWithdraw(PackId, pair.Key, fromPack);
                }
                if (pair.Value > fromPack)
                {
                    _buffers.TryWithdraw(_postId, pair.Key, pair.Value - fromPack);
                }
            }
            return true;
        }

        /// <summary>
        /// The first good of <paramref name="bundle"/> the wallet has no room for, or null when
        /// all of it fits. What a refused refund names: "pack full of Scrap".
        /// </summary>
        public string FirstMisfit(IReadOnlyList<RecipeIngredient> bundle)
        {
            if (bundle == null)
            {
                return null;
            }
            // Summed per good, as TryCharge sums a price: two lines of Scrap that each fit but
            // together don't must be refused, or Refund deposits one and clamps the other.
            foreach (KeyValuePair<string, int> pair in Totals(bundle))
            {
                if (RoomFor(pair.Key) < pair.Value)
                {
                    return pair.Key;
                }
            }
            return null;
        }

        // Each good's total across the bundle, in first-named order; non-positive lines dropped.
        private static List<KeyValuePair<string, int>> Totals(IReadOnlyList<RecipeIngredient> bundle)
        {
            var totals = new List<KeyValuePair<string, int>>();
            foreach (RecipeIngredient entry in bundle)
            {
                if (entry.quantity <= 0 || string.IsNullOrEmpty(entry.itemType))
                {
                    continue;
                }
                int at = totals.FindIndex(t => t.Key == entry.itemType);
                if (at < 0)
                {
                    totals.Add(new KeyValuePair<string, int>(entry.itemType, entry.quantity));
                }
                else
                {
                    totals[at] = new KeyValuePair<string, int>(entry.itemType, totals[at].Value + entry.quantity);
                }
            }
            return totals;
        }

        /// <summary>
        /// Hands goods back -- a demolition, a dismantled golem, a cancelled purchase -- into the
        /// pack, then the Post. All or nothing: false, with nothing deposited, when any good won't
        /// fit, so a caller that skipped <see cref="FirstMisfit"/> still loses nothing.
        /// </summary>
        public bool Refund(IReadOnlyList<RecipeIngredient> bundle)
        {
            if (bundle == null || bundle.Count == 0)
            {
                return true;
            }
            if (_buffers == null || FirstMisfit(bundle) != null)
            {
                return false;
            }

            foreach (KeyValuePair<string, int> pair in Totals(bundle))
            {
                int intoPack = _buffers.Deposit(PackId, pair.Key, pair.Value);
                if (intoPack < pair.Value && _postId != null)
                {
                    _buffers.Deposit(_postId, pair.Key, pair.Value - intoPack);
                }
            }
            return true;
        }
    }
}
