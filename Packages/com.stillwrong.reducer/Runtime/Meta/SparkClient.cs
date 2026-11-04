using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StillWrong.Meta
{
    public sealed class SparkClientOptions
    {
        public string BaseUrl { get; set; }
        public string ModelId { get; set; }
        public bool AllowContributor { get; set; }
        public Func<string> ApiKeyProvider { get; set; }

        public SparkClientOptions()
        {
            BaseUrl = "https://api.meta.ai/v1";
            ModelId = "muse-spark-1.3";
            AllowContributor = false;
            ApiKeyProvider = DefaultApiKeyProvider;
        }

        public static string DefaultApiKeyProvider()
        {
            return Environment.GetEnvironmentVariable("MODEL_API_KEY");
        }
    }

    public sealed class SparkMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public string ToolCallId { get; set; }
        public string Name { get; set; }
        public List<SparkToolCall> ToolCalls { get; set; }

        public SparkMessage()
        {
            Role = string.Empty;
            Content = string.Empty;
            ToolCallId = string.Empty;
            Name = string.Empty;
            ToolCalls = new List<SparkToolCall>();
        }
    }

    public sealed class SparkToolDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string ParametersJson { get; set; }

        public SparkToolDefinition()
        {
            Name = string.Empty;
            Description = string.Empty;
            ParametersJson = string.Empty;
        }
    }

    public sealed class SparkToolCall
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ArgumentsJson { get; set; }

        public SparkToolCall()
        {
            Id = string.Empty;
            Name = string.Empty;
            ArgumentsJson = string.Empty;
        }
    }

    public sealed class SparkResponse
    {
        public string AssistantMessage { get; set; }
        public List<SparkToolCall> ToolCalls { get; set; }
        public string FinishReason { get; set; }
        public int? InputTokens { get; set; }
        public int? OutputTokens { get; set; }

        public SparkResponse()
        {
            AssistantMessage = string.Empty;
            ToolCalls = new List<SparkToolCall>();
            FinishReason = string.Empty;
        }
    }

    public sealed class SparkClient
    {
        private readonly HttpClient httpClient;
        private readonly SparkClientOptions options;
        private readonly CostLog costLog;
        private readonly MetaRetryPolicy retryPolicy;

        public SparkClient(HttpMessageHandler handler, SparkClientOptions clientOptions, CostLog log, MetaRetryPolicy policy)
        {
            if (clientOptions == null)
            {
                clientOptions = new SparkClientOptions();
            }
            ValidateModel(clientOptions);
            options = clientOptions;
            costLog = log;
            if (policy != null)
            {
                retryPolicy = policy;
            }
            else
            {
                retryPolicy = new MetaRetryPolicy(3, MetaRetryPolicy.DefaultBackoff, DefaultDelay);
            }
            if (handler != null)
            {
                httpClient = new HttpClient(handler, false);
            }
            else
            {
                httpClient = new HttpClient();
            }
        }

        public SparkClient(HttpClient client, SparkClientOptions clientOptions, CostLog log, MetaRetryPolicy policy)
        {
            if (clientOptions == null)
            {
                clientOptions = new SparkClientOptions();
            }
            ValidateModel(clientOptions);
            options = clientOptions;
            if (client == null)
            {
                httpClient = new HttpClient();
            }
            else
            {
                httpClient = client;
            }
            costLog = log;
            if (policy != null)
            {
                retryPolicy = policy;
            }
            else
            {
                retryPolicy = new MetaRetryPolicy(3, MetaRetryPolicy.DefaultBackoff, DefaultDelay);
            }
        }

        private static void ValidateModel(SparkClientOptions clientOptions)
        {
            if (clientOptions.ModelId != null && clientOptions.ModelId.IndexOf("contributor", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!clientOptions.AllowContributor)
                {
                    throw new ArgumentException("Contributor model requires AllowContributor to be enabled.");
                }
            }
        }

        private static Task DefaultDelay(TimeSpan wait, CancellationToken token)
        {
            return Task.Delay(wait, token);
        }

        public string ModelId
        {
            get { return options == null || options.ModelId == null ? string.Empty : options.ModelId; }
        }

        private string RequireApiKey()
        {
            string key = null;
            if (options.ApiKeyProvider != null)
            {
                key = options.ApiKeyProvider();
            }
            if (key == null || key.Length == 0)
            {
                throw new MissingApiKeyException();
            }
            return key;
        }

        private string BaseUrlTrimmed()
        {
            string baseUrl = options.BaseUrl;
            if (baseUrl == null || baseUrl.Length == 0)
            {
                baseUrl = "https://api.meta.ai/v1";
            }
            if (baseUrl.EndsWith("/", StringComparison.Ordinal))
            {
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);
            }
            return baseUrl;
        }

        public Task<SparkResponse> ChatAsync(IList<SparkMessage> messages, IList<SparkToolDefinition> tools, CancellationToken token)
        {
            return SendChatAsync(messages, tools, null, null, token);
        }

        public Task<SparkResponse> ChatStructuredAsync(IList<SparkMessage> messages, string schemaName, string schemaJson, CancellationToken token)
        {
            ValidateStrictSchema(schemaJson);
            return SendChatAsync(messages, null, schemaName, schemaJson, token);
        }

        public async Task<string> RunToolLoopAsync(IList<SparkMessage> messages, IList<SparkToolDefinition> tools, Func<string, string, Task<string>> executor, int maxToolCalls, CancellationToken token)
        {
            if (maxToolCalls <= 0)
            {
                maxToolCalls = 8;
            }
            List<SparkMessage> working = new List<SparkMessage>(messages);
            int calls = 0;
            while (true)
            {
                SparkResponse response = await SendChatAsync(working, tools, null, null, token).ConfigureAwait(false);
                if (response.ToolCalls == null || response.ToolCalls.Count == 0)
                {
                    return response.AssistantMessage;
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
                    string result = await executor(call.Name, call.ArgumentsJson).ConfigureAwait(false);
                    SparkMessage toolMessage = new SparkMessage();
                    toolMessage.Role = "tool";
                    toolMessage.Content = result;
                    toolMessage.ToolCallId = call.Id;
                    working.Add(toolMessage);
                    calls++;
                }
                if (calls > maxToolCalls)
                {
                    throw new SparkToolLimitException("Tool call limit of " + maxToolCalls + " exceeded.");
                }
            }
        }

        private async Task<SparkResponse> SendChatAsync(IList<SparkMessage> messages, IList<SparkToolDefinition> tools, string schemaName, string schemaJson, CancellationToken token)
        {
            string apiKey = RequireApiKey();
            string url = BaseUrlTrimmed() + "/chat/completions";
            string requestJson = BuildChatJson(options.ModelId, messages, tools, schemaName, schemaJson);
            HttpResponseMessage response = await MetaRetry.SendWithRetry(httpClient, () => BuildJsonRequest(url, apiKey, requestJson), retryPolicy, null, token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status < 200 || status >= 300)
            {
                throw new MetaApiException(status, body);
            }
            SparkResponse parsed = ParseChatResponse(body);
            if (costLog != null)
            {
                CostEntry entry = new CostEntry();
                entry.Kind = "spark";
                entry.Model = options.ModelId;
                entry.InputTokens = parsed.InputTokens;
                entry.OutputTokens = parsed.OutputTokens;
                entry.CostUsd = CostLog.SparkCost(options.ModelId, parsed.InputTokens, parsed.OutputTokens);
                CaptureRateLimit(response, entry);
                costLog.Add(entry);
            }
            return parsed;
        }

        private static HttpRequestMessage BuildJsonRequest(string url, string apiKey, string json)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return request;
        }

        private static string BuildChatJson(string model, IList<SparkMessage> messages, IList<SparkToolDefinition> tools, string schemaName, string schemaJson)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"model\": \"");
            builder.Append(MetaJson.Escape(model));
            builder.Append("\", \"messages\": [");
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }
                builder.Append(BuildMessageJson(messages[i]));
            }
            builder.Append("]");
            if (tools != null && tools.Count > 0)
            {
                builder.Append(", \"tools\": [");
                for (int i = 0; i < tools.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }
                    builder.Append(BuildToolJson(tools[i]));
                }
                builder.Append("]");
            }
            if (schemaName != null && schemaJson != null)
            {
                builder.Append(", \"response_format\": {\"type\": \"json_schema\", \"json_schema\": {\"name\": \"");
                builder.Append(MetaJson.Escape(schemaName));
                builder.Append("\", \"schema\": ");
                builder.Append(schemaJson);
                builder.Append(", \"strict\": true}}");
            }
            builder.Append("}");
            return builder.ToString();
        }

        private static string BuildMessageJson(SparkMessage message)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"role\": \"");
            builder.Append(MetaJson.Escape(message.Role));
            builder.Append("\"");
            if (message.Content != null && message.Content.Length > 0)
            {
                builder.Append(", \"content\": \"");
                builder.Append(MetaJson.Escape(message.Content));
                builder.Append("\"");
            }
            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                builder.Append(", \"tool_calls\": [");
                for (int ti = 0; ti < message.ToolCalls.Count; ti++)
                {
                    if (ti > 0)
                    {
                        builder.Append(", ");
                    }
                    SparkToolCall call = message.ToolCalls[ti];
                    builder.Append("{\"id\": \"");
                    builder.Append(MetaJson.Escape(call.Id));
                    builder.Append("\", \"type\": \"function\", \"function\": {\"name\": \"");
                    builder.Append(MetaJson.Escape(call.Name));
                    builder.Append("\", \"arguments\": \"");
                    if (call.ArgumentsJson != null && call.ArgumentsJson.Length > 0)
                    {
                        builder.Append(MetaJson.Escape(call.ArgumentsJson));
                    }
                    else
                    {
                        builder.Append(MetaJson.Escape("{}"));
                    }
                    builder.Append("\"");
                    builder.Append("}}");
                }
                builder.Append("]");
            }
            if (message.ToolCallId != null && message.ToolCallId.Length > 0)
            {
                builder.Append(", \"tool_call_id\": \"");
                builder.Append(MetaJson.Escape(message.ToolCallId));
                builder.Append("\"");
            }
            if (message.Name != null && message.Name.Length > 0)
            {
                builder.Append(", \"name\": \"");
                builder.Append(MetaJson.Escape(message.Name));
                builder.Append("\"");
            }
            builder.Append("}");
            return builder.ToString();
        }

        private static string BuildToolJson(SparkToolDefinition tool)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"type\": \"function\", \"function\": {\"name\": \"");
            builder.Append(MetaJson.Escape(tool.Name));
            builder.Append("\", \"description\": \"");
            builder.Append(MetaJson.Escape(tool.Description));
            builder.Append("\", \"parameters\": ");
            if (tool.ParametersJson != null && tool.ParametersJson.Length > 0)
            {
                builder.Append(tool.ParametersJson);
            }
            else
            {
                builder.Append("{\"type\": \"object\", \"properties\": {}, \"additionalProperties\": false, \"required\": []}");
            }
            builder.Append("}}");
            return builder.ToString();
        }

        public static void ValidateStrictSchema(string schemaJson)
        {
            if (schemaJson == null || schemaJson.Length == 0)
            {
                throw new SparkSchemaException(string.Empty, "Schema is empty.");
            }
            JToken document;
            try
            {
                document = JToken.Parse(schemaJson);
            }
            catch (Exception inner)
            {
                throw new SparkSchemaException(string.Empty, "Schema is not valid JSON: " + inner.Message);
            }
            ValidateStrictNode(document, "$");
        }

        private static void ValidateStrictNode(JToken node, string path)
        {
            JObject obj = node as JObject;
            if (obj == null)
            {
                return;
            }
            bool isObjectType = false;
            JToken typeElement;
            if (obj.TryGetValue("type", StringComparison.Ordinal, out typeElement))
            {
                if (typeElement.Type == JTokenType.String)
                {
                    string typeName = typeElement.Value<string>();
                    if (typeName != null && typeName.Equals("object", StringComparison.Ordinal))
                    {
                        isObjectType = true;
                    }
                }
            }
            JToken properties;
            if (obj.TryGetValue("properties", StringComparison.Ordinal, out properties))
            {
                isObjectType = true;
            }
            if (isObjectType)
            {
                JToken additional;
                if (!obj.TryGetValue("additionalProperties", StringComparison.Ordinal, out additional))
                {
                    throw new SparkSchemaException(path, "Object node at " + path + " is missing additionalProperties false.");
                }
                if (!(additional.Type == JTokenType.Boolean && additional.Value<bool>() == false))
                {
                    throw new SparkSchemaException(path, "Object node at " + path + " must set additionalProperties false.");
                }
                JToken required;
                if (!obj.TryGetValue("required", StringComparison.Ordinal, out required))
                {
                    throw new SparkSchemaException(path, "Object node at " + path + " is missing required.");
                }
                if (!(required is JArray))
                {
                    throw new SparkSchemaException(path, "Object node at " + path + " required must be an array.");
                }
                HashSet<string> requiredNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (JToken item in (JArray)required)
                {
                    if (item.Type == JTokenType.String)
                    {
                        requiredNames.Add(item.Value<string>());
                    }
                }
                JToken props;
                if (obj.TryGetValue("properties", StringComparison.Ordinal, out props))
                {
                    if (!(props is JObject))
                    {
                        throw new SparkSchemaException(path, "Object node at " + path + " properties must be an object.");
                    }
                    foreach (JProperty property in ((JObject)props).Properties())
                    {
                        if (!requiredNames.Contains(property.Name))
                        {
                            throw new SparkSchemaException(path, "Object node at " + path + " property " + property.Name + " is not listed in required.");
                        }
                        ValidateStrictNode(property.Value, path + "." + property.Name);
                    }
                }
            }
            JToken items;
            if (obj.TryGetValue("items", StringComparison.Ordinal, out items))
            {
                ValidateStrictNode(items, path + "[]");
            }
            string[] nestedKeys = new string[] { "additionalProperties", "contains", "if", "then", "else", "not" };
            for (int i = 0; i < nestedKeys.Length; i++)
            {
                JToken nested;
                if (obj.TryGetValue(nestedKeys[i], StringComparison.Ordinal, out nested))
                {
                    if (nested is JObject)
                    {
                        ValidateStrictNode(nested, path + "." + nestedKeys[i]);
                    }
                }
            }
            string[] defKeys = new string[] { "$defs", "definitions" };
            for (int i = 0; i < defKeys.Length; i++)
            {
                JToken defs;
                if (obj.TryGetValue(defKeys[i], StringComparison.Ordinal, out defs))
                {
                    if (defs is JObject)
                    {
                        foreach (JProperty def in ((JObject)defs).Properties())
                        {
                            ValidateStrictNode(def.Value, path + "." + defKeys[i] + "." + def.Name);
                        }
                    }
                }
            }
            string[] choiceKeys = new string[] { "anyOf", "oneOf", "allOf" };
            for (int i = 0; i < choiceKeys.Length; i++)
            {
                JToken choices;
                if (obj.TryGetValue(choiceKeys[i], StringComparison.Ordinal, out choices))
                {
                    if (choices is JArray)
                    {
                        int ci = 0;
                        foreach (JToken choice in (JArray)choices)
                        {
                            ValidateStrictNode(choice, path + "." + choiceKeys[i] + "[" + ci + "]");
                            ci++;
                        }
                    }
                }
            }
        }

        public static SparkResponse ParseChatResponse(string body)
        {
            SparkResponse response = new SparkResponse();
            JObject root = JObject.Parse(body);
            JToken choices;
            if (root.TryGetValue("choices", StringComparison.Ordinal, out choices))
            {
                if (choices is JArray && ((JArray)choices).Count > 0)
                {
                    JToken first = ((JArray)choices)[0];
                    JToken finish = first["finish_reason"];
                    if (finish != null && finish.Type == JTokenType.String)
                    {
                        response.FinishReason = finish.Value<string>();
                    }
                    JToken message = first["message"];
                    if (message != null)
                    {
                        JToken content = message["content"];
                        if (content != null)
                        {
                            if (content.Type == JTokenType.String)
                            {
                                response.AssistantMessage = content.Value<string>();
                            }
                            else if (content.Type == JTokenType.Null)
                            {
                                response.AssistantMessage = string.Empty;
                            }
                        }
                        JToken toolCalls = message["tool_calls"];
                        if (toolCalls != null && toolCalls is JArray)
                        {
                            foreach (JToken call in (JArray)toolCalls)
                            {
                                SparkToolCall parsed = new SparkToolCall();
                                JToken id = call["id"];
                                if (id != null && id.Type == JTokenType.String)
                                {
                                    parsed.Id = id.Value<string>();
                                }
                                JToken function = call["function"];
                                if (function != null)
                                {
                                    JToken name = function["name"];
                                    if (name != null && name.Type == JTokenType.String)
                                    {
                                        parsed.Name = name.Value<string>();
                                    }
                                    JToken args = function["arguments"];
                                    if (args != null)
                                    {
                                        if (args.Type == JTokenType.String)
                                        {
                                            parsed.ArgumentsJson = args.Value<string>();
                                        }
                                        else
                                        {
                                            parsed.ArgumentsJson = args.ToString(Formatting.None);
                                        }
                                    }
                                }
                                else
                                {
                                    JToken directName = call["name"];
                                    if (directName != null && directName.Type == JTokenType.String)
                                    {
                                        parsed.Name = directName.Value<string>();
                                    }
                                }
                                response.ToolCalls.Add(parsed);
                            }
                        }
                    }
                }
            }
            JToken usage = root["usage"];
            if (usage != null && usage is JObject)
            {
                response.InputTokens = ReadUsageInt((JObject)usage, "prompt_tokens", "input_tokens", response.InputTokens);
                response.OutputTokens = ReadUsageInt((JObject)usage, "completion_tokens", "output_tokens", response.OutputTokens);
            }
            return response;
        }

        private static int? ReadUsageInt(JObject usage, string first, string second, int? current)
        {
            JToken a = usage[first];
            if (a != null && a.Type == JTokenType.Integer)
            {
                long v = a.Value<long>();
                if (v >= int.MinValue && v <= int.MaxValue)
                {
                    current = (int)v;
                }
            }
            JToken b = usage[second];
            if (b != null && b.Type == JTokenType.Integer)
            {
                long v = b.Value<long>();
                if (v >= int.MinValue && v <= int.MaxValue)
                {
                    current = (int)v;
                }
            }
            return current;
        }

        private static void CaptureRateLimit(HttpResponseMessage response, CostEntry entry)
        {
            if (response == null || response.Headers == null || entry == null)
            {
                return;
            }
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
            {
                if (header.Key == null)
                {
                    continue;
                }
                if (header.Key.StartsWith("x-ratelimit", StringComparison.OrdinalIgnoreCase))
                {
                    entry.RateLimit[header.Key] = string.Join(",", header.Value);
                }
            }
        }

    }
}
