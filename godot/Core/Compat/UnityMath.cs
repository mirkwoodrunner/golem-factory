using System;

// The handful of Unity value types and Mathf members the ported rules use, so
// those files move across with one line changed (`using UnityEngine;` becomes
// `using GolemFactory.Compat;`) instead of a rewrite.
//
// Core keeps its own types rather than borrowing Godot's Vector2I: Core has no
// engine reference, which is what lets its tests run under plain `dotnet test`.
// Nodes convert at the boundary (see the Godot project's GridConversions).
//
// Semantics follow Unity where they differ from System.Math, because the ported
// tests pin Unity's answers: RoundToInt rounds half to even, Lerp clamps t, and
// Repeat wraps negatives upward.
namespace GolemFactory.Compat
{
    [Serializable]
    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x;
        public int y;

        public Vector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2Int zero => new Vector2Int(0, 0);
        public static Vector2Int one => new Vector2Int(1, 1);
        public static Vector2Int up => new Vector2Int(0, 1);
        public static Vector2Int down => new Vector2Int(0, -1);
        public static Vector2Int left => new Vector2Int(-1, 0);
        public static Vector2Int right => new Vector2Int(1, 0);

        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new Vector2Int(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new Vector2Int(a.x - b.x, a.y - b.y);
        public static Vector2Int operator -(Vector2Int a) => new Vector2Int(-a.x, -a.y);
        public static Vector2Int operator *(Vector2Int a, int k) => new Vector2Int(a.x * k, a.y * k);
        public static Vector2Int operator *(int k, Vector2Int a) => new Vector2Int(a.x * k, a.y * k);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2Int a, Vector2Int b) => !(a == b);

        public static implicit operator Vector2(Vector2Int v) => new Vector2(v.x, v.y);

