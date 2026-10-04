using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// Stall badges and the interaction caption both anchor to whatever they describe, so two
    /// golems standing a cell apart used to put their labels in the same place. A stall badge
    /// nobody can read is a stall nobody fixes.
    /// </summary>
    public class WorldHudLayoutTests
    {
        [TearDown]
        public void TearDown() => WorldHudRegistry.Clear();

        private static WorldHudRequest At(float x, float y, string owner) =>
            new WorldHudRequest(new Vector3(x, y, 0f), owner);

        [Test]
        public void ALoneLabelSitsExactlyOnItsAnchor()
        {
            // The common case -- one golem stalled by itself -- must be untouched.
            Vector3[] resolved = WorldHudLayout.Resolve(new[] { At(3f, 2f, "GolemA") });

            Assert.AreEqual(3f, resolved[0].x, 0.0001f);
            Assert.AreEqual(2f, resolved[0].y, 0.0001f);
        }

        [Test]
        public void TwoLabelsInOneColumn_Stack()
        {
            Vector3[] resolved = WorldHudLayout.Resolve(new[]
            {
                At(0f, 5f, "GolemNorth"),
                At(0f, 4f, "GolemSouth"),
            });

            // The southern one stays put -- nearest the camera is likeliest to be the one being
            // acted on -- and the northern one climbs.
            Assert.AreEqual(4f, resolved[1].y, 0.0001f);
            Assert.AreEqual(5f + WorldHudLayout.RowHeight, resolved[0].y, 0.0001f);
        }

        [Test]
        public void StackedLabelsKeepTheirColumn()
        {
            // Pushed straight up, never sideways: a radial nudge would put a badge over a
            // NEIGHBOUR at exactly the moment the neighbour also has something to say.
            Vector3[] resolved = WorldHudLayout.Resolve(new[]
            {
                At(2f, 1f, "A"),
                At(2f, 2f, "B"),
                At(2f, 3f, "C"),
            });

            foreach (Vector3 position in resolved)
            {
                Assert.AreEqual(2f, position.x, 0.0001f);
            }
        }

        [Test]
        public void LabelsAColumnApart_AreLeftAlone()
        {
            Vector3[] resolved = WorldHudLayout.Resolve(new[]
            {
                At(0f, 1f, "A"),
                At(4f, 1f, "B"),
            });

            Assert.AreEqual(1f, resolved[0].y, 0.0001f);
            Assert.AreEqual(1f, resolved[1].y, 0.0001f);
        }

        [Test]
        public void ThreeInAColumn_ClimbInEvenSteps()
        {
            Vector3[] resolved = WorldHudLayout.Resolve(new[]
            {
                At(0f, 0f, "A"),
                At(0f, 0f, "B"),
                At(0f, 0f, "C"),
            });

            var ys = new List<float> { resolved[0].y, resolved[1].y, resolved[2].y };
            ys.Sort();

            Assert.AreEqual(0f, ys[0], 0.0001f);
            Assert.AreEqual(WorldHudLayout.RowHeight, ys[1], 0.0001f);
            Assert.AreEqual(WorldHudLayout.RowHeight * 2f, ys[2], 0.0001f);
        }

        [Test]
        public void TiesAreBrokenByOwnerId_NotByRegistrationOrder()
        {
            // Two golems on the same row: without a tie-break the order is whoever registered
            // first, which is exactly the frame-to-frame flicker this exists to prevent.
            var forwards = new[] { At(0f, 0f, "Alpha"), At(0f, 0f, "Beta") };
            var backwards = new[] { At(0f, 0f, "Beta"), At(0f, 0f, "Alpha") };

            Vector3[] a = WorldHudLayout.Resolve(forwards);
            Vector3[] b = WorldHudLayout.Resolve(backwards);

            Assert.AreEqual(a[0].y, b[1].y, 0.0001f, "Alpha lands in the same slot either way");
            Assert.AreEqual(a[1].y, b[0].y, 0.0001f);
        }

        [Test]
        public void AnEmptyScreenResolvesToNothing()
        {
            Assert.AreEqual(0, WorldHudLayout.Resolve(new WorldHudRequest[0]).Length);
            Assert.AreEqual(0, WorldHudLayout.Resolve(null).Length);
        }

        // --- the registry ---------------------------------------------------------------------

        [Test]
        public void TheRegistryAnswersFromTheCrowdItWasLastGiven()
        {
            // Frame 1: two labels register and get their raw anchors, because nothing has been
            // solved yet.
            WorldHudRegistry.Solve(1);
            Vector3 first = WorldHudRegistry.Resolve(new Vector3(0f, 5f, 0f), "GolemNorth");
            Vector3 second = WorldHudRegistry.Resolve(new Vector3(0f, 4f, 0f), "GolemSouth");
            Assert.AreEqual(5f, first.y, 0.0001f);
            Assert.AreEqual(4f, second.y, 0.0001f);

            // Frame 2: the crowd is known, so the northern badge is lifted clear.
            WorldHudRegistry.Solve(2);
            Vector3 lifted = WorldHudRegistry.Resolve(new Vector3(0f, 5f, 0f), "GolemNorth");

            Assert.AreEqual(5f + WorldHudLayout.RowHeight, lifted.y, 0.0001f);
        }

        [Test]
        public void ALabelThatStopsRegistering_StopsPushingItsNeighbours()
        {
            // A golem that resumes (or is deleted) must not go on displacing the badge beside it.
            WorldHudRegistry.Solve(1);
            WorldHudRegistry.Resolve(new Vector3(0f, 5f, 0f), "GolemNorth");
            WorldHudRegistry.Resolve(new Vector3(0f, 4f, 0f), "GolemSouth");

            WorldHudRegistry.Solve(2);
            Vector3 aloneNow = WorldHudRegistry.Resolve(new Vector3(0f, 5f, 0f), "GolemNorth");
            Assert.AreNotEqual(5f, aloneNow.y, "still crowded from last frame's pair");

            WorldHudRegistry.Solve(3);
            Vector3 settled = WorldHudRegistry.Resolve(new Vector3(0f, 5f, 0f), "GolemNorth");

            Assert.AreEqual(5f, settled.y, 0.0001f, "the neighbour is gone, so the badge drops back");
        }

        [Test]
        public void Clear_ForgetsAWorldThatNoLongerExists()
        {
            WorldHudRegistry.Solve(1);
            WorldHudRegistry.Resolve(Vector3.zero, "A");

            WorldHudRegistry.Clear();

            Assert.AreEqual(0, WorldHudRegistry.PendingCount);
            Assert.AreEqual(0, WorldHudRegistry.ResolvedCount);
        }
    }
}
