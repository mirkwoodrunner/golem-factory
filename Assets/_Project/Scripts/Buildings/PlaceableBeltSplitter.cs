using UnityEngine;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// Marks a placed tile as a belt SPLITTER: one input, and an output to every neighbour
    /// facing away from it.
    ///
    /// <para>
    /// A marker component and nothing else, exactly as <see cref="PlaceableBelt"/> is. The
    /// simulation side belongs to <c>BeltNetwork</c>, which creates the segment and decides its
    /// outputs -- keeping registration in one place is what stops a splitter existing visually
    /// but not logically.
    /// </para>
    ///
    /// <para>
    /// <b>Why a splitter needs to exist at all.</b> A belt has one facing, so it points at
    /// exactly one cell: fan-out is geometrically impossible for a belt and there is nowhere in
    /// the belt rules to put it. A splitter is the cell that has no facing of its own, so the
    /// neighbours decide -- a belt whose tail is against it is fed by it, a belt pointing into
    /// it feeds it, and a belt running past it sideways is just a neighbour.
    /// </para>
    ///
    /// <para>
    /// MERGING never needed one: two belts pointing at the same cell already both link to it,
    /// and the receiving lane's spacing rule makes them contend for slots. The backlog recorded
    /// "no merge or splitter" as one gap; it was two, and only half of it was real.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(PlaceableBuilding))]
    [RequireComponent(typeof(PlaceableBelt))]
    public sealed class PlaceableBeltSplitter : MonoBehaviour
    {
    }
}
