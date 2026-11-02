using System.Collections.Generic;
using System.Linq;
using GolemFactory.Audio;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Audio
{
    // docs/audio-design.md §7: what the director asks to play.
    public class AudioDirectorTests
    {
        private AudioDirector _director;

        [SetUp]
        public void SetUp() => _director = new AudioDirector(AuthoredData.Load().AudioSlots);

        [TearDown]
        public void TearDown() => _director.Detach();

        // --- Data ------------------------------------------------------------------------

        [Test]
        public void EverySlotTheCodeAsksFor_IsInTheData()
        {
            var asked = new List<string>
            {
                AudioRules.Workshop, AudioRules.ClockTick, AudioRules.Steam, AudioRules.CardSnap,
                AudioRules.Engage, AudioRules.Patent, AudioRules.Claim, AudioRules.StallChime,
                AudioRules.Place, AudioRules.Demolish, AudioRules.Collect, AudioRules.TowerFanfare,
            };
            asked.AddRange(Enumerable.Range(1, AudioRules.MusicLayerCount).Select(AudioRules.MusicLayer));
            foreach (GolemWorkMotion.Motion motion in System.Enum.GetValues(typeof(GolemWorkMotion.Motion)))
            {
                if (motion != GolemWorkMotion.Motion.None)
                {
                    asked.Add(AudioDirector.WorkSlot(motion));
                }
            }
            foreach (string slot in asked)
            {
                Assert.IsTrue(_director.Slots.ContainsKey(slot), slot);
            }
            Assert.AreEqual(asked.Count, _director.Slots.Count, "a slot in audio.json that nothing plays");
        }

        [Test]
        public void EverySlot_HasItsFileUnderAudio_AndTheLoopsAreTheAmbienceAndMusic()
        {
            foreach (AudioSlotDefinition slot in _director.Slots.Values)
            {
                Assert.AreEqual("res://audio/" + slot.name + ".wav", slot.path, slot.name);
                bool shouldLoop = slot.name == AudioRules.Workshop || slot.name == AudioRules.Steam
                    || slot.name.StartsWith("music.layer.");
                Assert.AreEqual(shouldLoop, slot.loop, slot.name);
            }
        }

        [Test]
        public void WorkSounds_ArePositional_AndTheStallChimeIsNot()
        {
            Assert.IsTrue(_director.Slots["work.stamp"].positional);
            // A red golem off screen still needs the player: heard wherever the camera is.
            Assert.IsFalse(_director.Slots[AudioRules.StallChime].positional);
        }

        // --- The copy cap and the off-screen cut (invariant 5) -------------------------------

        [Test]
        public void TenPressersStampingAtOnce_RequestOnlyMaxCopies()
        {
            int cap = _director.Slots["work.stamp"].maxCopies;
            int played = Enumerable.Range(0, 10).Count(i => _director.Play("work.stamp", new Vector2(i, 0f)));
            Assert.AreEqual(cap, played);
            Assert.AreEqual(cap, _director.Drain().Count);
        }

        [Test]
        public void ACopyFinishing_FreesItsPlace()
        {
            _director.SetLength(AudioRules.CardSnap, 0.2f);
            Assert.IsTrue(_director.Play(AudioRules.CardSnap));
            Assert.IsFalse(_director.Play(AudioRules.CardSnap), "one copy at a time");
            _director.Update(0.25f, null, null, 0);
            Assert.IsTrue(_director.Play(AudioRules.CardSnap));
        }

        [Test]
        public void APositionalSoundFarOffScreen_IsDropped_AndOneInViewPlays()
        {
            _director.SetView(new Vector2(0f, 0f), new Vector2(10f, 6f));
            Assert.IsFalse(_director.Play("work.stamp", new Vector2(40f, 0f)));
            Assert.IsTrue(_director.Play("work.stamp", new Vector2(3f, 2f)));
            AudioRequest request = _director.Drain().Single();
            Assert.IsTrue(request.HasPosition);
            Assert.AreEqual(new Vector2(3f, 2f), request.Position);
        }

        [Test]
        public void ALoopOrAnUnknownSlot_IsNeverQueued()
        {
            Assert.IsFalse(_director.Play(AudioRules.Workshop));
            Assert.IsFalse(_director.Play("no.such.slot"));
            Assert.AreEqual(0, _director.Pending.Count);
        }

        [Test]
        public void WhileSuppressed_NothingIsQueued()
        {
            _director.Suppressed = true;
            Assert.IsFalse(_director.Play(AudioRules.Place));
            _director.Suppressed = false;
            Assert.IsTrue(_director.Play(AudioRules.Place));
        }

        // --- Work sounds follow the simulation (invariant 3) -------------------------------

        [Test]
        public void APresser_StampsOncePerStroke()
        {
            // 40 ticks of Assemble is five 8-tick strokes: five stamps, one per stroke, however
            // many frames each takes.
            GolemEntity golem = Assembler(40);
            var golems = new List<GolemEntity> { golem };
            for (int tick = 0; tick < 40; tick++)
            {
                golem.Tick(tick);
                for (int frame = 0; frame < 3; frame++)
                {
                    _director.Update(1f / 60f, null, golems, 0);
                }
            }
            Assert.AreEqual(5, _director.Requested("work.stamp"));
        }

        [Test]
        public void APausedClock_RequestsNoWorkSound()
        {
            GolemEntity golem = Assembler(40);
            var golems = new List<GolemEntity> { golem };
            for (int tick = 0; tick < 12; tick++)
            {
                golem.Tick(tick);
                _director.Update(0.1f, null, golems, 0);
            }
            int before = _director.Requested("work.stamp");
            // Paused: no tick runs, frames keep coming.
            for (int frame = 0; frame < 600; frame++)
            {
                _director.Update(1f / 60f, null, golems, 0);
            }
            Assert.AreEqual(before, _director.Requested("work.stamp"));
        }

        [Test]
        public void TheWorkSound_StandsWhereItsGolemDoes()
        {
            GolemEntity golem = Assembler(40);
            golem.SetPlacement(new Vector2Int(3, -2), Facing.East);
            var golems = new List<GolemEntity> { golem };
            for (int tick = 0; tick < 8; tick++)
            {
                golem.Tick(tick);
                _director.Update(0.1f, null, golems, 0);
            }
            AudioRequest stamp = _director.Drain().First(r => r.Slot == "work.stamp");
            Assert.AreEqual(new Vector2(3f, -2f), stamp.Position);
        }

        // --- The stall chime (invariant 4) ------------------------------------------------

        [Test]
        public void ARedStall_ChimesOnce_HoweverOftenItRetries()
        {
            // An Assemble with nothing to assemble: stalled, red (no facts say anyone will feed it).
            GolemEntity golem = Assembler(40, stocked: false);
            var golems = new List<GolemEntity> { golem };
            for (int tick = 0; tick < 50; tick++)
            {
                golem.Tick(tick);
                _director.Update(0.1f, null, golems, 0);
            }
            Assert.AreEqual(GolemMood.Stalled, golem.Mood);
            Assert.AreEqual(1, _director.Requested(AudioRules.StallChime));
        }

        [Test]
        public void AStallCleared_AndHitAgain_ChimesAgain()
        {
            GolemEntity golem = Assembler(4, stocked: false);
            var golems = new List<GolemEntity> { golem };
            int tick = 0;
            void Run(int ticks)
            {
                for (int i = 0; i < ticks; i++, tick++)
                {
                    golem.Tick(tick);
                    _director.Update(3f, null, golems, 0); // long frames: the chime's copy is done
                }
            }

            Run(3);
            golem.Inventory.AddInput(ItemType.Scrap, 1); // fed: it runs, and the stall is over
            Run(2);
            Assert.AreEqual(GolemState.Running, golem.Program.State);
            Run(20); // used up, red again
            Assert.AreEqual(2, _director.Requested(AudioRules.StallChime));
        }

        [Test]
        public void ANoSteamStall_IsBlue_AndDoesNotChime()
        {
            GolemEntity golem = Assembler(40);
            var steam = new GolemFactory.Steam.SteamNetwork();
            golem.ConfigureSteam(steam); // no boiler anywhere: every step waits on steam
            var golems = new List<GolemEntity> { golem };
            for (int tick = 0; tick < 20; tick++)
            {
                golem.Tick(tick);
                _director.Update(0.1f, null, golems, 0);
            }
            Assert.AreEqual(GolemMood.Starved, golem.Mood);
            Assert.AreEqual(0, _director.Requested(AudioRules.StallChime));
        }

        // --- The clock, the loops and the tower ---------------------------------------------

        [Test]
        public void TheClock_TicksOnceAGameSecond_AndNotWhilePaused()
        {
            var clock = new SimulationClock { TicksPerSecond = 10f };
            clock.Play();
            for (int frame = 0; frame < 300; frame++) // 5 s at 60 fps
            {
                clock.Advance(1f / 60f);
                _director.Update(1f / 60f, clock, null, 0);
            }
            int running = _director.Requested(AudioRules.ClockTick);
            Assert.AreEqual(5, running, 1);

            clock.Pause();
            for (int frame = 0; frame < 300; frame++)
            {
                clock.Advance(1f / 60f);
                _director.Update(1f / 60f, clock, null, 0);
            }
            Assert.AreEqual(running, _director.Requested(AudioRules.ClockTick));
        }

        [Test]
        public void TheLoopsLevels_FollowTheBoilersAndTheFactorysSize()
        {
            var golems = Enumerable.Range(0, 16).Select(_ => new GolemEntity()).ToList();
            _director.Update(0.1f, null, golems, 6);
            Assert.AreEqual(AudioRules.SteamHissVolume(6), _director.SteamHiss);
            Assert.AreEqual(3, _director.MusicLayers);

            golems.RemoveAt(0);
            _director.Update(0.1f, null, golems, 0);
            Assert.AreEqual(0f, _director.SteamHiss);
            Assert.AreEqual(3, _director.MusicLayers, "one golem wrecked doesn't drop a layer");
        }

        [Test]
        public void ATowerStage_PlaysTheFanfare()
        {
            _director.Attach();
            EventBus.Publish(new ClockTowerStageCompletedEvent(1, "Frame", false, 0));
            Assert.AreEqual(1, _director.Requested(AudioRules.TowerFanfare));
            _director.Detach();
            EventBus.Publish(new ClockTowerStageCompletedEvent(1, "Frame", false, 0));
            Assert.AreEqual(1, _director.Requested(AudioRules.TowerFanfare));
        }

        // --- Fixtures -----------------------------------------------------------------------

        /// <summary>A golem whose one step is an Assemble of <paramref name="ticks"/>: a stamping Presser.</summary>
        private static GolemEntity Assembler(int ticks, bool stocked = true)
        {
            var recipe = new RecipeDefinition { outputItemType = "IronPlate", outputQuantity = 1, durationTicks = ticks };
            recipe.inputs.Add(new RecipeIngredient(ItemType.Scrap, 1));
            var step = new AppendageActionDefinition { actionType = AppendageActionType.Assemble, recipe = recipe };

            var golem = new GolemEntity();
            golem.Configure("Presser", null);
            golem.Program.logicCore = new LogicCoreDefinition { triggerType = TriggerType.AlwaysOn };
            golem.Program.appendages.Add(step);
            if (stocked)
            {
                golem.Inventory.AddInput(ItemType.Scrap, 6);
            }
            return golem;
        }
    }
}
