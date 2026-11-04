using System;
using System.Collections.Generic;

namespace StillWrong.Core
{
    public sealed class GroundingScore
    {
        public string ElementId { get; set; }
        public int MaskPixelsOwned { get; set; }
        public int ElementVisiblePixels { get; set; }
        public double FractionOfMask { get; set; }
        public double FractionOfElementInMask { get; set; }
        public double RectOverlapPreScore { get; set; }

        public GroundingScore()
        {
            ElementId = string.Empty;
        }
    }

    public sealed class GroundingResult
    {
        public List<GroundingScore> Scores { get; set; }
        public bool Ambiguous { get; set; }

        public GroundingResult()
        {
            Scores = new List<GroundingScore>();
        }
    }

    public static class GroundingScorer
    {
        public static GroundingResult ScoreRaster(byte[] maskRaster, int maskWidth, int maskHeight, int offsetX, int offsetY, int[] ownership, int frameWidth, int frameHeight, IList<string> elementIds, IDictionary<string, Rect2D> registryRects)
        {
            List<int> xs = new List<int>();
            List<int> ys = new List<int>();
            if (maskRaster != null)
            {
                for (int y = 0; y < maskHeight; y++)
                {
                    for (int x = 0; x < maskWidth; x++)
                    {
                        if (maskRaster[y * maskWidth + x] != 0)
                        {
                            xs.Add(x + offsetX);
                            ys.Add(y + offsetY);
                        }
                    }
                }
            }
            return ScorePixels(xs, ys, ownership, frameWidth, frameHeight, elementIds, registryRects);
        }

        public static GroundingResult ScorePixels(IList<int> xs, IList<int> ys, int[] ownership, int frameWidth, int frameHeight, IList<string> elementIds, IDictionary<string, Rect2D> registryRects)
        {
            GroundingResult result = new GroundingResult();
            if (xs == null || ys == null || ownership == null || elementIds == null)
            {
                return result;
            }
            int count = xs.Count < ys.Count ? xs.Count : ys.Count;
            Dictionary<int, int> ownedById = new Dictionary<int, int>();
            int totalSet = count;
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            for (int i = 0; i < count; i++)
            {
                int x = xs[i];
                int y = ys[i];
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                if (x < 0 || y < 0 || x >= frameWidth || y >= frameHeight)
                {
                    continue;
                }
                int owner = ownership[y * frameWidth + x];
                if (owner <= 0 || owner >= elementIds.Count)
                {
                    continue;
                }
                int current;
                if (!ownedById.TryGetValue(owner, out current))
                {
                    current = 0;
                }
                ownedById[owner] = current + 1;
            }
            Dictionary<int, int> visibleById = new Dictionary<int, int>();
            if (ownership.Length == frameWidth * frameHeight)
            {
                for (int i = 0; i < ownership.Length; i++)
                {
                    int owner = ownership[i];
                    if (owner <= 0 || owner >= elementIds.Count)
                    {
                        continue;
                    }
                    int current;
                    if (!visibleById.TryGetValue(owner, out current))
                    {
                        current = 0;
                    }
                    visibleById[owner] = current + 1;
                }
            }
            Rect2D maskBox = Rect2D.Zero;
            if (count > 0)
            {
                maskBox = new Rect2D((float)minX, (float)minY, (float)(maxX - minX + 1), (float)(maxY - minY + 1));
            }
            List<GroundingScore> scores = new List<GroundingScore>();
            foreach (KeyValuePair<int, int> entry in ownedById)
            {
                GroundingScore score = new GroundingScore();
                score.ElementId = elementIds[entry.Key];
                score.MaskPixelsOwned = entry.Value;
                int visible;
                if (!visibleById.TryGetValue(entry.Key, out visible))
                {
                    visible = 0;
                }
                score.ElementVisiblePixels = visible;
                score.FractionOfMask = totalSet > 0 ? (double)entry.Value / (double)totalSet : 0.0;
                score.FractionOfElementInMask = visible > 0 ? (double)entry.Value / (double)visible : 0.0;
                score.RectOverlapPreScore = RectPreScore(score.ElementId, maskBox, count, registryRects);
                scores.Add(score);
            }
            scores.Sort(CompareScores);
            result.Scores = scores;
            if (scores.Count >= 2)
            {
                double gap = scores[0].FractionOfMask - scores[1].FractionOfMask;
                result.Ambiguous = gap <= 0.10;
            }
            else
            {
                result.Ambiguous = false;
            }
            return result;
        }

        private static double RectPreScore(string elementId, Rect2D maskBox, int maskCount, IDictionary<string, Rect2D> registryRects)
        {
            if (maskCount == 0 || registryRects == null)
            {
                return 0.0;
            }
            Rect2D rect;
            if (!registryRects.TryGetValue(elementId, out rect))
            {
                return 0.0;
            }
            if (rect.Area <= 0.0f)
            {
                return 0.0;
            }
            return (double)rect.Intersection(maskBox).Area / (double)rect.Area;
        }

        private static int CompareScores(GroundingScore a, GroundingScore b)
        {
            if (a.FractionOfMask > b.FractionOfMask)
            {
                return -1;
            }
            if (a.FractionOfMask < b.FractionOfMask)
            {
                return 1;
            }
            return string.Compare(a.ElementId, b.ElementId, StringComparison.Ordinal);
        }
    }
}
