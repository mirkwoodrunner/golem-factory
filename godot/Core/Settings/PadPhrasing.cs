using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GolemFactory.Settings
{
    /// <summary>
    /// Player text with the pad in use (docs/steam-deck-design.md D7). Key names are tokens
    /// (<see cref="KeyNames"/>); what the mouse does ("click Boiler in the build menu", "drag a
    /// run") is written out, so it is reworded here into what the pad does, by phrase, the most
    /// particular first. <see cref="Leftover"/> finds a mouse or keyboard word left in a text, and
    /// a test reads every guide step, question and build-menu line through it.
    /// </summary>
    public static class PadPhrasing
    {
        // How the pad does what the mouse does (D4 to D6): L1 steps the build menu; the right
        // stick moves the cursor and R2 places at it, held to lay a run; L2 takes the wrecking
        // bar; on a screen the d-pad moves a ring, A presses what it rests on, X moves what fits,
        // and L1 and R1 change a tab or a Haul's good. The most particular phrase comes first.
        private static readonly (Regex mouse, string pad)[] Phrases =
        {
            // The Workbench: A sends the card it rests on to the next socket that takes it.
            P(@"Drag the glowing card onto the glowing socket", "Press A on the glowing card to send it to the glowing socket"),
            P(@"Drag the glowing cards", "Press A on each glowing card"),
            P(@"On the second Haul, click the arrow until it says (\w+)", "On the second Haul, press R1 until it says $1"),
            P(@"\bdrop cards below to build a cycle", "send cards below to build a cycle"),
            P(@"\bdrop a logic core\b", "send a logic core"),
            P(@"\bdrop an appendage\b", "send an appendage"),

            // The build tool.
            P(@"\b[Cc]lick ([A-Z][\w ]*?) in the build menu", "Pick $1 in the build menu with L1"),
            P(@"\bPick ([A-Z][\w ]*?) in the build menu(?! with)", "Pick $1 in the build menu with L1"),
            P(@"click the marked tile((?: [a-z][\w' ]*?)?)(?=[,.])", "aim the right stick at the marked tile$1 and press R2"),
            P(@" and DRAG along the marked run", ", then hold R2 and step the right stick along the marked run"),
            P(@"a dragged run", "a run laid that way"),
            P(@"Steam Pipe drags too\.", "Steam Pipe lays the same way: hold R2 and step the right stick."),
            P(@"Drag a pipe run", "Lay a pipe run"),
            P(@"Drag to lay a run\.", "Hold R2 and step the right stick to lay a run."),
            P(@"drag the run again", "lay the run again, holding R2,"),
            P(@"Click it to take it up", "Press R2 on it to take it up"),
            P(@"Click it again", "Press R2 on it again"),
            P(@"then click again to build", "then press R2 again to build"),

            // The Crate screen: A moves one, X what fits.
            P(@"Shift-click moves all that fits, a click moves one", "X moves all that fits, A moves one"),
            P(@"Shift-click (\w+)", "press X on $1"),

            // Management's tabs.
            P(@"open (the Assembly Line|the Ledger|Save / Load)", "open $1 with L1 or R1"),
            P(@"click Claim on ", "press A on Claim beside "),
            P(@"click Upgrade on the ", "press A on Upgrade beside the "),

            // A golem's card shows for the one the pad's cursor rests on: the cell ahead (D5.2).
            P(@"\bHover any golem\b", "Face any golem"),
            P(@"\bHover it\b", "Face it"),
            P(@"\bHovering a golem\b", "Facing a golem"),
            P(@"its badge and hover tell", "its badge and card tell"),
            P(@"is on hover only", "is on its card only"),

            // The playtest's questions about the mouse's gestures.
            P(@"did they show you what to drag\?", "did they show you which card to send?"),
            P(@"by dragging feel good\?", "by holding R2 feel good?"),
            P(@"Which chapter dragged\?", "Which chapter felt slow?"),

            // Management's lines.
            P(@"drag the chart to pan", "the right stick pans the chart"),
            P(@"and a click takes you to it", "and A on it takes you to it"),
            P(@"Click a key to change it\.", "The keys are the keyboard's; the pad's buttons stay as they are."),

            // Anything else pressed: A on it.
            P(@"\bclick\b", "press A on"),
            P(@"\bClick\b", "Press A on"),
        };

        // Interpreted, not Compiled: compiling ~30 patterns to IL cost one ~60 ms frame the first time
        // the pad was picked up (the frame watch, D8); interpreted they cost nothing to make, and the
        // strings they run over are a sentence or two.
        private static (Regex, string) P(string pattern, string pad) => (new Regex(pattern), pad);

        /// <summary>The pad's wording of <paramref name="text"/>.</summary>
        public static string Apply(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            foreach ((Regex mouse, string pad) in Phrases)
            {
                text = mouse.Replace(text, pad);
            }
            return text;
        }

        // A word only the mouse or keyboard means: a click, a drag, a named key, a key in brackets.
        private static readonly Regex MouseWords = new Regex(
            @"\b([Cc]lick\w*|[Dd]rag\w*|[Hh]over(ing)?|[Mm]ouse|[Ww]heel|Tab|Escape|Enter|Shift|Space|F\d{1,2})\b|\[(E|G|I|R)\]|\b[Pp]ress (E|G|I|R)\b",
            RegexOptions.None);

        /// <summary>The mouse or keyboard words left in <paramref name="text"/>, for the test.</summary>
        public static IEnumerable<string> Leftover(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                yield break;
            }
            foreach (Match m in MouseWords.Matches(text))
            {
                yield return m.Value;
            }
        }
    }
}
