using Godot;
using GolemFactory.Player;
using CoreVector2 = GolemFactory.Compat.Vector2;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The camera: eases after the player and zooms with the wheel, by Core's
    /// <see cref="CameraRigRules"/> -- Unity's CameraRigController, the rules extracted.
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

        public override void _Ready()
        {
            _target = TargetPath != null && !TargetPath.IsEmpty ? GetNode<Node2D>(TargetPath) : null;
            Bind(ZoomIn, MouseButton.WheelUp);
            Bind(ZoomOut, MouseButton.WheelDown);
            if (_target != null)
            {
                GlobalPosition = _target.GlobalPosition;
            }
            ApplyZoom();
            MakeCurrent();
        }

        public override void _UnhandledInput(InputEvent e)
        {
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
            if (_target != null)
            {
                CoreVector2 next = CameraRigRules.Follow(
                    new CoreVector2(GlobalPosition.X, GlobalPosition.Y),
                    new CoreVector2(_target.GlobalPosition.X, _target.GlobalPosition.Y),
                    (float)delta);
                GlobalPosition = new Vector2(next.x, next.y);
            }

            if (_pendingZoom != 0f)
            {
                OrthographicSize = CameraRigRules.Zoom(OrthographicSize, _pendingZoom, WheelNotchSeconds);
                _pendingZoom = 0f;
                ApplyZoom();
            }
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
