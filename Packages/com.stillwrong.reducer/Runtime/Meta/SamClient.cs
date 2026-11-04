using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StillWrong.Meta
{
    public sealed class SamClientOptions
    {
        public string BaseUrl { get; set; }
        public string ModelId { get; set; }
        public int? ClipFrameCount { get; set; }
        public Func<string> ApiKeyProvider { get; set; }

        public SamClientOptions()
        {
            BaseUrl = "https://api.meta.ai/v1";
            ModelId = "sam-3.1";
            ApiKeyProvider = DefaultApiKeyProvider;
        }

        public static string DefaultApiKeyProvider()
        {
            return Environment.GetEnvironmentVariable("MODEL_API_KEY");
        }
    }

    public sealed class SamFileUploadResult
    {
        public string FileId { get; set; }

        public SamFileUploadResult()
        {
            FileId = string.Empty;
        }
    }

    public sealed class SamClient
    {
        private readonly HttpClient httpClient;
        private readonly SamClientOptions options;
        private readonly CostLog costLog;
        private readonly MetaRetryPolicy retryPolicy;
        private readonly bool ownsClient;

        public SamClient(HttpMessageHandler handler, SamClientOptions clientOptions, CostLog log, MetaRetryPolicy policy)
        {
            if (clientOptions == null)
            {
                clientOptions = new SamClientOptions();
            }
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
            ownsClient = false;
        }

        public SamClient(HttpClient client, SamClientOptions clientOptions, CostLog log, MetaRetryPolicy policy)
        {
            if (clientOptions == null)
            {
                clientOptions = new SamClientOptions();
            }
            options = clientOptions;
            if (client == null)
            {
                httpClient = new HttpClient();
                ownsClient = true;
            }
            else
            {
                httpClient = client;
                ownsClient = false;
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

        private static Task DefaultDelay(TimeSpan wait, CancellationToken token)
        {
            return Task.Delay(wait, token);
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

        public Task<SamParseResult> SegmentImageByConceptAsync(string concept, string imageUrlOrDataUrl, CancellationToken token)
        {
            return SendSegmentationAsync(concept, true, imageUrlOrDataUrl, false, null, false, token);
        }

        public Task<SamParseResult> SegmentImageByPromptAsync(string promptText, string imageUrlOrDataUrl, CancellationToken token)
        {
            return SendSegmentationAsync(promptText, false, imageUrlOrDataUrl, false, null, false, token);
        }

        public Task<SamParseResult> SegmentVideoByConceptAsync(string concept, string videoUrlOrFileId, bool isFileId, CancellationToken token)
        {
            return SendSegmentationAsync(concept, true, videoUrlOrFileId, true, null, isFileId, token);
        }

        public Task<SamParseResult> SegmentVideoByPromptAsync(string promptText, string videoUrlOrFileId, bool isFileId, CancellationToken token)
        {
            return SendSegmentationAsync(promptText, false, videoUrlOrFileId, true, null, isFileId, token);
        }

        private async Task<SamParseResult> SendSegmentationAsync(string promptText, bool isConcept, string mediaValue, bool isVideo, List<TimeSpan> observedDelays, bool isFileId, CancellationToken token)
        {
            string apiKey = RequireApiKey();
            string url = BaseUrlTrimmed() + "/responses";
            string mediaJson = BuildMediaJson(isVideo, mediaValue, isFileId);
            string requestJson = BuildSegmentationJson(options.ModelId, promptText, mediaJson);
            List<TimeSpan> delays = observedDelays;
            if (delays == null)
            {
                delays = new List<TimeSpan>();
            }
            HttpResponseMessage response = await MetaRetry.SendWithRetry(httpClient, () => BuildJsonRequest(url, apiKey, requestJson), retryPolicy, delays, token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status < 200 || status >= 300)
            {
                throw new MetaApiException(status, body);
            }
            string outputText = ExtractOutputText(body);
            SamParseResult parsed = SamOutputParser.Parse(outputText);
            if (costLog != null)
            {
                CostEntry entry = new CostEntry();
                entry.Model = options.ModelId;
                if (isVideo)
                {
                    entry.Kind = "video";
                    entry.FrameCount = options.ClipFrameCount;
                    entry.CostUsd = CostLog.SamVideoCost(options.ClipFrameCount);
                }
                else
                {
                    entry.Kind = "image";
                    entry.ImageCount = 1;
                    entry.CostUsd = CostLog.SamImageCost(1);
                }
                CaptureRateLimit(response, entry);
                costLog.Add(entry);
            }
            return parsed;
        }

        private static string BuildMediaJson(bool isVideo, string mediaValue, bool isFileId)
        {
            string escaped = MetaJson.Escape(mediaValue);
            if (!isVideo)
            {
                return "{\"type\": \"input_image\", \"image_url\": \"" + escaped + "\"}";
            }
            if (isFileId)
            {
                return "{\"type\": \"input_video\", \"file_id\": \"" + escaped + "\"}";
            }
            return "{\"type\": \"input_video\", \"video_url\": \"" + escaped + "\"}";
        }

        private static string BuildSegmentationJson(string model, string promptText, string mediaJson)
        {
            string escapedModel = MetaJson.Escape(model);
            string escapedPrompt = MetaJson.Escape(promptText);
            return "{\"model\": \"" + escapedModel + "\", \"input\": [{\"type\": \"message\", \"role\": \"user\", \"content\": [{\"type\": \"input_text\", \"text\": \"" + escapedPrompt + "\"}, " + mediaJson + "]}], \"stream\": true, \"metadata\": {\"mask_encoding\": \"one_bit\"}}";
        }

        private static HttpRequestMessage BuildJsonRequest(string url, string apiKey, string json)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
            request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return request;
        }

        public static string ExtractOutputText(string body)
        {
            if (body == null || body.Length == 0)
            {
                return string.Empty;
            }
            string trimmed = body.Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }
            if (trimmed[0] == '{')
            {
                try
                {
                    JObject document = MetaJson.ParseObject(trimmed);
                    JToken lane = document["output_text"];
                    if (lane != null && lane.Type == JTokenType.String)
                    {
                        return lane.Value<string>();
                    }
                }
                catch (Exception)
                {
                }
            }
            StringBuilder output = new StringBuilder();
            string[] lines = trimmed.Split((char)10);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                if (line.Equals("data: [DONE]", StringComparison.Ordinal))
                {
                    break;
                }
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }
                string data = line.Substring(5).Trim();
                if (data.Equals("[DONE]", StringComparison.Ordinal))
                {
                    break;
                }
                try
                {
                    JObject root = MetaJson.ParseObject(data);
                    JToken typeElement = root["type"];
                    if (typeElement == null)
                    {
                        continue;
                    }
                    string type = typeElement.Value<string>();
                    if (type == null)
                    {
                        continue;
                    }
                    if (type.Equals("response.output_text.delta", StringComparison.Ordinal))
                    {
                        JToken delta = root["delta"];
                        if (delta != null && delta.Type == JTokenType.String)
                        {
                            output.Append(delta.Value<string>());
                        }
                    }
                    if (type.Equals("response.completed", StringComparison.Ordinal))
                    {
                        break;
                    }
                }
                catch (Exception)
                {
                    continue;
                }
            }
            if (output.Length > 0)
            {
                return output.ToString();
            }
            return string.Empty;
        }

        public async Task<string> UploadFileAsync(byte[] content, string filename, string contentType, CancellationToken token)
        {
            string apiKey = RequireApiKey();
            string url = BaseUrlTrimmed() + "/files";
            HttpResponseMessage response = await MetaRetry.SendWithRetry(httpClient, () => BuildUploadRequest(url, apiKey, content, filename, contentType), retryPolicy, null, token).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            int status = (int)response.StatusCode;
            if (status < 200 || status >= 300)
            {
                throw new MetaApiException(status, body);
            }
            try
            {
                JObject document = MetaJson.ParseObject(body);
                JToken id = document["id"];
                if (id != null && id.Type == JTokenType.String)
                {
                    return id.Value<string>();
                }
            }
            catch (Exception)
            {
            }
            throw new MetaApiException(status, body);
        }

        private static HttpRequestMessage BuildUploadRequest(string url, string apiKey, byte[] content, string filename, string contentType)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
            MultipartFormDataContent form = new MultipartFormDataContent();
            StringContent purpose = new StringContent("user_data", Encoding.UTF8);
            form.Add(purpose, "purpose");
            ByteArrayContent file = new ByteArrayContent(content);
            if (contentType != null && contentType.Length > 0)
            {
                try
                {
                    file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
                }
                catch (Exception)
                {
                }
            }
            string name = filename;
            if (name == null || name.Length == 0)
            {
                name = "clip.mp4";
            }
            form.Add(file, "file", name);
            request.Content = form;
            return request;
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
                    string joined = string.Join(",", header.Value);
                    entry.RateLimit[header.Key] = joined;
                }
            }
            if (response.Content != null && response.Content.Headers != null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
                {
                    if (header.Key == null)
                    {
                        continue;
                    }
                    if (header.Key.StartsWith("x-ratelimit", StringComparison.OrdinalIgnoreCase))
                    {
                        string joined = string.Join(",", header.Value);
                        entry.RateLimit[header.Key] = joined;
                    }
                }
            }
        }

    }
}
