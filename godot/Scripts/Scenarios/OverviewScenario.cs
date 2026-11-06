using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.Menus;
using GolemFactory.Overview;
using GolemFactory.Player;
using GolemFactory.Steam;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The factory overview (docs/menus-design.md §8), with real keys and clicks, on the
    /// workshop the guide's first six chapters build:
    /// <list type="number">
    ///   <item>After a minute of work, Tab and the Overview tab: the map has every golem, every
    ///   building and the player, and no label holds an id.</item>
    ///   <item>The pointer names a golem on the map; a click closes the screen with the camera on
    ///   it, and walking brings the camera back.</item>
    ///   <item>The graph draws what was made; the pointer reads a point; a legend row hides and
    ///   redraws its line in the same colour; the range buttons switch the time axis.</item>
    ///   <item>Boilers run dry: the map rings the stopped golems and the alerts list them, still
    ///   stopped; stoked again, each says how long it was stopped; a click on one looks at it.</item>
    /// </list>
    /// </summary>
    public sealed class OverviewScenario : StepScenario
    {
        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private ManagementScreen Management => Tree.Root.FindChild("Management", true, false) as ManagementScreen;
        private OverviewTab Tab => Management.Overview;
        private PlayerNode Player => Tree.Root.FindChild("Player", true, false) as PlayerNode;

        private TitleShowcase _showcase;
        private bool _keep = true;
        private GolemEntity _picked;
        private string _hidden;
        private int _hiddenSlot;
        private string _lookedAt;
        private Button _row;

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            Do("stage the guide's first six chapters", () =>
            {
                _showcase = TitleShowcase.Stage(Sandbox, TitleShowcase.Chapters);
                return Sandbox.Golems.Count(g => !g.IsRemoved) >= 6 ? null : "the kit built too little";
            });
            Until("a minute of work", 75, () =>
            {
                Keep();
                return Sandbox.History.BucketCount >= 6 && Sandbox.History.Goods(HistoryRange.TenMinutes).Count > 0
                    ? null : $"{Sandbox.History.BucketCount} buckets";
            });
            PlanMap();
            PlanGraph();
            PlanAlerts();
        }

        /// <summary>The showcase's upkeep, so the workshop keeps working while the scenario watches (not while it starves it).</summary>
        private void Keep()
        {
            if (_keep)
            {
                _showcase.Keep(1f / 60f);
            }
        }

        private void OpenOverview(string name)
        {
            Do(name + ": Tab", () => { Tap(Key.Tab); return null; });
            Do(name + ": click Overview", () =>
            {
                if (!Management.IsOpen)
                {
                    return "Tab didn't open Management";
                }
                Click(Management.TabButtons[ManagementTab.Overview]);
                return null;
            });
            Until(name + ": the overview is drawn", 1, () =>
                Management.TabContent[ManagementTab.Overview].Visible && Tab.Map.Model != null ? null : "not showing");
        }

        // --- The map --------------------------------------------------------------------------

        private void PlanMap()
        {
            OpenOverview("open");
            Do("the map has everything", () =>
            {
                FactoryMap map = Tab.Map.Model;
                List<GolemEntity> golems = Sandbox.Golems.Where(g => g != null && !g.IsRemoved).ToList();
                int marked = map.Marks.Count(m => m.Kind == MapMarkKind.Golem);
                int built = Sandbox.Build.Buildings.Count(b => !b.IsRemoved);
                if (marked != golems.Count || map.Marks.Count(m => m.Kind == MapMarkKind.Player) != 1)
                {
                    return $"{marked} golems marked of {golems.Count}";
                }
                if (map.Marks.Count(m => m.Kind is MapMarkKind.Belt or MapMarkKind.Crate or MapMarkKind.Steam or MapMarkKind.Machine or MapMarkKind.Station) < built)
                {
                    return "a building is missing from the map";
                }
                string raw = map.Marks.Select(m => PlayerText.RawIdIn(m.Label)).FirstOrDefault(r => r != null);
                if (raw != null)
                {
                    return "a label holds the id " + raw;
                }
                if (Tab.Summary != map.Summary || !Tab.Summary.StartsWith(golems.Count + " golems"))
                {
                    return "the summary reads " + Tab.Summary;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "overview");
                return null;
            });
            Do("point at a golem", () =>
            {
                _picked = Sandbox.Golems.First(g => g != null && !g.IsRemoved && Tab.Map.Model.MarkAt(g.Cell)?.GolemId == g.GolemId);
                PointAt(Tab.Map.GlobalCentreOf(_picked.Cell));
                return null;
            });
            Do("the map names it", () =>
                Tab.Map.HoverText.StartsWith(GolemNames.Short(_picked)) ? null : $"the pointer reads '{Tab.Map.HoverText}'");
            Do("click it", () => { Click(Tab.Map.GlobalCentreOf(_picked.Cell)); return null; });
            Do("the screen closed, the camera on it", () =>
            {
                if (Management.IsOpen)
                {
                    return "Management is still open";
                }
                return Sandbox.Camera.Focus == CameraFocus.Alert && Sandbox.Camera.AlertGolemId == _picked.GolemId
                    ? null : $"camera on {Sandbox.Camera.Focus} {Sandbox.Camera.AlertGolemId}";
            });
            Wait(1.0);
            Do("capture, then walk", () =>
            {
                ScenarioCapture.Shot(Runner.GetViewport(), "overview-look");
                Press(Key.D, true);
                return null;
            });
            Wait(0.3);
            Do("walking brings the camera back", () =>
            {
                Press(Key.D, false);
                Log($"the map: {Tab.Map.Model.Summary}; clicked {GolemNames.Short(_picked)}, the camera went to it");
                return Sandbox.Camera.Focus == CameraFocus.Player ? null : "the camera stayed on " + Sandbox.Camera.Focus;
            });
        }

        // --- The graph ------------------------------------------------------------------------

        private void PlanGraph()
        {
            OpenOverview("again");
            Do("the graph draws what was made", () =>
            {
                List<string> shown = Tab.Chart.Shown.ToList();
                if (shown.Count == 0 || shown.Count > ProductionChart.MaxLines)
                {
                    return shown.Count + " lines";
                }
                IReadOnlyList<ChartRow> rows = Tab.Chart.Rows();
                if (Tab.LegendRows.Count != rows.Count)
                {
                    return $"{Tab.LegendRows.Count} legend rows for {rows.Count} goods";
                }
                PointAt(Tab.Graph.GlobalPointAt(ProductionHistory.Points - 1));
                return null;
            });
            Do("the pointer reads the newest point", () =>
            {
                IReadOnlyList<string> lines = Tab.Graph.HoverLines;
                if (lines.Count != Tab.Chart.Shown.Count() + 1 || lines[0] != "now" || lines.Skip(1).Any(l => !l.Contains(" a min")))
                {
                    return "the pointer reads " + string.Join(" / ", lines);
                }
                _hidden = Tab.Chart.Shown.First();
                _hiddenSlot = Tab.Chart.SlotOf(_hidden);
                Click(Tab.LegendRows[_hidden]);
                return null;
            });
            Do("its line is hidden", () =>
            {
                if (Tab.Chart.SlotOf(_hidden) != -1)
                {
                    return "still drawn";
                }
                Click(Tab.LegendRows[_hidden]);
                return null;
            });
            Do("and back in its colour", () =>
                Tab.Chart.SlotOf(_hidden) == _hiddenSlot ? null : $"slot {Tab.Chart.SlotOf(_hidden)}, was {_hiddenSlot}");
            Do("click 1 hour", () => { Click(Tab.RangeButtons[HistoryRange.Hour]); return null; });
            Do("the hour", () =>
            {
                if (Tab.Chart.Range != HistoryRange.Hour || Tab.Chart.SlotOf(_hidden) != _hiddenSlot)
                {
                    return $"range {Tab.Chart.Range}, slot {Tab.Chart.SlotOf(_hidden)}";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "overview-hour");
                Click(Tab.RangeButtons[HistoryRange.TenMinutes]);
                return null;
            });
            Do("ten minutes again", () =>
            {
                if (Tab.Chart.Range != HistoryRange.TenMinutes)
                {
                    return "range " + Tab.Chart.Range;
                }
                Log($"the graph: {string.Join(", ", Tab.Chart.Shown.Select(Economy.ItemTiers.DisplayName))}; a legend click hid and redrew a line in its colour; 1 hour and 10 min");
                Tap(Key.Escape);
                return null;
            });
        }

        // --- The alerts -----------------------------------------------------------------------

        private void PlanAlerts()
        {
            Do("the boilers run dry", () =>
            {
                _keep = false;
                foreach (PlaceableBuilding b in Sandbox.Build.Buildings.Where(b => !b.IsRemoved))
                {
                    b.GetPart<PlaceableBoiler>()?.Boiler?.SetCoke(0);
                }
                return null;
            });
            Until("golems stop", 6, () => Sandbox.Golems.Any(g => !g.IsRemoved && g.Mood == GolemMood.Starved) ? null : "none starved yet");
            Wait(1.0);
            OpenOverview("alerts");
            Do("the map rings them, the alerts list them", () =>
            {
                int stopped = Tab.Map.Model.Marks.Count(m => m.Stopped);
                int open = Sandbox.Alerts.Log.Entries.Count(e => e.Open && e.NoSteam);
                if (stopped == 0 || open != stopped)
                {
                    return $"{stopped} ringed, {open} open steam alerts";
                }
                Button first = Tab.AlertRows.FirstOrDefault();
                string when = first?.GetNode<Label>("When").Text ?? "";
                if (!when.Contains("out of steam · still stopped"))
                {
                    return "the newest alert reads " + when;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "overview-alerts");
                Tap(Key.Escape);
                return null;
            });
            Do("stoke them", () =>
            {
                _keep = true;
                Keep();
                foreach (PlaceableBuilding b in Sandbox.Build.Buildings.Where(b => !b.IsRemoved))
                {
                    SteamBoiler boiler = b.GetPart<PlaceableBoiler>()?.Boiler;
                    boiler?.AddCoke(boiler.CokeRoom);
                }
                return null;
            });
            Until("they run again", 6, () =>
            {
                Keep();
                return Sandbox.Alerts.Log.Entries.Any(e => e.Open && e.NoSteam) ? "still out of steam" : null;
            });
            OpenOverview("after");
            Do("each says how long", () =>
            {
                // A finished steam stop; stoking may start a fresh one while the pressure builds.
                Button steam = Tab.AlertRows.FirstOrDefault(r => r.GetNode<Label>("When").Text.Contains("out of steam · stopped for"));
                string when = steam?.GetNode<Label>("When").Text ?? "";
                if (!when.Contains("stopped for "))
                {
                    return "a steam alert reads " + when;
                }
                _lookedAt = Sandbox.Alerts.Log.Entries[Tab.AlertRows.IndexOf(steam)].GolemId;
                _row = steam;
                // Scrolled to, as a player would: the list holds more than it shows.
                (steam.GetParent().GetParent() as ScrollContainer)?.EnsureControlVisible(steam);
                return null;
            });
            Do("click it", () =>
            {
                Rect2 list = ((Control)_row.GetParent().GetParent()).GetGlobalRect();
                if (!list.HasPoint(_row.GetGlobalRect().GetCenter()))
                {
                    return "the row is still out of sight";
                }
                Click(_row);
                return null;
            });
            Do("a click looks at it", () =>
            {
                if (Management.IsOpen)
                {
                    return "Management is still open";
                }
                if (Sandbox.Camera.AlertGolemId != _lookedAt)
                {
                    return "the camera is on " + Sandbox.Camera.AlertGolemId;
                }
                Log($"alerts: {Sandbox.Alerts.Log.Entries.Count(e => e.NoSteam)} steam stops listed, ringed on the map, timed when they ran again; a click looked at one");
                return null;
            });
        }
    }
}
