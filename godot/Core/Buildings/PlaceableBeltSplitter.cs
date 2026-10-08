namespace GolemFactory.Buildings
{
    /// <summary>
    /// Marks a belt as a SPLITTER. Its presence is the whole of it: build mode places a
    /// splitter through the identical BeltNetwork call with one flag, because it IS a belt as
    /// far as the lane, the capacity and the handoff pass are concerned -- only its outputs
    /// differ. A building with this part also carries a <see cref="PlaceableBelt"/>, as Unity's
    /// [RequireComponent] demanded. Ported from Unity's sibling component (G2b).
    /// </summary>
    public sealed class PlaceableBeltSplitter : IBuildingPart
    {
        public IBuildingPart CloneForInstance() => new PlaceableBeltSplitter();
    }
}
