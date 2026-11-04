using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace StillWrong.Meta
{
    public sealed class CostEntry
    {
        public string Kind { get; set; }
        public string Model { get; set; }
        public int? ImageCount { get; set; }
        public int? FrameCount { get; set; }
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }
        public double? CostUsd { get; set; }
        public Dictionary<string, string> RateLimit { get; set; }

        public CostEntry()
        {
            Kind = string.Empty;
            Model = string.Empty;
            RateLimit = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public sealed class CostLog
    {
        private readonly object gate = new object();
        private readonly List<CostEntry> entries = new List<CostEntry>();

        public void Add(CostEntry entry)
        {
            lock (gate)
            {
                entries.Add(entry);
            }
        }

        public IList<CostEntry> Snapshot()
        {
            lock (gate)
            {
                return new List<CostEntry>(entries);
            }
        }

        public string ExportJson()
        {
            lock (gate)
            {
                List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
                for (int i = 0; i < entries.Count; i++)
                {
                    CostEntry entry = entries[i];
                    Dictionary<string, object> item = new Dictionary<string, object>(StringComparer.Ordinal);
                    item["kind"] = entry.Kind;
                    item["model"] = entry.Model;
                    item["imageCount"] = entry.ImageCount;
                    item["frameCount"] = entry.FrameCount;
                    item["inputTokens"] = entry.InputTokens;
                    item["outputTokens"] = entry.OutputTokens;
                    item["costUsd"] = entry.CostUsd;
                    item["rateLimit"] = entry.RateLimit;
                    items.Add(item);
                }
                return MetaJson.SerializeRaw(items);
            }
        }

        public static double? SamImageCost(int? imageCount)
        {
            if (!imageCount.HasValue)
            {
                return null;
            }
            return (double)imageCount.Value * 2.50 / 1000.0;
        }

        public static double? SamVideoCost(int? frameCount)
        {
            if (!frameCount.HasValue)
            {
                return null;
            }
            int billed = frameCount.Value < 50 ? 50 : frameCount.Value;
            return (double)billed * 0.20 / 1000.0;
        }

        public static double? SparkCost(string model, int? inputTokens, int? outputTokens)
        {
            if (!inputTokens.HasValue || !outputTokens.HasValue)
            {
                return null;
            }
            bool contributor = model != null && model.IndexOf("contributor", StringComparison.OrdinalIgnoreCase) >= 0;
            double inputRate;
            double outputRate;
            if (contributor)
            {
                inputRate = 0.10;
                outputRate = 0.20;
            }
            else
            {
                inputRate = 1.25;
                outputRate = 4.25;
            }
            return (double)inputTokens.Value * inputRate / 1000000.0 + (double)outputTokens.Value * outputRate / 1000000.0;
        }
    }
}
