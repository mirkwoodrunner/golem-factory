using GolemFactory.Buildings;

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

    /// <summary>The Workbench, as the interactor's [E] at a golem needs it.</summary>
    public interface IWorkbenchScreen : IScreen, IWorkbenchTarget
    {
        void Open();
    }
}
