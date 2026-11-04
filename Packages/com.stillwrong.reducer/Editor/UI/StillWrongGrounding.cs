using System;
using System.Collections.Generic;
using StillWrong.Core;
using StillWrong.Inspection;
using StillWrong.Oracle;
using UnityEngine;

namespace StillWrong.Editor.UI
{
    public sealed class StillWrongGroundingPoint
    {
        public Vector2 Point;
        public bool IsNegative;
    }

    public sealed class StillWrongGroundingRequest
    {
        public Rect Box;
        public bool HasBox;
        public List<Vector2> PositivePoints = new List<Vector2>();
        public List<Vector2> NegativePoints = new List<Vector2>();
        public bool[,] SamMask;
        public int SamMaskWidth;
        public int SamMaskHeight;
        public string Sentence = string.Empty;
    }

    public sealed class StillWrongGroundingCandidate
    {
        public string ElementPath = string.Empty;
        public int ElementId;
        public double MaskCoverage;
        public double ElementCoverage;
        public double BoxAreaInclusive;
        public bool IsAnalytic;
        public bool IsConfirmedTarget;
        public string Warning = string.Empty;
    }

    public sealed class StillWrongGroundingResult
    {
        public List<StillWrongGroundingCandidate> Candidates = new List<StillWrongGroundingCandidate>();
        public bool IsAmbiguous;
        public bool UsedSamMask;
        public bool UsedIdBuffer;
        public string DegradedReason = string.Empty;
        public int MaskAreaPx;
        public double MaskAreaMedianRatio;
    }

    public interface IStillWrongGroundingService
    {
        StillWrongGroundingResult Ground(StillWrongGroundingRequest request);
    }

    public static class StillWrongGrounding
    {
        private static IStillWrongGroundingService _service;

        public static void SetService(IStillWrongGroundingService service)
        {
            _service = service;
        }

        public static IStillWrongGroundingService ActiveService
        {
            get
            {
                if (_service == null)
                {
                    _service = new StillWrongDegradedGroundingService();
                }
                return _service;
            }
        }

        public static bool IsCustomService
        {
            get { return _service != null; }
        }
    }

    public sealed class StillWrongDegradedGroundingService : IStillWrongGroundingService
    {
        public StillWrongGroundingResult Ground(StillWrongGroundingRequest request)
        {
            StillWrongGroundingResult result = new StillWrongGroundingResult();
            if (request == null)
            {
                result.DegradedReason = "empty-request";
                return result;
            }
            List<SpatialHitElement> elements = UnitySpatialHitProvider.CollectAllSpatialElements();
            if (request.SamMask != null)
            {
                GroundWithMask(request, elements, result);
                return result;
            }
            GroundWithBox(request, elements, result);
            return result;
        }

