namespace GolemFactory.Buildings
{
    /// <summary>
    /// One kind-specific half of a building -- a depot's buffer, a belt's lane, a boiler's
    /// fire. What Unity modelled as a sibling MonoBehaviour next to <c>PlaceableBuilding</c>
    /// (which was sealed, so kinds could not subclass it); here a building holds a list of
    /// parts, and <see cref="PlaceableBuilding.GetPart{T}"/> stands in for GetComponent.
    /// </summary>
    public interface IBuildingPart
    {
        /// <summary>
        /// A fresh copy for a newly placed building -- what Unity's Instantiate did to a
        /// prefab's component: AUTHORED settings carry over (a depot's buffer id, a boiler's
        /// starting coke), RUNTIME state does not (a registration, a bound segment).
        /// </summary>
        IBuildingPart CloneForInstance();
    }
}
