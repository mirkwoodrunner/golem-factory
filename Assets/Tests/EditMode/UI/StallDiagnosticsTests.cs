using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Events;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    public class StallDiagnosticsTests
    {
        // --- The regression that motivated all of this -------------------------------------
        // The alerts strip read "All golems running." while two golems sat Stalled, because
        // StallTracker only ever heard the event stream and a listener never hears stalls that
        // happened before it subscribed. Reconcile re-derives from truth.

        [Test]
        public void Reconcile_GolemStalledBeforeTheTrackerSubscribed_IsStillReported()
        {
            var tracker = new StallTracker();
            tracker.Subscribe();

            // No GolemStalledEvent was ever delivered to this tracker -- it joined late.
            Assert.AreEqual(0, tracker.Count, "precondition: the event stream told it nothing");

            tracker.Reconcile(new List<StallSnapshot>
            {
                new StallSnapshot("GolemD", StallReason.NodeEmpty, "AetherNode")
            });

            Assert.AreEqual(1, tracker.Count);
            Assert.IsTrue(tracker.IsStalled("GolemD"));
            tracker.Unsubscribe();
        }

        [Test]
        public void Reconcile_GolemThatSilentlyResumed_IsDroppedEvenWithoutAResumedEvent()
        {
            var tracker = new StallTracker();
            tracker.Reconcile(new List<StallSnapshot>
            {
                new StallSnapshot("GolemD", StallReason.NodeEmpty, "AetherNode")
            });
            Assert.IsTrue(tracker.IsStalled("GolemD"));

            // Truth now says nothing is stalled; no GolemResumedEvent was published.
            tracker.Reconcile(new List<StallSnapshot>());

            Assert.AreEqual(0, tracker.Count);
            Assert.IsFalse(tracker.IsStalled("GolemD"));
        }

        [Test]
        public void Reconcile_NullList_ClearsRatherThanThrowing()
        {
            var tracker = new StallTracker();
            tracker.Reconcile(new List<StallSnapshot>
            {
                new StallSnapshot("GolemD", StallReason.NodeEmpty, "AetherNode")
            });

            tracker.Reconcile(null);

            Assert.AreEqual(0, tracker.Count);
        }

        [Test]
        public void Reconcile_CarriesTheReasonAndResourceId()
        {
            var tracker = new StallTracker();
            tracker.Reconcile(new List<StallSnapshot>
            {
                new StallSnapshot("GolemD", StallReason.BeltFull, "ScrapBeltA")
            });

            StallSnapshot snapshot;
            Assert.IsTrue(tracker.TryGetStall("GolemD", out snapshot));
            Assert.AreEqual(StallReason.BeltFull, snapshot.Reason);
            Assert.AreEqual("ScrapBeltA", snapshot.ResourceId);
        }

        [Test]
        public void PrimaryStall_IsDeterministicRatherThanDictionaryOrdered()
        {
            var tracker = new StallTracker();
            tracker.Reconcile(new List<StallSnapshot>
            {
                new StallSnapshot("GolemZ", StallReason.BufferEmpty, "ScrapBuffer"),
                new StallSnapshot("GolemA", StallReason.NodeEmpty, "AetherNode"),
                new StallSnapshot("GolemM", StallReason.NodeEmpty, "ScrapNode")
            });

            StallSnapshot primary;
            Assert.IsTrue(tracker.TryGetPrimaryStall(out primary));
            // NodeEmpty sorts before BufferEmpty; GolemA before GolemM on the id tiebreak.
            Assert.AreEqual("GolemA", primary.GolemId);
        }

        [Test]
        public void PrimaryStall_NothingStalled_IsFalse()
        {
            var tracker = new StallTracker();
            StallSnapshot primary;
            Assert.IsFalse(tracker.TryGetPrimaryStall(out primary));
        }

        // --- Strip text --------------------------------------------------------------------

        [Test]
        public void ComposeStripText_NothingStalled_ReportsAllRunning()
        {
            Assert.AreEqual("All golems running.",
                StallDiagnostics.ComposeStripText(0, default(StallSnapshot)));
        }

        [Test]
        public void ComposeStripText_OneStall_NamesTheBlockingResourceNotJustACount()
        {
            string text = StallDiagnostics.ComposeStripText(
                1, new StallSnapshot("GolemD", StallReason.NodeEmpty, "AetherNode"));

            StringAssert.Contains("GolemD", text);
            StringAssert.Contains("AetherNode", text);
            StringAssert.DoesNotContain("more", text);
        }

        [Test]
        public void ComposeStripText_ManyStalls_NamesOneAndCountsTheRest()
        {
            string text = StallDiagnostics.ComposeStripText(
                3, new StallSnapshot("GolemD", StallReason.BeltFull, "ScrapBeltA"));

            StringAssert.Contains("GolemD", text);
            StringAssert.Contains("(+2 more)", text);
        }

        [Test]
        public void ComposeStripText_UsesAsciiOnly_SoTheTmpAtlasCanRenderIt()
        {
            string text = StallDiagnostics.ComposeStripText(
                1, new StallSnapshot("GolemD", StallReason.NodeEmpty, "AetherNode"));

            foreach (char c in text)
            {
                Assert.Less((int)c, 128, "non-ASCII '" + c + "' has no glyph in LiberationSans SDF");
            }
        }

        [Test]
        public void Describe_EveryReason_ProducesDistinctActionableText()
        {
            var seen = new HashSet<string>();
            StallReason[] reasons =
            {
                StallReason.NodeEmpty, StallReason.BeltFull, StallReason.BeltEmpty,
                StallReason.BufferEmpty, StallReason.Unconfigured,
                StallReason.NoSourceAtTile, StallReason.NoTargetAtTile
            };

            foreach (StallReason reason in reasons)
            {
                string text = StallDiagnostics.Describe("GolemD", reason, "ScrapBeltA");
                Assert.IsTrue(seen.Add(text), "two reasons produced identical text: " + text);
            }
        }

        [Test]
        public void Describe_MissingResourceId_StillReadsAsASentence()
        {
            string text = StallDiagnostics.Describe("GolemD", StallReason.NodeEmpty, null);

            StringAssert.Contains("GolemD", text);
            Assert.IsFalse(text.Contains("  "), "collapsed placeholder left a double space");
        }

        [Test]
        public void DescribeShort_OmitsTheGolemId_SinceTheBadgeIsAlreadyAttachedToIt()
        {
            string text = StallDiagnostics.DescribeShort(StallReason.NodeEmpty, "AetherNode");

            StringAssert.Contains("AetherNode", text);
            StringAssert.DoesNotContain("GolemD", text);
        }

        // --- Spatial stalls ------------------------------------------------------------------
        // A facing-routed golem that is blocked is blocked because of *where it is pointing*.
        // Naming a resource id would be misleading -- there is no resource; the actionable fact
        // is the empty tile, so the text has to say which side is empty.

        [Test]
        public void DescribeShort_NoSourceAtTile_SaysNothingIsBehindIt()
        {
            string text = StallDiagnostics.DescribeShort(StallReason.NoSourceAtTile, "(-1, 1)");

            StringAssert.Contains("behind", text);
            StringAssert.Contains("(-1, 1)", text);
        }

        [Test]
        public void DescribeShort_NoTargetAtTile_SaysNothingIsInFrontOfIt()
        {
            string text = StallDiagnostics.DescribeShort(StallReason.NoTargetAtTile, "(0, 2)");

            StringAssert.Contains("front", text);
            StringAssert.Contains("(0, 2)", text);
        }

        [Test]
        public void Describe_SpatialReasons_DistinguishBehindFromInFront()
        {
            string behind = StallDiagnostics.Describe("GolemD", StallReason.NoSourceAtTile, "(0, 0)");
            string front = StallDiagnostics.Describe("GolemD", StallReason.NoTargetAtTile, "(0, 2)");

            StringAssert.Contains("GolemD", behind);
            StringAssert.Contains("GolemD", front);
            Assert.AreNotEqual(behind, front);
        }

        [Test]
        public void Describe_SpatialReasonsWithoutACell_StillReadAsSentences()
        {
            foreach (StallReason reason in new[] { StallReason.NoSourceAtTile, StallReason.NoTargetAtTile })
            {
                string text = StallDiagnostics.Describe("GolemD", reason, null);
                StringAssert.Contains("GolemD", text);
                Assert.IsFalse(text.Contains("  "), "collapsed placeholder left a double space");
                StringAssert.DoesNotContain("tile ", text, "named a tile it does not have");
            }
        }

        [Test]
        public void SpatialStallText_IsPlainAscii()
        {
            // Same LiberationSans SDF constraint as ComposeStripText: no arrow/warning glyphs.
            string[] texts =
            {
                StallDiagnostics.DescribeShort(StallReason.NoSourceAtTile, "(0, 0)"),
                StallDiagnostics.DescribeShort(StallReason.NoTargetAtTile, "(0, 2)"),
                StallDiagnostics.Describe("GolemD", StallReason.NoSourceAtTile, "(0, 0)"),
                StallDiagnostics.Describe("GolemD", StallReason.NoTargetAtTile, "(0, 2)")
            };

            foreach (string text in texts)
            {
                foreach (char c in text)
                {
                    Assert.Less((int)c, 128, "non-ASCII '" + c + "' has no glyph in LiberationSans SDF");
                }
            }
        }
    }
}