        private void GroundWithMask(StillWrongGroundingRequest request, List<SpatialHitElement> elements, StillWrongGroundingResult result)
        {
            StillWrongIdMap map = StillWrongIdBuffer.LastIdMap;
            if (map == null || map.Ids == null || map.Ids.Length == 0)
            {
                result.DegradedReason = "sam-mask-without-id-buffer";
                GroundWithBox(request, elements, result);
                return;
            }
            Dictionary<string, int> pathToId = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < elements.Count; i++)
            {
                int id = 0;
                if (StillWrongIdBuffer.TryGetIdForPath(elements[i].HierarchyPath, out id) && id != 0 && !pathToId.ContainsKey(elements[i].HierarchyPath))
                {
                    pathToId.Add(elements[i].HierarchyPath, id);
                }
            }
            Dictionary<int, string> idToPath = new Dictionary<int, string>();
            foreach (KeyValuePair<string, int> kv in pathToId)
            {
                if (!idToPath.ContainsKey(kv.Value))
                {
                    idToPath.Add(kv.Value, kv.Key);
                }
            }
            List<SamCoverageResult> coverage = SamCoverage.FromMask(request.SamMask, map, idToPath);
            result.UsedSamMask = true;
            result.UsedIdBuffer = true;
            int maskArea = 0;
            for (int y = 0; y < request.SamMask.GetLength(0); y++)
            {
                for (int x = 0; x < request.SamMask.GetLength(1); x++)
                {
                    if (request.SamMask[y, x])
                    {
                        maskArea++;
                    }
                }
            }
            result.MaskAreaPx = maskArea;
            for (int i = 0; i < coverage.Count; i++)
            {
                SamCoverageResult entry = coverage[i];
                if (entry.ElementId == 0)
                {
                    continue;
                }
                StillWrongGroundingCandidate candidate = new StillWrongGroundingCandidate();
                candidate.ElementPath = entry.ElementPath;
                candidate.ElementId = entry.ElementId;
                candidate.MaskCoverage = entry.MaskCoverage;
                candidate.ElementCoverage = entry.ElementCoverage;
                candidate.BoxAreaInclusive = entry.BoxAreaInclusive;
                candidate.IsAnalytic = entry.IsAnalytic;
                candidate.Warning = entry.Warning;
                result.Candidates.Add(candidate);
            }
            result.IsAmbiguous = SamCoverage.IsAmbiguous(coverage);
        }

        private void GroundWithBox(StillWrongGroundingRequest request, List<SpatialHitElement> elements, StillWrongGroundingResult result)
        {
            result.UsedSamMask = false;
            StillWrongIdMap map = StillWrongIdBuffer.LastIdMap;
            if (map != null && map.Ids != null && map.Ids.Length > 0 && request.HasBox)
            {
                GroundBoxWithIdBuffer(request, map, result);
                return;
            }
            result.UsedIdBuffer = false;
            result.DegradedReason = "box-overlap-only-no-sam-key";
            if (!request.HasBox && request.PositivePoints.Count == 0)
            {
                return;
            }
            for (int i = 0; i < elements.Count; i++)
            {
                SpatialHitElement element = elements[i];
                if (element == null || !element.IsActiveAndEnabled)
                {
                    continue;
                }
                double maskCoverage = 0.0;
                double elementCoverage = 0.0;
                if (request.HasBox)
                {
                    double ix1;
                    double iy1;
                    double ix2;
                    double iy2;
                    SamBoxInclusive.ToInclusiveCorners(request.Box, out ix1, out iy1, out ix2, out iy2);
                    double boxArea = SamBoxInclusive.AreaFromCorners(ix1, iy1, ix2, iy2);
                    double overlap = OverlapArea(request.Box, element.ScreenRect);
                    maskCoverage = boxArea > 0.0 ? overlap / boxArea : 0.0;
                    elementCoverage = element.ScreenRect.Area > 0.0 ? overlap / element.ScreenRect.Area : 0.0;
                    if (overlap <= 0.0)
                    {
                        continue;
                    }
                }
                else
                {
                    bool hit = false;
                    for (int p = 0; p < request.PositivePoints.Count; p++)
                    {
                        if (ContainsPoint(element.ScreenRect, request.PositivePoints[p]))
                        {
                            hit = true;
                            break;
                        }
                    }
                    if (!hit)
                    {
                        continue;
                    }
                    double area = element.ScreenRect.Area;
                    maskCoverage = area > 0.0 ? 1.0 / area : 0.0;
                    elementCoverage = maskCoverage;
                }
                StillWrongGroundingCandidate candidate = new StillWrongGroundingCandidate();
                candidate.ElementPath = element.HierarchyPath;
                candidate.ElementId = 0;
                candidate.MaskCoverage = maskCoverage;
                candidate.ElementCoverage = elementCoverage;
                candidate.BoxAreaInclusive = request.HasBox ? BoxArea(request.Box) : 1.0;
                candidate.IsAnalytic = false;
                result.Candidates.Add(candidate);
            }
            result.Candidates.Sort(CompareCandidates);
            result.IsAmbiguous = IsAmbiguousOverlap(result.Candidates);
        }

