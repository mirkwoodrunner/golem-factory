namespace GolemFactory.World
{
    /// <summary>
    /// An endpoint that accepts exactly one good, and can say which.
    ///
    /// <para>
    /// <b>Deliberately NOT part of <see cref="IItemEndpoint"/>.</b> That interface's own comment
    /// records that it was kept narrow on purpose and widened exactly once, for a reason written
    /// down in two places. A filter is not something a belt, a node or a boiler's fuel hatch has,
    /// and adding <c>AcceptedItemType</c> to all of them would mean five implementations
    /// returning null to describe a concept they do not have.
    /// </para>
    ///
    /// <para>
    /// It is asked exactly one question, on exactly one path:
    /// <c>GolemEntity.PushStockInto</c> reaching the "nothing moved" branch needs to tell
    /// <b>"this crate is full"</b> from <b>"this crate is for something else"</b>, because those
    /// have opposite fixes -- wait, versus go somewhere else. That is the same shape as
    /// <c>GolemEntity.EmptyReasonFor</c>, which already type-tests the endpoint
    /// (<c>endpoint is ResourceNodeEndpoint</c>) to pick the right stall reason, and it is on the
    /// failure path only, never per unit.
    /// </para>
    /// </summary>
    public interface IFilteredEndpoint
    {
        /// <summary>The single good this endpoint accepts and dispenses.</summary>
        string AcceptedItemType { get; }
    }
}
