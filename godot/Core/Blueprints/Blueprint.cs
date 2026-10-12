using System.Collections.Generic;
using GolemFactory.PunchCards;
using GolemFactory.UI;

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

        /// <summary>Each slot's hauled good (G10), or null for a patent from before there was one.</summary>
        public IReadOnlyList<string> ItemTypes { get; }

        /// <summary>Each slot's batch size, or null for a patent from before there was one.</summary>
        public IReadOnlyList<int> Quantities { get; }

        public Blueprint(
            string blueprintId, string ownerId, ChassisDefinition chassis,
            LogicCoreDefinition logicCore, IReadOnlyList<AppendageActionDefinition> appendages,
            IReadOnlyList<string> itemTypes = null, IReadOnlyList<int> quantities = null)
        {
            ItemTypes = itemTypes;
            Quantities = quantities;
            BlueprintId = blueprintId;
            OwnerId = ownerId;
            Chassis = chassis;
            LogicCore = logicCore;
            Appendages = appendages;
        }

        /// <summary>
        /// The good step <paramref name="index"/> works: its saved one, or for a patent from
        /// before goods were saved, the card's own.
        /// </summary>
        public string ItemTypeAt(int index)
        {
            AppendageActionDefinition card = Appendages[index];
            return card == null ? ""
                : ItemTypes != null && index < ItemTypes.Count ? ItemTypes[index] : card.inputItemType ?? "";
        }

        /// <summary>Step <paramref name="index"/>'s batch size, clamped; the card's own for an older patent.</summary>
        public int QuantityAt(int index)
        {
            AppendageActionDefinition card = Appendages[index];
            return card == null ? WorkbenchQuantityPolicy.MinQuantity
                : Quantities != null && index < Quantities.Count ? WorkbenchQuantityPolicy.Clamp(Quantities[index])
                : WorkbenchQuantityPolicy.Clamp(card.haulQuantity);
        }
    }
}
