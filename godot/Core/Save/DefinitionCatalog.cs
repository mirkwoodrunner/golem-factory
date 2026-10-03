using System.Collections.Generic;
using GolemFactory.PunchCards;

namespace GolemFactory.Save
{
    // Resolves ScriptableObject asset names back to references at load time -- JSON can't
    // serialize an object reference, only a name. Built from whatever roster the caller
    // already has on hand (e.g. WorkbenchController's availableChassis/LogicCores/
    // Appendages) rather than an AssetDatabase search, so it works identically in a
    // build as in the Editor.
    public sealed class DefinitionCatalog
    {
        private readonly Dictionary<string, ChassisDefinition> _chassis = new Dictionary<string, ChassisDefinition>();
        private readonly Dictionary<string, LogicCoreDefinition> _logicCores = new Dictionary<string, LogicCoreDefinition>();
        private readonly Dictionary<string, AppendageActionDefinition> _appendages = new Dictionary<string, AppendageActionDefinition>();

        public DefinitionCatalog(
            IEnumerable<ChassisDefinition> chassisRoster,
            IEnumerable<LogicCoreDefinition> logicCoreRoster,
            IEnumerable<AppendageActionDefinition> appendageRoster)
        {
            foreach (ChassisDefinition c in chassisRoster)
            {
                if (c != null)
                {
                    _chassis[c.name] = c;
                }
            }
            foreach (LogicCoreDefinition l in logicCoreRoster)
            {
                if (l != null)
                {
                    _logicCores[l.name] = l;
                }
            }
            foreach (AppendageActionDefinition a in appendageRoster)
            {
                if (a != null)
                {
                    _appendages[a.name] = a;
                }
            }
        }

        public ChassisDefinition FindChassis(string assetName) =>
            !string.IsNullOrEmpty(assetName) && _chassis.TryGetValue(assetName, out ChassisDefinition c) ? c : null;

        public LogicCoreDefinition FindLogicCore(string assetName) =>
            !string.IsNullOrEmpty(assetName) && _logicCores.TryGetValue(assetName, out LogicCoreDefinition l) ? l : null;

        public AppendageActionDefinition FindAppendage(string assetName) =>
            !string.IsNullOrEmpty(assetName) && _appendages.TryGetValue(assetName, out AppendageActionDefinition a) ? a : null;
    }
}
