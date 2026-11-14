using System.Linq;
using Godot;
using GolemFactory.Golems;
using GolemFactory.World;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// Grey for waiting, red for stuck, on steam with real golems and crates
    /// (docs/onboarding-feedback-design.md §8, `moods`): a golem the factory will restart is
    /// grey, quiet and uncounted; one only the player can restart is red, counted, and its [E]
    /// prompt says why. The street is <see cref="StreetFixture"/>'s.
    /// </summary>
    public sealed class MoodsScenario : StreetFixture
    {
        protected override void Plan()
        {
            GolemNode p = null;
            Do("find P", () => (p = NodeOf(P)) != null ? null : "no node for P");

            // 1. Waiting: grey after the dwell, a "waiting for Scrap" badge, no shake, uncounted.
            float maxShake = 0f;
            Until("P waits, grey", 4.0, () =>
            {
                maxShake = Mathf.Max(maxShake, Mathf.Abs(p.DrawnOffset.X));
                return Is(p, GolemMood.Waiting);
            });
            Wait(0.6);
            Do("P reads waiting", () =>
            {
                if (maxShake > 0.01f)
                {
                    return $"P shook ({maxShake:0.00} px) on starting to wait";
                }
                if (!p.BadgeVisible || p.BadgeText != "waiting for Scrap")
                {
                    return $"badge visible={p.BadgeVisible} '{p.BadgeText}'";
                }
                if (p.DrawnModulate.R > 0.95f || Mathf.Abs(p.DrawnModulate.R - p.DrawnModulate.G) > 0.05f)
                {
                    return $"tint {p.DrawnModulate} isn't grey";
                }
                if (World.Alerts.Tracker.IsStalled(P.GolemId))
                {
                    return "the strip counts a waiting golem";
                }
                Log.Add($"P waiting: badge '{p.BadgeText}', tint grey, no shake");
                return null;
            });
            Until("the strip: nobody needs you", 1.0, () =>
                World.Alerts.Text == "No golem needs you · 1 waiting for goods." ? null : $"strip '{World.Alerts.Text}'");
            Do("log the strip", () => { Log.Add($"strip '{World.Alerts.Text}'"); return null; });

            // The collecting Haul (§13): two Scrap of a batch of five, held while it waits.
            Do("P's batch to 5, two Scrap into its crate", () =>
            {
                P.Program.SetQuantityAt(0, 5);
                Feed(CrateAt(PCrate), Economy.ItemType.Scrap, 2);
                return null;
            });
            Until("P collects: 2/5 Scrap, grey", 4.0, () =>
                Is(p, GolemMood.Waiting) ?? (p.BadgeVisible && p.BadgeText == "2/5 Scrap" ? null : $"badge '{p.BadgeText}' ({P.StallReason} {P.StallShortfall})"));
            Do("log P collecting", () => { Log.Add($"P collecting: badge '{p.BadgeText}', crate empty"); return null; });

            // 3, 4 and 6: N faces nothing, F pushes into a full crate, X has no steam.
            // A golem that has just STOPPED jolts once, where one that starts waiting did not:
            // its view reads the golem's own count of stalls (GolemEntity.StallIncidents).
            float stopShake = 0f;
            Do("program N, F and X", () => { ProgramTheStuck(); return null; });
            Until("N, F red; X blue", 2.0, () =>
            {
                stopShake = Mathf.Max(stopShake, Mathf.Abs(NodeOf(N).DrawnOffset.X));
                return Is(NodeOf(N), GolemMood.Stalled) ?? Is(NodeOf(F), GolemMood.Stalled) ?? Is(NodeOf(X), GolemMood.Starved)
                    ?? (N.StallReason == Events.StallReason.NoSourceAtTile ? null : "N stalled " + N.StallReason)
                    ?? (F.StallReason == Events.StallReason.BeltFull ? null : "F stalled " + F.StallReason);
            });
            Until("N jolts as it stops", 1.0, () =>
            {
                stopShake = Mathf.Max(stopShake, Mathf.Abs(NodeOf(N).DrawnOffset.X));
                return stopShake > 0.01f ? null : $"N stopped without its jolt ({N.StallIncidents} stalls counted)";
            });
            Do("log N's jolt", () => { Log.Add($"N stopped with a jolt ({stopShake:0.00} px)"); return null; });
            Do("the strip counts the red and blue only", () =>
            {
                World.Alerts.Reconcile();
                int count = World.Alerts.Tracker.Count;
                bool countsP = World.Alerts.Tracker.IsStalled(P.GolemId);
                Log.Add($"strip '{World.Alerts.Text}' ({count} stopped, {World.Alerts.WaitingCount} waiting)");
                return count == 3 && !countsP && World.Alerts.WaitingCount >= 1 ? null : $"{count} counted, P counted {countsP}";
            });

            // Phase 2 critique: every badge, the strip and the prompt in player names, and no
            // badge over a neighbour's. F (red) and H (violet, unprogrammed) stand two cells
            // apart in a row, as a guide line's golems do.
            Until("badges and strip in player names, none overlapping", 3.0, () =>
            {
                if (!NodeOf(H).BadgeVisible || !NodeOf(F).BadgeVisible)
                {
                    return "F's and H's badges aren't both up yet";
                }
                var shown = Tree.GetNodesInGroup(GolemNodeGroup.Name).OfType<GolemNode>().Where(n => n.BadgeVisible).ToList();
                foreach (GolemNode node in shown)
                {
                    string raw = UI.PlayerText.RawIdIn(node.BadgeText);
                    if (raw != null)
                    {
                        return $"{node.GolemId}'s badge '{node.BadgeText}' shows the id '{raw}'";
                    }
                }
                string stripRaw = UI.PlayerText.RawIdIn(World.Alerts.Text);
                if (stripRaw != null)
                {
                    return $"the strip '{World.Alerts.Text}' shows the id '{stripRaw}'";
                }
                for (int i = 0; i < shown.Count; i++)
                {
                    for (int j = i + 1; j < shown.Count; j++)
                    {
                        if (shown[i].BadgeRect.Intersects(shown[j].BadgeRect))
                        {
                            return $"'{shown[i].BadgeText}' {shown[i].BadgeRect} overlaps '{shown[j].BadgeText}' {shown[j].BadgeRect}";
                        }
                    }
                }
                Log.Add($"{shown.Count} badges in player names, none overlapping ('{NodeOf(F).BadgeText}', '{NodeOf(H).BadgeText}'); strip '{World.Alerts.Text}'");
                return null;
            });
            Do("capture the red street", () => { ScenarioCapture.Shot(Viewport, "critique-moods-red"); return null; });

            // 7. Straining: no badge, ever.
            int strainingFrames = 0;
            string badge = null;
            Do("top W up to 12 Coal", () => { W.Inventory.AddInput(Economy.ItemType.Coal, 12 - W.Inventory.GetInput(Economy.ItemType.Coal)); return null; });
            Until("W strains for 3 s with no badge", 3.0, () =>
            {
                GolemNode w = NodeOf(W);
                if (W.Mood == GolemMood.Straining)
                {
                    strainingFrames++;
                    badge ??= w.BadgeVisible ? w.BadgeText : null;
                }
                return "watching";
            }, mayTimeOut: true);
            Do("W never wore a badge", () =>
            {
                if (strainingFrames < 60)
                {
                    return $"W strained only {strainingFrames} frames";
                }
                Log.Add($"W straining {strainingFrames} frames at {W.Inventory.FullestTypeUnits}/12, no badge");
                return badge == null ? null : "a straining golem wore a badge: " + badge;
            });

            // 4b. A golem set to haul from F's full crate makes F grey.
            Do("program H to haul from F's crate", () =>
            {
                Program(H, 3, -1, ("HaulScrap", 1), ("PushOutput", 0));
                return F.Mood == GolemMood.Waiting ? null : $"F reads {F.Mood} with H hauling from its crate";
            });

            // 2. Carry S away (G): P turns red at once, and is counted.
            Do("stand by S", () => { Stand(-8f, 2f); return null; });
            Do("press G", () => { Tap(Key.G); return null; });
            Until("P red at once", 1.0, () =>
            {
                if (World.Interactor.CarriedGolem != S)
                {
                    return "G didn't pick S up (" + World.Interactor.LastStatusMessage + ")";
                }
                return Is(p, GolemMood.Stalled);
            });
            Until("the strip counts P", 1.0, () => World.Alerts.Tracker.IsStalled(P.GolemId) ? null : "not counted: " + World.Alerts.Text);
            Do("press G to set S down", () => { Tap(Key.G); return null; });

            // The [E] prompt at a stuck golem carries its problem (§11 question 5).
            Do("stand by N", () => { Stand(-3f, -2f); return null; });
            Until("N's prompt names its problem", 1.0, () =>
            {
                string prompt = World.Interactor.CurrentPrompt;
                if (!prompt.Contains("Stuck: nothing to take from at (-4,-1), behind it"))
                {
                    return $"prompt '{prompt}'";
                }
                if (UI.PlayerText.RawIdIn(prompt) is string raw)
                {
                    return $"prompt '{prompt}' shows the id '{raw}'";
                }
                Log.Add($"N's prompt '{prompt}'");
                return null;
            });

            // 5. A Coal cart, just added and empty: a golem extracting there waits, grey, because
            // the cart refills itself (docs/supply-design.md §4).
            Do("an extractor at a new, empty Coal cart", () =>
            {
                Compat.Vector2Int spot = World.Setup.HomeSpot(Economy.ItemType.Coal).Value;
                Supply.SupplyResult added = World.GrantCart(spot, Economy.ItemType.Coal);
                if (!added.Done)
                {
                    return "coal cart: " + added.Message;
                }
                // F moves there, rewritten as an extractor. No pipe reaches the cart, so it is
                // taken off the steam grid: this step is about the cart, not the boiler.
                Compat.Vector2Int at = new Compat.Vector2Int(spot.x, spot.y + 1);
                Data.DefinitionSet defs = World.Definitions;
                F.Inventory.Clear();
                GolemProgramWriter.TryWrite(F.Program, null, defs.LogicCores["AlwaysOnCore"],
                    new[] { defs.Appendages["ExtractScrap"], defs.Appendages["PushOutput"] },
                    new[] { "", "" }, new[] { 1, 1 });
                F.SetPlacement(at, Facing.North);
                F.ConfigureSteam(null);
                return null;
            });
            Until("F grey at the empty cart, waiting for it to refill", 2.0, () =>
            {
                if (F.StallReason != Events.StallReason.NodeEmpty || F.Mood != GolemMood.Waiting)
                {
                    return $"{F.Mood} {F.StallReason}";
                }
                Log.Add("Coal cart: empty and refilling, so its extractor waits grey");
                return null;
            });
        }
    }
}
