using System;
using System.Runtime.CompilerServices;
using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>
    /// Entities by grid cell: a spatial hash over flat arrays, each bucket an intrusive linked
    /// list threaded through <c>_next</c>/<c>_prev</c>. Any number of entities may share a cell.
    ///
    /// <para>
    /// <b>Nothing here allocates once the arrays are sized.</b> An add, a move, a removal by
    /// handle and a lookup are O(1) and touch no heap; a query writes into a caller's
    /// <see cref="Span{T}"/> (stack-allocated for handles). The arrays double when the grid
    /// outgrows them, which is the only allocation, so a caller that must never allocate sizes
    /// the grid for its peak in the constructor.
    /// </para>
    ///
    /// <para>
    /// <b>Order.</b> The hash has no per-process seed, so the same operations give the same
    /// layout on every run and every machine. A query still promises no order among its
    /// results: entities sharing a bucket come back in an order that follows insertion history.
    /// A caller that must not depend on that history picks by its own rule
    /// (<see cref="CellOrder"/>, an id).
    /// </para>
    /// </summary>
    public sealed class SpatialHashGrid<T>
    {
        private const int None = -1;
        private const int Free = -2;

        private int[] _head;            // bucket -> first slot, or None
        private int[] _next;            // slot -> next slot in its bucket; a free slot -> the next free one
        private int[] _prev;            // slot -> previous slot in its bucket; None at the head; Free when unused
        private T[] _entities;
        private Vector2Int[] _positions;

        private int _mask;
        private int _count;
        private int _used;              // slots ever handed out; [_used, capacity) are untouched
        private int _freeHead = None;

        /// <param name="capacity">Entities held before the arrays first grow.</param>
        public SpatialHashGrid(int capacity = 64)
        {
            capacity = RoundUpToPowerOfTwo(Math.Max(4, capacity));
            _head = new int[capacity];
            _next = new int[capacity];
            _prev = new int[capacity];
            _entities = new T[capacity];
            _positions = new Vector2Int[capacity];
            _mask = capacity - 1;
            Array.Fill(_head, None);
        }

        public int Count => _count;

        /// <summary>Entities the arrays hold before they grow.</summary>
        public int Capacity => _entities.Length;

        /// <summary>The entity a handle names. A handle is valid from its add until its removal.</summary>
        public T this[int handle]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _entities[handle];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _entities[handle] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector2Int CellOf(int handle) => _positions[handle];

        // --- Writes --------------------------------------------------------------------------

        /// <summary>Puts <paramref name="entity"/> on <paramref name="cell"/> and returns its handle.</summary>
        public int Add(Vector2Int cell, T entity)
        {
            int slot;
            if (_freeHead != None)
            {
                slot = _freeHead;
                _freeHead = _next[slot];
            }
            else
            {
                if (_used == _entities.Length)
                {
                    Grow();
                }
                slot = _used++;
            }

            _entities[slot] = entity;
            _positions[slot] = cell;
            Link(slot, Bucket(cell.x, cell.y));
            _count++;
            return slot;
        }

        /// <summary>Takes the entity <paramref name="handle"/> names off the grid.</summary>
        public void Remove(int handle)
        {
            Unlink(handle);
            _entities[handle] = default;
            _prev[handle] = Free;
            _next[handle] = _freeHead;
            _freeHead = handle;
            _count--;
        }

        /// <summary>Moves an entity to <paramref name="cell"/>; its handle stays the same.</summary>
        public void Move(int handle, Vector2Int cell)
        {
            Vector2Int from = _positions[handle];
            if (from.x == cell.x && from.y == cell.y)
            {
                return;
            }
            Unlink(handle);
            _positions[handle] = cell;
            Link(handle, Bucket(cell.x, cell.y));
        }

        /// <summary>Removes one entity standing on <paramref name="cell"/>, if any does.</summary>
        public bool RemoveAt(Vector2Int cell)
        {
            if (!TryGetHandle(cell, out int handle))
            {
                return false;
            }
            Remove(handle);
            return true;
        }

        /// <summary>Empties the grid, keeping its arrays.</summary>
        public void Clear()
        {
            Array.Fill(_head, None);
            Array.Clear(_entities, 0, _used);
            _count = 0;
            _used = 0;
            _freeHead = None;
        }

        // --- Point lookups -------------------------------------------------------------------

        /// <summary>The handle of one entity on <paramref name="cell"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetHandle(Vector2Int cell, out int handle)
        {
            int[] next = _next;
            Vector2Int[] positions = _positions;
            for (int slot = _head[Bucket(cell.x, cell.y)]; slot != None; slot = next[slot])
            {
                Vector2Int at = positions[slot];
                if (at.x == cell.x && at.y == cell.y)
                {
                    handle = slot;
                    return true;
                }
            }
            handle = None;
            return false;
        }

        /// <summary>One entity on <paramref name="cell"/>: the only one, where a caller keeps one to a cell.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(Vector2Int cell, out T entity)
        {
            if (TryGetHandle(cell, out int handle))
            {
                entity = _entities[handle];
                return true;
            }
            entity = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Vector2Int cell) => TryGetHandle(cell, out _);

        // --- Queries -------------------------------------------------------------------------
        // Each returns how many entities matched and writes as many handles as the span holds,
        // so a result larger than the span says the buffer was too small.

        /// <summary>The entities on <paramref name="cell"/>.</summary>
        public int QueryCell(Vector2Int cell, Span<int> handles)
        {
            int found = 0;
            for (int slot = _head[Bucket(cell.x, cell.y)]; slot != None; slot = _next[slot])
            {
                Vector2Int at = _positions[slot];
                if (at.x == cell.x && at.y == cell.y)
                {
                    if (found < handles.Length)
                    {
                        handles[found] = slot;
                    }
                    found++;
                }
            }
            return found;
        }

        /// <summary>
        /// The entities within <paramref name="radius"/> cells of <paramref name="centre"/> on
        /// both axes (Chebyshev distance, the grid's own: a radius of 1 is the eight neighbours
        /// and the centre).
        /// </summary>
        public int QueryRadius(Vector2Int centre, int radius, Span<int> handles)
        {
            if (radius < 0)
            {
                return 0;
            }
            return QueryRect(new Vector2Int(centre.x - radius, centre.y - radius),
                new Vector2Int(centre.x + radius, centre.y + radius), handles);
        }

        /// <summary>The entities on the cells from <paramref name="min"/> to <paramref name="max"/>, both included.</summary>
        public int QueryRect(Vector2Int min, Vector2Int max, Span<int> handles)
        {
            if (_count == 0 || max.x < min.x || max.y < min.y)
            {
                return 0;
            }

            // Whichever walk is shorter: a bucket per cell of the rectangle, or every slot.
            long cells = ((long)max.x - min.x + 1) * ((long)max.y - min.y + 1);
            int found = 0;
            if (cells > _used)
            {
                for (int slot = 0; slot < _used; slot++)
                {
                    if (_prev[slot] == Free)
                    {
                        continue;
                    }
                    Vector2Int at = _positions[slot];
                    if (at.x >= min.x && at.x <= max.x && at.y >= min.y && at.y <= max.y)
                    {
                        if (found < handles.Length)
                        {
                            handles[found] = slot;
                        }
                        found++;
                    }
                }
                return found;
            }

            for (int x = min.x; x <= max.x; x++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int slot = _head[Bucket(x, y)]; slot != None; slot = _next[slot])
                    {
                        Vector2Int at = _positions[slot];
                        if (at.x == x && at.y == y)
                        {
                            if (found < handles.Length)
                            {
                                handles[found] = slot;
                            }
                            found++;
                        }
                    }
                }
            }
            return found;
        }

        /// <summary><see cref="QueryRadius"/>, written as the entities themselves.</summary>
        public int CollectRadius(Vector2Int centre, int radius, Span<T> entities)
        {
            if (radius < 0)
            {
                return 0;
            }
            return CollectRect(new Vector2Int(centre.x - radius, centre.y - radius),
                new Vector2Int(centre.x + radius, centre.y + radius), entities);
        }

        /// <summary><see cref="QueryRect"/>, written as the entities themselves.</summary>
        public int CollectRect(Vector2Int min, Vector2Int max, Span<T> entities)
        {
            if (_count == 0 || max.x < min.x || max.y < min.y)
            {
                return 0;
            }

            long cells = ((long)max.x - min.x + 1) * ((long)max.y - min.y + 1);
            int found = 0;
            if (cells > _used)
            {
                for (int slot = 0; slot < _used; slot++)
                {
                    if (_prev[slot] == Free)
                    {
                        continue;
                    }
                    Vector2Int at = _positions[slot];
                    if (at.x >= min.x && at.x <= max.x && at.y >= min.y && at.y <= max.y)
                    {
                        if (found < entities.Length)
                        {
                            entities[found] = _entities[slot];
                        }
                        found++;
                    }
                }
                return found;
            }

            for (int x = min.x; x <= max.x; x++)
            {
                for (int y = min.y; y <= max.y; y++)
                {
                    for (int slot = _head[Bucket(x, y)]; slot != None; slot = _next[slot])
                    {
                        Vector2Int at = _positions[slot];
                        if (at.x == x && at.y == y)
                        {
                            if (found < entities.Length)
                            {
                                entities[found] = _entities[slot];
                            }
                            found++;
                        }
                    }
                }
            }
            return found;
        }

        // --- The chains ----------------------------------------------------------------------

        // Multiplicative, mixed down so the high bits reach the mask. No seed: two runs agree.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Bucket(int x, int y)
        {
            uint h = (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u;
            h ^= h >> 15;
            return (int)(h & (uint)_mask);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Link(int slot, int bucket)
        {
            int first = _head[bucket];
            _next[slot] = first;
            _prev[slot] = None;
            if (first != None)
            {
                _prev[first] = slot;
            }
            _head[bucket] = slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Unlink(int slot)
        {
            int before = _prev[slot];
            int after = _next[slot];
            if (before == Free)
            {
                throw new InvalidOperationException("SpatialHashGrid: the handle " + slot + " names no entity.");
            }
            if (before == None)
            {
                Vector2Int at = _positions[slot];
                _head[Bucket(at.x, at.y)] = after;
            }
            else
            {
                _next[before] = after;
            }
            if (after != None)
            {
                _prev[after] = before;
            }
        }

        private void Grow()
        {
            int capacity = _entities.Length * 2;
            Array.Resize(ref _next, capacity);
            Array.Resize(ref _prev, capacity);
            Array.Resize(ref _entities, capacity);
            Array.Resize(ref _positions, capacity);

            // One bucket per slot, so a chain stays about one entity long. Every slot in use is
            // relinked (there are no free ones: the arrays only grow when the free list is empty).
            _head = new int[capacity];
            _mask = capacity - 1;
            Array.Fill(_head, None);
            for (int slot = 0; slot < _used; slot++)
            {
                Vector2Int at = _positions[slot];
                Link(slot, Bucket(at.x, at.y));
            }
        }

        private static int RoundUpToPowerOfTwo(int value)
        {
            int power = 1;
            while (power < value)
            {
                power <<= 1;
            }
            return power;
        }
    }
}
