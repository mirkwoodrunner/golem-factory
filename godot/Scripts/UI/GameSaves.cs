using System;
using Godot;
using GolemFactory.Menus;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The game's saving (docs/menus-design.md §7): a slot's save and its picture, the autosave
    /// on quit and when a tower stage completes, and the two ways out of a game, Quit to title
    /// and Quit to desktop. The rules (which slots, what a save records, when an autosave is
    /// due) are Core's <see cref="SaveSlots"/> and <see cref="SandboxWorld"/>; this node writes
    /// the picture, which needs the renderer, and leaves the scene.
    ///
    /// <para>
    /// A picture is the world alone: a second viewport shares the world's canvas and the
    /// camera's view, so the screens drawn over it (Management, the HUD) are not in it. It
    /// arrives a frame after its save; a headless run takes none.
    /// </para>
    /// </summary>
    public partial class GameSaves : Node
    {
        public const int PictureWidth = 320;
        public const int PictureHeight = 180;
        public const string QuitReason = "On quit";

        private WorldNode _world;
        private bool _leaving;

        /// <summary>The scene's node, by its place.</summary>
        public static GameSaves Find(Node from) => from.GetTree().CurrentScene?.GetNodeOrNull<GameSaves>("Saves");

        /// <summary>A game, with somewhere to save: not the title's showcase.</summary>
        public bool InGame { get; private set; }

        /// <summary>What Quit to desktop does after its autosave. A scenario replaces it.</summary>
        public Action QuitAction { get; set; }

        /// <summary>A slot's picture was written: the list showing it draws it again.</summary>
        public event Action<string> PictureTaken;

        public int PicturesTaken { get; private set; }

        /// <summary>The last autosave's status line, for scenarios.</summary>
        public string LastAutosave { get; private set; }

        private SandboxWorld Sandbox => _world.Sandbox;

        public override void _Ready()
        {
            _world = WorldNode.Find(this);
            InGame = _world != null && _world.Launch != LaunchKind.Title && Sandbox.SaveFolder != null;
            QuitAction ??= () => GetTree().Quit();
            // In a game, closing the window autosaves first (_Notification); the title just goes.
            GetTree().AutoAcceptQuit = !InGame;
        }

        public override void _ExitTree()
        {
            // The next scene may be the title, which quits at once.
            GetTree().AutoAcceptQuit = true;
        }

        public override void _Process(double delta)
        {
            if (InGame && !_leaving && Sandbox.AutosaveDue != null)
            {
                Autosave(Sandbox.AutosaveDue, null);
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationWMCloseRequest && InGame)
            {
                // Deferred: the notification reaches every node while the tree is busy
                // telling them, and the picture's viewport can't be added until it is done.
                Callable.From(QuitGame).CallDeferred();
            }
        }

        // --- Slots ---------------------------------------------------------------------------

        /// <summary>Saves into a slot (never the autosave) and takes its picture. Returns the status line.</summary>
        public string SaveSlot(string key)
        {
            if (!InGame || SaveSlots.IsAutosave(key))
            {
                return "";
            }
            try
            {
                string status = Write(key, () => Sandbox.SaveTo(SaveSlots.PathIn(Sandbox.SaveFolder, key)));
                return $"{SaveSlots.Title(key)}: {status}";
            }
            catch (Exception e)
            {
                GD.PushError($"{SaveSlots.Title(key)} could not be written: {e.Message}");
                return $"{SaveSlots.Title(key)} couldn't be saved: {e.Message}";
            }
        }

        /// <summary>Loads a slot into this game. A save that doesn't read is reported and left alone.</summary>
        public string LoadSlot(string key)
        {
            string path = SaveSlots.PathIn(Sandbox.SaveFolder, key);
            if (!System.IO.File.Exists(path))
            {
                return $"{SaveSlots.Title(key)} is empty now.";
            }
            try
            {
                return $"{SaveSlots.Title(key)}: {Sandbox.LoadFrom(path)}";
            }
            catch (Exception e)
            {
                GD.PushWarning($"{path} could not be loaded: {e.Message}");
                return $"{SaveSlots.Title(key)} couldn't be read, so it was left as it is.";
            }
        }

        public string DeleteSlot(string key)
        {
            SaveSlots.Delete(Sandbox.SaveFolder, key);
            return $"{SaveSlots.Title(key)} is empty.";
        }

        // --- Leaving ------------------------------------------------------------------------

        /// <summary>Autosaves, then back to the title.</summary>
        public void QuitToTitle() => Leave(() =>
        {
            GameLaunch.Request(LaunchKind.Title);
            GetTree().ReloadCurrentScene();
        });

        /// <summary>Autosaves, then quits: Quit to desktop, and closing the window.</summary>
        public void QuitGame() => Leave(() => QuitAction());

        public bool Leaving => _leaving;

        private void Leave(Action then)
        {
            if (_leaving)
            {
                return;
            }
            _leaving = true;
            if (InGame)
            {
                Autosave(QuitReason, then);
            }
            else
            {
                then();
            }
        }

        private void Autosave(string reason, Action then)
        {
            try
            {
                LastAutosave = Write(SaveSlots.AutosaveKey, () => Sandbox.Autosave(reason), then);
            }
            catch (Exception e)
            {
                // A disk that refuses the autosave mustn't keep the player from leaving.
                GD.PushError($"The autosave failed: {e.Message}");
                LastAutosave = null;
                then?.Invoke();
            }
        }

        /// <summary>Drops the slot's old picture, writes the save, then takes the new picture.</summary>
        private string Write(string key, Func<string> save, Action then = null)
        {
            string picture = SaveSlots.ThumbnailIn(Sandbox.SaveFolder, key);
            if (System.IO.File.Exists(picture))
            {
                System.IO.File.Delete(picture); // never an old picture beside a new save
            }
            string status = save();
            TakePicture(key, picture, then);
            return status;
        }

        /// <summary>
        /// The picture's viewport: one for the scene, drawing only while a picture is wanted.
        /// It shares the world's canvas, and freeing one that does tripped the renderer, so it
        /// goes with the scene.
        /// </summary>
        private SubViewport _view;
        private int _picturing;

        private async void TakePicture(string key, string path, Action then)
        {
            Viewport main = GetViewport();
            if (DisplayServer.GetName() == "headless")
            {
                then?.Invoke();
                return;
            }
            if (_view == null)
            {
                _view = new SubViewport
                {
                    Name = "SavePicture",
                    World2D = main.World2D,
                    RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                    CanvasItemDefaultTextureFilter = main.CanvasItemDefaultTextureFilter,
                };
                AddChild(_view);
            }
            // No camera of its own: the world drawn exactly as the screen draws it now.
            _view.Size = (Vector2I)main.GetVisibleRect().Size;
            _view.CanvasTransform = main.CanvasTransform;
            _view.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            _picturing++;
            try
            {
                // Two frames: the one it was asked for in may already be drawing.
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Image image = _view.GetTexture().GetImage();
                if (image != null && !image.IsEmpty())
                {
                    image.Resize(PictureWidth, PictureHeight, Image.Interpolation.Lanczos);
                    if (image.SavePng(path) == Error.Ok)
                    {
                        PicturesTaken++;
                        PictureTaken?.Invoke(key);
                    }
                }
            }
            catch (Exception e)
            {
                GD.PushWarning($"No picture for {SaveSlots.Title(key)}: {e.Message}");
            }
            finally
            {
                if (--_picturing == 0 && IsInstanceValid(_view))
                {
                    _view.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                }
                then?.Invoke();
            }
        }
    }
}
