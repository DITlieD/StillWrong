using System;

namespace StillWrong.Core
{
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public float X { get; }
        public float Y { get; }

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => new Vec2(0f, 0f);
        public static Vec2 One => new Vec2(1f, 1f);

        public float LengthSquared => X * X + Y * Y;
        public float Length => (float)Math.Sqrt(LengthSquared);

        public float DistanceTo(Vec2 other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float scalar) => new Vec2(a.X * scalar, a.Y * scalar);
        public static Vec2 operator /(Vec2 a, float scalar) => new Vec2(a.X / scalar, a.Y / scalar);
        public static bool operator ==(Vec2 a, Vec2 b) => Math.Abs(a.X - b.X) < 0.0001f && Math.Abs(a.Y - b.Y) < 0.0001f;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public bool Equals(Vec2 other) => this == other;

        public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }

        public override string ToString() => $"({X:F2}, {Y:F2})";
    }

    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero => new Vec3(0f, 0f, 0f);
        public static Vec3 One => new Vec3(1f, 1f, 1f);

        public float LengthSquared => X * X + Y * Y + Z * Z;
        public float Length => (float)Math.Sqrt(LengthSquared);

        public float DistanceTo(Vec3 other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, float scalar) => new Vec3(a.X * scalar, a.Y * scalar, a.Z * scalar);
        public static bool operator ==(Vec3 a, Vec3 b) => Math.Abs(a.X - b.X) < 0.0001f && Math.Abs(a.Y - b.Y) < 0.0001f && Math.Abs(a.Z - b.Z) < 0.0001f;
        public static bool operator !=(Vec3 a, Vec3 b) => !(a == b);

        public bool Equals(Vec3 other) => this == other;

        public override bool Equals(object? obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                return (hash * 397) ^ Z.GetHashCode();
            }
        }

        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }

    public readonly struct Rect2D : IEquatable<Rect2D>
    {
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        public Rect2D(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public static Rect2D Zero => new Rect2D(0f, 0f, 0f, 0f);

        public float XMin => X;
        public float YMin => Y;
        public float XMax => X + Width;
        public float YMax => Y + Height;

        public float Area => Math.Max(0f, Width) * Math.Max(0f, Height);

        public Vec2 Center => new Vec2(X + Width * 0.5f, Y + Height * 0.5f);

        public static Rect2D FromMinMax(float minX, float minY, float maxX, float maxY)
        {
            return new Rect2D(minX, minY, Math.Max(0f, maxX - minX), Math.Max(0f, maxY - minY));
        }

        public bool Contains(Vec2 point)
        {
            return point.X >= XMin && point.X <= XMax && point.Y >= YMin && point.Y <= YMax;
        }

        public bool Overlaps(Rect2D other)
        {
            return XMin < other.XMax && XMax > other.XMin && YMin < other.YMax && YMax > other.YMin;
        }

        public Rect2D Intersection(Rect2D other)
        {
            float minX = Math.Max(XMin, other.XMin);
            float minY = Math.Max(YMin, other.YMin);
            float maxX = Math.Min(XMax, other.XMax);
            float maxY = Math.Min(YMax, other.YMax);

            if (maxX <= minX || maxY <= minY)
            {
                return Zero;
            }

            return FromMinMax(minX, minY, maxX, maxY);
        }

        public static bool operator ==(Rect2D a, Rect2D b)
        {
            return Math.Abs(a.X - b.X) < 0.0001f &&
                   Math.Abs(a.Y - b.Y) < 0.0001f &&
                   Math.Abs(a.Width - b.Width) < 0.0001f &&
                   Math.Abs(a.Height - b.Height) < 0.0001f;
        }

        public static bool operator !=(Rect2D a, Rect2D b) => !(a == b);

        public bool Equals(Rect2D other) => this == other;

        public override bool Equals(object? obj) => obj is Rect2D other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Width.GetHashCode();
                return (hash * 397) ^ Height.GetHashCode();
            }
        }

        public override string ToString() => $"[({X:F1}, {Y:F1}), {Width:F1}x{Height:F1}]";
    }

    public readonly struct Bounds3D
    {
        public Vec3 Center { get; }
        public Vec3 Size { get; }

        public Bounds3D(Vec3 center, Vec3 size)
        {
            Center = center;
            Size = size;
        }

        public Vec3 Extents => Size * 0.5f;
        public Vec3 Min => Center - Extents;
        public Vec3 Max => Center + Extents;

        public Vec3[] GetCorners()
        {
            Vec3 min = Min;
            Vec3 max = Max;
            return new[]
            {
                new Vec3(min.X, min.Y, min.Z),
                new Vec3(max.X, min.Y, min.Z),
                new Vec3(max.X, max.Y, min.Z),
                new Vec3(min.X, max.Y, min.Z),
                new Vec3(min.X, min.Y, max.Z),
                new Vec3(max.X, min.Y, max.Z),
                new Vec3(max.X, max.Y, max.Z),
                new Vec3(min.X, max.Y, max.Z)
            };
        }
    }

    public sealed class SessionQualifiedId : IEquatable<SessionQualifiedId>
    {
        public string AssetGuid { get; }
        public string LocalFileId { get; }
        public string PrefabPath { get; }
        public string HierarchyPath { get; }
        public string ComponentType { get; }
        public int ComponentIndex { get; }

        public SessionQualifiedId(
            string assetGuid = "",
            string localFileId = "",
            string prefabPath = "",
            string hierarchyPath = "",
            string componentType = "",
            int componentIndex = 0)
        {
            AssetGuid = assetGuid ?? string.Empty;
            LocalFileId = localFileId ?? string.Empty;
            PrefabPath = prefabPath ?? string.Empty;
            HierarchyPath = hierarchyPath ?? string.Empty;
            ComponentType = componentType ?? string.Empty;
            ComponentIndex = componentIndex;
        }

        public bool Equals(SessionQualifiedId? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(AssetGuid, other.AssetGuid, StringComparison.Ordinal) &&
                   string.Equals(LocalFileId, other.LocalFileId, StringComparison.Ordinal) &&
                   string.Equals(PrefabPath, other.PrefabPath, StringComparison.Ordinal) &&
                   string.Equals(HierarchyPath, other.HierarchyPath, StringComparison.Ordinal) &&
                   string.Equals(ComponentType, other.ComponentType, StringComparison.Ordinal) &&
                   ComponentIndex == other.ComponentIndex;
        }

        public override bool Equals(object? obj) => Equals(obj as SessionQualifiedId);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = AssetGuid.GetHashCode();
                hash = (hash * 397) ^ LocalFileId.GetHashCode();
                hash = (hash * 397) ^ PrefabPath.GetHashCode();
                hash = (hash * 397) ^ HierarchyPath.GetHashCode();
                hash = (hash * 397) ^ ComponentType.GetHashCode();
                return (hash * 397) ^ ComponentIndex.GetHashCode();
            }
        }

        public override string ToString() =>
            $"SessionQualifiedId({HierarchyPath}:{ComponentType}[{ComponentIndex}])";
    }

    public enum TargetClassification
    {
        Unknown,
        VisualSource,
        InputInterceptor,
        Occluder,
        Distractor,
        Background,
        InvariantGuard
    }

    public enum OracleVerdict
    {
        StillWrong,
        HealthyPass,
        Inconclusive
    }
}
