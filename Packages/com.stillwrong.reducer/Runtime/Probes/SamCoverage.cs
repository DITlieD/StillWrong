using System;
using System.Collections.Generic;
using StillWrong.Core;
using UnityEngine;

namespace StillWrong.Oracle
{
    public static class SamBoxInclusive
    {
        public static double AreaFromCorners(double x1, double y1, double x2, double y2)
        {
            double w = Math.Max(0.0, x2 - x1 + 1.0);
            double h = Math.Max(0.0, y2 - y1 + 1.0);
            return w * h;
        }

        public static double AreaFromSize(double w, double h)
        {
            return Math.Max(0.0, w) * Math.Max(0.0, h);
        }

        public static void ToInclusiveCorners(Rect box, out double x1, out double y1, out double x2, out double y2)
        {
            x1 = box.xMin;
            y1 = box.yMin;
            x2 = box.xMax - 1.0;
            y2 = box.yMax - 1.0;
        }
    }

    public sealed class SamCoverageResult
    {
        public string ElementPath = string.Empty;
        public int ElementId;
        public int PixelsInScope;
        public int ScopePixels;
        public double MaskCoverage;
        public double ElementCoverage;
        public double BoxAreaInclusive;
        public bool IsAnalytic;
        public string Warning = string.Empty;
    }

    public static class SamCoverage
    {
        public const double AmbiguityBand = 0.10;

