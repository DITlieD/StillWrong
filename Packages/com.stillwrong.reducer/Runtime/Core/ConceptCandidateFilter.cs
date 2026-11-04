using System;
using System.Collections.Generic;

namespace StillWrong.Core
{
    public sealed class ConceptCandidate
    {
        public string ElementId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public double Confidence { get; set; }

        public ConceptCandidate()
        {
            ElementId = string.Empty;
        }
    }

    public static class ConceptCandidateFilter
    {
        public static List<ConceptCandidate> Filter(
            IList<ConceptCandidate> candidates,
            IDictionary<string, Rect2D> registryRects,
            ICollection<string> allowedIds,
            Rect2D? region)
        {
            List<ConceptCandidate> result = new List<ConceptCandidate>();
            if (candidates == null)
            {
                return result;
            }
            bool restrictIds = allowedIds != null && allowedIds.Count > 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                ConceptCandidate candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }
                if (registryRects != null && !registryRects.ContainsKey(candidate.ElementId))
                {
                    continue;
                }
                if (restrictIds && !allowedIds.Contains(candidate.ElementId))
                {
                    continue;
                }
                if (region.HasValue && !region.Value.Contains(new Vec2(candidate.X, candidate.Y)))
                {
                    continue;
                }
                result.Add(candidate);
            }
            result.Sort(CompareCandidates);
            return result;
        }

        private static int CompareCandidates(ConceptCandidate a, ConceptCandidate b)
        {
            int byId = string.Compare(a.ElementId, b.ElementId, StringComparison.Ordinal);
            if (byId != 0)
            {
                return byId;
            }
            if (a.X < b.X)
            {
                return -1;
            }
            if (a.X > b.X)
            {
                return 1;
            }
            if (a.Y < b.Y)
            {
                return -1;
            }
            if (a.Y > b.Y)
            {
                return 1;
            }
            if (a.Confidence > b.Confidence)
            {
                return -1;
            }
            if (a.Confidence < b.Confidence)
            {
                return 1;
            }
            return 0;
        }
    }
}
