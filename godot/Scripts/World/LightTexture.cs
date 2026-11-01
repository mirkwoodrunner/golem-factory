using Godot;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The one texture every PointLight2D in the world uses: a radial pool in flat steps
    /// (<see cref="LightingRules.SconceBandStops"/>), so a lit patch reads as pixel art instead of
    /// an airbrush. Built once and shared.
    /// </summary>
    public static class LightTexture
    {
        public const int Size = 256;

        private static Texture2D _banded;

        public static Texture2D Banded()
        {
            if (_banded != null)
            {
                return _banded;
            }
            var stops = LightingRules.SconceBandStops();
            var colors = new Color[stops.Length];
            var offsets = new float[stops.Length];
            for (int i = 0; i < stops.Length; i++)
            {
                colors[i] = new Color(1, 1, 1, stops[i].Strength);
                offsets[i] = stops[i].Offset;
            }
            _banded = new GradientTexture2D
            {
                Width = Size,
                Height = Size,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(0.5f, 0f),
                Gradient = new Gradient
                {
                    Colors = colors,
                    Offsets = offsets,
                    InterpolationMode = Gradient.InterpolationModeEnum.Constant,
                },
            };
            return _banded;
        }

        /// <summary>The TextureScale that makes the pool <paramref name="radiusCells"/> cells in radius.</summary>
        public static float ScaleFor(float radiusCells) => radiusCells * 2f * GridConversions.CellPixels / Size;
    }
}
