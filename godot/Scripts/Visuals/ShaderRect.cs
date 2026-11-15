using Godot;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// A control drawn whole by one of the UI shaders (<c>res://shaders/</c>): a
    /// <see cref="ColorRect"/> with its own <see cref="ShaderMaterial"/> that never takes the
    /// pointer. The UI shaders work in the control's own pixels, so each takes a
    /// <c>rect_px</c>, kept in step with the control's size here, on the resize and not by the
    /// frame.
    /// </summary>
    public static class ShaderRect
    {
        public const string GearShader = TickVisualsNode.GearShader;
        public const string TapeShader = "res://shaders/logic_tape.gdshader";
        public const string CardShader = "res://shaders/punch_card.gdshader";

        private static readonly StringName RectPx = "rect_px";

        public static ColorRect Make(string name, string shaderPath, out ShaderMaterial material)
        {
            var shaded = new ShaderMaterial { Shader = GD.Load<Shader>(shaderPath) };
            var rect = new ColorRect
            {
                Name = name,
                Color = Colors.White,
                Material = shaded,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            // On entering the tree as well as on a resize: a control sized before it is in the
            // tree reports no resize once it is.
            void Sized() => shaded.SetShaderParameter(RectPx, rect.Size);
            rect.Resized += Sized;
            rect.Ready += Sized;
            material = shaded;
            return rect;
        }

        /// <summary>
        /// A gear that turns on the factory's clock (<c>clockwork_gear.gdshader</c>), a notch a
        /// tick: <paramref name="size"/> pixels square. Solid with an axle hole; a wheel of 48
        /// pixels or more is spoked, where there is room for spokes to read.
        /// </summary>
        public static ColorRect Gear(string name, float size, int teeth, Color body, Color light, Color shade)
        {
            ColorRect gear = Make(name, GearShader, out ShaderMaterial material);
            gear.CustomMinimumSize = new Vector2(size, size);
            float radius = Mathf.Floor(size / 2f) - 1f;
            material.SetShaderParameter("turns_per_tick", 1f / TickVisuals.GearTicksPerTurn);
            material.SetShaderParameter("radius_px", radius);
            material.SetShaderParameter("tooth_px", Mathf.Max(2f, Mathf.Round(radius * 0.22f)));
            material.SetShaderParameter("teeth", (float)teeth);
            material.SetShaderParameter("bore_px", Mathf.Max(1.5f, Mathf.Round(radius * 0.16f)));
            material.SetShaderParameter("spokes", size >= 48f ? 6f : 0f);
            material.SetShaderParameter("body_color", body);
            material.SetShaderParameter("light_color", light);
            material.SetShaderParameter("shade_color", shade);
            return gear;
        }
    }
}
