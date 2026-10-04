using System.Collections.Generic;
using Godot;
using GolemFactory.Player;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// What the player's [E] would do, drawn on the thing it would do it to, plus the short
    /// popups an action leaves behind ("+1 Scrap", "Depleted", "-15 Scrap"). Unity's
    /// InteractionPromptView and FloatingPopup.
    ///
    /// <para>
    /// The RING is a floor-level sprite under the target: breathing gold when [E] is ready,
    /// a still grey ring when the target is in sight but out of reach. The CAPTION is Core's
    /// <c>CurrentPrompt</c> -- "[E] Harvest Scrap · 60 left" -- over the target. Captions and
    /// popups are drawn in screen space at the target's projected position rather than as
    /// world-space text, so they stay one readable size at every zoom (Unity's world-space
    /// canvases shrank with the camera). Hidden while a full screen is open, as Unity's was
    /// (HudScreenPolicy, asked by the interactor itself).
    /// </para>
    /// </summary>
    public partial class InteractionPromptNode : Node2D
    {
        // Unity's InteractionPromptView colours and motion.
        private static readonly Color ReadyRingColor = new Color(1f, 0.82f, 0.42f, 1f);
        private static readonly Color OutOfRangeRingColor = new Color(0.66f, 0.70f, 0.74f, 0.55f);
        private static readonly Color ReadyTextColor = new Color(1f, 0.93f, 0.80f, 1f);
        private static readonly Color OutOfRangeTextColor = new Color(0.74f, 0.76f, 0.78f, 1f);
        private static readonly Color OutlineColor = new Color(20 / 255f, 12 / 255f, 8 / 255f, 1f);
        private const float PulsePeriod = 1.6f;
        private const float PulseDepth = 0.28f;
        private const float RingScaleMin = 0.92f;
        private const float RingScaleMax = 1f;
        private const float RingOffsetCells = -0.12f;
        private const float CaptionOffsetCells = 0.95f;

        // Unity's FloatingPopup and the colours its two callers chose.
        private const float PopupDuration = 0.9f;
        private const float PopupRiseCells = 0.55f;
        private static readonly Color GainPopupColor = new Color(1f, 0.86f, 0.50f, 1f);
        private static readonly Color RefusedPopupColor = new Color(0.72f, 0.75f, 0.78f, 1f);
        private static readonly Color BuildRefusedColor = new Color(1f, 0.52f, 0.40f, 1f);
        private static readonly Color BuildSpentColor = new Color(0.72f, 0.75f, 0.78f, 1f);
        private static readonly Color BuildRefundColor = new Color(0.56f, 0.86f, 0.50f, 1f);

        private sealed class Popup
        {
            public Label Label;
            public CoreVector3 Origin;
            public float Elapsed;
            public Color Color;
        }

        private readonly List<Popup> _popups = new List<Popup>();
        private WorldNode _world;
        private PlayerInteractor _interactor;
        private Sprite2D _ring;
        private CanvasLayer _overlay;
        private Label _caption;
        private double _time;

        /// <summary>The caption currently drawn ("" when hidden), for a scenario to read.</summary>
        public string Caption => _caption.Visible ? _caption.Text : "";

        public int LivePopups => _popups.Count;

        public override void _Ready()
        {
            _world = WorldNode.Find(this);
            _interactor = _world.Sandbox.Interactor;

            // Floor level, with the ghost and the belts: z -1 inside the entity layer's frame.
            ZIndex = -1;
            _ring = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/interaction_ring.png"), Visible = false };
            AddChild(_ring);

            _overlay = new CanvasLayer { Layer = 5 };
            AddChild(_overlay);
            _caption = MakeLabel(15, bold: true);
            _caption.Visible = false;
            _overlay.AddChild(_caption);

            _interactor.PopupRaised += OnInteractionPopup;
            _world.Sandbox.Build.PopupRaised += OnBuildPopup;
        }

        public override void _ExitTree()
        {
            if (_interactor != null)
            {
                _interactor.PopupRaised -= OnInteractionPopup;
                _world.Sandbox.Build.PopupRaised -= OnBuildPopup;
            }
        }

        public override void _Process(double delta)
        {
            _time += delta;
            ShowPrompt();
            AnimatePopups((float)delta);
        }

        private void ShowPrompt()
        {
            InteractionAffordance affordance = _interactor.CurrentAffordance;
            if (affordance == InteractionAffordance.Hidden || string.IsNullOrEmpty(_interactor.CurrentPrompt))
            {
                _ring.Visible = false;
                _caption.Visible = false;
                return;
            }

            bool ready = affordance == InteractionAffordance.Ready;
            CoreVector3 target = _interactor.PromptPosition;

            _ring.Visible = true;
            _ring.Position = ToPixels(target) + new Vector2(0f, -RingOffsetCells * GridConversions.CellPixels);
            if (ready)
            {
                float breathe = FeedbackMotion.Breathe01((float)_time, PulsePeriod);
                Color c = ReadyRingColor;
                c.A = ReadyRingColor.A * (1f - PulseDepth + PulseDepth * breathe);
                _ring.Modulate = c;
                _ring.Scale = Vector2.One * Mathf.Lerp(RingScaleMin, RingScaleMax, breathe);
            }
            else
            {
                _ring.Modulate = OutOfRangeRingColor;
                _ring.Scale = Vector2.One * RingScaleMin;
            }

            _caption.Visible = true;
            _caption.Text = _interactor.CurrentPrompt;
            _caption.AddThemeColorOverride("font_color", ready ? ReadyTextColor : OutOfRangeTextColor);
            Center(_caption, ScreenOf(new CoreVector3(target.x, target.y + CaptionOffsetCells, 0f)));
        }

        private void OnInteractionPopup(InteractionPopup popup) =>
            Spawn(new CoreVector3(popup.Position.x, popup.Position.y + popup.Height, 0f), popup.Text,
                popup.Kind == InteractionPopupKind.Gain ? GainPopupColor : RefusedPopupColor);

        private void OnBuildPopup(BuildPopup popup)
        {
            Color color = popup.Kind switch
            {
                BuildPopupKind.Refused => BuildRefusedColor,
                BuildPopupKind.Refund => BuildRefundColor,
                _ => BuildSpentColor,
            };
            // Unity spawned these 0.3 above the cell's centre.
            Spawn(new CoreVector3(popup.Cell.x, popup.Cell.y + 0.3f, 0f), popup.Text, color);
        }

        private void Spawn(CoreVector3 origin, string text, Color color)
        {
            Label label = MakeLabel(18, bold: true);
            label.Text = text;
            label.AddThemeColorOverride("font_color", color);
            _overlay.AddChild(label);
            _popups.Add(new Popup { Label = label, Origin = origin, Color = color });
            Place(_popups[_popups.Count - 1]);
        }

        private void AnimatePopups(float delta)
        {
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                Popup p = _popups[i];
                p.Elapsed += delta;
                if (p.Elapsed >= PopupDuration)
                {
                    p.Label.QueueFree();
                    _popups.RemoveAt(i);
                    continue;
                }
                Place(p);
            }
        }

        private void Place(Popup p)
        {
            float rise = FeedbackMotion.RiseOffset(p.Elapsed, PopupDuration, PopupRiseCells);
            Color c = p.Color;
            c.A = FeedbackMotion.FadeOutAlpha(p.Elapsed, PopupDuration);
            p.Label.Modulate = new Color(1f, 1f, 1f, c.A);
            Center(p.Label, ScreenOf(new CoreVector3(p.Origin.x, p.Origin.y + rise, 0f)));
        }

        private Label MakeLabel(int size, bool bold)
        {
            var label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", size);
            label.AddThemeColorOverride("font_outline_color", OutlineColor);
            label.AddThemeConstantOverride("outline_size", 4);
            if (bold)
            {
                label.AddThemeFontOverride("font", new FontVariation { BaseFont = label.GetThemeDefaultFont(), VariationEmbolden = 0.6f });
            }
            return label;
        }

        private static void Center(Label label, Vector2 screen)
        {
            label.ResetSize();
            label.Position = screen - new Vector2(label.Size.X / 2f, label.Size.Y);
        }

        private Vector2 ScreenOf(CoreVector3 cells) => GetViewport().GetCanvasTransform() * ToPixels(cells);

        private static Vector2 ToPixels(CoreVector3 p) =>
            new Vector2(p.x * GridConversions.CellPixels, -p.y * GridConversions.CellPixels);
    }
}
