using System.Collections.Generic;

namespace GolemFactory.Economy
{
    // Registry of named StorageBuffers, mirroring ConveyorSystem's segment dictionary
    // and World/ResourceNodeRegistry. Buffers are created on first deposit -- unlike
    // belt segments, nothing needs to pre-register a buffer's shape before golems start
    // depositing into it.
    public sealed class StorageBufferRegistry
    {
        private readonly Dictionary<string, StorageBuffer> _buffers = new Dictionary<string, StorageBuffer>();
        public IReadOnlyDictionary<string, StorageBuffer> Buffers => _buffers;

        /// <summary>
        /// Per-item-type capacity handed to buffers this registry auto-creates on first deposit.
        /// <see cref="StorageBuffer.Unlimited"/> by default, so a registry nobody has tuned
        /// behaves exactly as it did before capacity existed -- backpressure is opt-in per
        /// scene, and Main.unity opts out by simply never setting this.
        ///
        /// This is the scene's capacity POLICY, not a factory default for new buffers: setting
        /// it re-caps every buffer that has no explicit <see cref="SetCapacity"/> override, not
        /// just the ones created afterwards.
        ///
        /// It has to work that way. A buffer takes its capacity at creation, so if this only
        /// applied going forward, a buffer auto-created before the scene bootstrap ran -- by
        /// another component's Awake, or an earlier Start in an order nobody controls -- would
        /// be silently Unlimited while an identical buffer created a frame later was capped.
        /// Same scene, same kind of buffer, different backpressure, decided by execution order.
        /// That would surface during the asset-authoring pass as "why does *this* smelter never
        /// stall", and read as a tuning problem rather than an ordering one.
        ///
        /// Re-capping cannot lose goods: StorageBuffer.SetCapacityPerType keeps contents above
        /// the new cap rather than voiding them.
        /// </summary>
        public int DefaultCapacityPerType
        {
            get => _defaultCapacityPerType;
            set
            {
                _defaultCapacityPerType = value;
                foreach (KeyValuePair<string, StorageBuffer> pair in _buffers)
                {
                    if (!_capacityOverrides.ContainsKey(pair.Key))
                    {
                        pair.Value.SetCapacityPerType(value);
                    }
                }
            }
        }

        private int _defaultCapacityPerType = StorageBuffer.Unlimited;

        // Per-buffer exceptions to DefaultCapacityPerType. This is POLICY, not state: it
        // survives Clear() on purpose, because Clear() is the save/load path and every buffer
        // is rebuilt by GetOrCreate immediately afterwards. Without it, loading a save in a
        // scene with a finite default would silently re-cap the player's deliberately
        // uncapped stockpile and clamp away everything above the cap on the way in.
        private readonly Dictionary<string, int> _capacityOverrides = new Dictionary<string, int>();

        public StorageBuffer GetOrCreate(string bufferId)
        {
            if (!_buffers.TryGetValue(bufferId, out StorageBuffer buffer))
            {
                buffer = new StorageBuffer(bufferId, CapacityPolicyFor(bufferId));
                _buffers[bufferId] = buffer;
            }

            return buffer;
        }

        private int CapacityPolicyFor(string bufferId) =>
            _capacityOverrides.TryGetValue(bufferId, out int capacity) ? capacity : DefaultCapacityPerType;

        /// <summary>
        /// Caps (or uncaps, with <see cref="StorageBuffer.Unlimited"/>) one named buffer,
        /// creating it if it does not exist yet. Creating eagerly is the point: the player's
        /// stockpile has to be exempted from the scene's default cap at bootstrap time,
        /// *before* the first deposit auto-creates it with the default. The choice is
        /// remembered, so a buffer rebuilt after <see cref="Clear"/> keeps it.
        /// </summary>
        public StorageBuffer SetCapacity(string bufferId, int capacityPerType)
        {
            if (bufferId == null)
            {
                return null;
            }

            _capacityOverrides[bufferId] = capacityPerType;
            StorageBuffer buffer = GetOrCreate(bufferId);
            buffer.SetCapacityPerType(capacityPerType);
            return buffer;
        }

        public bool TryGetBuffer(string bufferId, out StorageBuffer buffer)
        {
            // Guard against a null id (an unset sourceId/destinationId), matching
            // ConveyorSystem.TryGetSegment -- Dictionary<string,_> throws on a null key.
            if (bufferId == null)
            {
                buffer = null;
                return false;
            }

            return _buffers.TryGetValue(bufferId, out buffer);
        }

