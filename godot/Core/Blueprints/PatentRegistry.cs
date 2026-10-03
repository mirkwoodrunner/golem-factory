using System.Collections.Generic;

namespace GolemFactory.Blueprints
{
    // Real single-player QoL (named/reusable saved programs) architected as the
    // multiplayer royalty seam from day one, per unity-implementation-plan.md's
    // "multiplayer-compatible seams" section: TryUseBlueprint already has the
    // royalty-charge branch, no-op'd when userId == the blueprint's OwnerId (there's no
    // other player's wallet to pay into in solo v1, so the branch is a documented no-op
    // rather than wired to a real payment -- that's a multiplayer-only concern).
    public sealed class PatentRegistry
    {
        private readonly Dictionary<string, Blueprint> _blueprints = new Dictionary<string, Blueprint>();
        public IReadOnlyDictionary<string, Blueprint> Blueprints => _blueprints;

        public bool TryPatent(Blueprint blueprint)
        {
            if (blueprint == null || _blueprints.ContainsKey(blueprint.BlueprintId))
            {
                return false;
            }

            _blueprints[blueprint.BlueprintId] = blueprint;
            return true;
        }

        public bool TryUseBlueprint(string blueprintId, string userId, out Blueprint blueprint)
        {
            if (!_blueprints.TryGetValue(blueprintId, out blueprint))
            {
                return false;
            }

            if (userId != blueprint.OwnerId)
            {
                // Multiplayer royalty charge would go here (paying the patent's owner);
                // no-op in solo v1 since there's no other player to pay.
            }

            return true;
        }
    }
}
