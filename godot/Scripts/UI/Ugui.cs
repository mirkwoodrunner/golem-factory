using Godot;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Builds Godot controls from Unity UGUI numbers, so a screen can be written straight off its
    /// prefab: <see cref="Place"/> takes a RectTransform's anchorMin/anchorMax/anchoredPosition/
    /// sizeDelta/pivot (Unity's frame, y up) and sets the equivalent Godot anchors and offsets.
    /// The canvas scaler the Unity screens used matches width at a 1280x720 reference, so at the
    /// project's 1280x720 window a UGUI pixel is a Godot pixel and the numbers carry over 1:1.
    /// </summary>
    public static class Ugui
    {
        /// <summary>Unity RectTransform → Godot anchors and offsets.</summary>
        public static T Place<T>(T control,
            float aminX, float aminY, float amaxX, float amaxY,
            float posX = 0f, float posY = 0f, float sizeX = 0f, float sizeY = 0f,
            float pivotX = 0.5f, float pivotY = 0.5f) where T : Control
        {
            float minX = posX - sizeX * pivotX;
            float minY = posY - sizeY * pivotY;
            float maxX = minX + sizeX;
            float maxY = minY + sizeY;

            control.AnchorLeft = aminX;
            control.AnchorRight = amaxX;
            control.AnchorTop = 1f - amaxY;
            control.AnchorBottom = 1f - aminY;
            control.OffsetLeft = minX;
            control.OffsetRight = maxX;
            control.OffsetTop = -maxY;
            control.OffsetBottom = -minY;
            return control;
        }

        /// <summary>Stretches over the parent (anchors 0..1, no offsets).</summary>
        public static T Fill<T>(T control) where T : Control => Place(control, 0f, 0f, 1f, 1f);

        /// <summary>
        /// A 9-sliced sprite, Unity's Image.Type Sliced (or Tiled: the centre and edges repeat).
        /// Unity's spriteBorder order is (left, bottom, right, top).
        /// </summary>
        public static StyleBoxTexture NineSlice(string path, int left, int bottom, int right, int top, bool tiled = false)
        {
            var box = new StyleBoxTexture
            {
                Texture = GD.Load<Texture2D>(path),
                TextureMarginLeft = left,
                TextureMarginBottom = bottom,
                TextureMarginRight = right,
                TextureMarginTop = top,
            };
            if (tiled)
            {
                box.AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile;
                box.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile;
            }
            return box;
        }

        public static StyleBoxTexture NineSlice(string path, int border, bool tiled = false) =>
            NineSlice(path, border, border, border, border, tiled);

        /// <summary>
        /// A UGUI Image: a panel drawing <paramref name="style"/> tinted by <paramref name="tint"/>.
        /// The tint is SelfModulate, so it colours this plate and not the controls inside it --
        /// Image.color never reached an Image's children either.
        /// </summary>
        public static Panel Image(string name, StyleBox style, Color tint)
        {
            var panel = new Panel { Name = name, SelfModulate = tint, MouseFilter = Control.MouseFilterEnum.Ignore };
            panel.AddThemeStyleboxOverride("panel", style);
            return panel;
        }

        /// <summary>A plain coloured rectangle, a UGUI Image with no sprite.</summary>
        public static ColorRect Rect(string name, Color color) =>
            new ColorRect { Name = name, Color = color, MouseFilter = Control.MouseFilterEnum.Ignore };

        /// <summary>
        /// A TMP label. <paramref name="align"/> is TMP's horizontal code (1 left, 2 centre, 4
        /// right); every label on these screens is vertically centred. Single line, truncated
        /// with an ellipsis at the end, as TMP's Truncate overflow was.
        /// </summary>
        public static Label Text(string name, string text, int size, Color color, int align = 1, bool bold = false)
        {
            var label = new Label
            {
                Name = name,
                Text = text,
                HorizontalAlignment = align == 2 ? HorizontalAlignment.Center : align == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.AddThemeFontSizeOverride("font_size", size);
            label.AddThemeColorOverride("font_color", color);
            if (bold)
            {
                label.AddThemeFontOverride("font", Bold);
            }
            return label;
        }

        private static FontVariation _bold;

        /// <summary>The project font, emboldened: TMP's FontStyles.Bold.</summary>
        public static FontVariation Bold => _bold ??= new FontVariation
        {
            BaseFont = GD.Load<Font>("res://fonts/LiberationSans.ttf"),
            VariationEmbolden = 0.7f,
        };

        public static Color ToGodot(Compat.Color c) => new Color(c.r, c.g, c.b, c.a);
    }
}
