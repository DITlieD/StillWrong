using System;
using System.Collections.Generic;
using System.IO;
namespace StillWrong.Core
{
    public sealed class RetentionChunk
    {
        public string Name = string.Empty;
            public double StartTimeSeconds;
        public double EndTimeSeconds;
        public long Bytes;
    }
    public sealed class RetentionPlan
    {
        public List<string> DeleteNames = new List<string>();
        public bool Refused;
        public string RefusalReason = string.Empty;
    }
    public static class RetentionPolicy
    {
        public const double WindowSeconds = 180.0;
        public const long CapBytes = 262144000L;
        public static RetentionPlan Plan(string sessionRoot, string assertedRoot, IList<RetentionChunk> chunks, double nowSeconds)
        {
            return Plan(sessionRoot, assertedRoot, chunks, nowSeconds, WindowSeconds);
        }
        public static RetentionPlan Plan(string sessionRoot, string assertedRoot, IList<RetentionChunk> chunks, double nowSeconds, double windowSeconds)
        {
            RetentionPlan plan = new RetentionPlan();
            if (!string.Equals(sessionRoot, assertedRoot, StringComparison.Ordinal))
            {
                plan.Refused = true;
                plan.RefusalReason = "root-mismatch";
                return plan;
            }
            if (chunks == null)
            {
                plan.Refused = true;
                plan.RefusalReason = "null-chunks";
                return plan;
            }
            string rootFull = Path.GetFullPath(sessionRoot);
            string prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            for (int i = 0; i < chunks.Count; i++)
            {
                RetentionChunk chunk = chunks[i];
                if (chunk == null || string.IsNullOrEmpty(chunk.Name))
                {
                    plan.Refused = true;
                    plan.RefusalReason = "bad-chunk-name";
                    plan.DeleteNames.Clear();
                    return plan;
                }
                if (chunk.Name.IndexOf(Path.DirectorySeparatorChar) >= 0 || chunk.Name.IndexOf(Path.AltDirectorySeparatorChar) >= 0 || chunk.Name.IndexOf("..") >= 0 || Path.IsPathRooted(chunk.Name))
                {
                    plan.Refused = true;
                    plan.RefusalReason = "path-escape";
                    plan.DeleteNames.Clear();
                    return plan;
                }
                string full = Path.GetFullPath(Path.Combine(rootFull, chunk.Name));
                if (!full.StartsWith(prefix, StringComparison.Ordinal))
                {
                    plan.Refused = true;
                    plan.RefusalReason = "path-escape";
                    plan.DeleteNames.Clear();
                    return plan;
                }
            }
            List<RetentionChunk> survivors = new List<RetentionChunk>();
            for (int i = 0; i < chunks.Count; i++)
            {
                if (chunks[i].EndTimeSeconds < nowSeconds - windowSeconds)
                {
                    plan.DeleteNames.Add(chunks[i].Name);
                }
                else
                {
                    survivors.Add(chunks[i]);
                }
            }
            long total = 0;
            for (int i = 0; i < survivors.Count; i++)
            {
                total += survivors[i].Bytes;
            }
            survivors.Sort(CompareChunksOldestFirst);
            for (int i = 0; i < survivors.Count; i++)
            {
                if (total <= CapBytes)
                {
                    break;
                }
                plan.DeleteNames.Add(survivors[i].Name);
                total -= survivors[i].Bytes;
            }
            return plan;
        }
        public static double CoveredSeconds(IList<RetentionChunk> chunks)
        {
            if (chunks == null)
            {
                return 0.0;
            }
            double total = 0.0;
            for (int i = 0; i < chunks.Count; i++)
            {
                if (chunks[i] == null)
                {
                    continue;
                }
                double span = chunks[i].EndTimeSeconds - chunks[i].StartTimeSeconds;
                if (span > 0.0)
                {
                    total += span;
                }
            }
            return total;
        }
        private static int CompareChunksOldestFirst(RetentionChunk a, RetentionChunk b)
        {
            if (a == null && b == null)
            {
                return 0;
            }
            if (a == null)
            {
                return -1;
            }
            if (b == null)
            {
                return 1;
            }
            int byTime = a.EndTimeSeconds.CompareTo(b.EndTimeSeconds);
            if (byTime != 0)
            {
                return byTime;
            }
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        }
    }
}
