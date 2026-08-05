using System.Collections.Generic;
using System.Text;

namespace GolemFactory.UI
{
    // The "diagnostic tape ticker" readout from docs/digital-design.md ("a live readout
    // tracking steam consumption and cycle speed as cards are slotted, giving immediate
    // feedback before activation").
    //
    // Deliberately a *pure, engine-free* class -- same idiom as GridCoordinateConverter /
    // YSortUtility / GolemAnimationUtility -- so the string the player reads is unit-
    // testable without a scene, and WorkbenchController stays a thin applier.
    //
    // Important scoping note: steam is NOT a simulated resource in this project and this
    // pass deliberately does not add one. Steam draw here is a *derived presentation
    // figure* computed from data that already exists (how many appendage steps are
    // slotted, and the chassis tier driving them) -- exactly the "immediate feedback
    // before activation" the design doc asks for, with no new system behind it.
    public static class WorkbenchDiagnostics
    {
        // A step's stated duration is floored at 1 tick, matching GolemEntity's own
        // treatment of durationTicks, so a 0-tick authoring mistake can't make a cycle
        // look free.
        public const int MinimumStepTicks = 1;

        // Steam drawn per slotted step, before the chassis tier multiplier. Arbitrary but
        // fixed, so the number moves predictably as the player slots cards.
        public const int SteamPerStep = 4;

        public static int ComputeCycleTicks(IEnumerable<int> stepDurationTicks)
        {
            if (stepDurationTicks == null)
            {
                return 0;
            }

            int total = 0;
            foreach (int ticks in stepDurationTicks)
            {
                total += ticks > MinimumStepTicks ? ticks : MinimumStepTicks;
            }

            return total;
        }

        public static int ComputeSteamDraw(int stepCount, int chassisTier)
        {
            if (stepCount <= 0)
            {
                return 0;
            }

            int tier = chassisTier > 1 ? chassisTier : 1;
            return stepCount * SteamPerStep * tier;
        }

        // Cycles per minute at the clock's tick rate. Returns 0 for an empty program
        // rather than dividing by zero -- an unprogrammed golem has no cycle, and the
        // ticker renders that as "--" rather than "0.0".
        public static float ComputeCyclesPerMinute(int cycleTicks, float ticksPerSecond)
        {
            if (cycleTicks <= 0 || ticksPerSecond <= 0f)
            {
                return 0f;
            }

            return ticksPerSecond * 60f / cycleTicks;
        }

        // "MainspringOverclocker" -> "Mainspring Overclocker". ScriptableObject asset names
        // are CamelCase ids; the Workbench is a player-facing screen, so it shows prose.
        // Runs of capitals ("PSIValve") and digits ("IntervalCore10") stay glued together
        // rather than exploding into single letters.
        public static string Humanize(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(assetName.Length + 4);
            for (int i = 0; i < assetName.Length; i++)
            {
                char c = assetName[i];
                if (i > 0 && NeedsSpaceBefore(assetName, i))
                {
                    sb.Append(' ');
                }
                sb.Append(c);
            }

            return sb.ToString();
        }

        // Not every definition the Workbench renders comes from an authored .asset --
        // demo bootstraps build some with ScriptableObject.CreateInstance, which leaves
        // `name` empty. Those cards used to render as a blank strip; fall back to
        // whatever the definition can describe itself as (its trigger/action type).
        public static string DisplayName(string assetName, string fallback)
        {
            string humanized = Humanize(assetName);
            return string.IsNullOrEmpty(humanized) ? Humanize(fallback) : humanized;
        }

        private static bool NeedsSpaceBefore(string s, int i)
        {
            char c = s[i];
            char prev = s[i - 1];
            if (char.IsWhiteSpace(c) || char.IsWhiteSpace(prev))
            {
                return false;
            }

            bool startsUpperWord = char.IsUpper(c) && !char.IsUpper(prev);
            bool startsDigitRun = char.IsDigit(c) && !char.IsDigit(prev);
            // "PSIValve": break before the V, i.e. the last capital of a run that is
            // followed by a lowercase letter.
            bool endsCapsRun = char.IsUpper(c) && char.IsUpper(prev)
                && i + 1 < s.Length && char.IsLower(s[i + 1]);
            return startsUpperWord || startsDigitRun || endsCapsRun;
        }

        // The tape line itself. Kept as one function returning one string so the exact
        // player-visible text is asserted in tests rather than assembled ad hoc in
        // Update().
        public static string ComposeTicker(
            string chassisName, int usedSlots, int maxSlots, string triggerName,
            int cycleTicks, int steamDraw, float cyclesPerMinute, float focus, float maxFocus)
        {
            var sb = new StringBuilder(160);
            sb.Append("CHASSIS ").Append(string.IsNullOrEmpty(chassisName) ? "-- none --" : Humanize(chassisName));
            // "SLOTS 1/0" is nonsense, and it used to be exactly what an unfitted chassis
            // produced -- the tape claimed a slotted step while the blueprint viewport
            // refused to draw any, since no chassis means no sockets. With no chassis
            // there is no denominator to report, so say so instead of inventing one.
            sb.Append("   SLOTS ");
            if (maxSlots > 0)
            {
                sb.Append(usedSlots).Append('/').Append(maxSlots);
            }
            else if (usedSlots > 0)
            {
                sb.Append(usedSlots).Append(" unfitted (no chassis)");
            }
            else
            {
                sb.Append("--");
            }
            sb.Append("   TRIGGER ").Append(string.IsNullOrEmpty(triggerName) ? "-- none --" : Humanize(triggerName));
            sb.Append("   CYCLE ");
            if (cycleTicks > 0)
            {
                sb.Append(cycleTicks).Append(cycleTicks == 1 ? " tick (" : " ticks (");
                sb.Append(cyclesPerMinute.ToString("F1")).Append("/min)");
            }
            else
            {
                sb.Append("--");
            }
            sb.Append("   STEAM ").Append(steamDraw).Append(" psi");
            sb.Append("   FOCUS ").Append(focus.ToString("F0")).Append('/').Append(maxFocus.ToString("F0"));
            return sb.ToString();
        }
    }
}
