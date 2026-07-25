using System.Collections.Generic;
using UnityEngine;
using GolemFactory.PunchCards;

namespace GolemFactory.AssemblyLine
{
    // M9 demo: seeds the Assembly Line with a DraftableCardDefinition for every
    // M3-authored Chassis/LogicCore/Appendage asset, built at runtime the same way
    // HardcodedDemoProgram builds its programs -- no pre-authored .asset files needed
    // for a set of cards this mechanical (a thin wrapper plus a cost).
    public sealed class AssemblyLineDemoBootstrap : MonoBehaviour
    {
        [SerializeField] private AssemblyLineStateHolder lineHolder;
        [SerializeField] private ChassisDefinition[] chassisRoster = new ChassisDefinition[0];
        [SerializeField] private LogicCoreDefinition[] logicCoreRoster = new LogicCoreDefinition[0];
        [SerializeField] private AppendageActionDefinition[] appendageRoster = new AppendageActionDefinition[0];

        private void Start()
        {
            var cards = new List<DraftableCardDefinition>();

            foreach (ChassisDefinition chassis in chassisRoster)
            {
                var card = ScriptableObject.CreateInstance<DraftableCardDefinition>();
                card.chassis = chassis;
                card.baseCost = 20 + chassis.tier * 15;
                cards.Add(card);
            }
            foreach (LogicCoreDefinition logicCore in logicCoreRoster)
            {
                var card = ScriptableObject.CreateInstance<DraftableCardDefinition>();
                card.logicCore = logicCore;
                cards.Add(card);
            }
            foreach (AppendageActionDefinition appendage in appendageRoster)
            {
                var card = ScriptableObject.CreateInstance<DraftableCardDefinition>();
                card.appendage = appendage;
                cards.Add(card);
            }

            lineHolder.State.SeedCandidates(cards);
        }
    }
}
