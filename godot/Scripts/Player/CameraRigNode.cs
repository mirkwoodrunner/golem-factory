using Godot;
using GolemFactory.ClockTower;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.World;
using CoreVector2 = GolemFactory.Compat.Vector2;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The camera: eases after the player and zooms with the wheel, by Core's
    /// <see cref="CameraRigRules"/> -- Unity's CameraRigController, the rules extracted -- and,
    /// since Phase 6, by the world's <see cref="CameraDirector"/>: the wheel steps between fixed
    /// zoom levels and glides there, a click on the alert strip glides to a stopped golem until
    /// the player walks, and a Clock Tower stage glides to the tower and back
    /// (docs/game-feel-design.md §2–§3).
    ///
    /// <para>
    /// Unity zoomed an orthographic camera by its SIZE (half the view's height, in cells); Godot
    /// zooms by a factor. <see cref="OrthographicSize"/> keeps Unity's quantity and converts at
    /// the boundary, so the clamps (3..15) and the starting framing (10) mean what they did.
    /// </para>
    /// </summary>
    public partial class CameraRigNode : Camera2D
    {
        public const string ZoomIn = "zoom_in";
        public const string ZoomOut = "zoom_out";

        [Export] public NodePath TargetPath { get; set; }
        [Export] public float OrthographicSize { get; set; } = CameraRigRules.DefaultOrthographicSize;

        /// <summary>How much one wheel notch counts as, in seconds of held zoom input.</summary>
        [Export] public float WheelNotchSeconds { get; set; } = 0.1f;

        private Node2D _target;
        private float _pendingZoom;
        private CameraDirector _director;
        private SandboxWorld _sandbox;

        // A size a scenario set directly stays put until the wheel moves it.
        private bool _pinned;

        /// <summary>The world point the camera is gliding to this frame.</summary>
        public Vector2 Aim { get; private set; }

        public override void _Ready()
        {
            _target = TargetPath != null && !TargetPath.IsEmpty ? GetNode<Node2D>(TargetPath) : null;
            _sandbox = WorldNode.Find(this)?.Sandbox;
            _director = _sandbox?.Camera;
            _director?.SnapZoomTo(OrthographicSize);
            Bind(ZoomIn, MouseButton.WheelUp);
            Bind(ZoomOut, MouseButton.WheelDown);
            if (_target != null)
            {
                GlobalPosition = _target.GlobalPosition;
            }
            ApplyZoom();
            GetViewport().SizeChanged += ApplyZoom; // a size is in cells, so a new window re-fits
            MakeCurrent();
        }

        public override void _UnhandledInput(InputEvent e)
        {
            // Any key while the tower's event plays skips it (decision 4).
            if (e is InputEventKey { Pressed: true, Echo: false } && _director != null &&
                (_director.TowerPhase == TowerEventPhase.GlideIn || _director.TowerPhase == TowerEventPhase.Reveal))
            {
                _director.SkipTowerEvent();
            }
            if (e.IsActionPressed(ZoomIn))
            {
                _pendingZoom += 1f;
            }
            else if (e.IsActionPressed(ZoomOut))
            {
                _pendingZoom -= 1f;
            }
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;
            if (_director == null)
            {
                FollowTarget(dt, CameraRigRules.FollowLerpSpeed, _target?.GlobalPosition);
                if (_pendingZoom != 0f)
                {
                    OrthographicSize = CameraRigRules.Zoom(OrthographicSize, _pendingZoom, WheelNotchSeconds);
                    _pendingZoom = 0f;
                    ApplyZoom();
                }
                return;
            }

            // Walking takes the camera back from a golem it was sent to.
            if (_director.Focus == CameraFocus.Alert && PlayerIsWalking())
            {
                _director.PlayerMoved();
            }
            _director.UpdateTowerEvent(dt);
            FollowTarget(dt, _director.GlideSpeed, FocusPoint());

            if (_pendingZoom != 0f)
            {
                _director.ZoomStep(_pendingZoom > 0f ? 1 : -1);
                _pendingZoom = 0f;
                _pinned = false;
            }
            if (!_pinned)
            {
                float size = _director.GlideZoom(OrthographicSize, dt);
                if (System.Math.Abs(size - OrthographicSize) > 0.0001f)
                {
                    OrthographicSize = size;
                    ApplyZoom();
                }
            }
        }

        private void FollowTarget(float dt, float speed, Vector2? aim)
        {
            if (aim == null)
            {
                return;
            }
            Aim = aim.Value;
            CoreVector2 next = CameraRigRules.Follow(
                new CoreVector2(GlobalPosition.X, GlobalPosition.Y), new CoreVector2(Aim.X, Aim.Y), dt, speed);
            GlobalPosition = new Vector2(next.x, next.y);
        }

        /// <summary>Where the director wants the camera: the player, a stopped golem, or the tower.</summary>
        private Vector2? FocusPoint()
        {
            switch (_director.Focus)
            {
                case CameraFocus.Alert:
                    GolemEntity golem = FindGolem(_director.AlertGolemId);
                    if (golem == null)
                    {
                        _director.PlayerMoved(); // gone: back to the player
                        return _target?.GlobalPosition;
                    }
                    return GridConversions.CellToWorld(golem.Cell);
                case CameraFocus.TowerEvent:
                    // The tower's middle: its feet are the footprint's south edge, the picture
                    // stands ten cells tall.
                    return GridConversions.FootprintFeet(TownSquare.TowerCentre, TownSquare.TowerSize)
                        - new Vector2(0f, ClockTowerArt.CanvasHeight * 0.45f);
                default:
                    return _target?.GlobalPosition;
            }
        }

        private GolemEntity FindGolem(string id)
        {
            if (id == null || _sandbox == null)
            {
                return null;
            }
            foreach (GolemEntity golem in _sandbox.Golems)
            {
                if (golem != null && !golem.IsRemoved && golem.GolemId == id)
                {
                    return golem;
                }
            }
            return null;
        }

        private bool PlayerIsWalking() =>
            !TextEntry.IsTyping(GetViewport()) && (Input.IsActionPressed(PlayerNode.MoveLeft) || Input.IsActionPressed(PlayerNode.MoveRight)
                || Input.IsActionPressed(PlayerNode.MoveUp) || Input.IsActionPressed(PlayerNode.MoveDown));

        /// <summary>Zooms straight to <paramref name="size"/> (clamped), as a scenario capture wants.</summary>
        public void SetOrthographicSize(float size)
        {
            OrthographicSize = System.Math.Clamp(size, CameraRigRules.MinOrthographicSize, CameraRigRules.MaxOrthographicSize);
            _director?.SnapZoomTo(OrthographicSize);
            _pinned = true;
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            float halfHeightPx = GetViewportRect().Size.Y / 2f;
            float z = halfHeightPx / (OrthographicSize * GridConversions.CellPixels);
            Zoom = new Vector2(z, z);
        }

        private static void Bind(string action, MouseButton button)
        {
            if (InputMap.HasAction(action))
            {
                return;
            }
            InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
        }
    }
}
