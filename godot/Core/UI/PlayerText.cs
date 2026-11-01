using System.Text.RegularExpressions;

namespace GolemFactory.UI
{
    /// <summary>
    /// The check that player text holds no internal id (Phase 2 critique): a golem id
    /// (<c>PlayerGolem-004</c>), a cell-keyed id (<c>Crate(2,-1)</c>, <c>Belt(0,-12)#3</c>), a
    /// node, buffer or belt id (<c>ScrapNode</c>, <c>ScrapBuffer</c>, <c>ScrapBeltA</c>), or any
    /// camel-cased definition name (<c>IronPlate</c>, <c>ClockworkScavenger</c>). Names are
    /// <see cref="GolemNames"/>, <see cref="TileNames"/> and <c>ItemTiers.DisplayName</c>; a
    /// scenario runs every drawn label through <see cref="RawIdIn"/>.
    /// </summary>
    public static class PlayerText
    {
        private static readonly Regex RawId = new Regex(
            @"PlayerGolem-\d" +                 // a golem id
            @"|[A-Za-z]\(-?\d" +                // Crate(2,-1), Belt(0,-12)#3, Mast(9,1): no space before the cell
            @"|[A-Za-z]Node\b" +                // ScrapNode
            @"|[A-Za-z]Buffer\b" +              // ScrapBuffer, IronPlateBuffer
            @"|[a-z]Belt[A-Z]?\b" +             // ScrapBeltA
            @"|\b[A-Z][a-z]+[A-Z][A-Za-z]*\b",  // IronPlate, PneumaticPost, ClockworkScavenger
            RegexOptions.CultureInvariant);

        /// <summary>The first internal id in <paramref name="text"/>, or null when it reads clean.</summary>
        public static string RawIdIn(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            Match match = RawId.Match(text);
            return match.Success ? match.Value : null;
        }
    }
}
