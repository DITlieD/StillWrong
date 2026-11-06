#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;

namespace StillWrong.Core
{
    public sealed class SpatialElement
    {
        public string Path { get; }
        public Rect2D ScreenRect { get; }
        public Rect2D ImageRect { get; }
        public long DrawOrder { get; }
        public float Alpha { get; }
        public bool RaycastTarget { get; }
        public bool IsActiveAndEnabled { get; }
        public float CameraDepth { get; }
        public string SortingLayerName { get; }
        public int SortingOrder { get; }

        public SpatialElement(
            string path,
            Rect2D screenRect,
            Rect2D? imageRect = null,
            long drawOrder = 0,
            float alpha = 1.0f,
            bool raycastTarget = true,
            bool isActiveAndEnabled = true,
            float cameraDepth = 0f,
            string sortingLayerName = "Default",
            int sortingOrder = 0)
        {
            Path = path ?? string.Empty;
            ScreenRect = screenRect;
            ImageRect = imageRect ?? screenRect;
            DrawOrder = drawOrder;
            Alpha = alpha;
            RaycastTarget = raycastTarget;
            IsActiveAndEnabled = isActiveAndEnabled;
            CameraDepth = cameraDepth;
            SortingLayerName = sortingLayerName ?? "Default";
            SortingOrder = sortingOrder;
        }

        public override string ToString() =>
            $"SpatialElement({Path}, Order={DrawOrder}, Alpha={Alpha:F2}, Raycast={RaycastTarget})";
    }

    public sealed class DualTargetResolution
    {
        public SpatialElement? VisualSource { get; }
        public SpatialElement? InputInterceptor { get; }
        public bool HasMismatch =>
            VisualSource != null &&
            InputInterceptor != null &&
            !string.Equals(VisualSource.Path, InputInterceptor.Path, StringComparison.Ordinal);
        public IReadOnlyList<SpatialElement> VisualStack { get; }
        public IReadOnlyList<SpatialElement> InterceptingStack { get; }

        public DualTargetResolution(
            SpatialElement? visualSource,
            SpatialElement? inputInterceptor,
            IEnumerable<SpatialElement>? visualStack = null,
            IEnumerable<SpatialElement>? interceptingStack = null)
        {
            VisualSource = visualSource;
            InputInterceptor = inputInterceptor;
            VisualStack = visualStack != null ? visualStack.ToList() : (IReadOnlyList<SpatialElement>)Array.Empty<SpatialElement>();
            InterceptingStack = interceptingStack != null ? interceptingStack.ToList() : (IReadOnlyList<SpatialElement>)Array.Empty<SpatialElement>();
        }
    }

    public sealed class CameraModel
    {
        public Vec3 Position { get; }
        public Vec3 Forward { get; }
        public Vec3 Up { get; }
        public Vec3 Right { get; }
        public float FovDegrees { get; }
        public float NearClipPlane { get; }
        public float FarClipPlane { get; }
        public float ViewportWidth { get; }
        public float ViewportHeight { get; }
        public int CullingMask { get; }

        public CameraModel(
            Vec3 position,
            Vec3 forward,
            Vec3 up,
            float fovDegrees = 60f,
            float nearClipPlane = 0.3f,
            float farClipPlane = 1000f,
            float viewportWidth = 1920f,
            float viewportHeight = 1080f,
            int cullingMask = ~0)
        {
            Position = position;
            Forward = Normalize(forward);
            Vec3 computedUp = Normalize(up);
            Right = Normalize(Cross(Forward, computedUp));
            Up = Normalize(Cross(Right, Forward));
            FovDegrees = fovDegrees;
            NearClipPlane = nearClipPlane;
            FarClipPlane = farClipPlane;
            ViewportWidth = viewportWidth;
            ViewportHeight = viewportHeight;
            CullingMask = cullingMask;
        }

