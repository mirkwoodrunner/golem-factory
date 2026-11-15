using System;
using Godot;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Many copies of one shaded quad in one draw call: a <see cref="MultiMesh"/> whose instances
    /// each carry a 2D transform and four floats the shader reads as <c>INSTANCE_CUSTOM</c>.
    /// <see cref="TickVisualsNode"/> keeps one for the belts, one for the hot pipes and one for the
    /// splitters' gears.
    ///
    /// <para>
    /// The instances live in one float array, filled by <see cref="Add"/> and handed to the
    /// renderer whole by <see cref="Commit"/>. The array only grows (doubling), so refilling a
    /// batch that has reached its size allocates nothing; and nothing is sent between commits,
    /// because the motion is the shader's, off the global tick phase.
    /// </para>
    /// </summary>
    public sealed class TileBatch
    {
        // A MultiMesh's buffer, per instance: a 2D transform as two rows of four, then the custom data.
        private const int TransformFloats = 8;
        private const int Stride = TransformFloats + 4;

        private readonly MultiMesh _mesh;
        private float[] _buffer;
        private int _capacity;
        private int _count;

        /// <summary>The node that draws the batch; add it to the tree.</summary>
        public MultiMeshInstance2D Node { get; }

        public ShaderMaterial Material { get; }

        /// <summary>Instances drawn.</summary>
        public int Count => _count;

        /// <summary>Instances the buffer holds before it grows.</summary>
        public int Capacity => _capacity;

        /// <summary>How many times the buffer has been handed to the renderer.</summary>
        public int Commits { get; private set; }

        public TileBatch(string name, Shader shader, float quadPixels, int z, int capacity = 64)
        {
            Material = new ShaderMaterial { Shader = shader };
            _mesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                UseCustomData = true,
                Mesh = new QuadMesh { Size = new Vector2(quadPixels, quadPixels) },
            };
            Node = new MultiMeshInstance2D
            {
                Name = name,
                Multimesh = _mesh,
                Material = Material,
                ZIndex = z,
                ZAsRelative = false,
            };
            Reserve(capacity);
            _mesh.VisibleInstanceCount = 0;
        }

        /// <summary>Empties the batch, ready to be filled again.</summary>
        public void Begin() => _count = 0;

        /// <summary>
        /// One quad centred on <paramref name="position"/>, turned by <paramref name="rotation"/>
        /// radians, with <paramref name="custom"/> as its <c>INSTANCE_CUSTOM</c>.
        /// </summary>
        public void Add(Vector2 position, float rotation, Color custom)
        {
            if (_count == _capacity)
            {
                Reserve(_capacity * 2);
            }
            float cos = Mathf.Cos(rotation), sin = Mathf.Sin(rotation);
            int at = _count * Stride;
            // Rows of the transform: (x.x, y.x, 0, origin.x) then (x.y, y.y, 0, origin.y).
            _buffer[at + 0] = cos;
            _buffer[at + 1] = -sin;
            _buffer[at + 2] = 0f;
            _buffer[at + 3] = position.X;
            _buffer[at + 4] = sin;
            _buffer[at + 5] = cos;
            _buffer[at + 6] = 0f;
            _buffer[at + 7] = position.Y;
            _buffer[at + 8] = custom.R;
            _buffer[at + 9] = custom.G;
            _buffer[at + 10] = custom.B;
            _buffer[at + 11] = custom.A;
            _count++;
        }

        /// <summary>Hands the filled instances to the renderer: one call, however many there are.</summary>
        public void Commit()
        {
            // Past the count the buffer holds whatever an earlier fill left; park those instances
            // on the first one's spot so the batch's bounds are the drawn instances'.
            for (int i = _count; i < _capacity; i++)
            {
                Array.Copy(_buffer, 0, _buffer, i * Stride, Stride);
            }
            RenderingServer.MultimeshSetBuffer(_mesh.GetRid(), _buffer);
            _mesh.VisibleInstanceCount = _count;
            Commits++;
        }

        private void Reserve(int capacity)
        {
            var grown = new float[capacity * Stride];
            if (_buffer != null)
            {
                Array.Copy(_buffer, grown, _count * Stride);
            }
            _buffer = grown;
            _capacity = capacity;
            // Setting the count sizes the renderer's own buffer, which Commit then fills.
            _mesh.InstanceCount = capacity;
            _mesh.VisibleInstanceCount = _count;
        }
    }
}