        public bool Equals(Vector2Int other) => this == other;
        public override bool Equals(object obj) => obj is Vector2Int other && this == other;
        public override int GetHashCode() => HashCode.Combine(x, y);
        public override string ToString() => $"({x}, {y})";
    }

    [Serializable]
    public struct Vector2 : IEquatable<Vector2>
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
        public static Vector2 up => new Vector2(0f, 1f);
        public static Vector2 down => new Vector2(0f, -1f);
        public static Vector2 left => new Vector2(-1f, 0f);
        public static Vector2 right => new Vector2(1f, 0f);

        public float sqrMagnitude => x * x + y * y;
        public float magnitude => MathF.Sqrt(sqrMagnitude);

        public Vector2 normalized
        {
            get
            {
                float m = magnitude;
                return m > 1e-5f ? new Vector2(x / m, y / m) : zero;
            }
        }

        public static Vector2 ClampMagnitude(Vector2 v, float maxLength)
        {
            float sq = v.sqrMagnitude;
            if (sq <= maxLength * maxLength)
            {
                return v;
            }
            float m = MathF.Sqrt(sq);
            return new Vector2(v.x / m * maxLength, v.y / m * maxLength);
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float k) => new Vector2(a.x * k, a.y * k);
        public static Vector2 operator *(float k, Vector2 a) => new Vector2(a.x * k, a.y * k);
        public static Vector2 operator /(Vector2 a, float k) => new Vector2(a.x / k, a.y / k);

        // Unity's == is approximate (squared distance under 1e-10), and tests that
        // compare computed positions rely on it.
        public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);

        public bool Equals(Vector2 other) => x.Equals(other.x) && y.Equals(other.y);
        public override bool Equals(object obj) => obj is Vector2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public override string ToString() => $"({x:F2}, {y:F2})";
    }

    [Serializable]
    public struct Vector3 : IEquatable<Vector3>
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z = 0f)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 down => new Vector3(0f, -1f, 0f);
        public static Vector3 left => new Vector3(-1f, 0f, 0f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);

        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);

        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;

        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float k) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator *(float k, Vector3 a) => new Vector3(a.x * k, a.y * k, a.z * k);
        public static Vector3 operator /(Vector3 a, float k) => new Vector3(a.x / k, a.y / k, a.z / k);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);

        public bool Equals(Vector3 other) => x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z);
        public override bool Equals(object obj) => obj is Vector3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})";
    }

    // Only BeltSignalUtility's colour maths needs this: Lerp, white, and the channels.
    [Serializable]
    public struct Color : IEquatable<Color>
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        /// <summary>Unity's perceived-luminance greyscale: 0.299 r + 0.587 g + 0.114 b.</summary>
        public float grayscale => 0.299f * r + 0.587f * g + 0.114f * b;

        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f, 1f);
        public static Color clear => new Color(0f, 0f, 0f, 0f);

        public static Color Lerp(Color x, Color y, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(x.r + (y.r - x.r) * t, x.g + (y.g - x.g) * t,
                x.b + (y.b - x.b) * t, x.a + (y.a - x.a) * t);
        }

        public static Color operator *(Color x, float k) => new Color(x.r * k, x.g * k, x.b * k, x.a * k);
        public static Color operator *(float k, Color x) => x * k;
        public static Color operator *(Color x, Color y) => new Color(x.r * y.r, x.g * y.g, x.b * y.b, x.a * y.a);

        // Unity compares colours approximately, like vectors.
        public static bool operator ==(Color x, Color y) =>
            MathF.Abs(x.r - y.r) < 1e-5f && MathF.Abs(x.g - y.g) < 1e-5f &&
            MathF.Abs(x.b - y.b) < 1e-5f && MathF.Abs(x.a - y.a) < 1e-5f;
        public static bool operator !=(Color x, Color y) => !(x == y);

        public bool Equals(Color other) => r.Equals(other.r) && g.Equals(other.g) && b.Equals(other.b) && a.Equals(other.a);
        public override bool Equals(object obj) => obj is Color other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(r, g, b, a);
        public override string ToString() => $"RGBA({r:F3}, {g:F3}, {b:F3}, {a:F3})";
    }

    // Only TechTreeChartLayout needs this: a position and a size.
    [Serializable]
    public struct Rect : IEquatable<Rect>
    {
        public float x;
        public float y;
        public float width;
        public float height;

        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public float xMin => x;
        public float yMin => y;
        public float xMax => x + width;
        public float yMax => y + height;
        public bool Overlaps(Rect other) =>
            other.xMax > xMin && other.xMin < xMax && other.yMax > yMin && other.yMin < yMax;

        public Vector2 center => new Vector2(x + width / 2f, y + height / 2f);

        public static bool operator ==(Rect l, Rect r) => l.Equals(r);
        public static bool operator !=(Rect l, Rect r) => !l.Equals(r);

        public bool Equals(Rect other) =>
            x.Equals(other.x) && y.Equals(other.y) && width.Equals(other.width) && height.Equals(other.height);
        public override bool Equals(object obj) => obj is Rect other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, width, height);
        public override string ToString() => $"(x:{x:F2}, y:{y:F2}, width:{width:F2}, height:{height:F2})";
    }

    public static class Mathf
    {
        public const float PI = MathF.PI;
        public const float Rad2Deg = 180f / MathF.PI;
        public const float Deg2Rad = MathF.PI / 180f;
        public const float Epsilon = float.Epsilon;

        public static int Abs(int v) => Math.Abs(v);
        public static float Abs(float v) => MathF.Abs(v);

        public static int Min(int a, int b) => a < b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;

        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        // Unity: (int)Math.Round(f), which is round-half-to-even.
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);

        public static float Repeat(float t, float length) => Clamp(t - MathF.Floor(t / length) * length, 0f, length);

        public static float Round(float f) => MathF.Round(f);
        public static float Pow(float f, float p) => MathF.Pow(f, p);

        // Unity's tolerance: relative to the larger magnitude, with a floor near zero.
        public static bool Approximately(float a, float b) =>
            MathF.Abs(b - a) < MathF.Max(1e-6f * MathF.Max(MathF.Abs(a), MathF.Abs(b)), float.Epsilon * 8f);

        public static float Sin(float f) => MathF.Sin(f);
        public static float Cos(float f) => MathF.Cos(f);
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
        public static float Sqrt(float f) => MathF.Sqrt(f);
    }
}
