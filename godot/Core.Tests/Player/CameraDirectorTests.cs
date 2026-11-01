using System.Collections.Generic;
using GolemFactory.Player;
using NUnit.Framework;

namespace GolemFactory.Tests.Player
{
    // docs/game-feel-design.md §2–§3: zoom levels, the pan to alerts, the Clock Tower's event.
    public class CameraDirectorTests
    {
        private CameraDirector _camera;

        [SetUp]
        public void SetUp() => _camera = new CameraDirector();

        // --- Zoom levels (decision 3) -----------------------------------------------------------

        [Test]
        public void TheLevels_RunCloseToWide_AndStartAtNormal()
        {
            CollectionAssert.AreEqual(new[] { 3f, 5.625f, 10f, 15f }, CameraDirector.ZoomLevels);
            Assert.AreEqual(CameraRigRules.DefaultOrthographicSize, _camera.TargetSize);
            Assert.AreEqual(CameraRigRules.MinOrthographicSize, CameraDirector.ZoomLevels[0]);
            Assert.AreEqual(CameraRigRules.MaxOrthographicSize, CameraDirector.ZoomLevels[CameraDirector.ZoomLevels.Length - 1]);
        }

        [Test]
        public void ANotch_StepsOneLevel_AndStopsAtTheEnds()
        {
            Assert.IsTrue(_camera.ZoomStep(+1));
            Assert.AreEqual(5.625f, _camera.TargetSize);
            Assert.IsTrue(_camera.ZoomStep(+1));
            Assert.IsFalse(_camera.ZoomStep(+1), "Close is the end");
            Assert.AreEqual(3f, _camera.TargetSize);
            _camera.ZoomStep(-1);
            _camera.ZoomStep(-1);
            _camera.ZoomStep(-1);
            Assert.IsFalse(_camera.ZoomStep(-1), "Wide is the end");
            Assert.AreEqual(15f, _camera.TargetSize);
        }

        [Test]
        public void TheZoom_GlidesToItsLevel_WithoutOvershooting()
        {
            _camera.ZoomStep(-1);
            float size = 10f;
            float last = size;
            for (int frame = 0; frame < 120; frame++)
            {
                size = _camera.GlideZoom(size, 1f / 60f);
                Assert.GreaterOrEqual(size, last);
                Assert.LessOrEqual(size, 15f);
                last = size;
            }
            Assert.AreEqual(15f, size, 0.01f, "there within two seconds");
        }

        [Test]
        public void ASizeSetDirectly_SnapsToTheNearestLevel()
        {
            _camera.SnapZoomTo(4.5f);
            Assert.AreEqual(5.625f, _camera.TargetSize);
            _camera.SnapZoomTo(13f);
            Assert.AreEqual(15f, _camera.TargetSize);
        }

        // --- The pan to alerts (decision 2) ---------------------------------------------------------

        [Test]
        public void AClick_GoesToTheWorstStoppedGolem_ThenTheNext_AndWrapsRound()
        {
            var stopped = new List<string> { "G2", "G7", "G3" };
            Assert.AreEqual("G2", _camera.NextAlert(stopped));
            Assert.AreEqual(CameraFocus.Alert, _camera.Focus);
            Assert.AreEqual("G7", _camera.NextAlert(stopped));
            Assert.AreEqual("G3", _camera.NextAlert(stopped));
            Assert.AreEqual("G2", _camera.NextAlert(stopped));
        }

        [Test]
        public void AClick_WithNothingStopped_LeavesTheCameraOnThePlayer()
        {
            Assert.IsNull(_camera.NextAlert(new List<string>()));
            Assert.AreEqual(CameraFocus.Player, _camera.Focus);
        }

        [Test]
        public void TheGolemLookedAtRecovering_TheNextClickStartsAtTheWorst()
        {
            _camera.NextAlert(new List<string> { "G2", "G7" });
            _camera.NextAlert(new List<string> { "G2", "G7" });
            Assert.AreEqual("G4", _camera.NextAlert(new List<string> { "G4", "G2" }), "G7 is running again");
        }

        [Test]
        public void Walking_BringsTheCameraBack()
        {
            _camera.NextAlert(new List<string> { "G2" });
            _camera.PlayerMoved();
            Assert.AreEqual(CameraFocus.Player, _camera.Focus);
            Assert.IsNull(_camera.AlertGolemId);
        }

        [Test]
        public void TheCamera_GlidesSlowerToAFocus_ThanItFollowsThePlayer()
        {
            Assert.AreEqual(CameraRigRules.FollowLerpSpeed, _camera.GlideSpeed);
            _camera.NextAlert(new List<string> { "G2" });
            Assert.Less(_camera.GlideSpeed, CameraRigRules.FollowLerpSpeed);
        }

        // --- The Clock Tower's event (decision 4) -----------------------------------------------

        [Test]
        public void TheTowerEvent_GlidesIn_RevealsOnce_AndGlidesBack_InAboutFourSeconds()
        {
            _camera.BeginTowerEvent(2, "The Movement");
            Assert.AreEqual(TowerEventPhase.GlideIn, _camera.TowerPhase);
            Assert.AreEqual(CameraFocus.TowerEvent, _camera.Focus);

            int strikes = 0;
            float seconds = 0f;
            var phases = new List<TowerEventPhase>();
            while (_camera.TowerPhase != TowerEventPhase.None && seconds < 10f)
            {
                if (_camera.UpdateTowerEvent(1f / 60f))
                {
                    strikes++;
                    Assert.AreEqual(TowerEventPhase.Reveal, _camera.TowerPhase, "the stage appears with the camera at the tower");
                }
                if (phases.Count == 0 || phases[phases.Count - 1] != _camera.TowerPhase)
                {
                    phases.Add(_camera.TowerPhase);
                }
                seconds += 1f / 60f;
            }
            Assert.AreEqual(1, strikes);
            CollectionAssert.AreEqual(new[] { TowerEventPhase.GlideIn, TowerEventPhase.Reveal, TowerEventPhase.GlideBack, TowerEventPhase.None }, phases);
            Assert.AreEqual(4.4f, seconds, 0.1f);
            Assert.IsTrue(_camera.TowerRevealed);
            Assert.AreEqual(CameraFocus.Player, _camera.Focus);
        }

        [Test]
        public void AnyKey_SkipsTheEvent_WithTheStageShown()
        {
            _camera.BeginTowerEvent(1, "Foundation");
            _camera.UpdateTowerEvent(0.3f);
            _camera.PlayerMoved(); // walking, or any key, during the glide-in
            Assert.IsTrue(_camera.TowerRevealed);
            Assert.AreEqual(CameraFocus.Player, _camera.Focus);
            Assert.AreEqual(TowerEventPhase.GlideBack, _camera.TowerPhase);
            _camera.UpdateTowerEvent(1f);
            Assert.AreEqual(TowerEventPhase.None, _camera.TowerPhase);
        }

        [Test]
        public void DuringTheEvent_TheAlertStripIsIgnored()
        {
            _camera.BeginTowerEvent(1, "Foundation");
            Assert.IsNull(_camera.NextAlert(new List<string> { "G2" }));
            Assert.AreEqual(CameraFocus.TowerEvent, _camera.Focus);
        }

        [Test]
        public void TheBanner_NamesTheStage_AndNoId()
        {
            Assert.AreEqual("Stage 2 complete · The Movement", TowerBanner.Text(2, "The Movement"));
            Assert.AreEqual("Clock Tower: stage 3 complete", TowerBanner.Text(3, null));
        }
    }
}
