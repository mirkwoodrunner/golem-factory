using System.Collections.Generic;
using GolemFactory.Economy;
using GolemFactory.Golems;

namespace GolemFactory.UI
{
    /// <summary>
    /// What the player calls a golem: <c>"Scavenger #4"</c>, the chassis's short name and the
    /// number in its <see cref="GolemEntity.GolemId"/>. The id stays the golem's identity (saves,
    /// guide roles, events, lookups; root CLAUDE.md: "a golem is named by its GolemId"); this
    /// is only how it is printed. The number comes from the id, so it is unique across chassis,
    /// survives a save and load, and never renumbers when another golem is dismantled.
    ///
    /// <para>
    /// The one naming path for player text: the badge-free places that name a golem (the
    /// alerts strip, the hover card's title, the Workbench header, the [E] prompt and the
    /// carry/turn messages) all read it, so a golem has one name on every screen
    /// (Phase 2 critique, docs/onboarding-feedback-design.md §16).
    /// </para>
    /// </summary>
    public static class GolemNames
    {
        /// <summary>The prefix the construction station gives every id it hands out.</summary>
        public const string IdPrefix = "PlayerGolem-";

        // The guide's, the roster's and the docs' word for each chassis. A chassis not listed
        // reads as the last word of its display name.
        private static readonly Dictionary<string, string> ShortChassis = new Dictionary<string, string>
        {
            { "ClockworkScavenger", "Scavenger" },
            { "BrassPresser", "Presser" },
            { "AetherHauler", "Hauler" },
            { "MainspringOverclocker", "Overclocker" },
            { "ZeppelinFreightLoader", "Zeppelin" },
        };

        /// <summary>"Scavenger #4"; "Golem #4" with no chassis fitted.</summary>
        public static string Short(GolemEntity golem)
        {
            if (golem == null)
            {
                return "A golem";
            }
            return Join(ChassisShortName(golem.Program?.chassis?.name), golem);
        }

        /// <summary>"Clockwork Scavenger #4": the hover card's title.</summary>
        public static string Full(GolemEntity golem)
        {
            if (golem == null)
            {
                return "A golem";
            }
            string chassis = golem.Program?.chassis != null ? ItemTiers.DisplayName(golem.Program.chassis.name) : "Golem";
            return Join(chassis, golem);
        }

        /// <summary>A chassis's short name: "Scavenger", "Zeppelin"; "Golem" for none.</summary>
        public static string ChassisShortName(string chassisName)
        {
            if (string.IsNullOrEmpty(chassisName))
            {
                return "Golem";
            }
            if (ShortChassis.TryGetValue(chassisName, out string known))
            {
                return known;
            }
            string display = ItemTiers.DisplayName(chassisName);
            int space = display.LastIndexOf(' ');
            return space >= 0 ? display.Substring(space + 1) : display;
        }

        /// <summary>The number in a station id ("PlayerGolem-007" → 7), or -1.</summary>
        public static int Number(string golemId) =>
            golemId != null && golemId.StartsWith(IdPrefix, System.StringComparison.Ordinal) &&
            int.TryParse(golemId.Substring(IdPrefix.Length), out int number) && number >= 0
                ? number
                : -1;

        private static string Join(string chassis, GolemEntity golem)
        {
            int number = Number(golem.GolemId);
            if (number >= 0)
            {
                return chassis + " #" + number;
            }
            // An id the station didn't hand out (a demo's "GolemD", a bare test): the id itself,
            // or the object's name when there is no id at all, rather than going blank.
            string id = !string.IsNullOrEmpty(golem.GolemId) ? golem.GolemId : golem.name;
            return string.IsNullOrEmpty(id) ? chassis : chassis + " " + id;
        }
    }
}
