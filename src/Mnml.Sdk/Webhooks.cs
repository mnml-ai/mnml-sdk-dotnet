using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mnml
{
    /// <summary>
    /// Verifies webhook deliveries: the Standard Webhooks scheme the API signs with
    /// (<c>webhook-id</c>, <c>webhook-timestamp</c>, <c>webhook-signature</c>, HMAC-SHA256 of
    /// <c>id.timestamp.body</c> under your <c>whsec_</c> secret). Pass the RAW body, the exact bytes
    /// received before any JSON parsing, or the check fails.
    /// <code>
    /// // ASP.NET Core
    /// var body = await new StreamReader(Request.Body).ReadToEndAsync();
    /// var ev = Webhooks.Verify(body, name => Request.Headers[name].ToString(), secret);
    /// if (ev.Type == "job.succeeded") Console.WriteLine(ev.AsJob()!.Outputs[0].Url);
    /// </code>
    /// </summary>
    public static class Webhooks
    {
        /// <summary>How far the timestamp may be from now.</summary>
        public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

        /// <summary>The parsed event, or a <see cref="WebhookVerificationException"/> when the signature or the timestamp does not hold.</summary>
        public static WebhookEvent Verify(string rawBody, Func<string, string?> header, string secret,
            DateTimeOffset? now = null)
        {
            var id = header("webhook-id");
            var ts = header("webhook-timestamp");
            var sig = header("webhook-signature");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(ts) || string.IsNullOrEmpty(sig))
                throw new WebhookVerificationException("Missing webhook headers.");
            if (!long.TryParse(ts, out var timestamp)
                || Math.Abs((now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() - timestamp) > Tolerance.TotalSeconds)
                throw new WebhookVerificationException("Webhook timestamp is too old or in the future.");

            var expected = Encoding.ASCII.GetBytes(Sign(secret, id!, timestamp, rawBody));
            var ok = false;
            // The header may carry several space-separated signatures (during a rotation).
            foreach (var candidate in sig!.Split(' '))
                ok |= FixedTimeEquals(Encoding.ASCII.GetBytes(candidate), expected);
            if (!ok) throw new WebhookVerificationException("Webhook signature does not match.");

            return JsonSerializer.Deserialize<WebhookEvent>(rawBody, MnmlClient.Json)
                   ?? throw new WebhookVerificationException("The webhook body is empty.");
        }

        /// <summary>The same, with the headers as a dictionary (names in any case).</summary>
        public static WebhookEvent Verify(string rawBody, IEnumerable<KeyValuePair<string, string>> headers,
            string secret, DateTimeOffset? now = null)
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers) byName[h.Key] = h.Value;
            return Verify(rawBody, name => byName.TryGetValue(name, out var v) ? v : null, secret, now);
        }

        /// <summary>The <c>webhook-signature</c> value for a delivery: <c>v1,&lt;base64&gt;</c>.</summary>
        public static string Sign(string secret, string id, long timestampSeconds, string body)
        {
            var key = Convert.FromBase64String(secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret.Substring(6) : secret);
            using var hmac = new HMACSHA256(key);
            var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{id}.{timestampSeconds}.{body}"));
            return "v1," + Convert.ToBase64String(mac);
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    /// <summary>A delivery whose signature or timestamp does not hold.</summary>
    public sealed class WebhookVerificationException : Exception
    {
        /// <summary>Why.</summary>
        public WebhookVerificationException(string message) : base(message) { }
    }

    /// <summary>
    /// A webhook message. <see cref="Type"/> is <c>job.succeeded</c>, <c>job.failed</c>,
    /// <c>job.canceled</c>, <c>credits.low</c> or <c>webhook.test</c>; new types may be added, so
    /// ignore one you do not handle.
    /// </summary>
    public sealed class WebhookEvent
    {
        /// <summary>What happened.</summary>
        [JsonPropertyName("type")] public string Type { get; set; } = "";

        /// <summary>When.</summary>
        [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; set; }

        /// <summary>The event's data, as sent.</summary>
        [JsonPropertyName("data")] public JsonElement Data { get; set; }

        /// <summary>For a <c>job.*</c> event: the job, as <see cref="JobsResource.GetAsync"/> returns it.</summary>
        public Job? AsJob() => Type.StartsWith("job.", StringComparison.Ordinal)
            ? Data.Deserialize<Job>(MnmlClient.Json)
            : null;

        /// <summary>For <c>credits.low</c>: the balance and the threshold it fell under.</summary>
        public CreditsLow? AsCreditsLow() => Type == "credits.low" ? Data.Deserialize<CreditsLow>(MnmlClient.Json) : null;
    }

    /// <summary><c>credits.low</c>: the balance fell under the threshold (sent at most once a day).</summary>
    public sealed class CreditsLow
    {
#pragma warning disable CS1591
        [JsonPropertyName("balance")] public long Balance { get; set; }
        [JsonPropertyName("threshold")] public long Threshold { get; set; }
#pragma warning restore CS1591
    }
}
