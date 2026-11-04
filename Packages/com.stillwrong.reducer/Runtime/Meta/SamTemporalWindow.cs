using System;
using System.Collections.Generic;

namespace StillWrong.Meta
{
    public sealed class SamFrameArea
    {
        public int FrameIndex { get; set; }
        public double Area { get; set; }
    }

    public sealed class SamObjectWindow
    {
        public string ObjectId { get; set; }
        public double MedianArea { get; set; }
        public double Threshold { get; set; }
        public bool HasWindow { get; set; }
        public int StartFrameIndex { get; set; }
        public int EndFrameIndex { get; set; }
        public List<int> Frames { get; set; }

        public SamObjectWindow()
        {
            ObjectId = string.Empty;
            Frames = new List<int>();
            StartFrameIndex = -1;
            EndFrameIndex = -1;
        }
    }

    public static class SamTemporalWindow
    {
        public static double MedianOf(IList<double> values)
        {
            if (values == null || values.Count == 0)
            {
                return 0.0;
            }
            List<double> sorted = new List<double>(values);
            sorted.Sort();
            int n = sorted.Count;
            if (n % 2 == 1)
            {
                return sorted[n / 2];
            }
            return (sorted[(n / 2) - 1] + sorted[n / 2]) / 2.0;
        }

        public static SamObjectWindow ProposeWindow(string objectId, IList<SamFrameArea> samples, int? clipFrameCount = null)
        {
            SamObjectWindow window = new SamObjectWindow();
            window.ObjectId = objectId == null ? string.Empty : objectId;
            List<SamFrameArea> ordered = FillClip(samples, clipFrameCount);
            if (ordered.Count == 0)
            {
                return window;
            }
            ordered.Sort(CompareByFrame);
            List<double> areas = new List<double>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                areas.Add(ordered[i].Area);
            }
            double median = MedianOf(areas);
            window.MedianArea = median;
            window.Threshold = median * 0.2;
            if (median <= 0.0)
            {
                return window;
            }
            int bestStart = -1;
            int bestEnd = -1;
            int runStart = -1;
            for (int i = 0; i < ordered.Count; i++)
            {
                bool below = ordered[i].Area < window.Threshold;
                if (below && runStart < 0)
                {
                    runStart = i;
                }
                if (runStart >= 0 && (!below || i == ordered.Count - 1))
                {
                    int runEnd = below ? i : i - 1;
                    if (bestStart < 0 || (runEnd - runStart) > (bestEnd - bestStart))
                    {
                        bestStart = runStart;
                        bestEnd = runEnd;
                    }
                    runStart = -1;
                }
            }
            if (bestStart < 0)
            {
                return window;
            }
            window.HasWindow = true;
            window.StartFrameIndex = ordered[bestStart].FrameIndex;
            window.EndFrameIndex = ordered[bestEnd].FrameIndex;
            for (int i = bestStart; i <= bestEnd; i++)
            {
                window.Frames.Add(ordered[i].FrameIndex);
            }
            return window;
        }

        public static List<SamObjectWindow> ProposeWindows(IDictionary<string, IList<SamFrameArea>> perObject)
        {
            List<SamObjectWindow> result = new List<SamObjectWindow>();
            if (perObject == null)
            {
                return result;
            }
            foreach (KeyValuePair<string, IList<SamFrameArea>> entry in perObject)
            {
                result.Add(ProposeWindow(entry.Key, entry.Value));
            }
            result.Sort(CompareById);
            return result;
        }

        private static List<SamFrameArea> FillClip(IList<SamFrameArea> samples, int? clipFrameCount)
        {
            List<SamFrameArea> effective = new List<SamFrameArea>();
            if (!clipFrameCount.HasValue || clipFrameCount.Value <= 0)
            {
                if (samples != null)
                {
                    for (int i = 0; i < samples.Count; i++)
                    {
                        if (samples[i] != null)
                        {
                            effective.Add(samples[i]);
                        }
                    }
                }
                return effective;
            }
            Dictionary<int, double> areas = new Dictionary<int, double>();
            if (samples != null)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    SamFrameArea sample = samples[i];
                    if (sample == null)
                    {
                        continue;
                    }
                    if (!areas.ContainsKey(sample.FrameIndex))
                    {
                        areas.Add(sample.FrameIndex, sample.Area);
                    }
                }
            }
            for (int frame = 0; frame < clipFrameCount.Value; frame++)
            {
                SamFrameArea filled = new SamFrameArea();
                filled.FrameIndex = frame;
                double area;
                if (areas.TryGetValue(frame, out area))
                {
                    filled.Area = area;
                }
                else
                {
                    filled.Area = 0.0;
                }
                effective.Add(filled);
            }
            return effective;
        }

        private static int CompareByFrame(SamFrameArea a, SamFrameArea b)
        {
            if (a.FrameIndex < b.FrameIndex)
            {
                return -1;
            }
            if (a.FrameIndex > b.FrameIndex)
            {
                return 1;
            }
            if (a.Area < b.Area)
            {
                return -1;
            }
            if (a.Area > b.Area)
            {
                return 1;
            }
            return 0;
        }

        private static int CompareById(SamObjectWindow a, SamObjectWindow b)
        {
            return string.Compare(a.ObjectId, b.ObjectId, StringComparison.Ordinal);
        }
    }
}
