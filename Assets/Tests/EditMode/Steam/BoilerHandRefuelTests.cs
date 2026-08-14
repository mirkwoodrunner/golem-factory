using NUnit.Framework;
using GolemFactory.Steam;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The last step of §10's total-blackout recovery, which did not exist.
    ///
    /// <para>
    /// THE LOOP: a boiler's only Coke writer was <c>BoilerFuelEndpoint</c>, which a golem
    /// <c>Push</c>es into; a golem needs a powered boiler to move; a player-built boiler starts
    /// at zero. So with <c>SandboxBootstrap.requireSteamPower</c> on, the first boiler could
    /// never be lit -- every golem stalls <c>NoSteam</c>, and nothing that is stalled can carry
    /// fuel. The Hand-Crank Bench cleared the goods half of §10 (it can make Coke) and nothing
    /// moved that Coke the last three feet into the firebox.
    /// </para>
    /// </summary>
    public class BoilerHandRefuelTests
    {
        // --- The policy, which is where the size of a hand-load is decided --------------------

        [Test]
        public void AmountToLoad_EmptyStockpile_IsZeroRatherThanATransferOfNothing()
        {
            Assert.AreEqual(0, BoilerRefuelPolicy.AmountToLoad(0));
        }

        [Test]
        public void AmountToLoad_LessThanABatch_MovesEverythingThePlayerHas()
        {
            Assert.AreEqual(7, BoilerRefuelPolicy.AmountToLoad(7));
        }

        [Test]
        public void AmountToLoad_MoreThanABatch_IsCappedSoTwoColdBoilersCanBeSplit()
        {
            Assert.AreEqual(
                BoilerRefuelPolicy.HandLoadBatch, BoilerRefuelPolicy.AmountToLoad(500));
        }

        // A hand-load must never be a substitute for a coking line. §3.1 wants Coke to be the
        // contended throat of the game; if one press covered a factory's burn for long enough,
        // standing at the boiler would beat building the economy.
        [Test]
        public void AHandLoad_IsWorthUnderHalfAMinuteOfAFullBoiler()
        {
            float secondsForOneGolem =
                BoilerRefuelPolicy.HandLoadBatch * (SteamNetwork.TicksPerCokePerPoweredGolem / 10f);
            float secondsForAFullBoiler = secondsForOneGolem / SteamNetwork.MaxGolemsPerBoiler;

            Assert.Less(secondsForAFullBoiler, 30f,
                "a single press should not keep a full boiler running long enough to replace a coking line");
            Assert.Greater(secondsForOneGolem, 60f,
                "but it must be worth walking over for -- a press that buys under a minute is busywork");
        }

    }
}
