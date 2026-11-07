using GolemFactory.Buildings;
using GolemFactory.Supply;

namespace GolemFactory.UI
{
    /// <summary>
    /// A full screen that dims the world behind it -- the Management panel, the Workbench, the
    /// construction panel. While one is open the world-space prompt hides
    /// (<see cref="HudScreenPolicy.ShouldShowWorldHud"/>). Unity held the concrete panels; Core
    /// asks only this.
    /// </summary>
    public interface IScreen
    {
        bool IsOpen { get; }
    }

    /// <summary>The golem construction panel, as the interactor's [E] at a station needs it.</summary>
    public interface IConstructionScreen : IScreen
    {
        void Open(GolemConstructionStation station);
    }

    /// <summary>The Pack screen, as the interactor's [I] away from a crate needs it.</summary>
    public interface IPackScreen : IScreen
    {
        void Open();
    }

    /// <summary>The Crate screen, as the interactor's [I] at a crate needs it.</summary>
    public interface ICrateScreen : IScreen
    {
        void Open(ICrate crate);
    }

    /// <summary>The cart panel (docs/supply-design.md §2), as [E] at an empty spot and [I] at a cart need it.</summary>
    public interface ISupplyScreen : IScreen
    {
        void Open(CartSpot spot);
    }

    /// <summary>The Workbench, as the interactor's [E] at a golem needs it.</summary>
    public interface IWorkbenchScreen : IScreen, IWorkbenchTarget
    {
        void Open();
    }
}
