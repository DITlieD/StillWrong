using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace StillWrong.Core.Oracle
{
    public sealed class InvalidReportException : Exception
    {
        public InvalidReportException(string message)
            : base(message)
        {
        }
    }

    public static class ReportGenerator
    {
        public static string Build(string specId, string specWords, string manifestJson)
        {
            if (specId == null)
            {
                specId = string.Empty;
            }
            if (specWords == null)
            {
                specWords = string.Empty;
            }
            if (manifestJson == null)
            {
                manifestJson = string.Empty;
            }
            JObject manifest = null;
            if (manifestJson.Length > 0)
            {
                try
                {
                    JToken parsed = JToken.Parse(manifestJson);
                    if (parsed is JObject)
                    {
                        manifest = (JObject)parsed;
                    }
                }
                catch (Exception)
                {
                    manifest = null;
                }
            }
            StringBuilder builder = new StringBuilder();
            builder.Append("# Still Wrong report - ");
            builder.Append(specId);
            builder.Append(((char)10).ToString());
            builder.Append(((char)10).ToString());
            builder.Append("## Symptom");
            builder.Append(((char)10).ToString());
            builder.Append(((char)10).ToString());
            builder.Append(specWords);
            builder.Append(((char)10).ToString());
            AppendScene(builder, manifest);
            AppendTrials(builder, manifest);
            AppendVerification(builder, manifest);
            string report = builder.ToString();
            List<string> offending = ReportNumbers.FindUnmanifested(report, manifestJson, specWords + " " + specId);
            if (offending.Count > 0)
            {
                throw new InvalidReportException("Report contains numbers absent from manifest and spec words: " + string.Join(", ", offending.ToArray()));
            }
            return report;
        }

        private static void AppendScene(StringBuilder builder, JObject manifest)
        {
            if (manifest == null)
            {
                return;
            }
            List<string> facts = new List<string>();
            AddCountFact(facts, manifest, "objectCountBefore", "objectCountAfter", "objects");
            AddCountFact(facts, manifest, "componentCountBefore", "componentCountAfter", "components");
            AddCountFact(facts, manifest, "bundleBytesBefore", "bundleBytesAfter", "bundle bytes");
            JToken unity = manifest["unityVersion"];
            if (unity != null && unity.Type == JTokenType.String)
            {
                facts.Add("Unity version " + unity.Value<string>());
            }
            if (facts.Count == 0)
            {
                return;
            }
            builder.Append(((char)10).ToString());
            builder.Append("## Minimal scene");
            builder.Append(((char)10).ToString());
            builder.Append(((char)10).ToString());
            for (int i = 0; i < facts.Count; i++)
            {
                builder.Append("- ");
                builder.Append(facts[i]);
                builder.Append(((char)10).ToString());
            }
        }

        private static void AddCountFact(List<string> facts, JObject manifest, string beforeKey, string afterKey, string label)
        {
            JToken before = manifest[beforeKey];
            JToken after = manifest[afterKey];
            if (before != null && after != null && before.Type == JTokenType.Integer && after.Type == JTokenType.Integer)
            {
                facts.Add(label + " " + before.Value<long>().ToString() + " before, " + after.Value<long>().ToString() + " after");
            }
        }

        private static void AppendTrials(StringBuilder builder, JObject manifest)
        {
            if (manifest == null)
            {
                return;
            }
            JToken trials = manifest["trials"];
            if (!(trials is JArray) || ((JArray)trials).Count == 0)
            {
                return;
            }
            builder.Append(((char)10).ToString());
            builder.Append("## Trials");
            builder.Append(((char)10).ToString());
            builder.Append(((char)10).ToString());
            foreach (JToken trial in (JArray)trials)
            {
                JToken verdict = trial["verdict"];
                JToken duration = trial["durationMs"];
                if (verdict != null && verdict.Type == JTokenType.String && duration != null && duration.Type == JTokenType.Integer)
                {
                    builder.Append("- ");
                    builder.Append(verdict.Value<string>());
                    builder.Append(" in ");
                    builder.Append(duration.Value<long>().ToString());
                    builder.Append(" ms");
                    builder.Append(((char)10).ToString());
                }
            }
        }

        private static void AppendVerification(StringBuilder builder, JObject manifest)
        {
            if (manifest == null)
            {
                return;
            }
            JToken verification = manifest["verification"];
            if (!(verification is JObject))
            {
                return;
            }
            JObject block = (JObject)verification;
            List<string> facts = new List<string>();
            JToken bundle = block["bundleVerdict"];
            if (bundle != null && bundle.Type == JTokenType.String)
            {
                facts.Add("bundle verdict " + bundle.Value<string>());
            }
            JToken negative = block["negativeControl"];
            if (negative != null && negative.Type == JTokenType.String)
            {
                facts.Add("negative control " + negative.Value<string>());
            }
            if (facts.Count == 0)
            {
                return;
            }
            builder.Append(((char)10).ToString());
            builder.Append("## Verification");
            builder.Append(((char)10).ToString());
            builder.Append(((char)10).ToString());
            for (int i = 0; i < facts.Count; i++)
            {
                builder.Append("- ");
                builder.Append(facts[i]);
                builder.Append(((char)10).ToString());
            }
        }
    }
}