        public bool WorldToScreenPoint(Vec3 worldPt, out Vec2 screenPt, out float depth)
        {
            Vec3 delta = worldPt - Position;
            float z = Dot(delta, Forward);
            depth = z;

            if (z < NearClipPlane || z > FarClipPlane)
            {
                screenPt = Vec2.Zero;
                return false;
            }

            float x = Dot(delta, Right);
            float y = Dot(delta, Up);

            float aspect = ViewportWidth / Math.Max(1f, ViewportHeight);
            float tanHalfFov = (float)Math.Tan((FovDegrees * Math.PI) / 360.0);

            float ndcX = x / (z * tanHalfFov * aspect);
            float ndcY = y / (z * tanHalfFov);

            float screenX = (ndcX + 1f) * 0.5f * ViewportWidth;
            float screenY = (ndcY + 1f) * 0.5f * ViewportHeight;

            screenPt = new Vec2(screenX, screenY);
            return true;
        }

        private static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        private static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);

        private static Vec3 Normalize(Vec3 v)
        {
            float len = v.Length;
            return len > 0.00001f ? v * (1f / len) : new Vec3(0f, 0f, 1f);
        }
    }

    public sealed class Spatial3DElement
    {
        public string Path { get; }
        public Bounds3D WorldBounds { get; }
        public int Layer { get; }
        public bool IsVisible { get; }

        public Spatial3DElement(string path, Bounds3D worldBounds, int layer = 0, bool isVisible = true)
        {
            Path = path ?? string.Empty;
            WorldBounds = worldBounds;
            Layer = layer;
            IsVisible = isVisible;
        }
    }

    public sealed class Hit3DResult
    {
        public Spatial3DElement Element { get; }
        public Rect2D ProjectedScreenRect { get; }
        public float DistanceFromCamera { get; }

        public Hit3DResult(Spatial3DElement element, Rect2D projectedScreenRect, float distanceFromCamera)
        {
            Element = element;
            ProjectedScreenRect = projectedScreenRect;
            DistanceFromCamera = distanceFromCamera;
        }
    }

    public static class SpatialHitResolver
    {
        public static readonly string[] DefaultOverlayIgnoreTokens = new[] { "BrightnessOverlay" };

        public static Vec2 ScreenPointToImagePoint(Vec2 screenPt, float nativeHeight, float scale = 1.0f)
        {
            float x = screenPt.X * scale;
            float y = (nativeHeight - screenPt.Y) * scale;
            return new Vec2(x, y);
        }

        public static Vec2 ImagePointToScreenPoint(Vec2 imagePt, float nativeHeight, float scale = 1.0f)
        {
            float safeScale = Math.Abs(scale) > 0.00001f ? scale : 1.0f;
            float x = imagePt.X / safeScale;
            float y = nativeHeight - (imagePt.Y / safeScale);
            return new Vec2(x, y);
        }

        public static Rect2D ScreenRectToImageRect(Rect2D screenRect, float nativeHeight, float scale = 1.0f)
        {
            float x = screenRect.XMin * scale;
            float y = (nativeHeight - screenRect.YMax) * scale;
            float w = screenRect.Width * scale;
            float h = screenRect.Height * scale;
            return new Rect2D(x, y, w, h);
        }

        public static Rect2D ImageRectToScreenRect(Rect2D imageRect, float nativeHeight, float scale = 1.0f)
        {
            float safeScale = Math.Abs(scale) > 0.00001f ? scale : 1.0f;
            float x = imageRect.XMin / safeScale;
            float w = imageRect.Width / safeScale;
            float h = imageRect.Height / safeScale;
            float maxY = nativeHeight - (imageRect.YMin / safeScale);
            float minY = maxY - h;
            return new Rect2D(x, minY, w, h);
        }

        public static Vec2 Normalize(Vec2 point, float width, float height)
        {
            float safeW = width > 0f ? width : 1f;
            float safeH = height > 0f ? height : 1f;
            return new Vec2(point.X / safeW, point.Y / safeH);
        }

        public static Vec2 Denormalize(Vec2 normalizedPoint, float width, float height)
        {
            return new Vec2(normalizedPoint.X * width, normalizedPoint.Y * height);
        }

        public static (int Width, int Height, float Scale) FitToCap(int w, int h, int cap = 2048)
        {
            if (w <= cap && h <= cap)
            {
                return (w, h, 1.0f);
            }

            int maxEdge = Math.Max(w, h);
            float scale = (float)cap / maxEdge;
            int fittedW = Math.Max(1, (int)Math.Round(w * scale));
            int fittedH = Math.Max(1, (int)Math.Round(h * scale));
            return (fittedW, fittedH, scale);
        }

        public static long ComputeDrawOrder(int sortingOrder, int hierarchyIndex)
        {
            return (long)sortingOrder * 100000L + hierarchyIndex;
        }

        public static bool IsIgnoredOverlay(string path, IEnumerable<string>? ignoreTokens = null)
        {
            if (string.IsNullOrEmpty(path)) return false;

            var tokens = ignoreTokens ?? DefaultOverlayIgnoreTokens;
            foreach (var token in tokens)
            {
                if (!string.IsNullOrEmpty(token) &&
                    path.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static SpatialElement? ResolveTopmost(
            Vec2 point,
            IEnumerable<SpatialElement> elements,
            bool useImageCoords = false,
            IEnumerable<string>? ignoreTokens = null)
        {
            if (elements == null) return null;

            var matching = elements.Where(e =>
                e.IsActiveAndEnabled &&
                !IsIgnoredOverlay(e.Path, ignoreTokens) &&
                (useImageCoords ? e.ImageRect.Contains(point) : e.ScreenRect.Contains(point)));

            return matching
                .OrderByDescending(e => e.DrawOrder)
                .ThenBy(e => e.ScreenRect.Area)
                .FirstOrDefault();
        }

        public static DualTargetResolution ResolveTargets(
            Vec2 point,
            IEnumerable<SpatialElement> elements,
            float visualAlphaThreshold = 0.05f,
            bool useImageCoords = false,
            IEnumerable<string>? ignoreTokens = null)
        {
            if (elements == null)
            {
                return new DualTargetResolution(null, null);
            }

            var matching = elements
                .Where(e =>
                    e.IsActiveAndEnabled &&
                    !IsIgnoredOverlay(e.Path, ignoreTokens) &&
                    (useImageCoords ? e.ImageRect.Contains(point) : e.ScreenRect.Contains(point)))
                .ToList();

            var visualStack = matching
                .Where(e => e.Alpha > visualAlphaThreshold)
                .OrderByDescending(e => e.DrawOrder)
                .ThenBy(e => e.ScreenRect.Area)
                .ToList();

            var interceptingStack = matching
                .Where(e => e.RaycastTarget)
                .OrderByDescending(e => e.DrawOrder)
                .ThenBy(e => e.ScreenRect.Area)
                .ToList();

            var visualSource = visualStack.FirstOrDefault();
            var inputInterceptor = interceptingStack.FirstOrDefault();

            return new DualTargetResolution(
                visualSource,
                inputInterceptor,
                visualStack,
                interceptingStack);
        }

        public static bool ProjectBoundingBox(
            Bounds3D bounds,
            CameraModel camera,
            out Rect2D screenRect,
            out float minDepth)
        {
            var corners = bounds.GetCorners();
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            minDepth = float.MaxValue;
            int validCount = 0;

            for (int i = 0; i < corners.Length; i++)
            {
                if (camera.WorldToScreenPoint(corners[i], out var pt, out float depth))
                {
                    validCount++;
                    if (pt.X < minX) minX = pt.X;
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y > maxY) maxY = pt.Y;
                    if (depth < minDepth) minDepth = depth;
                }
            }

            if (validCount == 0)
            {
                screenRect = Rect2D.Zero;
                minDepth = 0f;
                return false;
            }

            screenRect = Rect2D.FromMinMax(minX, minY, maxX, maxY);
            return true;
        }

        public static IReadOnlyList<Hit3DResult> HitTest3D(
            Vec2 screenPt,
            IEnumerable<Spatial3DElement> scene3DElements,
            CameraModel camera)
        {
            if (scene3DElements == null) return Array.Empty<Hit3DResult>();

            var hits = new List<Hit3DResult>();

            foreach (var elem in scene3DElements)
            {
                if (!elem.IsVisible) continue;
                if ((camera.CullingMask & (1 << elem.Layer)) == 0) continue;

                if (ProjectBoundingBox(elem.WorldBounds, camera, out var rect, out float depth))
                {
                    if (rect.Contains(screenPt))
                    {
                        hits.Add(new Hit3DResult(elem, rect, depth));
                    }
                }
            }

            hits.Sort((a, b) => a.DistanceFromCamera.CompareTo(b.DistanceFromCamera));
            return hits;
        }
    }
}
