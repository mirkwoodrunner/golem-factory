using System.Collections.Generic;
using GolemFactory.PunchCards;

namespace GolemFactory.Blueprints
{
    // A saved, reusable golem program (chassis + logic core + ordered appendages),
    // named and owned -- lets an Artificer patent a configuration once and reuse it
    // without re-dragging cards each time. Carries OwnerId from day one per the
    // multiplayer-compatible-seams convention (unity-implementation-plan.md), even
    // though v1 only ever has one LocalPlayer.
    public sealed class Blueprint
    {
        public string BlueprintId { get; }
        public string OwnerId { get; }
        public ChassisDefinition Chassis { get; }
        public LogicCoreDefinition LogicCore { get; }
        public IReadOnlyList<AppendageActionDefinition> Appendages { get; }

        public Blueprint(
            string blueprintId, string ownerId, ChassisDefinition chassis,
            LogicCoreDefinition logicCore, IReadOnlyList<AppendageActionDefinition> appendages)
        {
            BlueprintId = blueprintId;
            OwnerId = ownerId;
            Chassis = chassis;
            LogicCore = logicCore;
            Appendages = appendages;
        }
    }
}
