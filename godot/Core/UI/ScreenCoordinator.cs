using System.Collections.Generic;

namespace GolemFactory.UI
{
    /// <summary>A full screen that can be told to step aside.</summary>
    public interface IClosableScreen : IScreen
    {
        void Close();
    }

    /// <summary>
    /// Keeps exactly one full screen up: opening any registered screen closes the others.
    ///
    /// <para>
    /// Unity spread this across three classes -- the Workbench's Open closed Management and
    /// Construction, Management's closed the other two, Construction's closed the Workbench and
    /// Management -- and the HUD exclusivity suite existed because a pair of them had once
    /// stacked. One coordinator every screen reports to cannot miss a pair, and a fourth screen
    /// joins by registering rather than by every other screen learning its name.
    /// <see cref="HudScreenPolicy"/>'s rule -- world HUD only with no screen open -- is
    /// <see cref="AnyOpen"/>.
    /// </para>
    /// </summary>
    public sealed class ScreenCoordinator
    {
        private readonly List<IClosableScreen> _screens = new List<IClosableScreen>();

        public IReadOnlyList<IClosableScreen> Screens => _screens;

        public void Register(IClosableScreen screen)
        {
            if (screen != null && !_screens.Contains(screen))
            {
                _screens.Add(screen);
            }
        }

        /// <summary>Call as <paramref name="opening"/> opens: every other open screen closes.</summary>
        public void Opening(IClosableScreen opening)
        {
            foreach (IClosableScreen screen in _screens)
            {
                if (screen != opening && screen.IsOpen)
                {
                    screen.Close();
                }
            }
        }

        public bool AnyOpen
        {
            get
            {
                foreach (IClosableScreen screen in _screens)
                {
                    if (screen.IsOpen)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public int OpenCount
        {
            get
            {
                int open = 0;
                foreach (IClosableScreen screen in _screens)
                {
                    if (screen.IsOpen)
                    {
                        open++;
                    }
                }
                return open;
            }
        }
    }
}
