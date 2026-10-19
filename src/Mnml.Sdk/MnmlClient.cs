using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Mnml
{
    /// <summary>How a <see cref="MnmlClient"/> talks to the API.</summary>
    public sealed class MnmlClientOptions
    {
        /// <summary>Your API key (<c>mk_live_…</c>). Defaults to the <c>MNML_API_KEY</c> environment variable.</summary>
        public string? ApiKey { get; set; }

        /// <summary>Defaults to <c>https://api.mnml.ai</c>.</summary>
        public string BaseUrl { get; set; } = "https://api.mnml.ai";

        /// <summary>Retries for a 429, a 5xx or a dropped connection. Default 2.</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>Per request. Default 60 seconds; a held read (<c>wait</c>) adds its own time.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
    }

    /// <summary>How long <c>WaitAsync</c> keeps reading a job.</summary>
    public sealed class WaitOptions
    {
        /// <summary>
        /// The least time between two reads. Default 3 seconds. Each read is held by the API until
        /// the job settles (up to 90 s), so this only spaces reads the API answered early.
        /// </summary>
        public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>Give up after this long. Default 10 minutes; the job keeps running.</summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);
    }

    /// <summary>
    /// The mnml API client. Every call returns the answer's data; a refusal throws
    /// <see cref="MnmlException"/> with the API's code. Make one and reuse it: it holds an
    /// <see cref="HttpClient"/>.
    /// <code>
    /// using var mnml = new MnmlClient();
    /// var jobs = await mnml.Renders.CreateAndWaitAsync(new RenderRequest {
    ///     Prompt = "Timber facade, dusk",
    ///     Image = ImageInput.FromFile("house.jpg"),
    /// });
    /// Console.WriteLine(jobs[0].Outputs[0].Url);
    /// </code>
    /// </summary>
    public sealed class MnmlClient : IDisposable
    {
        /// <summary>This SDK's version, sent in the User-Agent.</summary>
        public const string Version = "0.1.0";

        /// <summary>The longest the API holds an answer (<c>wait</c>), under Cloudflare's 100-second limit.</summary>
        public const int MaxWaitSeconds = 90;

        /// <summary>A longer <c>Retry-After</c> (a daily limit runs to midnight UTC) is the caller's to handle.</summary>
        internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

        internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private static readonly Random Jitter = new Random();
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly int _maxRetries;
        private readonly TimeSpan _timeout;

        /// <summary>The pause between retries and between reads; tests replace it.</summary>
        internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

        /// <summary>A client with these options, over its own <see cref="HttpClient"/> or the one you pass.</summary>
        public MnmlClient(MnmlClientOptions? options = null, HttpClient? httpClient = null)
        {
            options ??= new MnmlClientOptions();
            var key = options.ApiKey ?? Environment.GetEnvironmentVariable("MNML_API_KEY");
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Pass ApiKey, or set the MNML_API_KEY environment variable.", nameof(options));
            _apiKey = key!;
            _baseUrl = options.BaseUrl.TrimEnd('/');
            _maxRetries = Math.Max(0, options.MaxRetries);
            _timeout = options.Timeout;
            if (httpClient is null)
            {
                // The client times each attempt itself, so a held read can outlast HttpClient's 100 s.
                _http = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
                _ownsHttp = true;
            }
            else
            {
                _http = httpClient;
            }

            Renders = new RendersResource(this);
            Edits = new EditsResource(this);
            Enhancements = new EnhancementsResource(this);
            Videos = new VideosResource(this);
            Jobs = new JobsResource(this);
            Files = new FilesResource(this);
            Account = new AccountResource(this);
            Engines = new EnginesResource(this);
        }

        /// <summary>A client with this key and the default options.</summary>
        public MnmlClient(string apiKey) : this(new MnmlClientOptions { ApiKey = apiKey }) { }

        /// <summary>Renders from a source image or a prompt alone.</summary>
        public RendersResource Renders { get; }

        /// <summary>Prompt edits and erasing, over the whole image or a region.</summary>
        public EditsResource Edits { get; }

        /// <summary>Upscale, enhance, background removal and outpainting.</summary>
        public EnhancementsResource Enhancements { get; }

        /// <summary>Video from a still image.</summary>
        public VideosResource Videos { get; }

        /// <summary>Read, wait for, follow and cancel jobs.</summary>
        public JobsResource Jobs { get; }

        /// <summary>A job's outputs.</summary>
        public FilesResource Files { get; }

        /// <summary>The key's account: balance, tier and limits.</summary>
        public AccountResource Account { get; }

        /// <summary>The engines and video models, with their prices and capabilities.</summary>
        public EnginesResource Engines { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        internal TimeSpan HeldTimeout(int? waitSeconds) =>
            waitSeconds is int w ? _timeout + TimeSpan.FromSeconds(w) : _timeout;

        internal static string WaitQuery(string path, int? waitSeconds)
        {
            if (waitSeconds is null) return path;
            if (waitSeconds < 1 || waitSeconds > MaxWaitSeconds)
                throw new ArgumentOutOfRangeException(nameof(waitSeconds), $"wait is 1 to {MaxWaitSeconds} seconds.");
            return $"{path}?wait={waitSeconds}";
        }

        /// <summary>A JSON call, answered with the envelope's data or thrown as <see cref="MnmlException"/>.</summary>
        internal async Task<T> RequestAsync<T>(HttpMethod method, string path, object? body,
            string? idempotencyKey, TimeSpan timeout, CancellationToken ct)
        {
            // One key per call, kept across the client's own retries, so a retry never charges twice.
            var key = method == HttpMethod.Post ? idempotencyKey ?? Guid.NewGuid().ToString() : null;
            var json = body is null ? null : JsonSerializer.Serialize(body, body.GetType(), Json);
            HttpRequestMessage Build()
            {
                var req = new HttpRequestMessage(method, _baseUrl + path);
                AddHeaders(req, authorized: true, accept: "application/json");
                if (key != null) req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
                if (json != null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return req;
            }

            using var res = await SendAsync(Build, timeout, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            Envelope<T>? envelope = null;
            try
            {
                envelope = JsonSerializer.Deserialize<Envelope<T>>(text, Json);
            }
            catch (JsonException)
            {
                // Not the API's envelope (a proxy's error page): reported below by status.
            }

            if (res.IsSuccessStatusCode && envelope is { Success: true, Data: not null }) return envelope.Data;
            throw ErrorFrom(res, envelope?.Error);
        }

        /// <summary>The request that opens a job's event stream.</summary>
        internal HttpRequestMessage StreamRequest(string path)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
            AddHeaders(req, authorized: true, accept: "text/event-stream");
            return req;
        }

        internal void AddHeaders(HttpRequestMessage req, bool authorized, string accept)
        {
            req.Headers.TryAddWithoutValidation("User-Agent", $"mnml-sdk-dotnet/{Version}");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            if (authorized) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        /// <summary>
        /// One call with the client's retries: a 429, a 5xx or a dropped connection is sent again
        /// (the same headers, so the same idempotency key) after <c>Retry-After</c> or a backoff.
        /// The last answer is returned, whatever it is.
        /// </summary>
        internal async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> build, TimeSpan timeout,
            HttpCompletionOption completion, CancellationToken ct)
        {
            for (var attempt = 0; ; attempt++)
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attemptCts.CancelAfter(timeout);
                HttpResponseMessage res;
                try
                {
                    using var req = build();
                    res = await _http.SendAsync(req, completion, attemptCts.Token).ConfigureAwait(false);
                }
                catch (Exception e) when (!ct.IsCancellationRequested
                                          && (e is HttpRequestException || e is OperationCanceledException))
                {
                    if (attempt >= _maxRetries)
                    {
                        if (e is OperationCanceledException)
                            throw new TimeoutException($"The request did not answer within {timeout.TotalSeconds:0} s.", e);
                        throw;
                    }

                    await Delay(Backoff(attempt), ct).ConfigureAwait(false);
                    continue;
                }

                if (IsRetryable(res.StatusCode) && attempt < _maxRetries)
                {
                    var wait = RetryAfter(res) ?? Backoff(attempt);
                    if (wait <= MaxRetryAfter)
                    {
                        res.Dispose();
                        await Delay(wait, ct).ConfigureAwait(false);
                        continue;
                    }
                }

                return res;
            }
        }

        private static bool IsRetryable(HttpStatusCode status)
        {
            var code = (int)status;
            return code == 429 || code == 500 || code == 502 || code == 503 || code == 504;
        }

        private static TimeSpan? RetryAfter(HttpResponseMessage res)
        {
            var header = res.Headers.RetryAfter;
            if (header?.Delta is TimeSpan delta && delta > TimeSpan.Zero) return delta;
            if (header?.Date is DateTimeOffset date)
            {
                var left = date - DateTimeOffset.UtcNow;
                if (left > TimeSpan.Zero) return left;
            }

            return null;
        }

        /// <summary>0.5 s, 1 s, 2 s … with a little jitter.</summary>
        internal static TimeSpan Backoff(int attempt)
        {
            int jitter;
            lock (Jitter) jitter = Jitter.Next(250);
            return TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt) + jitter);
        }

        internal static MnmlException ErrorFrom(HttpResponseMessage res, EnvelopeError? error)
        {
            string? requestId = null;
            if (res.Headers.TryGetValues("X-Request-Id", out var values))
                foreach (var v in values) { requestId = v; break; }
            return new MnmlException(
                error?.Code ?? "HTTP_ERROR",
                error?.Message ?? $"The API answered {(int)res.StatusCode}.",
                (int)res.StatusCode,
                requestId,
                error?.Details,
                error?.Issues);
        }

        internal static async Task<EnvelopeError?> ReadErrorAsync(HttpResponseMessage res)
        {
            try
            {
                var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JsonSerializer.Deserialize<Envelope<JsonElement>>(text, Json)?.Error;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
