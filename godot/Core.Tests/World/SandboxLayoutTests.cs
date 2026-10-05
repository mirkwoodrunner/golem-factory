using System.Collections.Generic;
using System.IO;
using System.Linq;
using GolemFactory.Compat;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.World
{
    /// <summary>
    /// The Sandbox shell, pinned to what Unity's SandboxFloorGenerator baked into Sandbox.unity.
    ///
    /// <para>
    /// The expected counts and positions were READ FROM Sandbox.unity at the G4 port
    /// (2026-10-04) and are written here rather than read live, because Assets/ is deleted at
    /// cutover and these tests must outlive it. They pin that the port reproduces the room the
    /// player knows: if one of these changes, the room changed.
    /// </para>
    /// </summary>
    public class SandboxLayoutTests
    {
        private static Dictionary<string, LayoutPiece> ByName(IEnumerable<LayoutPiece> pieces) =>
            pieces.ToDictionary(p => p.Name);

        private static int CountPrefix(IEnumerable<LayoutPiece> pieces, string prefix) =>
            pieces.Count(p => p.Name.StartsWith(prefix + "_"));

        [TestCase("WallEast", 33)]
        [TestCase("WallWest", 33)]
        [TestCase("WallNorth", 25)]
        [TestCase("SkirtSouth", 25)]
        [TestCase("KerbStreet", 37)]
        [TestCase("WallShoulder", 12)]
        [TestCase("WallPost", 2)]
        public void WallCountsMatchUnitysSandbox(string kind, int expected)
        {
            Assert.AreEqual(expected, CountPrefix(SandboxLayout.Walls(), kind));
        }

        [Test]
        public void SevenSconces_OneOnEveryFourthNorthSegment()
        {
            List<LayoutPiece> lit = SandboxLayout.Walls().Where(p => p.HasSconce).ToList();
            Assert.AreEqual(7, lit.Count, "Unity's Sandbox has seven SconceLight children");
            Assert.That(lit.All(p => p.Name.StartsWith("WallNorth_") && p.Sprite == "wall_segment_nw_lamp"));
        }

        [Test]
        public void FortyFivePropsMatchUnitysSandbox()
        {
            List<LayoutPiece> props = SandboxLayout.Props();
            Assert.AreEqual(45, props.Count);
            Assert.AreEqual(props.Count, props.Select(p => p.Name).Distinct().Count(), "one prop per cell");
            Assert.That(props.All(p => p.HasShadow), "every prop stands on a contact shadow");
        }

        // Positions read off Sandbox.unity's transforms.
        [TestCase("WallNorth_0", 0f, 12.5f)]
        [TestCase("WallNorth_4", 4f, 12.5f)]
        [TestCase("WallEast_0", 12.5f, 0f)]
        [TestCase("WallEast_-20", 18.5f, -20f)]
        [TestCase("WallWest_12", -12.5f, 12f)]
        [TestCase("SkirtSouth_0", 0f, -12.5f)]
        [TestCase("KerbStreet_-18", -18f, -20.5f)]
        [TestCase("KerbStreet_18", 18f, -20.5f)]
        [TestCase("WallShoulder_0", 13f, -12.5f)]
        [TestCase("WallShoulder_1", -13f, -12.5f)]
        [TestCase("WallPost_0", 12.5f, 12.5f)]
        [TestCase("WallPost_1", -12.5f, 12.5f)]
        public void WallPiecesStandWhereUnityPutThem(string name, float x, float y)
        {
            LayoutPiece piece = ByName(SandboxLayout.Walls())[name];
            Assert.AreEqual(x, piece.Anchor.x, 1e-4, name + ".x");
            Assert.AreEqual(y, piece.Anchor.y, 1e-4, name + ".y");
        }

        [TestCase("Prop_0_12", "prop_hearth")]
        [TestCase("Prop_-6_12", "prop_shelf")]
        [TestCase("Prop_6_12", "prop_workbench")]
        public void TheBackWallIsFurnished(string name, string sprite)
        {
            Assert.AreEqual(sprite, ByName(SandboxLayout.Props())[name].Sprite);
        }

        [Test]
        public void NoPropStandsOnPlayableFloor()
        {
            int he = FloorLayout.HalfExtent;
            foreach (LayoutPiece prop in SandboxLayout.Props())
            {
                var cell = new Vector2Int((int)prop.Anchor.x, (int)prop.Anchor.y);
                bool onWallRing = cell.x == he || cell.x == -he || cell.y == he || cell.y == -he;
                bool onStreetFarEnd = cell.y <= -he - FloorLayout.StreetDepth + 2;
                Assert.IsTrue(onWallRing || onStreetFarEnd, prop.Name + " stands on playable floor");
            }
        }

        [Test]
        public void NoPropBlocksAStallOrItsApproach()
        {
            // The stalls stand on one street row; their approach tiles flank them. §3.2's
            // extractor cap needs those open, and a barrel there would halve a stall silently.
            SandboxSetup setup = SandboxSetupTests.LoadReal();
            var props = new HashSet<(int, int)>(SandboxLayout.Props().Select(p => ((int)p.Anchor.x, (int)p.Anchor.y)));
            foreach (SandboxSetup.NodeEntry node in setup.nodes)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        Assert.IsFalse(props.Contains((node.x + dx, node.y + dy)),
                            $"a prop stands at or beside {node.id}'s stall");
                    }
                }
            }
        }

        [Test]
        public void ExpansionMovesTheNorthWallAndPosts_NotTheStreet()
        {
            Dictionary<string, LayoutPiece> grown = ByName(SandboxLayout.Walls(FloorLayout.DefaultNorthExtent + 2));
            Assert.AreEqual(FloorLayout.DefaultNorthExtent + 2.5f, grown["WallNorth_0"].Anchor.y, 1e-4);
            Assert.AreEqual(FloorLayout.DefaultNorthExtent + 2.5f, grown["WallPost_0"].Anchor.y, 1e-4);
            Assert.AreEqual(-20.5f, grown["KerbStreet_0"].Anchor.y, 1e-4, "the far kerb never moves");
        }

        [Test]
        public void StreetCobblesAlternate()
        {
            Assert.AreEqual(0, SandboxLayout.StreetTileVariant(new Vector2Int(0, 0)));
            Assert.AreEqual(1, SandboxLayout.StreetTileVariant(new Vector2Int(1, 0)));
            Assert.AreEqual(1, SandboxLayout.StreetTileVariant(new Vector2Int(0, 1)));
        }

        [Test]
        public void EverySpriteTheLayoutNamesExists()
        {
            string art = Path.Combine(Path.GetDirectoryName(AuthoredData.DataDirectory), "art");
            foreach (string sprite in SandboxLayout.Walls().Concat(SandboxLayout.Props()).Select(p => p.Sprite).Distinct())
            {
                FileAssert.Exists(Path.Combine(art, sprite + ".png"), sprite);
            }
        }
    }
}
