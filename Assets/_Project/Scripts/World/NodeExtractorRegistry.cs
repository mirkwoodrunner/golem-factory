using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.World
{
    // The two-extractor-per-node cap (docs/progression-design.md §3.2).
    //
    // "A node may be worked by at most 2 golems. Fiction: the seam collapses if over-crewed."
    // At 150 units/min per extractor that caps a node at 300/min, which is the mechanism that
    // makes stage 4's ~856 Coal/min require three separate coal seams -- so growth means
    // DISTANCE (long belt corridors and parallel steam pipe runs) rather than another golem on
    // the same tile. Without the cap, "place another extractor" answers every raw-material
    // bottleneck forever and none of §3's scarcity is real.
    //
    // ---------------------------------------------------------------------------------------
    // WHERE THE COUNT LIVES, and why not on ResourceNode.
    //
    // A crew is a relationship between a node and some golems, not a property of the deposit.
    // Putting a counter on ResourceNode would make it the node's job to know who is standing
    // where, and worse, an increment/decrement counter has to be released exactly once -- a
    // golem removed, retargeted, rotated away or reloaded from a save would leak a crew slot
    // and permanently under-crew a node with nothing to point at. So this is a registry of
    // CLAIMS that is re-derived from claimants, exactly as SteamNetwork re-derives its powered
    // set from topology rather than patching it: a stale claim is impossible because nothing is
    // ever cached across a membership change.
    //
    // Like SteamNetwork it does not know what a GolemEntity is. A claimant is a bare string id
    // plus a cell plus the node id it is working, the same bare-string-id convention belts,
    // buffers and nodes already use.
    //
    // ---------------------------------------------------------------------------------------
    // DETERMINISM. Which 2, when three or more golems face one node?
    //
    // The same discipline §1.4 established for "which 8 golems does a boiler power": claimants
    // are sorted into a TOTAL ORDER by cell (World.CellOrder), tie-broken by golem id, and the
    // first MaxExtractorsPerNode of them are working. Cell order is factory layout -- identical
    // between two identically-built factories, unchanged by a save/load, and it does not move
    // unless the player moves a golem.
    //
    // REJECTED, for the reasons SteamNetwork records at length:
    //   * Dictionary/HashSet iteration order -- not contractual, and rehashing on growth
    //     reshuffles it, so building a fourth golem elsewhere could flip which two are working.
    //   * First-come by registration or arrival tick -- two identical factories built in a
    //     different order would behave differently, and a load would crew a different two.
    //     It also rewards the player for nothing they can see.
    //   * Nearest-to-the-node -- every claimant is orthogonally adjacent by construction, so
    //     this is a four-way tie in the common case and needs the cell tiebreak underneath.
    //
    // An unstable choice here does not merely mis-order: the refused golems would swap places
    // every tick, so three golems would each run at two thirds rate and the cap would read as a
    // slowdown rather than a rule. The refusal must be stable to be legible.
    public sealed class NodeExtractorRegistry
    {
        /// <summary>§3.2: a node may be worked by at most 2 golems.</summary>
        public const int MaxExtractorsPerNode = 2;

        private readonly struct Claim
        {
            public readonly string GolemId;
            public readonly Vector2Int Cell;
            public readonly string NodeId;

            public Claim(string golemId, Vector2Int cell, string nodeId)
            {
                GolemId = golemId;
                Cell = cell;
                NodeId = nodeId;
            }
        }

        private readonly Dictionary<string, Claim> _claims = new Dictionary<string, Claim>();

        // nodeId -> the golem ids currently working it, in the stable order above. Rebuilt
        // wholesale whenever membership or a cell changes, never patched.
        private readonly Dictionary<string, List<string>> _crewsByNode =
            new Dictionary<string, List<string>>();

        private readonly List<Claim> _sortScratch = new List<Claim>();
        private bool _crewsDirty = true;

        public int ClaimCount => _claims.Count;

        /// <summary>
        /// Registers (or repositions, or re-targets) a golem as wanting to work a node.
        /// Idempotent, so a golem may call this on every Extract attempt -- which is exactly
        /// what it does -- without the registry accumulating ghosts or re-sorting per tick.
        /// </summary>
        public void RegisterExtractor(string golemId, Vector2Int cell, string nodeId)
        {
            if (string.IsNullOrEmpty(golemId) || string.IsNullOrEmpty(nodeId))
            {
                return;
            }

            Claim existing;
            if (_claims.TryGetValue(golemId, out existing) &&
                existing.Cell == cell &&
                string.Equals(existing.NodeId, nodeId, System.StringComparison.Ordinal))
            {
                return;
            }

            _claims[golemId] = new Claim(golemId, cell, nodeId);
            _crewsDirty = true;
        }

        /// <summary>
        /// Drops a golem's claim -- when it is destroyed, or when it turns to face something
        /// that is not a node. A claim it no longer wants must free the slot for the golem that
        /// was being refused, or the cap would ratchet down as the player rearranges.
        /// </summary>
        public bool UnregisterExtractor(string golemId)
        {
            if (string.IsNullOrEmpty(golemId) || !_claims.Remove(golemId))
            {
                return false;
            }

            _crewsDirty = true;
            return true;
        }

        /// <summary>
        /// Whether this golem is one of the (at most two) currently working
        /// <paramref name="nodeId"/>. A golem that never registered is not working it -- the
        /// claim IS the application, so this can never silently grant.
        /// </summary>
        public bool IsWorking(string nodeId, string golemId)
        {
            if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(golemId))
            {
                return false;
            }

            RebuildCrews();

            List<string> crew;
            return _crewsByNode.TryGetValue(nodeId, out crew) && crew.Contains(golemId);
        }

        /// <summary>How many golems are working a node right now (0..2).</summary>
        public int CrewCount(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return 0;
            }

            RebuildCrews();

            List<string> crew;
            return _crewsByNode.TryGetValue(nodeId, out crew) ? crew.Count : 0;
        }

        /// <summary>
        /// The crew of a node in the stable order, for tests and for a future §8 readout.
        /// Empty (not null) for a node nobody is working.
        /// </summary>
        public IReadOnlyList<string> CrewOf(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return System.Array.Empty<string>();
            }

            RebuildCrews();

            List<string> crew;
            return _crewsByNode.TryGetValue(nodeId, out crew) ? crew : System.Array.Empty<string>();
        }

        public void Clear()
        {
            _claims.Clear();
            _crewsByNode.Clear();
            _crewsDirty = true;
        }

        // Re-derives every node's crew from scratch. Cheap to call repeatedly -- it short-
        // circuits unless something registered, moved or left -- and re-deriving wholesale is
        // what makes a stale crew structurally impossible rather than a thing to remember.
        private void RebuildCrews()
        {
            if (!_crewsDirty)
            {
                return;
            }

            _crewsDirty = false;
            _crewsByNode.Clear();

            _sortScratch.Clear();
            foreach (Claim claim in _claims.Values)
            {
                _sortScratch.Add(claim);
            }

            // Nothing that decides who works a node is ever allowed to read _claims' iteration
            // order; the sort is the entire determinism story.
            _sortScratch.Sort(CompareClaims);

            for (int i = 0; i < _sortScratch.Count; i++)
            {
                Claim claim = _sortScratch[i];

                List<string> crew;
                if (!_crewsByNode.TryGetValue(claim.NodeId, out crew))
                {
                    crew = new List<string>(MaxExtractorsPerNode);
                    _crewsByNode[claim.NodeId] = crew;
                }

                if (crew.Count < MaxExtractorsPerNode)
                {
                    crew.Add(claim.GolemId);
                }
            }
        }

        // Cell first, golem id second. The id tiebreak is not reachable in the shipped game
        // (GridMap allows one occupant per cell) but it is what makes this a TOTAL order: a
        // comparison returning 0 for two distinct claims hands the decision straight back to
        // List.Sort's unstable introsort, which is the exact non-determinism this exists to
        // remove. Same reasoning, verbatim, as SteamNetwork.CompareConsumers.
        private static int CompareClaims(Claim a, Claim b)
        {
            int byCell = CellOrder.Compare(a.Cell, b.Cell);
            return byCell != 0 ? byCell : string.CompareOrdinal(a.GolemId, b.GolemId);
        }
    }
}