        /// <summary>
        /// Returns the units actually accepted, mirroring <see cref="StorageBuffer.Deposit"/>,
        /// so a capped destination cannot silently swallow the overflow. Zero for an unknown
        /// (null) buffer id. Existing call sites that ignore the result still compile and are
        /// unaffected while their buffers are Unlimited.
        /// </summary>
        public int Deposit(string bufferId, string itemType, int amount = 1)
        {
            if (bufferId == null)
            {
                return 0;
            }

            return GetOrCreate(bufferId).Deposit(itemType, amount);
        }

        /// <summary>
        /// Room left for <paramref name="itemType"/> in a named buffer.
        /// <see cref="int.MaxValue"/> for a buffer that does not exist yet: it would be
        /// created Unlimited-or-default on first deposit, and reporting 0 would make a
        /// caller think a never-touched buffer was full.
        /// </summary>
        public int RoomFor(string bufferId, string itemType)
        {
            if (TryGetBuffer(bufferId, out StorageBuffer buffer))
            {
                return buffer.RoomFor(itemType);
            }

            if (string.IsNullOrEmpty(itemType))
            {
                return 0;
            }

            int wouldBeCapacity = bufferId == null ? DefaultCapacityPerType : CapacityPolicyFor(bufferId);
            return wouldBeCapacity == StorageBuffer.Unlimited ? int.MaxValue : wouldBeCapacity;
        }

        public bool TryWithdraw(string bufferId, string itemType, int amount = 1) =>
            TryGetBuffer(bufferId, out StorageBuffer buffer) && buffer.TryWithdraw(itemType, amount);

        // Withdraws Scrap then Brass, refunding the Scrap portion if the Brass withdrawal
        // fails partway through, so a failed combined-cost purchase never leaves the
        // buffer partially charged. Centralizes the pattern Buildings/AssemblyBayStructure
        // .TryUpgrade already implemented once inline -- building placement and golem
        // construction both need the same "pay in Scrap and Brass together" check, so a
        // third copy wasn't worth pasting.
        public bool TryWithdrawScrapAndBrass(string bufferId, int scrapCost, int brassCost)
        {
            // A zero cost must trivially succeed even if that item type was never
            // deposited into this buffer -- StorageBuffer.TryWithdraw looks the item type
            // up first and fails on a miss regardless of the requested amount, so a
            // literal `TryWithdraw(id, Scrap, 0)` against an untouched buffer would
            // otherwise incorrectly reject a genuinely free purchase (e.g. the default
            // zero-cost PlaceableBuilding).
            if (scrapCost > 0 && !TryWithdraw(bufferId, ItemType.Scrap, scrapCost))
            {
                return false;
            }

            if (brassCost > 0 && !TryWithdraw(bufferId, ItemType.Brass, brassCost))
            {
                if (scrapCost > 0)
                {
                    Deposit(bufferId, ItemType.Scrap, scrapCost);
                }
                return false;
            }

            return true;
        }

        // For Save/SaveLoadService.RestoreState: a loaded save should *replace* buffer
        // state, not merge into whatever's currently there (Deposit is additive, which
        // would double-count anything already in a buffer at load time).
        //
        // Deliberately does NOT clear the capacity overrides -- see _capacityOverrides. The
        // save file carries quantities, and the scene bootstrap carries the capacity policy;
        // wiping the policy here would make a load silently re-cap the player's stockpile.
        //
        // EMPTIES THE BUFFERS, IT DOES NOT REMOVE THEM. Buildings/PlaceableDepot publishes a
        // World.StorageBufferEndpoint holding a direct reference to the instance, so dropping
        // the objects here left every depot's endpoint pointing at a detached buffer after a
        // load, while the registry and the HUD read a freshly created one. Every golem Push
        // into that depot then landed somewhere nothing displayed and nothing could spend --
        // silent item loss with no stall and no error. Keeping the instance alive and clearing
        // its contents makes the endpoint and the registry the same object again by
        // construction, rather than by remembering to re-publish endpoints after every load.
        //
        // A buffer that exists in the scene but not in the save file is therefore left empty
        // rather than vanishing, which is the same thing from every reader's point of view:
        // ClearContents removes the keys, so it reports as empty, not as a row of zeroes.
        public void Clear()
        {
            foreach (KeyValuePair<string, StorageBuffer> pair in _buffers)
            {
                pair.Value.ClearContents();
            }
        }
    }
}
