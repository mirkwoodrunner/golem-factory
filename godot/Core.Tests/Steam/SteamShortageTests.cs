using GolemFactory.Compat;
using GolemFactory.Events;
using GolemFactory.Steam;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.Steam
{
    /// <summary>
    /// Why a golem has no steam (G10, from playtest: a player whose pipe ran to the golem was
    /// told only "no steam at (2, -4)" while the boiler sat empty). Three causes, three fixes.
    /// </summary>
    public class SteamShortageTests
    {
        // A boiler at (0,0), a pipe at (1,0); a golem at (2,0) stands beside the pipe.
        private static SteamNetwork Network(int coke)
        {
            var network = new SteamNetwork();
            network.RegisterBoiler("B1", new Vector2Int(0, 0), coke);
            network.AddPipe(new Vector2Int(1, 0));
            return network;
        }

        [Test]
        public void ABoilerWithNoCoke_IsTheCause_NotThePipe()
        {
            SteamNetwork network = Network(coke: 0);
            network.RegisterConsumer("G", new Vector2Int(2, 0));

            Assert.AreEqual(SteamShortage.BoilerOutOfCoke, network.Diagnose("G", 1));
        }

        [Test]
        public void NoPipeReachingTheTile_IsTheCause()
        {
            SteamNetwork network = Network(coke: 10);
            network.RegisterConsumer("G", new Vector2Int(5, 5));

            Assert.AreEqual(SteamShortage.NoPipe, network.Diagnose("G", 1));
        }

        [Test]
        public void AFullBoiler_IsTheCause()
        {
            SteamNetwork network = Network(coke: 100);
            network.AddPipe(new Vector2Int(1, 1));
            network.AddPipe(new Vector2Int(1, 2));
            network.AddPipe(new Vector2Int(1, 3));
            // Nine golems, every one beside the boiler or a pipe; the boiler powers eight.
            Vector2Int[] cells =
            {
                new Vector2Int(2, 0), new Vector2Int(2, 1), new Vector2Int(2, 2), new Vector2Int(2, 3),
                new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(0, 3),
                new Vector2Int(1, 4), new Vector2Int(-1, 0),
            };
            for (int i = 0; i < cells.Length; i++)
            {
                network.RegisterConsumer("G" + i, cells[i]);
            }

            int unpowered = 0;
            for (int i = 0; i < SteamNetwork.MaxGolemsPerBoiler + 1; i++)
            {
                SteamShortage why = network.Diagnose("G" + i, 1);
                if (why != SteamShortage.None)
                {
                    unpowered++;
                    Assert.AreEqual(SteamShortage.BoilerAtCapacity, why, "G" + i);
                }
            }
            Assert.AreEqual(1, unpowered);
        }

        [Test]
        public void APoweredGolem_HasNoShortage()
        {
            SteamNetwork network = Network(coke: 10);
            network.RegisterConsumer("G", new Vector2Int(2, 0));

            Assert.AreEqual(SteamShortage.None, network.Diagnose("G", 1));
        }

        [Test]
        public void TheBadgeAndTheStrip_SayTheCause()
        {
            Assert.AreEqual("my boiler is out of Coke",
                StallDiagnostics.DescribeShort(StallReason.NoSteam, "(2, -4)", 0, SteamShortage.BoilerOutOfCoke));
            Assert.AreEqual("no steam pipe reaches me",
                StallDiagnostics.DescribeShort(StallReason.NoSteam, "(2, -4)", 0, SteamShortage.NoPipe));
            StringAssert.Contains("out of Coke",
                StallDiagnostics.ComposeStripText(1, new StallSnapshot("PlayerGolem-001", StallReason.NoSteam, "(2, -4)", 0, SteamShortage.BoilerOutOfCoke)));
            Assert.AreEqual("no steam at (2, -4)",
                StallDiagnostics.DescribeShort(StallReason.NoSteam, "(2, -4)"), "with no cause known, the old line");
        }
    }
}
