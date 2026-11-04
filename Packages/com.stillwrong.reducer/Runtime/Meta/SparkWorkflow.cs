using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace StillWrong.Meta
{
    public sealed class SemanticPickResult
    {
        public string Target { get; set; }
        public string Interceptor { get; set; }
        public string Occluder { get; set; }
        public string Justification { get; set; }

        public SemanticPickResult()
        {
            Target = string.Empty;
            Justification = string.Empty;
        }
    }

    public sealed class RankedGroup
    {
        public string GroupId { get; set; }
        public int Rank { get; set; }
        public string Reason { get; set; }

        public RankedGroup()
        {
            GroupId = string.Empty;
            Reason = string.Empty;
        }
    }

    public sealed class EngineCheck
    {
        public bool Accepted { get; set; }
        public string Notes { get; set; }

        public EngineCheck()
        {
            Notes = string.Empty;
        }
    }

    public enum AuthoringStatus
    {
        Accepted,
        NeedsHandEdit
    }

    public sealed class AuthoringOutcome
    {
        public AuthoringStatus Status { get; set; }
        public string SpecJson { get; set; }
        public List<string> Transcript { get; set; }

        public AuthoringOutcome()
        {
            SpecJson = string.Empty;
            Transcript = new List<string>();
        }
    }

    public static class SparkWorkflow
    {
        public const string SemanticPickSchemaName = "semantic_pick";

        public static string SemanticPickSchema()
        {
            return "{\"type\": \"object\", \"additionalProperties\": false, \"required\": [\"target\", \"interceptor\", \"occluder\", \"justification\"], \"properties\": {\"target\": {\"type\": \"string\"}, \"interceptor\": {\"type\": [\"string\", \"null\"]}, \"occluder\": {\"type\": [\"string\", \"null\"]}, \"justification\": {\"type\": \"string\"}}}";
        }

        public static SemanticPickResult ParseSemanticPick(string json)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception inner)
            {
                throw new SparkSchemaException("body", "Semantic pick is not valid JSON: " + inner.Message);
            }
            SemanticPickResult result = new SemanticPickResult();
            JToken target = root["target"];
            if (target == null || target.Type != JTokenType.String)
            {
                throw new SparkSchemaException("target", "Semantic pick is missing a string target.");
            }
            result.Target = target.Value<string>();
            JToken interceptor = root["interceptor"];
            if (interceptor != null && interceptor.Type == JTokenType.String)
            {
                result.Interceptor = interceptor.Value<string>();
            }
            JToken occluder = root["occluder"];
            if (occluder != null && occluder.Type == JTokenType.String)
            {
                result.Occluder = occluder.Value<string>();
            }
            JToken justification = root["justification"];
            if (justification == null || justification.Type != JTokenType.String)
            {
                throw new SparkSchemaException("justification", "Semantic pick is missing a string justification.");
            }
            result.Justification = justification.Value<string>();
            return result;
        }

        public const string RankingSchemaName = "candidate_ranking";

        public static string RankingSchema()
        {
            return "{\"type\": \"object\", \"additionalProperties\": false, \"required\": [\"groups\"], \"properties\": {\"groups\": {\"type\": \"array\", \"items\": {\"type\": \"object\", \"additionalProperties\": false, \"required\": [\"group_id\", \"rank\", \"reason\"], \"properties\": {\"group_id\": {\"type\": \"string\"}, \"rank\": {\"type\": \"integer\"}, \"reason\": {\"type\": \"string\"}}}}}}";
        }

        public static List<RankedGroup> ParseRanking(string json)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception inner)
            {
                throw new SparkSchemaException("body", "Ranking is not valid JSON: " + inner.Message);
            }
            JToken groups = root["groups"];
            if (groups == null || !(groups is JArray))
            {
                throw new SparkSchemaException("groups", "Ranking is missing a groups array.");
            }
            List<RankedGroup> result = new List<RankedGroup>();
            foreach (JToken item in (JArray)groups)
            {
                JToken id = item["group_id"];
                JToken rank = item["rank"];
                JToken reason = item["reason"];
                if (id == null || id.Type != JTokenType.String || rank == null || rank.Type != JTokenType.Integer || reason == null || reason.Type != JTokenType.String)
                {
                    throw new SparkSchemaException("groups[]", "Ranking entry needs group_id string, rank integer and reason string.");
                }
                RankedGroup group = new RankedGroup();
                group.GroupId = id.Value<string>();
                group.Rank = rank.Value<int>();
                group.Reason = reason.Value<string>();
                result.Add(group);
            }
            return result;
        }

        public static IList<SparkToolDefinition> MeasureTools()
        {
            List<SparkToolDefinition> tools = new List<SparkToolDefinition>();
            SparkToolDefinition measure = new SparkToolDefinition();
            measure.Name = "measure";
            measure.Description = "Read one probe value for a target at a frame offset.";
            measure.ParametersJson = "{\"type\": \"object\", \"additionalProperties\": false, \"required\": [\"probe\", \"target\", \"frame\"], \"properties\": {\"probe\": {\"type\": \"string\"}, \"target\": {\"type\": \"string\"}, \"frame\": {\"type\": \"integer\"}}}";
            tools.Add(measure);
            SparkToolDefinition nearby = new SparkToolDefinition();
            nearby.Name = "list_elements_near";
            nearby.Description = "List recorded elements near a screen point at a frame offset.";
            nearby.ParametersJson = "{\"type\": \"object\", \"additionalProperties\": false, \"required\": [\"x\", \"y\", \"frame\"], \"properties\": {\"x\": {\"type\": \"number\"}, \"y\": {\"type\": \"number\"}, \"frame\": {\"type\": \"integer\"}}}";
            tools.Add(nearby);
            SparkToolDefinition inspect = new SparkToolDefinition();
            inspect.Name = "inspect";
            inspect.Description = "Read recorded metadata for one target.";
            inspect.ParametersJson = "{\"type\": \"object\", \"additionalProperties\": false, \"required\": [\"target\"], \"properties\": {\"target\": {\"type\": \"string\"}}}";
            tools.Add(inspect);
            return tools;
        }
    }

    public static class ReportChecker
    {
        public static List<string> FindUnmanifestedNumbers(string draft, string manifestJson, string specWords)
        {
            List<string> offending = new List<string>();
            if (draft == null || draft.Length == 0)
            {
                return offending;
            }
            List<double> allowed = ExtractNumbers(manifestJson);
            allowed.AddRange(ExtractNumbers(specWords));
            foreach (Match match in Regex.Matches(draft, "-?[0-9]+(\\.[0-9]+)?"))
            {
                double value;
                if (!double.TryParse(match.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    continue;
                }
                bool found = false;
                for (int i = 0; i < allowed.Count; i++)
                {
                    if (Math.Abs(allowed[i] - value) < 0.000000001)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    offending.Add(match.Value);
                }
            }
            return offending;
        }

        private static List<double> ExtractNumbers(string text)
        {
            List<double> values = new List<double>();
            if (text == null || text.Length == 0)
            {
                return values;
            }
            foreach (Match match in Regex.Matches(text, "-?[0-9]+(\\.[0-9]+)?"))
            {
                double value;
                if (double.TryParse(match.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    values.Add(value);
                }
            }
            return values;
        }
    }

    public static class FlatDraft
    {
        private static readonly string[] ProbeNames = new string[]
        {
            "VisiblePixels",
            "VisibleFraction",
            "RectMinX",
            "RectMinY",
            "RectMaxX",
            "RectMaxY",
            "OverflowPx",
            "Inside",
            "Alpha",
            "Active",
            "TopmostAt",
            "RaycastTargetAt",
            "ScreenArea",
            "TextOverflowing",
            "Culled",
            "Layer",
            "SortOrder",
            "MeanColorRed",
            "MeanColorGreen",
            "MeanColorBlue",
            "ConsoleErrors", "Scale",
            "Scale"
        };

        private static readonly string[] OpNames = new string[]
        {
            "LessThan",
            "LessOrEqual",
            "GreaterThan",
            "GreaterOrEqual",
            "Equal",
            "NotEqual"
        };

        public static string SchemaJson()
        {
            string clause = @"{""type"": ""object"", ""additionalProperties"": false, ""required"": [""probe"", ""target"", ""secondTarget"", ""op"", ""numberValue"", ""boolValue"", ""textValue""], ""properties"": {""probe"": {""type"": ""string"", ""enum"": [""VisiblePixels"", ""VisibleFraction"", ""RectMinX"", ""RectMinY"", ""RectMaxX"", ""RectMaxY"", ""OverflowPx"", ""Inside"", ""Alpha"", ""Active"", ""TopmostAt"", ""RaycastTargetAt"", ""ScreenArea"", ""TextOverflowing"", ""Culled"", ""Layer"", ""SortOrder"", ""MeanColorRed"", ""MeanColorGreen"", ""MeanColorBlue"", ""ConsoleErrors""]}, ""target"": {""type"": [""string"", ""null""]}, ""secondTarget"": {""type"": [""string"", ""null""]}, ""op"": {""type"": ""string"", ""enum"": [""LessThan"", ""LessOrEqual"", ""GreaterThan"", ""GreaterOrEqual"", ""Equal"", ""NotEqual""]}, ""numberValue"": {""type"": [""number"", ""null""]}, ""boolValue"": {""type"": [""boolean"", ""null""]}, ""textValue"": {""type"": [""string"", ""null""]}}}";
            return @"{""type"": ""object"", ""additionalProperties"": false, ""required"": [""specId"", ""developerSentence"", ""targets"", ""measurementFrames"", ""failureCombinator"", ""failureClauses"", ""invariantClauses""], ""properties"": {""specId"": {""type"": ""string""}, ""developerSentence"": {""type"": ""string""}, ""targets"": {""type"": ""array"", ""items"": {""type"": ""object"", ""additionalProperties"": false, ""required"": [""name"", ""elementId""], ""properties"": {""name"": {""type"": ""string""}, ""elementId"": {""type"": ""string""}}}}, ""measurementFrames"": {""type"": ""array"", ""items"": {""type"": ""integer""}}, ""failureCombinator"": {""type"": ""string"", ""enum"": [""all"", ""any""]}, ""failureClauses"": {""type"": ""array"", ""items"": " + clause + @"}, ""invariantClauses"": {""type"": ""array"", ""items"": " + clause + @"}}}";
        }

        public static string ToOracleSpecJson(string flatJson)
        {
            JObject root = ParseObject(flatJson);
            string specId = RequireString(root, "specId");
            string sentence = RequireString(root, "developerSentence");
            JArray targets = RequireArray(root, "targets");
            JArray frames = RequireArray(root, "measurementFrames");
            string combinator = RequireString(root, "failureCombinator");
            string failureKind;
            if (combinator == "all")
            {
                failureKind = "And";
            }
            else if (combinator == "any")
            {
                failureKind = "Or";
            }
            else
            {
                throw new SparkSchemaException("failureCombinator", "Combinator must be all or any.");
            }
            JArray failureClauses = RequireArray(root, "failureClauses");
            if (failureClauses.Count == 0)
            {
                throw new SparkSchemaException("failureClauses", "At least one failure clause is required.");
            }
            JArray invariantClauses = RequireArray(root, "invariantClauses");
            JObject spec = new JObject();
            spec["specId"] = specId;
            spec["developerSentence"] = sentence;
            spec["targets"] = ReadTargets(targets);
            List<int> frameList = ReadFrames(frames);
            spec["windowStart"] = frameList.Count == 0 ? null : (JToken)frameList[0];
            spec["windowEnd"] = frameList.Count == 0 ? null : (JToken)frameList[frameList.Count - 1];
            JArray frameDtos = new JArray();
            for (int i = 0; i < frameList.Count; i++)
            {
                frameDtos.Add(frameList[i]);
            }
            spec["measurementFrames"] = frameDtos;
            spec["failure"] = BuildBranch(failureKind, failureClauses);
            JArray invariants = new JArray();
            for (int i = 0; i < invariantClauses.Count; i++)
            {
                invariants.Add(BuildComparison(invariantClauses[i], "invariantClauses[" + i + "]"));
            }
            spec["invariants"] = invariants;
            return spec.ToString();
        }

        private static JObject ParseObject(string flatJson)
        {
            if (flatJson == null || flatJson.Length == 0)
            {
                throw new SparkSchemaException("draft", "Flat draft is empty.");
            }
            try
            {
                JToken parsed = JToken.Parse(flatJson);
                if (parsed.Type != JTokenType.Object)
                {
                    throw new SparkSchemaException("draft", "Flat draft must be a JSON object.");
                }
                return (JObject)parsed;
            }
            catch (SparkSchemaException)
            {
                throw;
            }
            catch (Exception inner)
            {
                throw new SparkSchemaException("draft", "Flat draft is not valid JSON: " + inner.Message);
            }
        }

        private static string RequireString(JObject parent, string name)
        {
            JToken token = parent[name];
            if (token == null || token.Type != JTokenType.String)
            {
                throw new SparkSchemaException(name, "Field " + name + " must be a string.");
            }
            return token.Value<string>();
        }

        private static JArray RequireArray(JObject parent, string name)
        {
            JToken token = parent[name];
            if (token == null || token.Type != JTokenType.Array)
            {
                throw new SparkSchemaException(name, "Field " + name + " must be an array.");
            }
            return (JArray)token;
        }

        private static JArray ReadTargets(JArray targets)
        {
            JArray dtos = new JArray();
            for (int i = 0; i < targets.Count; i++)
            {
                string path = "targets[" + i + "]";
                if (targets[i] == null || targets[i].Type != JTokenType.Object)
                {
                    throw new SparkSchemaException(path, "Each target must be an object.");
                }
                JObject target = (JObject)targets[i];
                JObject dto = new JObject();
                dto["name"] = RequireString(target, "name");
                dto["elementId"] = RequireString(target, "elementId");
                dtos.Add(dto);
            }
            return dtos;
        }

        private static List<int> ReadFrames(JArray frames)
        {
            List<int> values = new List<int>();
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] == null || frames[i].Type != JTokenType.Integer)
                {
                    throw new SparkSchemaException("measurementFrames[" + i + "]", "Each measurement frame must be an integer.");
                }
                values.Add(frames[i].Value<int>());
            }
            values.Sort();
            return values;
        }

        private static JObject BuildBranch(string kind, JArray clauses)
        {
            JObject branch = new JObject();
            branch["kind"] = kind;
            branch["probe"] = null;
            branch["op"] = "LessThan";
            branch["numberValue"] = null;
            branch["boolValue"] = null;
            branch["textValue"] = null;
            JArray children = new JArray();
            for (int i = 0; i < clauses.Count; i++)
            {
                children.Add(BuildComparison(clauses[i], "failureClauses[" + i + "]"));
            }
            branch["children"] = children;
            branch["windowStart"] = null;
            branch["windowEnd"] = null;
            branch["threshold"] = null;
            return branch;
        }

        private static JObject BuildComparison(JToken clause, string path)
        {
            if (clause == null || clause.Type != JTokenType.Object)
            {
                throw new SparkSchemaException(path, "Each clause must be an object.");
            }
            JObject source = (JObject)clause;
            string probe = RequireString(source, "probe");
            if (!IsKnownProbe(probe))
            {
                throw new SparkSchemaException(path + ".probe", "Unknown probe " + probe + ".");
            }
            string op = RequireString(source, "op");
            if (!IsKnownOp(op))
            {
                throw new SparkSchemaException(path + ".op", "Unknown comparison " + op + ".");
            }
            JObject node = new JObject();
            node["kind"] = "Comparison";
            JObject probeDto = new JObject();
            probeDto["probe"] = probe;
            probeDto["target"] = NullableString(source["target"], path + ".target");
            probeDto["secondTarget"] = NullableString(source["secondTarget"], path + ".secondTarget");
            probeDto["pointX"] = null;
            probeDto["pointY"] = null;
            probeDto["regionX"] = null;
            probeDto["regionY"] = null;
            probeDto["regionW"] = null;
            probeDto["regionH"] = null;
            node["probe"] = probeDto;
            node["op"] = op;
            node["numberValue"] = NullableNumber(source["numberValue"], path + ".numberValue");
            node["boolValue"] = NullableBool(source["boolValue"], path + ".boolValue");
            node["textValue"] = NullableString(source["textValue"], path + ".textValue");
            node["children"] = new JArray();
            node["windowStart"] = null;
            node["windowEnd"] = null;
            node["threshold"] = null;
            return node;
        }

        private static JToken NullableString(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (token.Type != JTokenType.String)
            {
                throw new SparkSchemaException(path, "Expected a string or null.");
            }
            return token;
        }

        private static JToken NullableNumber(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
            {
                throw new SparkSchemaException(path, "Expected a number or null.");
            }
            return token;
        }

        private static JToken NullableBool(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }
            if (token.Type != JTokenType.Boolean)
            {
                throw new SparkSchemaException(path, "Expected a boolean or null.");
            }
            return token;
        }

        private static bool IsKnownProbe(string name)
        {
            for (int i = 0; i < ProbeNames.Length; i++)
            {
                if (string.Equals(ProbeNames[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsKnownOp(string name)
        {
            for (int i = 0; i < OpNames.Length; i++)
            {
                if (string.Equals(OpNames[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public sealed class PickCheck
    {
        public bool Accepted { get; set; }
        public string Notes { get; set; }

        public PickCheck()
        {
            Notes = string.Empty;
        }
    }

    public sealed class PickAttempt
    {
        public string Draft { get; set; }
        public bool Accepted { get; set; }
        public string Notes { get; set; }

        public PickAttempt()
        {
            Draft = string.Empty;
            Notes = string.Empty;
        }
    }

    public sealed class PickLoopOutcome
    {
        public bool Accepted { get; set; }
        public string AcceptedDraft { get; set; }
        public List<PickAttempt> Attempts { get; set; }
        public List<string> Transcript { get; set; }

        public PickLoopOutcome()
        {
            AcceptedDraft = string.Empty;
            Attempts = new List<PickAttempt>();
            Transcript = new List<string>();
        }
    }

    public sealed class SemanticPickLoop
    {
        private readonly SparkClient spark;
        private readonly Func<string, PickCheck> validator;
        private readonly int maxAttempts;

        public SemanticPickLoop(SparkClient sparkClient, Func<string, PickCheck> pickValidator, int attemptLimit)
        {
            spark = sparkClient;
            validator = pickValidator;
            maxAttempts = attemptLimit <= 0 ? 2 : attemptLimit;
        }

        public async Task<PickLoopOutcome> RunAsync(IList<SparkMessage> start, CancellationToken token)
        {
            PickLoopOutcome outcome = new PickLoopOutcome();
            List<SparkMessage> working = new List<SparkMessage>(start);
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                outcome.Transcript.Add(attempt == 0 ? "pick" : "retry-pick");
                SparkResponse response = await spark.ChatStructuredAsync(working, "semantic_pick", SparkWorkflow.SemanticPickSchema(), token).ConfigureAwait(false);
                string draft = response.AssistantMessage == null ? string.Empty : response.AssistantMessage;
                PickCheck check = validator(draft);
                PickAttempt record = new PickAttempt();
                record.Draft = draft;
                record.Accepted = check != null && check.Accepted;
                record.Notes = check == null || check.Notes == null ? string.Empty : check.Notes;
                outcome.Attempts.Add(record);
                if (record.Accepted)
                {
                    outcome.Accepted = true;
                    outcome.AcceptedDraft = draft;
                    outcome.Transcript.Add("accepted");
                    return outcome;
                }
                outcome.Transcript.Add(attempt + 1 >= maxAttempts ? "unvalidated" : "pick-rejected");
                SparkMessage retry = new SparkMessage();
                retry.Role = "user";
                retry.Content = record.Notes;
                working.Add(retry);
            }
            return outcome;
        }
    }

    public sealed class SpecAuthoringLoop
    {
        private readonly SparkClient spark;
        private readonly IList<SparkToolDefinition> tools;
        private readonly Func<string, string, Task<string>> measure;
        private readonly Func<string, EngineCheck> engine;
        private readonly int maxToolCalls;

        public SpecAuthoringLoop(SparkClient sparkClient, IList<SparkToolDefinition> toolDefinitions, Func<string, string, Task<string>> measureExecutor, Func<string, EngineCheck> engineCheck, int maxCalls)
        {
            spark = sparkClient;
            tools = toolDefinitions;
            measure = measureExecutor;
            engine = engineCheck;
            maxToolCalls = maxCalls <= 0 ? 8 : maxCalls;
        }

        public async Task<AuthoringOutcome> RunAsync(IList<SparkMessage> start, string schemaName, string schemaJson, CancellationToken token)
        {
            AuthoringOutcome outcome = new AuthoringOutcome();
            List<SparkMessage> working = new List<SparkMessage>(start);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                outcome.Transcript.Add(attempt == 0 ? "measure" : "retry-measure");
                await MeasurePhase(working, token).ConfigureAwait(false);
                outcome.Transcript.Add(attempt == 0 ? "propose" : "retry-propose");
                SparkResponse draft = await spark.ChatStructuredAsync(working, schemaName, schemaJson, token).ConfigureAwait(false);
                EngineCheck check = engine(draft.AssistantMessage);
                if (check.Accepted)
                {
                    outcome.Status = AuthoringStatus.Accepted;
                    outcome.SpecJson = draft.AssistantMessage;
                    outcome.Transcript.Add("accepted");
                    return outcome;
                }
                outcome.SpecJson = draft.AssistantMessage;
                outcome.Transcript.Add("engine-rejected");
                SparkMessage retry = new SparkMessage();
                retry.Role = "user";
                retry.Content = check.Notes;
                working.Add(retry);
            }
            outcome.Status = AuthoringStatus.NeedsHandEdit;
            outcome.Transcript.Add("hand-edit");
            return outcome;
        }

        private async Task MeasurePhase(List<SparkMessage> working, CancellationToken token)
        {
            int calls = 0;
            while (true)
            {
                SparkResponse response = await spark.ChatAsync(working, tools, token).ConfigureAwait(false);
                if (response.ToolCalls == null || response.ToolCalls.Count == 0)
                {
                    return;
                }
                if (calls + response.ToolCalls.Count > maxToolCalls)
                {
                    throw new SparkToolLimitException("Tool call limit of " + maxToolCalls + " exceeded.");
                }
                SparkMessage assistantRecord = new SparkMessage();
                assistantRecord.Role = "assistant";
                assistantRecord.Content = response.AssistantMessage;
                for (int i = 0; i < response.ToolCalls.Count; i++)
                {
                    SparkToolCall copy = new SparkToolCall();
                    copy.Id = response.ToolCalls[i].Id;
                    copy.Name = response.ToolCalls[i].Name;
                    copy.ArgumentsJson = response.ToolCalls[i].ArgumentsJson;
                    assistantRecord.ToolCalls.Add(copy);
                }
                working.Add(assistantRecord);
                for (int i = 0; i < response.ToolCalls.Count; i++)
                {
                    SparkToolCall call = response.ToolCalls[i];
                    string result = await measure(call.Name, call.ArgumentsJson).ConfigureAwait(false);
                    SparkMessage toolMessage = new SparkMessage();
                    toolMessage.Role = "tool";
                    toolMessage.Content = result;
                    toolMessage.ToolCallId = call.Id;
                    working.Add(toolMessage);
                    calls++;
                }
            }
        }
    }
}