        public static List<SamCoverageResult> FromBox(Rect box, StillWrongIdMap map, IReadOnlyDictionary<int, string> idToPath, Func<string, Rect2D> rectLookup, HashSet<string> analyticPaths)
        {
            List<SamCoverageResult> results = new List<SamCoverageResult>();
            if (map == null || map.Ids == null || map.Ids.Length == 0)
            {
                return results;
            }
            int x0 = Math.Max(0, Mathf.FloorToInt(box.xMin));
            int y0 = Math.Max(0, Mathf.FloorToInt(box.yMin));
            int x1 = Math.Min(map.Width - 1, Mathf.CeilToInt(box.xMax) - 1);
            int y1 = Math.Min(map.Height - 1, Mathf.CeilToInt(box.yMax) - 1);
            Dictionary<int, int> inScope = new Dictionary<int, int>();
            int scopePixels = 0;
            if (x1 >= x0 && y1 >= y0)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int id = map.Ids[y * map.Width + x];
                        int count = 0;
                        inScope.TryGetValue(id, out count);
                        inScope[id] = count + 1;
                        scopePixels++;
                    }
                }
            }
            double ix1;
            double iy1;
            double ix2;
            double iy2;
            SamBoxInclusive.ToInclusiveCorners(box, out ix1, out iy1, out ix2, out iy2);
            double boxArea = SamBoxInclusive.AreaFromCorners(ix1, iy1, ix2, iy2);
            if (idToPath != null)
            {
                foreach (KeyValuePair<int, string> kv in idToPath)
                {
                    if (kv.Key == 0)
                    {
                        continue;
                    }
                    int owned = 0;
                    inScope.TryGetValue(kv.Key, out owned);
                    int total = 0;
                    map.Counts.TryGetValue(kv.Key, out total);
                    SamCoverageResult entry = new SamCoverageResult();
                    entry.ElementPath = kv.Value;
                    entry.ElementId = kv.Key;
                    entry.PixelsInScope = owned;
                    entry.ScopePixels = scopePixels;
                    entry.MaskCoverage = scopePixels > 0 ? (double)owned / (double)scopePixels : 0.0;
                    entry.ElementCoverage = total > 0 ? (double)owned / (double)total : 0.0;
                    entry.BoxAreaInclusive = boxArea;
                    entry.IsAnalytic = false;
                    results.Add(entry);
                }
            }
            int background = 0;
            inScope.TryGetValue(0, out background);
            if (background > 0)
            {
                SamCoverageResult unowned = new SamCoverageResult();
                unowned.ElementPath = string.Empty;
                unowned.ElementId = 0;
                unowned.PixelsInScope = background;
                unowned.ScopePixels = scopePixels;
                unowned.MaskCoverage = scopePixels > 0 ? (double)background / (double)scopePixels : 0.0;
                unowned.ElementCoverage = 0.0;
                unowned.BoxAreaInclusive = boxArea;
                unowned.IsAnalytic = false;
                unowned.Warning = unowned.MaskCoverage > 0.5 ? "majority-unowned-pixels" : "unowned-pixels-present";
                results.Add(unowned);
            }
            if (analyticPaths != null && rectLookup != null)
            {
                foreach (string path in analyticPaths)
                {
                    bool already = false;
                    for (int i = 0; i < results.Count; i++)
                    {
                        if (string.Equals(results[i].ElementPath, path, StringComparison.Ordinal))
                        {
                            already = true;
                            break;
                        }
                    }
                    if (already)
                    {
                        continue;
                    }
                    Rect2D rect = rectLookup(path);
                    double overlap = OverlapAreaInclusive(box, rect);
                    SamCoverageResult analytic = new SamCoverageResult();
                    analytic.ElementPath = path;
                    analytic.ElementId = 0;
                    analytic.PixelsInScope = (int)Math.Round(overlap);
                    analytic.ScopePixels = scopePixels;
                    analytic.MaskCoverage = scopePixels > 0 ? overlap / (double)Math.Max(1, scopePixels) : 0.0;
                    analytic.ElementCoverage = rect.Area > 0.0 ? overlap / rect.Area : 0.0;
                    analytic.BoxAreaInclusive = boxArea;
                    analytic.IsAnalytic = true;
                    analytic.Warning = "analytic-rect-not-pixels";
                    results.Add(analytic);
                }
            }
            results.Sort(CompareCoverage);
            return results;
        }

        public static List<SamCoverageResult> FromMask(bool[,] mask, StillWrongIdMap map, IReadOnlyDictionary<int, string> idToPath)
        {
            List<SamCoverageResult> results = new List<SamCoverageResult>();
            if (mask == null || map == null || map.Ids == null || map.Ids.Length == 0)
            {
                return results;
            }
            int maskH = mask.GetLength(0);
            int maskW = mask.GetLength(1);
            int w = Math.Min(maskW, map.Width);
            int h = Math.Min(maskH, map.Height);
            Dictionary<int, int> inScope = new Dictionary<int, int>();
            int scopePixels = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!mask[y, x])
                    {
                        continue;
                    }
                    int id = map.Ids[y * map.Width + x];
                    int count = 0;
                    inScope.TryGetValue(id, out count);
                    inScope[id] = count + 1;
                    scopePixels++;
                }
            }
            if (idToPath != null)
            {
                foreach (KeyValuePair<int, string> kv in idToPath)
                {
                    if (kv.Key == 0)
                    {
                        continue;
                    }
                    int owned = 0;
                    inScope.TryGetValue(kv.Key, out owned);
                    int total = 0;
                    map.Counts.TryGetValue(kv.Key, out total);
                    SamCoverageResult entry = new SamCoverageResult();
                    entry.ElementPath = kv.Value;
                    entry.ElementId = kv.Key;
                    entry.PixelsInScope = owned;
                    entry.ScopePixels = scopePixels;
                    entry.MaskCoverage = scopePixels > 0 ? (double)owned / (double)scopePixels : 0.0;
                    entry.ElementCoverage = total > 0 ? (double)owned / (double)total : 0.0;
                    entry.BoxAreaInclusive = scopePixels;
                    entry.IsAnalytic = false;
                    results.Add(entry);
                }
            }
            results.Sort(CompareCoverage);
            return results;
        }

        public static bool IsAmbiguous(List<SamCoverageResult> results)
        {
            if (results == null || results.Count < 2)
            {
                return false;
            }
            SamCoverageResult first = null;
            SamCoverageResult second = null;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].ElementId == 0)
                {
                    continue;
                }
                if (first == null)
                {
                    first = results[i];
                }
                else
                {
                    second = results[i];
                    break;
                }
            }
            if (first == null || second == null)
            {
                return false;
            }
            return Math.Abs(first.MaskCoverage - second.MaskCoverage) <= AmbiguityBand;
        }

        private static int CompareCoverage(SamCoverageResult a, SamCoverageResult b)
        {
            if (a == null || b == null)
            {
                return 0;
            }
            return b.MaskCoverage.CompareTo(a.MaskCoverage);
        }

        private static double OverlapAreaInclusive(Rect box, Rect2D rect)
        {
            double x1;
            double y1;
            double x2;
            double y2;
            SamBoxInclusive.ToInclusiveCorners(box, out x1, out y1, out x2, out y2);
            double rx1 = rect.XMin;
            double ry1 = rect.YMin;
            double rx2 = rect.XMax - 1.0;
            double ry2 = rect.YMax - 1.0;
            return SamBoxInclusive.AreaFromCorners(Math.Max(x1, rx1), Math.Max(y1, ry1), Math.Min(x2, rx2), Math.Min(y2, ry2));
        }
    }
}

