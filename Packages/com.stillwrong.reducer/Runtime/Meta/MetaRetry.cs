using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace StillWrong.Meta
{
    public sealed class MetaRetryPolicy
    {
        public int MaxRetries { get; }
        public Func<int, TimeSpan> Backoff { get; }
        public Func<TimeSpan, CancellationToken, Task> Delay { get; }

        public MetaRetryPolicy(int maxRetries, Func<int, TimeSpan> backoff, Func<TimeSpan, CancellationToken, Task> delay)
        {
            MaxRetries = maxRetries;
            Backoff = backoff;
            Delay = delay;
        }

        public static MetaRetryPolicy Default(Func<TimeSpan, CancellationToken, Task> delay)
        {
            return new MetaRetryPolicy(3, DefaultBackoff, delay);
        }

        public static TimeSpan DefaultBackoff(int attempt)
        {
            double seconds = 1.0;
            for (int i = 1; i < attempt; i++)
            {
                seconds = seconds * 2.0;
            }
            return TimeSpan.FromSeconds(seconds);
        }
    }

    internal static class MetaRetry
    {
        public static async Task<HttpResponseMessage> SendWithRetry(
            HttpClient client,
            Func<HttpRequestMessage> buildRequest,
            MetaRetryPolicy policy,
            List<TimeSpan> observedDelays,
            CancellationToken cancellationToken)
        {
            int attempt = 0;
            while (true)
            {
                HttpRequestMessage request = buildRequest();
                HttpResponseMessage response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if (status != 429)
                {
                    return response;
                }
                if (attempt >= policy.MaxRetries)
                {
                    string body = await SafeReadBody(response).ConfigureAwait(false);
                    throw new MetaApiException(status, body);
                }
                TimeSpan wait = RetryAfterDelay(response);
                if (wait == TimeSpan.Zero)
                {
                    int nextAttempt = attempt + 1;
                    wait = policy.Backoff(nextAttempt);
                }
                if (observedDelays != null)
                {
                    observedDelays.Add(wait);
                }
                if (policy.Delay != null)
                {
                    await policy.Delay(wait, cancellationToken).ConfigureAwait(false);
                }
                response.Dispose();
                request.Dispose();
                attempt++;
            }
        }

        public static TimeSpan RetryAfterDelay(HttpResponseMessage response)
        {
            if (response == null || response.Headers == null)
            {
                return TimeSpan.Zero;
            }
            if (!response.Headers.Contains("Retry-After"))
            {
                return TimeSpan.Zero;
            }
            foreach (string value in response.Headers.GetValues("Retry-After"))
            {
                if (value == null)
                {
                    continue;
                }
                string trimmed = value.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                if (int.TryParse(trimmed, out int seconds))
                {
                    if (seconds < 0)
                    {
                        continue;
                    }
                    return TimeSpan.FromSeconds((double)seconds);
                }
            }
            return TimeSpan.Zero;
        }

        private static async Task<string> SafeReadBody(HttpResponseMessage response)
        {
            try
            {
                if (response.Content == null)
                {
                    return string.Empty;
                }
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
