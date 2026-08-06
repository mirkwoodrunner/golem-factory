using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Economy;
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
                StallReason.NoSourceAtTile, StallReason.NoTargetAtTile,
                StallReason.InputFull, StallReason.OutputFull, StallReason.MissingItem
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

        // --- Internal-stock stalls ----------------------------------------------------------
        // These two carry an ITEM TYPE as their resource id rather than a belt/node/buffer id,
        // because nothing external is blocking the golem -- its own hold is full. progression
        // -design section 8 requires OutputFull specifically to name the blocked type: a
        // smelter jammed on Slag rather than on Iron Plate is the whole Slag economy, and the
        // player cannot tell those apart from "GolemD is stalled".

        [Test]
        public void DescribeShort_InputFull_NamesTheBlockedItemType()
        {
            string text = StallDiagnostics.DescribeShort(StallReason.InputFull, ItemType.Scrap);

            StringAssert.Contains(ItemType.Scrap, text);
            StringAssert.DoesNotContain("source", text, "pointed the player at an external source");
        }

        [Test]
        public void DescribeShort_OutputFull_NamesTheBlockedItemType()
        {
            string text = StallDiagnostics.DescribeShort(StallReason.OutputFull, "Slag");

            StringAssert.Contains("Slag", text);
            StringAssert.Contains("output", text);
        }

        [Test]
        public void Describe_OutputFull_NamesBothTheGolemAndTheBlockedItemType()
        {
            string text = StallDiagnostics.Describe("GolemD", StallReason.OutputFull, "Slag");

            StringAssert.Contains("GolemD", text);
            StringAssert.Contains("Slag", text);
        }

        [Test]
        public void Describe_InputAndOutputFull_AreDistinguishable()
        {
            string input = StallDiagnostics.Describe("GolemD", StallReason.InputFull, ItemType.Scrap);
            string output = StallDiagnostics.Describe("GolemD", StallReason.OutputFull, ItemType.Scrap);

            Assert.AreNotEqual(input, output,
                "a jammed input and a jammed output need different fixes and must read differently");
        }

        [Test]
        public void StockStallText_WithoutAnItemType_StillReadsAsASentence()
        {
            StallReason[] reasons =
            {
                StallReason.InputFull, StallReason.OutputFull, StallReason.MissingItem
            };

            foreach (StallReason reason in reasons)
            {
                string text = StallDiagnostics.Describe("GolemD", reason, null);
                StringAssert.Contains("GolemD", text);
                Assert.IsFalse(text.Contains("  "), "collapsed placeholder left a double space");
            }
        }

        // --- MissingItem ---------------------------------------------------------------------
        // "The type this step named is not available", which is NOT the same claim as "this
        // place is empty". A typed Haul against a buffer holding 500 of the wrong good used to
        // report BufferEmpty, sending the player to inspect the one container they can see is
        // full. This is the file that exists to stop exactly that.

        [Test]
        public void Describe_MissingItem_NamesTheTypeAndNotTheEndpoint()
        {
            string text = StallDiagnostics.Describe("GolemD", StallReason.MissingItem, ItemType.Aether);

            StringAssert.Contains("GolemD", text);
            StringAssert.Contains(ItemType.Aether, text);
            StringAssert.DoesNotContain("empty", text, "it still reads as 'the place is empty'");
        }

        [Test]
        public void Describe_MissingItem_ReadsDifferentlyFromBufferEmpty()
        {
            // Same golem, same blocked step, two different underlying problems with two
            // different fixes -- look upstream of a stocked buffer, versus wait for an empty one.
            string missing = StallDiagnostics.Describe("GolemD", StallReason.MissingItem, ItemType.Aether);
            string empty = StallDiagnostics.Describe("GolemD", StallReason.BufferEmpty, "ScrapBuffer");

            Assert.AreNotEqual(missing, empty);
        }

        [Test]
        public void DescribeShort_MissingItem_NamesTheType()
        {
            StringAssert.Contains("Slag", StallDiagnostics.DescribeShort(StallReason.MissingItem, "Slag"));
        }

        [Test]
        public void MissingItem_WithNoType_DoesNotNameTheGolemTwiceOrInventASource()
        {
            // The empty-hold Push case: the step names no type, so the sentence must fall back
            // to something generic without pointing at a "source" that is not the problem.
            string text = StallDiagnostics.Describe("GolemD", StallReason.MissingItem, null);

            Assert.AreEqual(1, CountOccurrences(text, "GolemD"), "the golem's name appears twice: " + text);
            StringAssert.DoesNotContain("source", text);
        }

        // --- The shortfall amount (progression-design §8, item §1.3) --------------------------
        // §8's "Why is this golem stopped?" row asks for "the specific short ingredient AND
        // amount" for Assemble. On R15 (10 Casing + 6 Iron Plate + 4 Brass), "no Casing
        // available" cannot tell the player whether they are one short or nine.

        [Test]
        public void Describe_MissingItem_WithAShortfall_SaysHowManyMoreAreNeeded()
        {
            string text = StallDiagnostics.Describe("GolemD", StallReason.MissingItem, "Casing", 9);

            StringAssert.Contains("GolemD", text);
            StringAssert.Contains("Casing", text);
            StringAssert.Contains("9", text);
        }

        [Test]
        public void DescribeShort_MissingItem_WithAShortfall_SaysHowManyMoreAreNeeded()
        {
            string text = StallDiagnostics.DescribeShort(StallReason.MissingItem, "Casing", 9);

            StringAssert.Contains("Casing", text);
            StringAssert.Contains("9", text);
        }

        [Test]
        public void MissingItem_OneShortReadsDifferentlyFromNineShort()
        {
            // The whole point of carrying the amount: a hiccup and a dead line must not produce
            // the same sentence.
            Assert.AreNotEqual(
                StallDiagnostics.Describe("GolemD", StallReason.MissingItem, "Casing", 1),
                StallDiagnostics.Describe("GolemD", StallReason.MissingItem, "Casing", 9));
        }

        [Test]
        public void MissingItem_WithNoShortfall_ReadsExactlyAsItDidBeforeShortfallsExisted()
        {
            // A Haul type mismatch and an empty Push hold have no meaningful amount, so the
            // text must fall back to the exact wording those two cases have always had --
            // pinned literally, because "still contains the item type" would not catch a
            // regression that quietly started printing "need 0 more".
            Assert.AreEqual("GolemD stalled: no Aether available",
                StallDiagnostics.Describe("GolemD", StallReason.MissingItem, ItemType.Aether));
            Assert.AreEqual("GolemD stalled: no Aether available",
                StallDiagnostics.Describe("GolemD", StallReason.MissingItem, ItemType.Aether, 0));
            Assert.AreEqual("no Aether",
                StallDiagnostics.DescribeShort(StallReason.MissingItem, ItemType.Aether));

            // The empty-hold Push case: no type either, so an amount can never apply.
            Assert.AreEqual("GolemD stalled: no goods available",
                StallDiagnostics.Describe("GolemD", StallReason.MissingItem, null, 3));
            Assert.AreEqual("no goods",
                StallDiagnostics.DescribeShort(StallReason.MissingItem, null, 3));
        }

        [Test]
        public void ShortfallText_IsPlainAscii()
        {
            // Same LiberationSans SDF constraint as everything else the badge renders.
            string[] texts =
            {
                StallDiagnostics.DescribeShort(StallReason.MissingItem, "Casing", 9),
                StallDiagnostics.Describe("GolemD", StallReason.MissingItem, "Casing", 9)
            };

            foreach (string text in texts)
            {
                foreach (char c in text)
                {
                    Assert.Less((int)c, 128, "non-ASCII '" + c + "' has no glyph in LiberationSans SDF");
                }
            }
        }

        [Test]
        public void ComposeStripText_CarriesThePrimaryStallsShortfall()
        {
            string text = StallDiagnostics.ComposeStripText(
                1, new StallSnapshot("GolemD", StallReason.MissingItem, "Casing", 9));

            StringAssert.Contains("Casing", text);
            StringAssert.Contains("9", text);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = haystack.IndexOf(needle, System.StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = haystack.IndexOf(needle, index + needle.Length, System.StringComparison.Ordinal);
            }

            return count;
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
