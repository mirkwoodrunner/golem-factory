using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.UI
{
    /// <summary>The hover card's four lines; any may be "".</summary>
    public readonly struct HoverCard
    {
        public string Title { get; }
        public string State { get; }
        public string Fix { get; }
        public string Route { get; }

        public HoverCard(string title, string state, string fix, string route)
        {
            Title = title;
            State = state;
            Fix = fix;
            Route = route;
        }

        /// <summary>The non-empty lines, one per line.</summary>
        public string Text
        {
            get
            {
                var text = new System.Text.StringBuilder();
                foreach (string line in new[] { Title, State, Fix, Route })
                {
                    if (!string.IsNullOrEmpty(line))
                    {
                        if (text.Length > 0)
                        {
                            text.Append('\n');
                        }
                        text.Append(line);
                    }
                }
                return text.ToString();
            }
        }
    }

    /// <summary>
    /// Every sentence the hover card says about a golem (docs/onboarding-feedback-design.md
    /// §4.3), composed in Core so a test reads exactly what the player will. A red golem's card
    /// names the tile, the good and the fix; a grey one says what it waits for.
    /// </summary>
    public static class GolemHoverText
    {
        /// <param name="golem">The hovered golem.</param>
        /// <param name="mood">The mood as drawn (<c>GolemMoodRules.Presented</c>).</param>
        /// <param name="facts">Its stall facts, default when it isn't stalled.</param>
        /// <param name="tiles">Names the tiles behind and in front of it.</param>
        /// <param name="strainingHeld">
        /// <see cref="NearlyFull"/> has held for its dwell (§4.4): a working or sleeping golem's
        /// card then reads as straining.
        /// </param>
        public static HoverCard Compose(GolemEntity golem, GolemMood mood, StallFacts facts, TileNames tiles,
            bool strainingHeld = false)
        {
            if (golem == null)
            {
                return default;
            }
            if (strainingHeld && (mood == GolemMood.Working || mood == GolemMood.Sleeping))
            {
                mood = GolemMood.Straining;
            }

            string chassis = golem.Program.chassis != null ? ItemTiers.DisplayName(golem.Program.chassis.name) : "Golem";
            string title = chassis + " · " + golem.GolemId;
            string source = tiles != null ? tiles.Name(golem.SourceCell) : TileNames.Cell(golem.SourceCell);
            string target = tiles != null ? tiles.Name(golem.TargetCell) : TileNames.Cell(golem.TargetCell);
            string route = "From " + source + " → to " + target;

            switch (mood)
            {
                case GolemMood.Stalled:
                    return new HoverCard(title, "Stuck: " + Stuck(golem, facts, source, target, tiles),
                        Fix(golem, facts), route);
                case GolemMood.Starved:
                    return new HoverCard(title,
                        "No steam: " + StallDiagnostics.DescribeShort(StallReason.NoSteam, golem.StallResourceId, 0, golem.SteamShortage),
                        SteamFix(golem.SteamShortage), route);
                case GolemMood.Waiting:
                    return new HoverCard(title, Waiting(golem, facts, source, target), "", route);
                case GolemMood.Unprogrammed:
                    return new HoverCard(title, "No program · press E at it to open the Workbench", "", route);
                case GolemMood.Sleeping:
                    return new HoverCard(title, "Sleeping until its trigger: " + Trigger(golem.Program.logicCore), "", route);
                default:
                    return new HoverCard(title, Working(golem, mood == GolemMood.Straining && strainingHeld), "", route);
            }
        }

        /// <summary>
        /// A running golem's hold is nearly full. The view times it over the HOLD, not the mood:
        /// an AlwaysOn golem is Idle for a tick at the end of every cycle, which would restart a
        /// mood stopwatch each cycle and keep "nearly full" off the card forever.
        /// </summary>
        public static bool NearlyFull(GolemEntity golem) =>
            golem.Inventory.FullestTypeUnits >= GolemMoodRules.StrainingUnits &&
            golem.Program.State != GolemState.Stalled;

        private static string Working(GolemEntity golem, bool straining)
        {
            // Straining reads the fullest good in EITHER stock (GolemInventory.FullestTypeUnits),
            // so the line names which: the output Push drains, or the hold an Assemble draws on.
            GolemInventory inventory = golem.Inventory;
            bool output = inventory.Output.FullestTypeUnits >= inventory.Input.FullestTypeUnits;
            GolemInventory.Stock stock = output ? inventory.Output : inventory.Input;
            if (straining && stock.FullestType != null)
            {
                return "Working · " + (output || !golem.Program.HasAssembleStep ? "output " : "hold ") +
                       stock.FullestTypeUnits + "/" + GolemInventory.CapacityPerType + " " +
                       Good(stock.FullestType) + ", nearly full";
            }
            int index = golem.Program.CurrentStepIndex;
            AppendageActionDefinition step = golem.Program.CurrentStep;
            return "Working · step " + (index + 1) + " of " + golem.Program.appendages.Count +
                   (step != null ? ", " + step.Label : "");
        }

        private static string Trigger(LogicCoreDefinition core)
        {
            const string prefix = "trigger · ";
            string subtitle = WorkbenchSession.CardSubtitle(core, null);
            return subtitle.StartsWith(prefix) ? subtitle.Substring(prefix.Length) : subtitle;
        }

        private static string Waiting(GolemEntity golem, StallFacts facts, string source, string target)
        {
            string good = WantedGood(golem, facts);
            string head = string.IsNullOrEmpty(good) ? "Waiting" : "Waiting for " + Good(good);
            if (golem.TryGetHaulProgress(out _, out int held, out int batch))
            {
                head += " · " + held + "/" + batch;
            }
            string why = golem.StallReason == StallReason.BeltFull ? target + " is full"
                : facts.TruckloadOnTheWay ? "a truckload is on its way"
                : source + " is empty";
            return head + " · " + why;
        }

        private static string Stuck(GolemEntity golem, StallFacts facts, string source, string target, TileNames tiles)
        {
            string good = WantedGood(golem, facts);
            string resource = golem.StallResourceId;
            switch (golem.StallReason)
            {
                case StallReason.BufferEmpty:
                case StallReason.BeltEmpty:
                    return Empty(source, good);
                case StallReason.MissingItem:
                    if (!IsTake(golem))
                    {
                        return "needs " + golem.StallShortfall + " more " + Good(resource) + " than its Hauls bring";
                    }
                    if (golem.TryGetHaulProgress(out _, out int held, out int batch))
                    {
                        return Empty(source, good) + " (" + held + "/" + batch + ")";
                    }
                    return source + " has no " + Good(good);
                case StallReason.NodeEmpty:
                    return source + " is empty";
                case StallReason.BeltFull:
                {
                    string full = golem.PushStock.FullestType;
                    return string.IsNullOrEmpty(full) ? target + " is full" : target + " is full of " + Good(full);
                }
                case StallReason.NoSourceAtTile:
                    return "nothing to take from at " + TileNames.Cell(golem.SourceCell) + ", behind it";
                case StallReason.NoTargetAtTile:
                    return resource == "no Freight Mast"
                        ? "no Freight Mast to fly to"
                        : "nothing to give to at " + TileNames.Cell(golem.TargetCell) + ", in front of it";
                case StallReason.FilterMismatch:
                {
                    string label = tiles?.Label(golem.TargetCell);
                    return string.IsNullOrEmpty(label)
                        ? target + " won't take " + Good(resource)
                        : target + " takes only " + Good(label) + "; it carries " + Good(resource);
                }
                case StallReason.InputFull:
                    return "its hold is full of " + Good(resource) + ", and no step uses it";
                case StallReason.OutputFull:
                    return "its output is full of " + Good(resource) + "; " + target + " won't take it";
                case StallReason.NodeCrowded:
                    return source + " already has 2 golems";
                default:
                    return "its program can't run";
            }
        }

        private static string Empty(string source, string good) =>
            string.IsNullOrEmpty(good) ? source + " is empty" : source + " is empty, needs " + Good(good);

        /// <summary>The fix column of §3.3, one sentence, for a stuck golem.</summary>
        public static string Fix(GolemEntity golem, StallFacts facts)
        {
            string good = Good(WantedGood(golem, facts));
            switch (golem.StallReason)
            {
                case StallReason.NodeEmpty:
                    return "Order a truckload: E at the stall.";
                case StallReason.BeltFull:
                    return "Collect it with E, or stand a golem that hauls from it.";
                case StallReason.BufferEmpty:
                case StallReason.BeltEmpty:
                    return FillFix(good);
                case StallReason.MissingItem:
                    return IsTake(golem) ? FillFix(good) : "Raise the Haul's batch, then ENGAGE again.";
                case StallReason.NoSourceAtTile:
                    return "Turn it (R) or move it (G).";
                case StallReason.NoTargetAtTile:
                    return "Turn it (R), or build a crate there.";
                case StallReason.InputFull:
                    return "Change its program (E).";
                case StallReason.OutputFull:
                    return "Give " + Good(golem.StallResourceId) + " a crate, a heap or a carrier.";
                case StallReason.NodeCrowded:
                    return "Move it to another stall (G).";
                case StallReason.FilterMismatch:
                    return "Relabel the crate (I), or send " + Good(golem.StallResourceId) + " elsewhere.";
                default:
                    return "Reprogram it at the Workbench (E).";
            }
        }

        private static string FillFix(string good) =>
            "Nothing fills it. Put " + (string.IsNullOrEmpty(good) ? "goods" : good) +
            " in by hand (I), or stand a golem that pushes into it.";

        private static string SteamFix(SteamShortage shortage)
        {
            switch (shortage)
            {
                case SteamShortage.BoilerOutOfCoke: return "Fuel its boiler with Coke (E at the boiler).";
                case SteamShortage.BoilerAtCapacity: return "Build another boiler: this one already powers 8 golems.";
                default: return "Lay a steam pipe to it from a lit boiler.";
            }
        }

        private static bool IsTake(GolemEntity golem) => StallFacts.IsTakeAction(golem.Program.CurrentStep?.actionType);

        private static string WantedGood(GolemEntity golem, StallFacts facts) =>
            facts.WantedGood ?? (golem.Program.State == GolemState.Stalled
                ? StallFactsProbe.WantedGood(golem, golem.Program.CurrentStep, null)
                : null);

        private static string Good(string itemType) =>
            string.IsNullOrEmpty(itemType) ? "" : ItemTiers.DisplayName(itemType);
    }
}