        private void GroundBoxWithIdBuffer(StillWrongGroundingRequest request, StillWrongIdMap map, StillWrongGroundingResult result)
        {
            Dictionary<int, string> idToPath = new Dictionary<int, string>();
            List<SpatialHitElement> elements = UnitySpatialHitProvider.CollectAllSpatialElements();
            for (int i = 0; i < elements.Count; i++)
            {
                int id = 0;
                if (StillWrongIdBuffer.TryGetIdForPath(elements[i].HierarchyPath, out id) && id != 0 && !idToPath.ContainsKey(id))
                {
                    idToPath.Add(id, elements[i].HierarchyPath);
                }
            }
            List<SamCoverageResult> coverage = SamCoverage.FromBox(request.Box, map, idToPath, LookupRect, null);
            result.UsedIdBuffer = true;
            result.DegradedReason = "box-plus-id-buffer-no-sam-key";
            for (int i = 0; i < coverage.Count; i++)
            {
                SamCoverageResult entry = coverage[i];
                if (entry.ElementId == 0)
                {
                    continue;
                }
                StillWrongGroundingCandidate candidate = new StillWrongGroundingCandidate();
                candidate.ElementPath = entry.ElementPath;
                candidate.ElementId = entry.ElementId;
                candidate.MaskCoverage = entry.MaskCoverage;
                candidate.ElementCoverage = entry.ElementCoverage;
                candidate.BoxAreaInclusive = entry.BoxAreaInclusive;
                candidate.IsAnalytic = entry.IsAnalytic;
                candidate.Warning = entry.Warning;
                result.Candidates.Add(candidate);
            }
            result.IsAmbiguous = SamCoverage.IsAmbiguous(coverage);
        }

        private Rect2D LookupRect(string path)
        {
            GameObject go = StillWrong.Oracle.UnitySymptomOracleRunner.FindGameObjectByPath(path);
            if (go == null)
            {
                return Rect2D.Zero;
            }
            return StillWrong.Oracle.UnitySymptomOracleRunner.ComputeScreenRect(go, Camera.main);
        }

        private static double OverlapArea(Rect box, Rect2D rect)
        {
            double x1;
            double y1;
            double x2;
            double y2;
            SamBoxInclusive.ToInclusiveCorners(box, out x1, out y1, out x2, out y2);
            double rx2 = rect.XMax - 1.0;
            double ry2 = rect.YMax - 1.0;
            return SamBoxInclusive.AreaFromCorners(Math.Max(x1, rect.XMin), Math.Max(y1, rect.YMin), Math.Min(x2, rx2), Math.Min(y2, ry2));
        }

        private static double BoxArea(Rect box)
        {
            double x1;
            double y1;
            double x2;
            double y2;
            SamBoxInclusive.ToInclusiveCorners(box, out x1, out y1, out x2, out y2);
            return SamBoxInclusive.AreaFromCorners(x1, y1, x2, y2);
        }

        private static bool ContainsPoint(Rect2D rect, Vector2 point)
        {
            return point.x >= rect.XMin && point.x <= rect.XMax && point.y >= rect.YMin && point.y <= rect.YMax;
        }

        private static int CompareCandidates(StillWrongGroundingCandidate a, StillWrongGroundingCandidate b)
        {
            if (a == null || b == null)
            {
                return 0;
            }
            return b.MaskCoverage.CompareTo(a.MaskCoverage);
        }

        private static bool IsAmbiguousOverlap(List<StillWrongGroundingCandidate> candidates)
        {
            if (candidates == null || candidates.Count < 2)
            {
                return false;
            }
            return Math.Abs(candidates[0].MaskCoverage - candidates[1].MaskCoverage) <= SamCoverage.AmbiguityBand;
        }
    }
}

