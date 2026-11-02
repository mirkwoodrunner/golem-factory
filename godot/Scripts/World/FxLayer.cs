using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>The short effects (docs/game-feel-design.md §4): each is a sprite timed by one of <see cref="FeelRules"/>' curves.</summary>
    public enum FxKind
    {
        Dust,
        Spark,
        Puff,
        Glint,
        GoodPop,
    }

    /// <summary>
    /// Plays the short effects: dust as a building lands or comes down, the Overclocker's sparks,
    /// a burning boiler's chimney puffs, a good popping out of a crate's lid with a glint. Each is
    /// one sprite timed in real seconds by <see cref="FeelRules"/>, freed when its curve ends.
    /// Above everything standing (z 1), so an effect is never hidden behind what made it.
    ///
    /// <para>
    /// The pictures are <c>res://art/fx_*.png</c>; <c>fx_build_dust</c>, <c>fx_spark</c> and
    /// <c>fx_pop_glint</c> are the S7 set (decision 1). One missing falls back to the steam puff
    /// tinted to its colour, so an effect still plays.
    /// </para>
    /// </summary>
    public partial class FxLayer : Node2D
    {
        public const string GroupName = "fx_layer";

        private const string Puff = "res://art/fx_steam_puff.png";

        private static readonly Dictionary<FxKind, (string Path, Color Fallback)> Art = new Dictionary<FxKind, (string, Color)>
        {
            [FxKind.Dust] = ("res://art/fx_build_dust.png", new Color(0.82f, 0.74f, 0.64f)),
            [FxKind.Spark] = ("res://art/fx_spark.png", new Color(1.0f, 0.70f, 0.30f)),
            [FxKind.Puff] = (Puff, Colors.White),
            [FxKind.Glint] = ("res://art/fx_pop_glint.png", new Color(1.0f, 0.85f, 0.45f)),
        };

        /// <summary>The pictures' file names under <c>res://art/</c>: what the <c>art</c> scenario requires.</summary>
        public static IEnumerable<string> PictureFiles => Art.Values.Select(a => a.Path.Substring("res://art/".Length)).Distinct();

        private readonly Dictionary<FxKind, (Texture2D Texture, Color Tint)> _textures = new Dictionary<FxKind, (Texture2D, Color)>();
        private readonly List<Effect> _live = new List<Effect>();

        /// <summary>How many effects have started, by kind: what a scenario counts.</summary>
        public Dictionary<FxKind, int> Started { get; } = new Dictionary<FxKind, int>();

        public int LiveCount => _live.Count;

        private sealed class Effect
        {
            public FxKind Kind;
            public Sprite2D Sprite;
            public Vector2 Origin;
            public float Age;
            public float Side;
        }

        public static FxLayer Find(Node from) => from.GetTree().GetFirstNodeInGroup(GroupName) as FxLayer;

        public override void _EnterTree() => AddToGroup(GroupName);

        public override void _Ready() => ZIndex = 1;

        /// <summary>Starts an effect at <paramref name="at"/> (a global position). A good's pop takes its icon.</summary>
        public void Spawn(FxKind kind, Vector2 at, Texture2D icon = null)
        {
            at = ToLocal(at);
            Texture2D texture;
            Color tint = Colors.White;
            if (kind == FxKind.GoodPop)
            {
                texture = icon;
            }
            else
            {
                (texture, tint) = TextureFor(kind);
            }
            if (texture == null)
            {
                return;
            }
            var sprite = new Sprite2D { Texture = texture, Modulate = tint, Position = at, Visible = false };
            AddChild(sprite);
            _live.Add(new Effect { Kind = kind, Sprite = sprite, Origin = at, Side = _live.Count % 2 == 0 ? 1f : -1f });
            Started[kind] = (Started.TryGetValue(kind, out int n) ? n : 0) + 1;
        }

        private (Texture2D, Color) TextureFor(FxKind kind)
        {
            if (!_textures.TryGetValue(kind, out (Texture2D Texture, Color Tint) entry))
            {
                (string path, Color fallback) = Art[kind];
                entry = ResourceLoader.Exists(path)
                    ? (GD.Load<Texture2D>(path), Colors.White)
                    : (ResourceLoader.Exists(Puff) ? GD.Load<Texture2D>(Puff) : null, fallback);
                _textures[kind] = entry;
            }
            return entry;
        }

        public override void _Process(double delta)
        {
            float cell = GridConversions.CellPixels;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Effect e = _live[i];
                e.Age += (float)delta;
                bool alive;
                Color tint = e.Sprite.Modulate;
                switch (e.Kind)
                {
                    case FxKind.Dust:
                    {
                        var d = FeelRules.Dust(e.Age);
                        alive = d.alpha > 0f;
                        e.Sprite.Position = e.Origin + new Vector2(e.Side * d.spread, -d.rise) * cell;
                        e.Sprite.Scale = new Vector2(d.scale * e.Side, d.scale);
                        tint.A = d.alpha;
                        break;
                    }
                    case FxKind.Spark:
                    {
                        var s = FeelRules.Spark(e.Age);
                        alive = s.alpha > 0f;
                        e.Sprite.Position = e.Origin + new Vector2(0f, -s.rise) * cell;
                        e.Sprite.Scale = new Vector2(s.scale, s.scale);
                        tint.A = s.alpha;
                        break;
                    }
                    case FxKind.Puff:
                    {
                        var p = Golems.GolemWorkMotion.Puff(e.Age);
                        alive = p.alpha > 0f;
                        e.Sprite.Position = e.Origin + new Vector2(e.Side * p.drift * 0.4f, -p.rise * 3f) * cell;
                        e.Sprite.Scale = new Vector2(p.scale, p.scale);
                        tint.A = p.alpha;
                        break;
                    }
                    case FxKind.Glint:
                    {
                        var g = FeelRules.GoodPop(e.Age);
                        alive = g.alpha > 0f;
                        e.Sprite.Position = e.Origin + new Vector2(0f, -g.rise * 0.6f) * cell;
                        e.Sprite.Scale = new Vector2(g.scale, g.scale);
                        tint.A = g.alpha;
                        break;
                    }
                    default:
                    {
                        var g = FeelRules.GoodPop(e.Age);
                        alive = g.alpha > 0f;
                        e.Sprite.Position = e.Origin + new Vector2(0f, -g.rise) * cell;
                        e.Sprite.Scale = new Vector2(g.scale, g.scale);
                        tint.A = g.alpha;
                        break;
                    }
                }
                e.Sprite.Modulate = tint;
                e.Sprite.Visible = alive;
                if (!alive && e.Age > 0.05f)
                {
                    e.Sprite.QueueFree();
                    _live.RemoveAt(i);
                }
            }
        }
    }
}
