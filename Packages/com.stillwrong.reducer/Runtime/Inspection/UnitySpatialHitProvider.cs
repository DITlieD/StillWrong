using System;
using System.Collections.Generic;
using System.Linq;
using StillWrong.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StillWrong.Inspection
{
    public sealed class SpatialHitElement
    {
        public GameObject TargetObject { get; }
        public Component TargetComponent { get; }
        public string HierarchyPath { get; }
        public Rect2D ScreenRect { get; }
        public float Alpha { get; }
        public bool RaycastTarget { get; }
        public bool IsActiveAndEnabled { get; }
        public float CameraDepth { get; }
        public string SortingLayerName { get; }
        public int SortingOrder { get; }
        public long DrawOrder { get; }
        public bool IsRenderer3D { get; }
        public SpatialElement CoreElement { get; }

        public SpatialHitElement(
            GameObject targetObject,
            Component targetComponent,
            string hierarchyPath,
            Rect2D screenRect,
            float alpha,
            bool raycastTarget,
            bool isActiveAndEnabled,
            float cameraDepth,
            string sortingLayerName,
            int sortingOrder,
            long drawOrder,
            bool isRenderer3D)
        {
            TargetObject = targetObject;
            TargetComponent = targetComponent;
            HierarchyPath = hierarchyPath ?? string.Empty;
            ScreenRect = screenRect;
            Alpha = alpha;
            RaycastTarget = raycastTarget;
            IsActiveAndEnabled = isActiveAndEnabled;
            CameraDepth = cameraDepth;
            SortingLayerName = sortingLayerName ?? "Default";
            SortingOrder = sortingOrder;
            DrawOrder = drawOrder;
            IsRenderer3D = isRenderer3D;

            CoreElement = new SpatialElement(
                HierarchyPath,
                ScreenRect,
                null,
                DrawOrder,
                Alpha,
                RaycastTarget,
                IsActiveAndEnabled,
                CameraDepth,
                SortingLayerName,
                SortingOrder);
        }
    }

    public sealed class UnityDualTargetResult
    {
        public SpatialHitElement VisualSource { get; }
        public SpatialHitElement InputInterceptor { get; }
        public DualTargetResolution Resolution { get; }
        public IReadOnlyList<SpatialHitElement> AllElements { get; }
        public bool HasMismatch => Resolution != null && Resolution.HasMismatch;

        public UnityDualTargetResult(
            SpatialHitElement visualSource,
            SpatialHitElement inputInterceptor,
            DualTargetResolution resolution,
            IEnumerable<SpatialHitElement> allElements)
        {
            VisualSource = visualSource;
            InputInterceptor = inputInterceptor;
            Resolution = resolution;
            AllElements = allElements != null ? allElements.ToList() : (IReadOnlyList<SpatialHitElement>)Array.Empty<SpatialHitElement>();
        }
    }

    public static class UnitySpatialHitProvider
    {
        public static List<SpatialHitElement> CollectAllSpatialElements(Camera camera = null)
        {
            Camera activeCam = camera != null ? camera : Camera.main;
            var elements = new List<SpatialHitElement>();

            CollectUgUiElements(elements, activeCam);
            CollectRendererElements(elements, activeCam);

            return elements;
        }

        public static UnityDualTargetResult ResolveAt(
            Vector2 screenPoint,
            Camera camera = null,
            float visualAlphaThreshold = 0.05f,
            IEnumerable<string> ignoreTokens = null)
        {
            return ResolveAt(new Vec2(screenPoint.x, screenPoint.y), camera, visualAlphaThreshold, ignoreTokens);
        }

        public static UnityDualTargetResult ResolveAt(
            Vec2 screenPoint,
            Camera camera = null,
            float visualAlphaThreshold = 0.05f,
            IEnumerable<string> ignoreTokens = null)
        {
            var hitElements = CollectAllSpatialElements(camera);
            var coreElements = hitElements.Select(h => h.CoreElement).ToList();

            var resolution = SpatialHitResolver.ResolveTargets(
                screenPoint,
                coreElements,
                visualAlphaThreshold,
                false,
                ignoreTokens);

            SpatialHitElement visualHit = null;
            if (resolution.VisualSource != null)
            {
                visualHit = hitElements.FirstOrDefault(h => string.Equals(h.HierarchyPath, resolution.VisualSource.Path, StringComparison.Ordinal));
            }

            SpatialHitElement inputHit = null;
            if (resolution.InputInterceptor != null)
            {
                inputHit = hitElements.FirstOrDefault(h => string.Equals(h.HierarchyPath, resolution.InputInterceptor.Path, StringComparison.Ordinal));
            }

            return new UnityDualTargetResult(visualHit, inputHit, resolution, hitElements);
        }

        public static void CollectUgUiElements(List<SpatialHitElement> list, Camera camera)
        {
            Graphic[] graphics = UnityEngine.Object.FindObjectsByType<Graphic>();
            if (graphics == null) return;

            Vector3[] worldCorners = new Vector3[4];

            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null) continue;

                GameObject go = graphic.gameObject;
                bool active = go.activeInHierarchy && graphic.enabled;
                RectTransform rt = graphic.rectTransform;
                if (rt == null) continue;

                Canvas canvas = graphic.canvas;
                Camera eventCamera = null;
                if (canvas != null)
                {
                    if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        eventCamera = null;
                    }
                    else
                    {
                        eventCamera = canvas.worldCamera != null ? canvas.worldCamera : camera;
                    }
                }

                rt.GetWorldCorners(worldCorners);

                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;

                for (int c = 0; c < 4; c++)
                {
                    Vector2 screenPoint;
                    if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        screenPoint = new Vector2(worldCorners[c].x, worldCorners[c].y);
                    }
                    else if (eventCamera != null)
                    {
                        screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, worldCorners[c]);
                    }
                    else
                    {
                        screenPoint = new Vector2(worldCorners[c].x, worldCorners[c].y);
                    }

                    if (screenPoint.x < minX) minX = screenPoint.x;
                    if (screenPoint.y < minY) minY = screenPoint.y;
                    if (screenPoint.x > maxX) maxX = screenPoint.x;
                    if (screenPoint.y > maxY) maxY = screenPoint.y;
                }

                Rect2D rect = Rect2D.FromMinMax(minX, minY, maxX, maxY);

                float alpha = graphic.color.a;
                CanvasGroup[] canvasGroups = go.GetComponentsInParent<CanvasGroup>();
                for (int cg = 0; cg < canvasGroups.Length; cg++)
                {
                    if (canvasGroups[cg].gameObject.activeInHierarchy)
                    {
                        alpha *= canvasGroups[cg].alpha;
                    }
                }

                int sortingOrder = canvas != null ? canvas.sortingOrder : 0;
                string sortingLayer = canvas != null ? canvas.sortingLayerName : "Default";
                int sibling = rt.GetSiblingIndex();
                long drawOrder = SpatialHitResolver.ComputeDrawOrder(sortingOrder, sibling);
                float depth = eventCamera != null ? (rt.position - eventCamera.transform.position).magnitude : 0f;
                string path = GetHierarchyPath(go);

                list.Add(new SpatialHitElement(
                    go,
                    graphic,
                    path,
                    rect,
                    alpha,
                    graphic.raycastTarget,
                    active,
                    depth,
                    sortingLayer,
                    sortingOrder,
                    drawOrder,
                    false));
            }
        }

        public static void CollectRendererElements(List<SpatialHitElement> list, Camera camera)
        {
            if (camera == null) return;

            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>();
            if (renderers == null) return;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer rend = renderers[i];
                if (rend == null) continue;

                GameObject go = rend.gameObject;
                bool active = go.activeInHierarchy && rend.enabled;

                Bounds bounds = rend.bounds;
                Vector3 center = bounds.center;
                Vector3 ext = bounds.extents;

                Vector3[] corners = new[]
                {
                    new Vector3(center.x - ext.x, center.y - ext.y, center.z - ext.z),
                    new Vector3(center.x + ext.x, center.y - ext.y, center.z - ext.z),
                    new Vector3(center.x - ext.x, center.y + ext.y, center.z - ext.z),
                    new Vector3(center.x + ext.x, center.y + ext.y, center.z - ext.z),
                    new Vector3(center.x - ext.x, center.y - ext.y, center.z + ext.z),
                    new Vector3(center.x + ext.x, center.y - ext.y, center.z + ext.z),
                    new Vector3(center.x - ext.x, center.y + ext.y, center.z + ext.z),
                    new Vector3(center.x + ext.x, center.y + ext.y, center.z + ext.z)
                };

                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;
                int validPoints = 0;

                for (int c = 0; c < 8; c++)
                {
                    Vector3 sp = camera.WorldToScreenPoint(corners[c]);
                    if (sp.z > 0f)
                    {
                        validPoints++;
                        if (sp.x < minX) minX = sp.x;
                        if (sp.y < minY) minY = sp.y;
                        if (sp.x > maxX) maxX = sp.x;
                        if (sp.y > maxY) maxY = sp.y;
                    }
                }

                if (validPoints == 0) continue;

                Rect2D rect = Rect2D.FromMinMax(minX, minY, maxX, maxY);

                float alpha = 1.0f;
                Material mat = rend.sharedMaterial;
                if (mat != null)
                {
                    if (mat.HasProperty("_Color"))
                    {
                        alpha = mat.color.a;
                    }
                    else if (mat.HasProperty("_BaseColor"))
                    {
                        alpha = mat.GetColor("_BaseColor").a;
                    }
                }

                bool hasCollider = go.GetComponent<Collider>() != null || go.GetComponent<Collider2D>() != null;
                int sortingOrder = rend.sortingOrder;
                string sortingLayer = rend.sortingLayerName;
                int sibling = go.transform.GetSiblingIndex();
                long drawOrder = SpatialHitResolver.ComputeDrawOrder(sortingOrder, sibling);
                float depth = Vector3.Dot(go.transform.position - camera.transform.position, camera.transform.forward);
                string path = GetHierarchyPath(go);

                list.Add(new SpatialHitElement(
                    go,
                    rend,
                    path,
                    rect,
                    alpha,
                    hasCollider,
                    active,
                    depth,
                    sortingLayer,
                    sortingOrder,
                    drawOrder,
                    true));
            }
        }

        public static string GetHierarchyPath(GameObject go)
        {
            if (go == null) return string.Empty;
            string path = go.name;
            Transform p = go.transform.parent;
            while (p != null)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }
    }
}
