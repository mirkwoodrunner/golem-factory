using Godot;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.PunchCards;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Hand-Crank Bench's interface: Unity's HandCrankPanelView, shown only while the player
    /// stands at a bench (PlayerInteractor.NearestBench).
    ///
    /// <para>
    /// "HAND-CRANK BENCH   R1 Coking" / "1 Coal → 1 Coke" / "[█████░░░░░] 50%" / the hint or
    /// the shortfall -- the text is Core's HandCrankReadout.Format, unchanged. Hold [E] to turn
    /// the handle; [R] cycles the recipe. Bottom-centre, 430x96, 74px up, the dark HUD plate
    /// and amber text Unity's authoring pass gave it; the text turns coral when the stockpile
    /// cannot cover the recipe.
    /// </para>
    /// </summary>
    public partial class HandCrankPanelNode : CanvasLayer
    {
        private static readonly Color PlateColor = new Color(0.09f, 0.08f, 0.07f, 0.94f);
        private static readonly Color NormalColor = new Color(0.85f, 0.66f, 0.32f, 1f);
        private static readonly Color BlockedColor = new Color(1f, 0.52f, 0.40f, 1f);
        private const float NominalTicksPerSecond = 10f;

        private WorldNode _world;
        private PlayerInteractor _interactor;
        private Panel _body;
        private Label _text;

        public bool IsShowing => _body.Visible;
        public string Text => _text.Text;

        public override void _Ready()
        {
            _world = WorldNode.Find(this);
            _interactor = _world.Sandbox.Interactor;

            _body = new Panel { Name = "HandCrankPanel", Size = new Vector2(430f, 96f), MouseFilter = Control.MouseFilterEnum.Ignore };
            _body.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = PlateColor });
            AddChild(_body);
            _text = new Label { Position = new Vector2(8f, 4f), Size = new Vector2(430f - 16f, 96f - 8f) };
            _text.AddThemeFontSizeOverride("font_size", 15);
            _body.AddChild(_text);
            Place();
            GetViewport().SizeChanged += Place;
        }

        public override void _Process(double delta)
        {
            HandCrankBench bench = _interactor.NearestBench;
            bool show = bench != null && !ModalScreens.AnyOpen(GetTree());
            _body.Visible = show;
            if (!show)
            {
                return;
            }

            HandCrankReading reading = Read(bench);
            _text.Text = HandCrankReadout.Format(reading);
            _text.AddThemeColorOverride("font_color", reading.CanAfford ? NormalColor : BlockedColor);
        }

        private HandCrankReading Read(HandCrankBench bench)
        {
            RecipeDefinition recipe = bench.SelectedRecipe;
            bool canAfford = bench.CanAffordSelected();
            return new HandCrankReading(
                hasBench: true,
                recipeLabel: HandCrankReadout.RecipeLabel(recipe),
                conversionLabel: HandCrankReadout.ConversionLabel(recipe),
                progressPercent: HandCrankReadout.ProgressPercent(bench.ProgressTicks, bench.RequiredTicks),
                remainingSeconds: HandCrankReadout.RemainingSeconds(bench.ProgressTicks, bench.RequiredTicks, NominalTicksPerSecond),
                canAfford: canAfford,
                shortfallLabel: canAfford ? string.Empty : HandCrankReadout.ShortfallLabel(recipe, StockOf),
                isCranking: bench.IsCranking,
                recipeCount: bench.CrankableRecipes.Count);
        }

        private int StockOf(string itemType) =>
            _world.Buffers.GetQuantity(_world.Sandbox.StockpileBufferId, itemType);

        private void Place()
        {
            Vector2 screen = GetViewport().GetVisibleRect().Size;
            _body.Position = new Vector2((screen.X - _body.Size.X) / 2f, screen.Y - 74f - _body.Size.Y);
        }
    }
}
