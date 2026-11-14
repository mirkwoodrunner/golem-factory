using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.World;
using NUnit.Framework;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.EditMode
{
    public class SpatialHashGridTests
    {
        [Test]
        public void Add_ThenTryGet_FindsTheEntityOnItsCell()
        {
            var grid = new SpatialHashGrid<string>();
            int handle = grid.Add(new Vector2Int(3, -2), "crate");

            Assert.AreEqual(1, grid.Count);
            Assert.IsTrue(grid.TryGet(new Vector2Int(3, -2), out string found));
            Assert.AreEqual("crate", found);
            Assert.AreEqual("crate", grid[handle]);
            Assert.AreEqual(new Vector2Int(3, -2), grid.CellOf(handle));
            Assert.IsFalse(grid.TryGet(new Vector2Int(-2, 3), out _), "the mirrored cell is another cell");
            Assert.IsFalse(grid.Contains(new Vector2Int(3, -1)));
        }

        [Test]
        public void Remove_TakesOnlyThatEntity_AndItsSlotIsReused()
        {
            var grid = new SpatialHashGrid<string>(4);
            int a = grid.Add(new Vector2Int(1, 1), "a");
            int b = grid.Add(new Vector2Int(1, 1), "b");
            grid.Remove(a);

            Assert.AreEqual(1, grid.Count);
            Assert.IsTrue(grid.TryGet(new Vector2Int(1, 1), out string left));
            Assert.AreEqual("b", left);

            int c = grid.Add(new Vector2Int(9, 9), "c");
            Assert.AreEqual(a, c, "a freed slot is handed out again before the arrays grow");
            Assert.AreEqual("b", grid[b]);
            Assert.Throws<InvalidOperationException>(() => { grid.Remove(a); grid.Remove(a); });
        }

        [Test]
        public void Move_KeepsTheHandle_AndLeavesTheOldCell()
        {
            var grid = new SpatialHashGrid<string>();
            int handle = grid.Add(new Vector2Int(0, 0), "golem");
            grid.Move(handle, new Vector2Int(40, -7));

            Assert.IsFalse(grid.Contains(new Vector2Int(0, 0)));
            Assert.IsTrue(grid.TryGetHandle(new Vector2Int(40, -7), out int found));
            Assert.AreEqual(handle, found);
            Assert.AreEqual(1, grid.Count);
        }

        [Test]
        public void RemoveAt_SaysWhetherTheCellHeldAnything()
        {
            var grid = new SpatialHashGrid<string>();
            grid.Add(new Vector2Int(2, 2), "x");

            Assert.IsTrue(grid.RemoveAt(new Vector2Int(2, 2)));
            Assert.IsFalse(grid.RemoveAt(new Vector2Int(2, 2)));
            Assert.AreEqual(0, grid.Count);
        }

        [Test]
        public void Growing_KeepsEveryEntity_AndEveryHandle()
        {
            var grid = new SpatialHashGrid<int>(4);
            var handles = new List<int>();
            for (int i = 0; i < 1000; i++)
            {
                handles.Add(grid.Add(new Vector2Int(i % 37 - 18, i / 37 - 13), i));
            }

            Assert.AreEqual(1000, grid.Count);
            Assert.GreaterOrEqual(grid.Capacity, 1000);
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(i, grid[handles[i]]);
                Assert.IsTrue(grid.TryGet(new Vector2Int(i % 37 - 18, i / 37 - 13), out int found));
                Assert.AreEqual(i, found);
            }
        }

        [Test]
        public void QueryRadius_ReturnsTheSquareAroundTheCentre()
        {
            var grid = new SpatialHashGrid<string>();
            for (int x = -3; x <= 3; x++)
            {
                for (int y = -3; y <= 3; y++)
                {
                    grid.Add(new Vector2Int(x, y), x + "," + y);
                }
            }

            Span<int> handles = stackalloc int[16];
            int found = grid.QueryRadius(new Vector2Int(2, 2), 1, handles);

            Assert.AreEqual(9, found);
            var cells = new List<Vector2Int>();
            for (int i = 0; i < found; i++)
            {
                cells.Add(grid.CellOf(handles[i]));
            }
            Assert.IsTrue(cells.All(c => Math.Abs(c.x - 2) <= 1 && Math.Abs(c.y - 2) <= 1));
            Assert.AreEqual(9, cells.Distinct().Count());
            Assert.AreEqual(1, grid.QueryRadius(new Vector2Int(2, 2), 0, handles));
            Assert.AreEqual(0, grid.QueryRadius(new Vector2Int(2, 2), -1, handles));
        }

        [Test]
        public void AQueryLargerThanItsBuffer_ReportsTheWholeCount()
        {
            var grid = new SpatialHashGrid<int>();
            for (int i = 0; i < 10; i++)
            {
                grid.Add(new Vector2Int(i, 0), i);
            }

            Span<int> three = stackalloc int[3];
            int found = grid.QueryRect(new Vector2Int(0, 0), new Vector2Int(9, 0), three);

            Assert.AreEqual(10, found, "more than the buffer holds: the caller can tell it was too small");
        }

        [Test]
        public void QueryCell_ReturnsEveryEntitySharingTheCell()
        {
            var grid = new SpatialHashGrid<string>();
            grid.Add(new Vector2Int(5, 5), "a");
            grid.Add(new Vector2Int(5, 5), "b");
            grid.Add(new Vector2Int(5, 6), "c");

            Span<int> handles = stackalloc int[4];
            int found = grid.QueryCell(new Vector2Int(5, 5), handles);

            Assert.AreEqual(2, found);
            CollectionAssert.AreEquivalent(new[] { "a", "b" }, new[] { grid[handles[0]], grid[handles[1]] });
        }

        [Test]
        public void Clear_EmptiesTheGrid_AndItCanBeFilledAgain()
        {
            var grid = new SpatialHashGrid<string>();
            grid.Add(new Vector2Int(1, 2), "a");
            grid.Add(new Vector2Int(3, 4), "b");
            grid.Clear();

            Assert.AreEqual(0, grid.Count);
            Assert.IsFalse(grid.Contains(new Vector2Int(1, 2)));
            grid.Add(new Vector2Int(1, 2), "c");
            Assert.IsTrue(grid.TryGet(new Vector2Int(1, 2), out string found));
            Assert.AreEqual("c", found);
        }

        /// <summary>
        /// Ten thousand random adds, moves, removals and queries, checked after each against a
        /// plain list of what should be where.
        /// </summary>
        [Test]
        public void RandomOperations_AgreeWithAPlainList()
        {
            var random = new Random(20261113);
            var grid = new SpatialHashGrid<int>(8);
            var model = new Dictionary<int, (Vector2Int cell, int value)>(); // by handle
            var entities = new int[64];
            int nextValue = 0;
            Vector2Int RandomCell() => new Vector2Int(random.Next(-12, 13), random.Next(-12, 13));

            for (int step = 0; step < 10000; step++)
            {
                int roll = random.Next(10);
                if (roll < 4 || model.Count == 0)
                {
                    Vector2Int cell = RandomCell();
                    int handle = grid.Add(cell, nextValue);
                    Assert.IsFalse(model.ContainsKey(handle), "a live handle is never handed out twice");
                    model[handle] = (cell, nextValue++);
                }
                else if (roll < 6)
                {
                    int handle = model.Keys.ElementAt(random.Next(model.Count));
                    grid.Remove(handle);
                    model.Remove(handle);
                }
                else if (roll < 8)
                {
                    int handle = model.Keys.ElementAt(random.Next(model.Count));
                    Vector2Int cell = RandomCell();
                    grid.Move(handle, cell);
                    model[handle] = (cell, model[handle].value);
                }
                else
                {
                    Vector2Int centre = RandomCell();
                    int radius = random.Next(0, 30);
                    int found = grid.CollectRadius(centre, radius, entities.AsSpan());
                    int[] expected = model.Values
                        .Where(e => Math.Abs(e.cell.x - centre.x) <= radius && Math.Abs(e.cell.y - centre.y) <= radius)
                        .Select(e => e.value).OrderBy(v => v).ToArray();
                    Assert.AreEqual(expected.Length, found, $"step {step}: radius {radius} at {centre}");
                    if (found <= entities.Length)
                    {
                        CollectionAssert.AreEqual(expected, entities.Take(found).OrderBy(v => v).ToArray(), $"step {step}");
                    }
                }

                Assert.AreEqual(model.Count, grid.Count);
            }

            foreach (KeyValuePair<int, (Vector2Int cell, int value)> entry in model)
            {
                Assert.AreEqual(entry.Value.value, grid[entry.Key]);
                Assert.AreEqual(entry.Value.cell, grid.CellOf(entry.Key));
                Assert.IsTrue(grid.Contains(entry.Value.cell));
            }
        }

        /// <summary>
        /// A grid sized for its load allocates nothing: adds, moves, removals, lookups and
        /// queries into a stack buffer leave the thread's allocated bytes where they were.
        /// </summary>
        [Test]
        public void ASizedGrid_AllocatesNothing()
        {
            var grid = new SpatialHashGrid<object>(2048);
            var entity = new object();
            var handles = new int[1024];
            Churn(grid, entity, handles); // once, so everything is compiled

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int round = 0; round < 20; round++)
            {
                Churn(grid, entity, handles);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0, allocated, "bytes allocated by 20 rounds of adds, moves, queries and removals");
        }

        private static int Churn(SpatialHashGrid<object> grid, object entity, int[] handles)
        {
            int hits = 0;
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = grid.Add(new Vector2Int(i % 32, i / 32), entity);
            }
            Span<int> near = stackalloc int[32];
            for (int i = 0; i < handles.Length; i++)
            {
                grid.Move(handles[i], new Vector2Int(i % 32 + 1, i / 32 - 1));
                hits += grid.QueryRadius(new Vector2Int(i % 32, i / 32), 1, near);
                hits += grid.TryGet(new Vector2Int(i / 32, i % 32), out _) ? 1 : 0;
            }
            for (int i = 0; i < handles.Length; i++)
            {
                grid.Remove(handles[i]);
            }
            return hits;
        }

        /// <summary>A factory-sized block of cells spreads across the buckets: no chain grows long.</summary>
        [Test]
        public void ADenseBlock_KeepsLookupsShort()
        {
            var grid = new SpatialHashGrid<int>(4096);
            for (int x = -32; x < 32; x++)
            {
                for (int y = -32; y < 32; y++)
                {
                    grid.Add(new Vector2Int(x, y), 0);
                }
            }

            // Every cell's own bucket, walked: the longest holds a handful, not a column.
            int longest = 0;
            Span<int> handles = stackalloc int[1];
            for (int x = -32; x < 32; x++)
            {
                for (int y = -32; y < 32; y++)
                {
                    Assert.AreEqual(1, grid.QueryCell(new Vector2Int(x, y), handles));
                }
            }
            var chains = (int[])typeof(SpatialHashGrid<int>)
                .GetField("_head", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(grid);
            var next = (int[])typeof(SpatialHashGrid<int>)
                .GetField("_next", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(grid);
            foreach (int head in chains)
            {
                int length = 0;
                for (int slot = head; slot != -1; slot = next[slot])
                {
                    length++;
                }
                longest = Math.Max(longest, length);
            }
            Assert.LessOrEqual(longest, 8, "the longest bucket chain over a 64 x 64 block");
        }
    }
}
