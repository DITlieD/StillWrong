using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StillWrong.Meta
{
    public sealed class FrozenSessionRequest
    {
        public string Sentence { get; set; }
        public string TargetName { get; set; }
        public string TargetElementId { get; set; }
        public string ContainerName { get; set; }
        public string ContainerElementId { get; set; }
        public int MeasurementFrame { get; set; }
        public List<ProbeReading> Measurements { get; set; }
        public bool AllowModelRoute { get; set; }

        public FrozenSessionRequest()
        {
            Sentence = string.Empty;
            TargetName = string.Empty;
            TargetElementId = string.Empty;
            ContainerName = string.Empty;
            ContainerElementId = string.Empty;
            MeasurementFrame = -1;
            Measurements = new List<ProbeReading>();
            AllowModelRoute = true;
        }
    }

    public sealed class ProbeReading
    {
        public string Probe { get; set; }
        public string Target { get; set; }
        public string SecondTarget { get; set; }
        public int Frame { get; set; }
        public string Value { get; set; }

        public ProbeReading()
        {
            Probe = string.Empty;
            Target = string.Empty;
            SecondTarget = string.Empty;
            Value = string.Empty;
        }
    }

    public sealed class ToolTraceEntry
    {
        public string ToolName { get; set; }
        public string ArgumentsJson { get; set; }
        public string ResultText { get; set; }

        public ToolTraceEntry()
        {
            ToolName = string.Empty;
            ArgumentsJson = string.Empty;
            ResultText = string.Empty;
        }
    }

    public sealed class ProposalRecord
    {
        public string DraftJson { get; set; }
        public bool Accepted { get; set; }
        public string EngineNotes { get; set; }

        public ProposalRecord()
        {
            DraftJson = string.Empty;
            EngineNotes = string.Empty;
        }
    }

    public sealed class FrozenSessionResult
    {
        public bool Accepted { get; set; }
        public string SpecJson { get; set; }
        public string FailureMessage { get; set; }
        public bool UsedDemoDefault { get; set; }
        public int Attempts { get; set; }
        public List<string> Transcript { get; set; }
        public List<ToolTraceEntry> ToolTrace { get; set; }
        public List<ProposalRecord> Proposals { get; set; }
        public string ExceptionType { get; set; }
        public string ExceptionMessage { get; set; }

        public FrozenSessionResult()
        {
            SpecJson = string.Empty;
            FailureMessage = string.Empty;
            Transcript = new List<string>();
            ToolTrace = new List<ToolTraceEntry>();
            Proposals = new List<ProposalRecord>();
            ExceptionType = string.Empty;
            ExceptionMessage = string.Empty;
        }
    }

    public static class FrozenSessionAuthoring
    {
        public const int ToolCallLimit = 8;

        public static IList<SparkToolDefinition> FrozenTools()
        {
            List<SparkToolDefinition> tools = new List<SparkToolDefinition>();
            SparkToolDefinition measure = new SparkToolDefinition();
            measure.Name = "measure";
            measure.Description = "Read one probe value for a target at a frame.";
            measure.ParametersJson = @"{""type"": ""object"", ""additionalProperties"": false, ""required"": [""probe"", ""target"", ""secondTarget"", ""frame""], ""properties"": {""probe"": {""type"": ""string""}, ""target"": {""type"": ""string""}, ""secondTarget"": {""type"": ""string""}, ""frame"": {""type"": ""integer""}}}";
            tools.Add(measure);
            return tools;
        }

        public static string Acknowledgement(FrozenSessionRequest request)
        {
            string sentence = request == null || request.Sentence == null ? string.Empty : request.Sentence;
            string target = request == null || request.TargetName == null ? string.Empty : request.TargetName;
            string element = request == null || request.TargetElementId == null ? string.Empty : request.TargetElementId;
            string container = request == null || request.ContainerName == null ? string.Empty : request.ContainerName;
            string containerPath = request == null || request.ContainerElementId == null ? string.Empty : request.ContainerElementId;
            int frame = request == null ? -1 : request.MeasurementFrame;
            List<string> parts = new List<string>();
            parts.Add(sentence);
            parts.Add("Grounded target alias body is " + target + " at " + element + ".");
            parts.Add("Container alias mask is " + container + " at " + containerPath + ".");
            parts.Add("Recorded frame is " + frame + ".");
            parts.Add("Recorded readings: " + ReadingsSummary(request) + ".");
            parts.Add("Draft contract: use target alias body with element path " + element + "; use container alias mask with element path " + containerPath + "; OverflowPx and Inside clauses use secondTarget mask; measurementFrames is [" + frame + "]. Only the measure tool exists; do not call any other tool.");
            parts.Add("You have at most " + ToolCallLimit + " measure tool calls in total. Measure only to confirm values, then output only the oracle spec JSON object. You must propose the spec before the tool budget runs out; if the budget is exhausted the run ends with no spec accepted.");
            return string.Join(" ", parts.ToArray());
        }

        public static string ReadingsSummary(FrozenSessionRequest request)
        {
            if (request == null || request.Measurements == null || request.Measurements.Count == 0)
            {
                return "none";
            }
            List<string> cells = new List<string>();
            for (int i = 0; i < request.Measurements.Count; i++)
            {
                ProbeReading reading = request.Measurements[i];
                if (reading == null)
                {
                    continue;
                }
                string label = reading.Probe + "(" + reading.Target;
                if (reading.SecondTarget != null && reading.SecondTarget.Length > 0)
                {
                    label += "," + reading.SecondTarget;
                }
                label += ")@" + reading.Frame + "=" + reading.Value;
                cells.Add(label);
            }
            if (cells.Count == 0)
            {
                return "none";
            }
            return string.Join("; ", cells.ToArray());
        }

        public static string MeasureReply(FrozenSessionRequest request, string probe, string target, string secondTarget, int frame)
        {
            if (request == null || request.Measurements == null)
            {
                return "missing: no measurements provided";
            }
            string wantSecond = secondTarget == null ? string.Empty : secondTarget;
            for (int i = 0; i < request.Measurements.Count; i++)
            {
                ProbeReading reading = request.Measurements[i];
                if (reading == null)
                {
                    continue;
                }
                string haveSecond = reading.SecondTarget == null ? string.Empty : reading.SecondTarget;
                if (reading.Frame == frame && string.Equals(reading.Probe, probe, StringComparison.OrdinalIgnoreCase) && string.Equals(reading.Target, target, StringComparison.Ordinal) && string.Equals(haveSecond, wantSecond, StringComparison.Ordinal))
                {
                    return reading.Value;
                }
            }
            if (wantSecond.Length > 0)
            {
                return "missing: no value for " + probe + " on " + target + "," + wantSecond + " at frame " + frame + ". Recorded: " + ReadingsSummary(request);
            }
            return "missing: no value for " + probe + " on " + target + " at frame " + frame + ". Recorded: " + ReadingsSummary(request);
        }

        public static string MeasureReply(FrozenSessionRequest request, string probe, string target, int frame)
        {
            return MeasureReply(request, probe, target, string.Empty, frame);
        }

        public static Func<string, string, Task<string>> MeasureExecutorFor(FrozenSessionRequest request, List<ToolTraceEntry> trace)
        {
            return (name, argumentsJson) =>
            {
                string output;
                if (!string.Equals(name, "measure", StringComparison.Ordinal))
                {
                    output = "error: frozen session supports measure only; recorded evidence: " + ReadingsSummary(request);
                }
                else
                {
                    output = RunMeasure(request, argumentsJson);
                }
                if (trace != null)
                {
                    ToolTraceEntry entry = new ToolTraceEntry();
                    entry.ToolName = name == null ? string.Empty : name;
                    entry.ArgumentsJson = argumentsJson == null ? string.Empty : argumentsJson;
                    entry.ResultText = output;
                    trace.Add(entry);
                }
                return Task.FromResult(output);
            };
        }

        public static Func<string, string, Task<string>> MeasureExecutorFor(FrozenSessionRequest request)
        {
            return MeasureExecutorFor(request, null);
        }

        private static string RunMeasure(FrozenSessionRequest request, string argumentsJson)
        {
            string probe = string.Empty;
            string target = string.Empty;
            string secondTarget = string.Empty;
            int frame = 0;
            try
            {
                JObject args = JObject.Parse(argumentsJson == null ? string.Empty : argumentsJson);
                JToken probeToken = args["probe"];
                if (probeToken != null && probeToken.Type == JTokenType.String)
                {
                    probe = probeToken.Value<string>();
                }
                JToken targetToken = args["target"];
                if (targetToken != null && targetToken.Type == JTokenType.String)
                {
                    target = targetToken.Value<string>();
                }
                JToken secondToken = args["secondTarget"];
                if (secondToken != null && secondToken.Type == JTokenType.String)
                {
                    secondTarget = secondToken.Value<string>();
                }
                JToken frameToken = args["frame"];
                if (frameToken != null && frameToken.Type == JTokenType.Integer)
                {
                    frame = frameToken.Value<int>();
                }
            }
            catch (Exception)
            {
                return "error: arguments are not valid JSON";
            }
            return MeasureReply(request, probe, target, secondTarget, frame);
        }

        public static async Task<FrozenSessionResult> RunAsync(SparkClient client, FrozenSessionRequest request, Func<string, EngineCheck> engineCheck, CancellationToken token)
        {
            FrozenSessionResult result = new FrozenSessionResult();
            if (request == null)
            {
                result.FailureMessage = "failed: no frozen session request";
                return result;
            }
            if (client == null || !request.AllowModelRoute)
            {
                result.UsedDemoDefault = true;
                result.FailureMessage = string.Empty;
                return result;
            }
            List<SparkMessage> start = new List<SparkMessage>();
            SparkMessage user = new SparkMessage();
            user.Role = "user";
            user.Content = Acknowledgement(request);
            start.Add(user);
            SpecAuthoringLoop loop = new SpecAuthoringLoop(client, FrozenTools(), MeasureExecutorFor(request, result.ToolTrace), TracedCheck(engineCheck, result.Proposals), ToolCallLimit);
            AuthoringOutcome outcome;
            try
            {
                outcome = await loop.RunAsync(start, "flat_spec", FlatDraft.SchemaJson(), token).ConfigureAwait(false);
            }
            catch (MissingApiKeyException missing)
            {
                result.UsedDemoDefault = true;
                result.FailureMessage = string.Empty;
                result.ExceptionType = missing.GetType().Name;
                result.ExceptionMessage = missing.Message;
                return result;
            }
            catch (MetaApiException api)
            {
                result.FailureMessage = "failed: model request error " + api.StatusCode;
                result.ExceptionType = api.GetType().Name;
                result.ExceptionMessage = RequestDump(client, start, api);
                return result;
            }
            catch (SparkToolLimitException limit)
            {
                result.FailureMessage = "failed: " + limit.Message;
                result.ExceptionType = limit.GetType().Name;
                result.ExceptionMessage = limit.Message;
                return result;
            }
            result.Attempts = CountAttempts(outcome);
            if (outcome.Transcript != null)
            {
                result.Transcript.AddRange(outcome.Transcript);
            }
            if (outcome.Status == AuthoringStatus.Accepted)
            {
                result.Accepted = true;
                result.SpecJson = outcome.SpecJson;
                return result;
            }
            result.FailureMessage = "failed: spec authoring did not converge";
            return result;
        }

        public static string RequestDump(SparkClient client, IList<SparkMessage> start, MetaApiException api)
        {
            string model = client == null ? string.Empty : client.ModelId;
            int messageLength = 0;
            if (start != null)
            {
                for (int i = 0; i < start.Count; i++)
                {
                    if (start[i] != null && start[i].Content != null)
                    {
                        messageLength += start[i].Content.Length;
                    }
                }
            }
            string body = api == null || api.ResponseBody == null ? string.Empty : api.ResponseBody;
            if (body.Length > 2000)
            {
                body = body.Substring(0, 2000);
            }
            return "model=" + model + " tools=" + ToolsFingerprint() + " messageLength=" + messageLength + " status=" + (api == null ? 0 : api.StatusCode) + " body=" + body;
        }

        public static string ToolsFingerprint()
        {
            IList<SparkToolDefinition> tools = FrozenTools();
            List<string> names = new List<string>();
            for (int i = 0; i < tools.Count; i++)
            {
                names.Add(tools[i].Name + ":" + tools[i].ParametersJson);
            }
            return "[" + string.Join(",", names.ToArray()) + "]";
        }

        private static Func<string, EngineCheck> TracedCheck(Func<string, EngineCheck> inner, List<ProposalRecord> proposals)
        {
            return (draft) =>
            {
                EngineCheck check;
                if (inner == null)
                {
                    check = new EngineCheck();
                    check.Accepted = false;
                    check.Notes = "Rejected: no engine check supplied.";
                }
                else
                {
                    check = inner(draft);
                }
                if (check == null)
                {
                    check = new EngineCheck();
                    check.Accepted = false;
                    check.Notes = "Rejected: engine check returned nothing.";
                }
                if (proposals != null)
                {
                    ProposalRecord record = new ProposalRecord();
                    record.DraftJson = draft == null ? string.Empty : draft;
                    record.Accepted = check.Accepted;
                    record.EngineNotes = check.Notes == null ? string.Empty : check.Notes;
                    proposals.Add(record);
                }
                return check;
            };
        }

        private static int CountAttempts(AuthoringOutcome outcome)
        {
            if (outcome == null || outcome.Transcript == null)
            {
                return 0;
            }
            int count = 0;
            for (int i = 0; i < outcome.Transcript.Count; i++)
            {
                if (outcome.Transcript[i] == "propose" || outcome.Transcript[i] == "retry-propose")
                {
                    count++;
                }
            }
            return count;
        }
    }
}
