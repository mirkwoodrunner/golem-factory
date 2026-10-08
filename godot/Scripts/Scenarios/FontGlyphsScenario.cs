using System.Collections.Generic;
using Godot;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The project font covers every character the game is allowed to draw.
    ///
    /// <para>
    /// The rule comes from the Unity project's TMP text (root CLAUDE.md, "TMP text is Latin-1
    /// plus four baked glyphs"): game text is printable Latin-1 plus <c>→ ≥ █ ░</c>, with
    /// <c>·</c> as the separator. In Unity a missing glyph rendered as a box on one machine and
    /// re-dirtied a committed atlas on another. Godot draws from the bundled TTF directly, so the
    /// question becomes simply whether that file has every glyph -- answered here, headless,
    /// rather than by noticing a box in a screenshot.
    /// </para>
    ///
    /// <para>
    /// Also checks that the font IS the project font (<c>gui/theme/custom_font</c>), so a
    /// passing check cannot be about a file nothing uses.
    /// </para>
    /// </summary>
    public sealed class FontGlyphsScenario : IScenario
    {
        public const string FontPath = "res://fonts/LiberationSans.ttf";

        /// <summary>The four allowlisted characters above U+00FF.</summary>
        public static readonly int[] AllowlistedAboveLatin1 = { 0x2192, 0x2265, 0x2588, 0x2591 };

        private ScenarioResult _result;

        public void Begin(ScenarioRunner runner)
        {
            string configured = (string)ProjectSettings.GetSetting("gui/theme/custom_font", "");
            if (configured != FontPath)
            {
                _result = new ScenarioResult(false,
                    $"the project font is '{configured}', not {FontPath}");
                return;
            }

            var font = GD.Load<FontFile>(FontPath);
            if (font == null)
            {
                _result = new ScenarioResult(false, $"{FontPath} did not load as a font");
                return;
            }

            var missing = new List<string>();
            var wanted = new List<int>();
            for (int c = 0x20; c <= 0x7E; c++)
            {
                wanted.Add(c);
            }
            for (int c = 0xA0; c <= 0xFF; c++)
            {
                wanted.Add(c);
            }
            wanted.AddRange(AllowlistedAboveLatin1);

            foreach (int c in wanted)
            {
                if (!font.HasChar(c))
                {
                    missing.Add($"U+{c:X4}");
                }
            }

            _result = missing.Count == 0
                ? new ScenarioResult(true,
                    $"{FontPath} covers printable Latin-1 and → ≥ █ ░ ({wanted.Count} characters)")
                : new ScenarioResult(false,
                    $"{FontPath} lacks {missing.Count}: {string.Join(", ", missing)} -- add a fallback font");
        }

        public ScenarioResult? Step(double delta) => _result.Report == null ? (ScenarioResult?)null : _result;
    }
}
