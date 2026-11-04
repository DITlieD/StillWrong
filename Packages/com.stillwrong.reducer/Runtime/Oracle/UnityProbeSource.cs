using System;
using System.Collections.Generic;
using StillWrong.Core;
using StillWrong.Core.Oracle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace StillWrong.Oracle
{
    public sealed class UnityProbeSource : IMeasurementSource
    {
        private readonly Dictionary<string, string> elementPaths;

        public UnityProbeSource(OracleSpec spec)
        {
            elementPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            if (spec != null && spec.Targets != null)
            {
                for (int i = 0; i < spec.Targets.Count; i++)
                {
                    TargetRef target = spec.Targets[i];
                    if (target != null && !string.IsNullOrEmpty(target.Name))
                    {
                        elementPaths[target.Name] = target.ElementId;
                    }
                }
            }
        }

        public bool TargetExists(string targetName, int frameOffset)
        {
            if (string.IsNullOrEmpty(targetName))
            {
                return true;
            }
            string path;
            if (!elementPaths.TryGetValue(targetName, out path))
            {
                return false;
            }
            return UnitySymptomOracleRunner.FindGameObjectByPath(path) != null;
        }

        public bool TryGetValue(ProbeCall probe, int frameOffset, out MeasuredValue value)
        {
            value = null;
            if (probe == null)
            {
                return false;
            }
            switch (probe.Probe)
            {
                case ProbeKind.Active:
                    return TryActive(probe, out value);
                case ProbeKind.RectMinX:
                case ProbeKind.RectMinY:
                case ProbeKind.RectMaxX:
                case ProbeKind.RectMaxY:
                    return TryRect(probe, out value);
                case ProbeKind.OverflowPx:
                    return TryOverflow(probe, out value);
                case ProbeKind.Inside:
                    return TryInside(probe, out value);
                case ProbeKind.Alpha:
                    return TryAlpha(probe, out value);
                case ProbeKind.SortOrder:
                    return TrySortOrder(probe, out value);
                case ProbeKind.ScreenArea:
                    return TryScreenArea(probe, out value);
                case ProbeKind.Layer:
                    return TryLayer(probe, out value);
                case ProbeKind.TopmostAt:
                case ProbeKind.RaycastTargetAt:
                    return TryRaycast(probe, out value);
                case ProbeKind.TextOverflowing:
                    return TryTextOverflowing(probe, out value);
                case ProbeKind.VisiblePixels:
                case ProbeKind.VisibleFraction:
                    return TryVisible(probe, out value);
                case ProbeKind.Culled:
                    return TryCulled(probe, out value);
                case ProbeKind.MeanColorRed:
                case ProbeKind.MeanColorGreen:
                case ProbeKind.MeanColorBlue:
                    return TryMeanColor(probe, out value);
                case ProbeKind.ConsoleErrors:
                    return TryConsoleErrors(out value);
                case ProbeKind.Scale:
                    return TryScale(probe, out value);
                default:
                    value = MeasuredValue.Missing(UnbuiltReason(probe.Probe));
                    return true;
            }
        }

        private bool TryResolve(string targetName, out GameObject go)
        {
            go = null;
            string path;
            if (!elementPaths.TryGetValue(targetName, out path))
            {
                return false;
            }
            go = UnitySymptomOracleRunner.FindGameObjectByPath(path);
            return go != null;
        }

        private bool TryActive(ProbeCall probe, out MeasuredValue value)
        {
            GameObject go;
            if (!TryResolve(probe.Target, out go))
            {
                value = null;
                return false;
            }
            value = MeasuredValue.FromBool(go.activeInHierarchy);
            return true;
        }

        private bool TryScale(ProbeCall probe, out MeasuredValue value)
        {
            GameObject go;
            if (!TryResolve(probe.Target, out go))
            {
                value = null;
                return false;
            }
            UnityEngine.Vector3 local = go.transform.localScale;
            float scale = UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(local.x), UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(local.y), UnityEngine.Mathf.Abs(local.z)));
            value = MeasuredValue.FromNumber(scale);
            return true;
        }

        private bool TryRect(ProbeCall probe, out MeasuredValue value)
        {
            GameObject go;
            if (!TryResolve(probe.Target, out go))
            {
                value = null;
                return false;
            }
            Rect2D rect = UnitySymptomOracleRunner.ComputeScreenRect(go, Camera.main);
            if (probe.Probe == ProbeKind.RectMinX)
            {
                value = MeasuredValue.FromNumber(rect.XMin);
            }
            else if (probe.Probe == ProbeKind.RectMinY)
            {
                value = MeasuredValue.FromNumber(rect.YMin);
            }
            else if (probe.Probe == ProbeKind.RectMaxX)
            {
                value = MeasuredValue.FromNumber(rect.XMax);
            }
            else
            {
                value = MeasuredValue.FromNumber(rect.YMax);
            }
            return true;
        }

        private bool TryOverflow(ProbeCall probe, out MeasuredValue value)
        {
            GameObject target;
            GameObject container;
            if (!TryResolve(probe.Target, out target) || !TryResolve(probe.SecondTarget, out container))
            {
                value = null;
                return false;
            }
            if (target.GetComponent<RectTransform>() == null || container.GetComponent<RectTransform>() == null)
            {
                value = MeasuredValue.Missing("no RectTransform");
                return true;
            }
            Rect2D t = UnitySymptomOracleRunner.ComputeScreenRect(target, Camera.main);
            Rect2D c = UnitySymptomOracleRunner.ComputeScreenRect(container, Camera.main);
            float overflow = c.XMin - t.XMin;
            overflow = Math.Max(overflow, t.XMax - c.XMax);
            overflow = Math.Max(overflow, c.YMin - t.YMin);
            overflow = Math.Max(overflow, t.YMax - c.YMax);
            value = MeasuredValue.FromNumber(Math.Max(0.0f, overflow));
            return true;
        }

        private bool TryInside(ProbeCall probe, out MeasuredValue value)
        {
            GameObject target;
            GameObject container;
            if (!TryResolve(probe.Target, out target) || !TryResolve(probe.SecondTarget, out container))
            {
                value = null;
                return false;
            }
            if (target.GetComponent<RectTransform>() == null || container.GetComponent<RectTransform>() == null)
            {
                value = MeasuredValue.Missing("no RectTransform");
                return true;
            }
            Rect2D t = UnitySymptomOracleRunner.ComputeScreenRect(target, Camera.main);
            Rect2D c = UnitySymptomOracleRunner.ComputeScreenRect(container, Camera.main);
            bool inside = t.XMin >= c.XMin && t.XMax <= c.XMax && t.YMin >= c.YMin && t.YMax <= c.YMax;
            value = MeasuredValue.FromBool(inside);
            return true;
        }

        private bool TryAlpha(ProbeCall probe, out MeasuredValue value)
        {
            GameObject go;
            if (!TryResolve(probe.Target, out go))
            {
                value = null;
                return false;
            }
            Graphic graphic = go.GetComponent<Graphic>();
            float alpha = graphic != null ? graphic.color.a : 1.0f;
            CanvasGroup[] groups = go.GetComponentsInParent<CanvasGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                alpha *= groups[i].alpha;
            }
            value = MeasuredValue.FromNumber(alpha);
            return true;
        }

        private bool TrySortOrder(ProbeCall probe, out MeasuredValue value)
        {
            GameObject go;
            if (!TryResolve(probe.Target, out go))
            {
                value = null;
                return false;
            }
            value = MeasuredValue.FromNumber(UnitySymptomOracleRunner.GetSortingOrder(go));
            return true;
        }

        private bool TryScreenArea(ProbeCall probe, out MeasuredValue value)
        {
            GameObject go;
            if (!TryResolve(probe.Target, out go))
            {
                value = null;
                return false;
            }
            Rect2D rect = UnitySymptomOracleRunner.ComputeScreenRect(go, Camera.main);
            value = MeasuredValue.FromNumber(rect.Area);
            return true;
        }

        private bool TryLayer(ProbeCall probe, out MeasuredValue value)
        {
            GameObject go;
            if (!TryResolve(probe.Target, out go))
            {
                value = null;
                return false;
            }
            value = MeasuredValue.FromNumber(go.layer);
            return true;
        }

        private bool TryRaycast(ProbeCall probe, out MeasuredValue value)
        {
            EventSystem events = EventSystem.current;
            if (events == null)
            {
                value = MeasuredValue.Missing("no-event-system");
                return true;
            }
            if (!probe.PointX.HasValue || !probe.PointY.HasValue)
            {
                value = MeasuredValue.Missing("point-probe-without-coordinates");
                return true;
            }
            var pointer = new PointerEventData(events);
            pointer.position = new Vector2((float)probe.PointX.Value, (float)probe.PointY.Value);
            var hits = new List<RaycastResult>();
            events.RaycastAll(pointer, hits);
            if (hits.Count == 0 || hits[0].gameObject == null)
            {
                value = MeasuredValue.FromText(string.Empty);
                return true;
            }
            value = MeasuredValue.FromText(UnitySymptomOracleRunner.GetHierarchyPath(hits[0].gameObject));
            return true;
        }

        private bool TryTextOverflowing(ProbeCall probe, out MeasuredValue value)
        {
            GameObject go;
            if (!TryResolve(probe.Target, out go))
            {
                value = null;
                return false;
            }
            Text text = go.GetComponent<Text>();
            RectTransform rect = go.GetComponent<RectTransform>();
            TMP_Text tmp = go.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                if (rect == null)
                {
                    value = MeasuredValue.Missing("no-layout-rect");
                    return true;
                }
                value = MeasuredValue.FromBool(tmp.preferredHeight > rect.rect.height + 0.5f);
                return true;
            }
            if (text == null || rect == null)
            {
                value = MeasuredValue.Missing("no-legacy-text-layout");
                return true;
            }
            float preferred = LayoutUtility.GetPreferredHeight(rect);
            value = MeasuredValue.FromBool(preferred > rect.rect.height + 0.5f);
            return true;
        }

        private bool TryVisible(ProbeCall probe, out MeasuredValue value)
        {
            GameObject go;
            if (!TryResolve(probe.Target, out go))
            {
                value = null;
                return false;
            }
            string path = UnitySymptomOracleRunner.GetHierarchyPath(go);
            int id = 0;
            if (!StillWrongIdBuffer.TryGetIdForPath(path, out id) || id == 0)
            {
                value = MeasuredValue.Missing("no-id-buffer");
                return true;
            }
            if (probe.Probe == ProbeKind.VisiblePixels)
            {
                int count = 0;
                if (!StillWrongIdBuffer.TryGetIdCount(id, out count))
                {
                    value = MeasuredValue.Missing("no-id-buffer");
                    return true;
                }
                value = MeasuredValue.FromNumber(count);
                return true;
            }
            double area = 0.0;
            if (!TryExpectedAreaPx(go, path, out area) || area <= 0.0)
            {
                value = MeasuredValue.Missing("no-expected-area");
                return true;
            }
            double fraction = 0.0;
            if (!StillWrongIdBuffer.TryGetIdFraction(id, area, out fraction))
            {
                value = MeasuredValue.Missing("no-id-buffer");
                return true;
            }
            value = MeasuredValue.FromNumber(fraction);
            return true;
        }

        private bool TryExpectedAreaPx(GameObject go, string path, out double area)
        {
            area = 0.0;
            StillWrongIdMap map = StillWrongIdBuffer.LastIdMap;
            if (map == null)
            {
                return false;
            }
            Rect2D rect = Rect2D.Zero;
            bool have = false;
            Graphic graphic = go.GetComponent<Graphic>();
            Renderer renderer = go.GetComponent<Renderer>();
            if (graphic != null)
            {
                rect = UnitySymptomOracleRunner.ComputeScreenRect(go, Camera.main);
                have = true;
            }
            else if (renderer != null)
            {
                rect = UnitySymptomOracleRunner.ComputeRendererScreenRect(renderer, Camera.main);
                have = true;
            }
            else
            {
                UnityEngine.UIElements.UIDocument document = go.GetComponent<UnityEngine.UIElements.UIDocument>();
                if (document != null)
                {
                    Dictionary<string, Rect2D> docs = StillWrongIdBuffer.CollectUidocumentRects(Camera.main);
                    if (docs != null && docs.ContainsKey(path))
                    {
                        rect = docs[path];
                        have = true;
                    }
                }
            }
            if (!have)
            {
                return false;
            }
            area = ClipAreaToFrame(rect, map.Width, map.Height);
            return area > 0.0;
        }
        private static double ClipAreaToFrame(Rect2D rect, int frameW, int frameH)
        {
            double x0 = Math.Max(rect.XMin, 0.0);
            double y0 = Math.Max(rect.YMin, 0.0);
            double x1 = Math.Min(rect.XMax, (double)frameW);
            double y1 = Math.Min(rect.YMax, (double)frameH);
            return Math.Max(0.0, (x1 - x0) * (y1 - y0));
        }
        private bool TryCulled(ProbeCall probe, out MeasuredValue value)
        {
            GameObject go;
            if (!TryResolve(probe.Target, out go))
            {
                value = null;
                return false;
            }
            if (!go.activeInHierarchy)
            {
                value = MeasuredValue.FromBool(false);
                return true;
            }
            string cameraPath = probe.SecondTarget;
            if (!string.IsNullOrEmpty(cameraPath))
            {
                string mapped = null;
                if (elementPaths.TryGetValue(cameraPath, out mapped) && !string.IsNullOrEmpty(mapped))
                {
                    cameraPath = mapped;
                }
            }
            GameObject cameraGo = !string.IsNullOrEmpty(cameraPath) ? UnitySymptomOracleRunner.FindGameObjectByPath(cameraPath) : null;
            Camera camera = cameraGo != null ? cameraGo.GetComponent<Camera>() : Camera.main;
            if (camera == null)
            {
                value = MeasuredValue.Missing("no-camera");
                return true;
            }
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                value = MeasuredValue.FromBool(StillWrongIdBuffer.TestCulledByCamera(renderer, camera));
                return true;
            }
            value = MeasuredValue.Missing("no-culling-probe-for-overlay");
            return true;
        }

        private bool TryMeanColor(ProbeCall probe, out MeasuredValue value)
        {
            GameObject go;
            if (!TryResolve(probe.Target, out go))
            {
                value = null;
                return false;
            }
            Rect2D region = UnitySymptomOracleRunner.ComputeScreenRect(go, Camera.main);
            if (probe.RegionX.HasValue && probe.RegionY.HasValue && probe.RegionW.HasValue && probe.RegionH.HasValue)
            {
                region = Rect2D.FromMinMax((float)probe.RegionX.Value, (float)probe.RegionY.Value, (float)(probe.RegionX.Value + probe.RegionW.Value), (float)(probe.RegionY.Value + probe.RegionH.Value));
            }
            Color color = new Color(0f, 0f, 0f, 1f);
            if (!StillWrongIdBuffer.TryMeanColor(region, out color))
            {
                value = MeasuredValue.Missing("no-color-frame");
                return true;
            }
            if (probe.Probe == ProbeKind.MeanColorRed)
            {
                value = MeasuredValue.FromNumber(color.r);
            }
            else if (probe.Probe == ProbeKind.MeanColorGreen)
            {
                value = MeasuredValue.FromNumber(color.g);
            }
            else
            {
                value = MeasuredValue.FromNumber(color.b);
            }
            return true;
        }

        private bool TryConsoleErrors(out MeasuredValue value)
        {
            value = MeasuredValue.FromNumber(StillWrongIdBuffer.AmbientConsoleErrors);
            return true;
        }

        private static string UnbuiltReason(ProbeKind kind)
        {
            switch (kind)
            {
                case ProbeKind.VisiblePixels:
                case ProbeKind.VisibleFraction:
                    return "no-id-buffer";
                case ProbeKind.Culled:
                    return "no-culling-probe";
                case ProbeKind.MeanColorRed:
                case ProbeKind.MeanColorGreen:
                case ProbeKind.MeanColorBlue:
                    return "no-color-probe";
                case ProbeKind.ConsoleErrors:
                    return "console-errors-travel-in-context";
                default:
                    return "no-live-probe";
            }
        }
    }
}
