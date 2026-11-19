using System.Collections.Generic;

namespace GolemFactory.Blueprints
{
    // The player's library of named, reusable golem programs. Patenting is free.
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

        public bool TryUseBlueprint(string blueprintId, out Blueprint blueprint) =>
            _blueprints.TryGetValue(blueprintId, out blueprint);
    }
}
