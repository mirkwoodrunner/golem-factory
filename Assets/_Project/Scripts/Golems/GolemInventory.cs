using System.Collections.Generic;

namespace GolemFactory.Golems
{
    // The golem's internal stock -- docs/progression-design.md §2, "A golem is a machine with
    // an internal inventory."
    //
    // Deliberately plain C# with no UnityEngine types, matching GridCoordinateConverter /
    // YSortUtility / PlayerMovement.ComputeDisplacement: the machine model is the thing every
    // ratio in the progression design is derived from, so it has to be unit-testable without a
    // scene, a tick clock or a GameObject.
    //
    // Two stocks, not one dictionary. §2 is explicit that keeping input and output separate is
    // what lets Push empty *everything* in a single step (mixed types, byproducts included) and
    // what makes the byproduct problem disappear -- an iron smelter emitting Plate *and* Slag
    // still costs one Push, not two. The cost of that separation is the pure-logistics rule,
    // which lives on GolemProgram.HasAssembleStep / GolemEntity.PushStock rather than here.
    public sealed class GolemInventory
    {
        /// <summary>
        /// Cap is per *item type*, not per golem (progression-design §2: "capped at 12 units
        /// per type"). Per-golem would deadlock the same way a whole-buffer cap does: one type
        /// backing up would lock out every other type, and a rigid golem can never drain the
        /// wrong one out.
        /// </summary>
        public const int CapacityPerType = 12;

        public Stock Input { get; } = new Stock();
        public Stock Output { get; } = new Stock();

        public int GetInput(string itemType) => Input.Get(itemType);
        public int GetOutput(string itemType) => Output.Get(itemType);

        public int InputRoomFor(string itemType) => Input.RoomFor(itemType);
        public int OutputRoomFor(string itemType) => Output.RoomFor(itemType);

        /// <summary>Adds up to <paramref name="quantity"/>; returns units actually added.</summary>
        public int AddInput(string itemType, int quantity) => Input.Add(itemType, quantity);

        /// <summary>Adds up to <paramref name="quantity"/>; returns units actually added.</summary>
        public int AddOutput(string itemType, int quantity) => Output.Add(itemType, quantity);

        /// <summary>All-or-nothing: a partial consume would leave a half-built recipe behind.</summary>
        public bool TryConsumeInput(string itemType, int quantity) => Input.TryConsume(itemType, quantity);

        /// <summary>All-or-nothing, mirroring <see cref="TryConsumeInput"/>.</summary>
        public bool TryConsumeOutput(string itemType, int quantity) => Output.TryConsume(itemType, quantity);

        public void Clear()
        {
            Input.Clear();
            Output.Clear();
        }

        /// <summary>
        /// One typed pile of goods, per-item-type capped.
        /// </summary>
        public sealed class Stock
        {
            private readonly Dictionary<string, int> _quantities = new Dictionary<string, int>();

            // Insertion order, kept as a parallel list purely so enumeration is DETERMINISTIC.
            // Dictionary iteration order is not contractual in .NET -- it happens to be stable
            // for an insert-only dictionary today, but a remove-then-reinsert reuses the freed
            // slot, so two golems that reached the same contents by different routes could
            // enumerate them in different orders. Push drains this in enumeration order, so a
            // non-deterministic order would make two identically-programmed golems behave
            // differently against a destination that fills partway. Hence an explicit list.
            private readonly List<string> _order = new List<string>();

            public IReadOnlyDictionary<string, int> Quantities => _quantities;

            /// <summary>Item types held, in the deterministic order Push drains them.</summary>
            public IReadOnlyList<string> TypesInOrder => _order;

            /// <summary>Total units across every type -- what Push's duration is derived from.</summary>
            public int TotalUnits
            {
                get
                {
                    int total = 0;
                    for (int i = 0; i < _order.Count; i++)
                    {
                        total += Get(_order[i]);
                    }
                    return total;
                }
            }

            public int Get(string itemType)
            {
                if (string.IsNullOrEmpty(itemType))
                {
                    return 0;
                }

                return _quantities.TryGetValue(itemType, out int quantity) ? quantity : 0;
            }

            public int RoomFor(string itemType)
            {
                if (string.IsNullOrEmpty(itemType))
                {
                    return 0;
                }

                int room = CapacityPerType - Get(itemType);
                return room > 0 ? room : 0;
            }

            /// <summary>
            /// Adds up to <paramref name="quantity"/>, clamped by the per-type cap. Returns the
            /// units actually added so the caller can tell whether anything was left over --
            /// a caller that assumed "all of it went in" would be quietly destroying goods.
            /// </summary>
            public int Add(string itemType, int quantity)
            {
                if (string.IsNullOrEmpty(itemType) || quantity <= 0)
                {
                    return 0;
                }

                int room = RoomFor(itemType);
                int added = quantity < room ? quantity : room;
                if (added <= 0)
                {
                    return 0;
                }

                if (!_quantities.ContainsKey(itemType))
                {
                    _order.Add(itemType);
                }

                _quantities[itemType] = Get(itemType) + added;
                return added;
            }

            /// <summary>
            /// All-or-nothing withdrawal. False (and no mutation) if the stock is short.
            /// </summary>
            public bool TryConsume(string itemType, int quantity)
            {
                if (string.IsNullOrEmpty(itemType) || quantity <= 0)
                {
                    return false;
                }

                int held = Get(itemType);
                if (held < quantity)
                {
                    return false;
                }

                int remaining = held - quantity;
                if (remaining == 0)
                {
                    _quantities.Remove(itemType);
                    _order.Remove(itemType);
                }
                else
                {
                    _quantities[itemType] = remaining;
                }

                return true;
            }

            public void Clear()
            {
                _quantities.Clear();
                _order.Clear();
            }
        }
    }
}
