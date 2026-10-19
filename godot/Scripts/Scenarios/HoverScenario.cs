using System.Linq;
using Godot;
using GolemFactory.Golems;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// Mouse hover over golems (docs/onboarding-feedback-design.md §8, `hover`), with the real
    /// pointer warped onto the street <see cref="StreetFixture"/> builds: the golem under the
    /// pointer (or the cell above its feet) lights its source and target tiles with arrows at z
    /// −1, and after a moment its card names what it is doing -- for a stuck one, the tile, the
    /// good and the fix -- while its badge steps aside. Nothing is hovered over the build menu,
    /// over a full screen, or with the wrecking bar up.
    /// </summary>
    public sealed class HoverScenario : StreetFixture
    {
        private GolemHoverNode _hover;

        protected override void Plan()
        {
            Do("find the hover node", () =>
                (_hover = Tree.Root.FindChild("GolemHover", true, false) as GolemHoverNode) != null ? null : "no GolemHover node in Sandbox.tscn");
            Do("stand away from the street; program N, F and X", () => { Stand(-2f, 6f); ProgramTheStuck(); return null; });
            Wait(2.5);

            // 1. The golem's cell, then the cell above it: hovered both times, tiles and arrows at z −1.
            Do("point at P's cell", () => { PointAtCell(P.Cell); return null; });
            Until("P hovered, tiles lit", 0.5, () => Hovered(P));
            Do("point at the cell above P's feet", () => { PointAtCell(new Compat.Vector2Int(P.Cell.x, P.Cell.y + 1)); return null; });
            Until("P still hovered", 0.5, () => Hovered(P));

            // 2. The card's lines are Core's.
            Until("P's card is Core's", 1.0, () =>
            {
                string expected = World.HoverCard(P, NodeOf(P).DrawnMood).Text;
                if (!_hover.CardVisible || _hover.CardText != expected)
                {
                    return $"card visible={_hover.CardVisible} '{_hover.CardText}', wanted '{expected}'";
                }
                Log.Add($"P's card '{_hover.CardText.Replace('\n', '|')}'");
                return null;
            });

            // 3. A stuck golem behind an empty, unfed crate (S, asleep -- so make it run).
            Do("wake S", () => { S.Program.logicCore = World.Definitions.LogicCores["AlwaysOnCore"]; return null; });
            Do("point at S", () => { PointAtCell(S.Cell); return null; });
            Until("S's card names the tile, the good and the fix", 2.0, () =>
            {
                GolemNode s = NodeOf(S);
                string text = _hover.CardText;
                if (!_hover.CardVisible || !text.Contains("Stuck: Crate (-9,1) is empty, needs Scrap") ||
                    !text.Contains("Nothing fills it. Put Scrap in by hand (I)"))
                {
                    return $"card '{text}' ({S.Mood} {S.StallReason})";
                }
                if (s.BadgeVisible)
                {
                    return "S's badge shows under its card";
                }
                Log.Add($"S's card '{text.Replace('\n', '|')}', badge hidden");
                return null;
            });

            // 4. A straining golem: "nearly full" on the card, no badge.
            Do("top W up and point at it", () =>
            {
                W.Inventory.AddInput(Economy.ItemType.Coal, 12 - W.Inventory.GetInput(Economy.ItemType.Coal));
                PointAtCell(W.Cell);
                return null;
            });
            Until("W's card says nearly full", 4.0, () =>
            {
                if (NodeOf(W).BadgeVisible)
                {
                    return "W wears a badge";
                }
                return _hover.CardText.Contains("nearly full") ? null : $"card '{_hover.CardText}' ({W.Mood})";
            });
            Do("log W", () => { Log.Add($"W's state '{_hover.CardText.Split('\n')[1]}', no badge"); return null; });

            // 5. Over the build menu, then with the Workbench open: nothing hovered.
            Do("point at the build menu", () =>
            {
                BuildMenuNode menu = Tree.Root.FindChild("BuildMenu", true, false) as BuildMenuNode;
                PointAt(menu.Panel.GetGlobalRect().GetCenter());
                return null;
            });
            Until("nothing hovered over the menu", 0.5, () => NothingHovered());
            Do("point at P, open the Workbench", () =>
            {
                PointAtCell(P.Cell);
                World.Workbench.RetargetGolem(P);
                (World.WorkbenchScreen as WorkbenchScreen)?.Open();
                return null;
            });
            Until("nothing hovered over the Workbench", 0.5, () => NothingHovered());
            Do("close the Workbench", () => { (World.WorkbenchScreen as WorkbenchScreen)?.Close(); return null; });
            Until("P hovered again", 0.5, () => Hovered(P));

            // 6. Carrying a golem: its tiles follow the landing; hovering another shows that one's too.
            Do("stand by N", () => { Stand(-3f, -2f); return null; });
            Do("press G", () => { Tap(Key.G); return null; });
            Until("N carried, its tiles on the landing; P's tiles too", 1.0, () =>
            {

                if (World.Interactor.CarriedGolem != N)
                {
                    return "not carrying N: " + World.Interactor.LastStatusMessage;
                }
                GolemNode n = NodeOf(N);
                if (!n.TilesVisible || n.SourceTileCell != FacingUtility.SourceCell(World.Interactor.CarryDropCell.Value, N.Facing))
                {
                    return "N's tiles aren't on its landing";
                }
                int lit = Tree.GetNodesInGroup(GolemNodeGroup.Name).OfType<GolemNode>().Count(g => g.TilesVisible);
                if (lit > 3)
                {
                    return lit + " golems show tiles";
                }
                string hovered = Hovered(P);
                if (hovered == null)
                {
                    Log.Add($"carrying N: tiles on its landing, P's under the pointer, {lit} golems lit");
                }
                return hovered;
            });
            Do("set N down", () => { Tap(Key.G); return null; });
            Do("hover P for the frames", () => { PointAtCell(P.Cell); return null; });
            Wait(0.5);
        }

        private string Hovered(GolemEntity golem)
        {
            GolemNode node = NodeOf(golem);
            if (_hover.Hovered != golem)
            {
                return $"hovered {_hover.Hovered?.GolemId ?? "nothing"} (pointer cell {World.Inspector.PointerCell?.ToString() ?? "none"}, control {Viewport.GuiGetHoveredControl()?.Name})";
            }
            if (!node.TilesVisible || !node.TileArrowsVisible)
            {
                return "its tiles or arrows are hidden";
            }
            if (node.TileZ != -1)
            {
                return "its tiles are at z " + node.TileZ;
            }
            return null;
        }

        private string NothingHovered() =>
            _hover.Hovered == null && !_hover.CardVisible ? null : $"hovered {_hover.Hovered?.GolemId}, card {_hover.CardVisible}";
    }
}
