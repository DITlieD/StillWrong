using System;
using System.Collections.Generic;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StillWrong.Oracle
{
    public static class UnitySymptomOracleRunner
    {
        public static OracleSpec SpecById(string specId)
        {
            List<OracleSpec> specs = FixtureSpecs.All();
            for (int i = 0; i < specs.Count; i++)
            {
                if (string.Equals(specs[i].SpecId, specId, StringComparison.Ordinal))
                {
                    return specs[i];
                }
            }
            return null;
        }

        public static OracleResult Evaluate(OracleSpec spec, IMeasurementSource source)
        {
            return Evaluate(spec, source, new EvaluationContext());
        }

        public static OracleResult Evaluate(OracleSpec spec, IMeasurementSource source, EvaluationContext context)
        {
            return OracleEvaluator.Evaluate(spec, source, context);
        }

        public static OracleResult EvaluateLive(OracleSpec spec)
        {
            return Evaluate(spec, new UnityProbeSource(spec), new EvaluationContext());
        }

        public static GameObject FindGameObjectByPath(string hierarchyPath)
        {
            if (string.IsNullOrEmpty(hierarchyPath)) return null;

            GameObject direct = GameObject.Find(hierarchyPath);
            if (direct != null) return direct;

            string[] segments = hierarchyPath.Split(new string[] { "/" }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) return null;

            GameObject[] roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                if (string.Equals(roots[r].name, segments[0], StringComparison.Ordinal))
                {
                    Transform curr = roots[r].transform;
                    bool match = true;
                    for (int s = 1; s < segments.Length; s++)
                    {
                        Transform next = curr.Find(segments[s]);
                        if (next == null)
                        {
                            match = false;
                            break;
                        }
                        curr = next;
                    }
                    if (match) return curr.gameObject;
                }
            }

            var all = UnityEngine.Object.FindObjectsByType<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                string p = GetHierarchyPath(all[i]);
                if (string.Equals(p, hierarchyPath, StringComparison.Ordinal))
                {
                    return all[i];
                }
            }

            return null;
        }
        public static Rect2D ComputeScreenRect(GameObject go, Camera camera)
        {
            if (go == null) return Rect2D.Zero;

            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                Vector3[] corners = new Vector3[4];
                rt.GetWorldCorners(corners);

                Canvas canvas = go.GetComponentInParent<Canvas>();
                Camera eventCam = null;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    eventCam = canvas.worldCamera != null ? canvas.worldCamera : camera;
                }

                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;

                for (int i = 0; i < 4; i++)
                {
                    Vector2 sp;
                    if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    {
                        sp = new Vector2(corners[i].x, corners[i].y);
                    }
                    else if (eventCam != null)
                    {
                        sp = RectTransformUtility.WorldToScreenPoint(eventCam, corners[i]);
                    }
                    else
                    {
                        sp = new Vector2(corners[i].x, corners[i].y);
                    }

                    if (sp.x < minX) minX = sp.x;
                    if (sp.y < minY) minY = sp.y;
                    if (sp.x > maxX) maxX = sp.x;
                    if (sp.y > maxY) maxY = sp.y;
                }

                return Rect2D.FromMinMax(minX, minY, maxX, maxY);
            }

            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                return ComputeRendererScreenRect(rend, camera);
            }

            return Rect2D.Zero;
        }

        public static Rect2D ComputeRendererScreenRect(Renderer rend, Camera camera)
        {
            if (rend == null || camera == null) return Rect2D.Zero;

            Bounds b = rend.bounds;
            Vector3 center = b.center;
            Vector3 ext = b.extents;

            Vector3[] pts = new[]
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
            int count = 0;

            for (int i = 0; i < 8; i++)
            {
                Vector3 sp = camera.WorldToScreenPoint(pts[i]);
                if (sp.z > 0f)
                {
                    count++;
                    if (sp.x < minX) minX = sp.x;
                    if (sp.y < minY) minY = sp.y;
                    if (sp.x > maxX) maxX = sp.x;
                    if (sp.y > maxY) maxY = sp.y;
                }
            }

            return count > 0 ? Rect2D.FromMinMax(minX, minY, maxX, maxY) : Rect2D.Zero;
        }

        public static int GetSortingOrder(GameObject go)
        {
            if (go == null) return 0;
            Canvas canvas = go.GetComponentInParent<Canvas>();
            if (canvas != null) return canvas.sortingOrder;
            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null) return rend.sortingOrder;
            return 0;
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
