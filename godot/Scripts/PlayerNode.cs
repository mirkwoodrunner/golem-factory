using Godot;
using GolemFactory.Player;
using GolemFactory.World;
using CoreVector2 = GolemFactory.Compat.Vector2;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Artificer. Moves in Core's frame -- one unit per cell, +y north -- with Core's
    /// <see cref="PlayerWalker"/> (movement, the world clamp and the walk frame)
    /// and <see cref="ArtificerWalkAnimation"/>, and converts to pixels only to draw. That is
    /// the same split Unity's PlayerController and ArtificerWalkAnimator made, so all three
    /// rules are the ported, tested ones rather than a Godot rewrite.
    /// </summary>
    public partial class PlayerNode : Node2D
    {
        public const string MoveLeft = "move_left";
        public const string MoveRight = "move_right";
        public const string MoveUp = "move_up";
        public const string MoveDown = "move_down";
        public const string Interact = "interact";
        public const string Carry = "golem_carry";

        /// <summary>Cells per second, Unity's PlayerController default.</summary>
        [Export] public float MoveSpeed { get; set; } = 4f;

        /// <summary>Starting cell, in Core's frame (north = +y).</summary>
        [Export] public Vector2I StartCell { get; set; }

        /// <summary>How far an interactable may be and still answer [E], in cells.</summary>
        [Export] public float InteractRange { get; set; } = 1.6f;

        /// <summary>Carry a plain Camera2D. Off where a CameraRigNode follows the player instead.</summary>
        [Export] public bool OwnCamera { get; set; } = true;

        /// <summary>
        /// When set, replaces keyboard input -- how a scenario walks the player. Core frame:
        /// +y is north.
        /// </summary>
        public Vector2? ScriptedMove { get; set; }

        /// <summary>Puts the player at <paramref name="cells"/> (Core frame) -- a scenario's shortcut for a walk.</summary>
        public void TeleportTo(CoreVector3 cells)
        {
            _walker.Position = cells;
            Position = ToPixels(cells);
        }

        /// <summary>Where the player stands, in cells (Core frame).</summary>
        public CoreVector3 CorePosition => _walker.Position;

        private static readonly GridCoordinateConverter UnitConverter = new GridCoordinateConverter(new CoreVector2(1f, 1f));

        private readonly Texture2D[] _frames = new Texture2D[ArtificerWalkAnimation.DirectionCount * ArtificerWalkAnimation.FramesPerDirection];
        private Sprite2D _sprite;
        private PlayerWalker _walker;
        private PlayerInteractor _interactor;

        /// <summary>The interactable [E] would use right now, or null.</summary>
        public IInteractable Focus { get; private set; }

        public override void _Ready()
        {
            RegisterInputActions();

            string[] rows = { "down", "left", "right", "up" }; // ArtificerFacing's order
            for (int row = 0; row < rows.Length; row++)
            {
                for (int frame = 0; frame < ArtificerWalkAnimation.FramesPerDirection; frame++)
                {
                    _frames[ArtificerWalkAnimation.ComputeSpriteIndex((ArtificerFacing)row, frame)] =
                        GD.Load<Texture2D>($"res://art/artificer_walk_{rows[row]}_{frame}.png");
                }
            }

            _sprite = new Sprite2D { Texture = _frames[0] };
            GridConversions.StandOnCell(_sprite);
            AddChild(_sprite);
            if (OwnCamera)
            {
                AddChild(new Camera2D());
            }

            // The Sandbox's [E] is Core's PlayerInteractor. LoopSlice has no composed world, so it
            // keeps the spike's IInteractable nodes.
            WorldNode world = WorldNode.Find(this);
            _interactor = world?.Setup != null ? world.Sandbox.Interactor : null;

            _walker = new PlayerWalker(MoveSpeed) { Position = new CoreVector3(StartCell.X, StartCell.Y, 0f) };
            _walker.SetFloorBounds(UnitConverter, FloorLayout.HalfExtent);
            Position = ToPixels(_walker.Position);
        }

        public override void _Process(double delta)
        {
            // A full screen owns the keys: the player does not walk off while choosing a chassis.
            Vector2 input = ScriptedMove
                ?? (ModalScreens.AnyOpen(GetTree()) ? Vector2.Zero : Input.GetVector(MoveLeft, MoveRight, MoveDown, MoveUp)); // +y = north

            // Bounded by the WORLD (workshop + street), with the north wall wherever Floor
            // Expansion has pushed it -- the clamp Unity's PlayerController applied.
            FloorBounds bounds = WorldNode.Find(this)?.Bounds ?? new FloorBounds();
            _walker.SetNorthExtent(bounds.NorthExtent);
            _walker.MoveBy(new CoreVector2(input.X, input.Y), (float)delta);
            Position = ToPixels(_walker.Position);
            _sprite.Texture = _frames[_walker.SpriteIndex];

            if (_interactor != null)
            {
                // Core's PlayerInteractor, as Unity's Update drove it: where the player stands,
                // whether [E] is held (the crank), then re-pick and refresh the prompt.
                _interactor.Position = _walker.Position;
                bool screenOpen = ModalScreens.AnyOpen(GetTree());
                _interactor.SetInteractHeld(!screenOpen && Input.IsActionPressed(Interact));
                _interactor.Poll();
                if (!screenOpen && Input.IsActionJustPressed(Interact))
                {
                    _interactor.Interact();
                }
                return;
            }

            Focus = FindNearestInteractable();
            if (Focus != null && Input.IsActionJustPressed(Interact))
            {
                Focus.Interact();
            }
        }

        public override void _UnhandledInput(InputEvent e)
        {
            if (_interactor == null || ModalScreens.AnyOpen(GetTree()))
            {
                return;
            }

            // R is shared with build mode, which hears it first (BuildCursorNode sits later in
            // the tree, and _UnhandledInput runs last child first) and consumes it only while a
            // placeable is in hand. Otherwise it turns the bench's dial or the nearest golem.
            if (e.IsActionPressed(BuildCursorNode.RotateAction) && _interactor.RotateKey())
            {
                GetViewport().SetInputAsHandled();
            }
            else if (e.IsActionPressed(Carry) && _interactor.ToggleCarryGolem())
            {
                GetViewport().SetInputAsHandled();
            }
        }

        private IInteractable FindNearestInteractable()
        {
            IInteractable best = null;
            float bestDistance = InteractRange * GridConversions.CellPixels;
            foreach (Node node in GetTree().GetNodesInGroup(InteractableGroup.Name))
            {
                if (node is IInteractable candidate && node is Node2D placed)
                {
                    float distance = placed.GlobalPosition.DistanceTo(GlobalPosition);
                    if (distance <= bestDistance)
                    {
                        best = candidate;
                        bestDistance = distance;
                    }
                }
            }
            return best;
        }

        private static Vector2 ToPixels(CoreVector3 p) =>
            new Vector2(p.x * GridConversions.CellPixels, -p.y * GridConversions.CellPixels);

        // Registered in code rather than in project.godot's [input] block, whose serialized
        // InputEvent objects are not something to hand-write. Still Godot's own InputMap, so the
        // editor's Input Map tab shows and can rebind them at runtime.
        private static void RegisterInputActions()
        {
            Bind(MoveLeft, Key.A, Key.Left);
            Bind(MoveRight, Key.D, Key.Right);
            Bind(MoveUp, Key.W, Key.Up);
            Bind(MoveDown, Key.S, Key.Down);
            Bind(Interact, Key.E);
            Bind(Carry, Key.G);
        }

        private static void Bind(string action, params Key[] keys)
        {
            if (InputMap.HasAction(action))
            {
                return;
            }
            InputMap.AddAction(action);
            foreach (Key key in keys)
            {
                InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
            }
        }
    }

    /// <summary>Something the Artificer can use with [E].</summary>
    public interface IInteractable
    {
        string Prompt { get; }
        void Interact();
    }

    public static class InteractableGroup
    {
        public const string Name = "interactable";
    }
}
