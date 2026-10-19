using System.Linq;
using Godot;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// Saves the game view as a PNG at a scenario's chosen moments, when the run passes
    /// <c>--capture-dir=&lt;dir&gt;</c> after <c>--</c>. A windowed run only: headless has no
    /// framebuffer, and without the flag nothing is written. Cheaper to look at than a
    /// <c>--write-movie</c> of every frame, and it names the moment it shows.
    /// </summary>
    public static class ScenarioCapture
    {
        private const string Flag = "--capture-dir=";

        public static string Directory =>
            OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(Flag))?.Substring(Flag.Length);

        /// <summary>Writes <c>&lt;dir&gt;/&lt;name&gt;.png</c>; a no-op without the flag or a framebuffer.</summary>
        public static void Shot(Viewport viewport, string name)
        {
            string dir = Directory;
            if (string.IsNullOrEmpty(dir) || DisplayServer.GetName() == "headless")
            {
                return;
            }
            Image image = viewport.GetTexture()?.GetImage();
            if (image == null)
            {
                return;
            }
            DirAccess.MakeDirRecursiveAbsolute(dir);
            image.SavePng(System.IO.Path.Combine(dir, name + ".png"));
        }
    }
}
