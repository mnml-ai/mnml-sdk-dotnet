using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mnml
{
    /// <summary>Renders from a source image or a prompt alone. <c>Count</c> starts several jobs at once.</summary>
    public sealed class RendersResource
    {
        private readonly MnmlClient _c;
        internal RendersResource(MnmlClient client) => _c = client;

        /// <summary>
        /// Starts a render. With <paramref name="waitSeconds"/> (1–90) the API holds the answer while
        /// the render runs, and <see cref="RenderStarted.Jobs"/> carries the outputs, with no polling.
        /// </summary>
        public Task<RenderStarted> CreateAsync(RenderRequest request, int? waitSeconds = null,
            string? idempotencyKey = null, CancellationToken cancellationToken = default) =>
            _c.RequestAsync<RenderStarted>(HttpMethod.Post, MnmlClient.WaitQuery("/v2/renders", waitSeconds), request,
                idempotencyKey, _c.HeldTimeout(waitSeconds), cancellationToken);

        /// <summary>
        /// Starts the render and waits for every job it started (one per <c>Count</c>). The API holds
        /// the first answer while it runs, so most renders come back without a single poll.
        /// </summary>
        public async Task<IReadOnlyList<Job>> CreateAndWaitAsync(RenderRequest request, WaitOptions? wait = null,
            string? idempotencyKey = null, CancellationToken cancellationToken = default)
        {
            var started = await CreateAsync(request, MnmlClient.MaxWaitSeconds, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            var jobs = started.Jobs ?? new List<Job>();
            if (jobs.Count == started.Ids.Count && jobs.All(j => j.IsSettled)) return jobs;
            return await _c.Jobs.WaitAllAsync(started.Ids, wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Prompt edits and erasing, over the whole image or a region.</summary>
    public sealed class EditsResource
    {
        private readonly MnmlClient _c;
        internal EditsResource(MnmlClient client) => _c = client;

        /// <summary>Starts an edit.</summary>
        public Task<JobStarted> CreateAsync(EditRequest request, string? idempotencyKey = null,
            CancellationToken cancellationToken = default) =>
            _c.RequestAsync<JobStarted>(HttpMethod.Post, "/v2/edits", request, idempotencyKey,
                _c.HeldTimeout(null), cancellationToken);

        /// <summary>Starts an edit and waits for it.</summary>
        public async Task<Job> CreateAndWaitAsync(EditRequest request, WaitOptions? wait = null,
            string? idempotencyKey = null, CancellationToken cancellationToken = default)
        {
            var started = await CreateAsync(request, idempotencyKey, cancellationToken).ConfigureAwait(false);
            return await _c.Jobs.WaitAsync(started.Id, wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Upscale, enhance, background removal and outpainting.</summary>
    public sealed class EnhancementsResource
    {
        private readonly MnmlClient _c;
        internal EnhancementsResource(MnmlClient client) => _c = client;

        /// <summary>Starts an enhancement.</summary>
        public Task<JobStarted> CreateAsync(EnhancementRequest request, string? idempotencyKey = null,
            CancellationToken cancellationToken = default) =>
            _c.RequestAsync<JobStarted>(HttpMethod.Post, "/v2/enhancements", request, idempotencyKey,
                _c.HeldTimeout(null), cancellationToken);

        /// <summary>Starts an enhancement and waits for it.</summary>
        public async Task<Job> CreateAndWaitAsync(EnhancementRequest request, WaitOptions? wait = null,
            string? idempotencyKey = null, CancellationToken cancellationToken = default)
        {
            var started = await CreateAsync(request, idempotencyKey, cancellationToken).ConfigureAwait(false);
            return await _c.Jobs.WaitAsync(started.Id, wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Video from a still image. A clip takes minutes.</summary>
    public sealed class VideosResource
    {
        private readonly MnmlClient _c;
        internal VideosResource(MnmlClient client) => _c = client;

        /// <summary>Starts a video.</summary>
        public Task<JobStarted> CreateAsync(VideoRequest request, string? idempotencyKey = null,
            CancellationToken cancellationToken = default) =>
            _c.RequestAsync<JobStarted>(HttpMethod.Post, "/v2/videos", request, idempotencyKey,
                _c.HeldTimeout(null), cancellationToken);

        /// <summary>Starts a video and waits for it: reads every 10 s, for up to 20 minutes, unless you say otherwise.</summary>
        public async Task<Job> CreateAndWaitAsync(VideoRequest request, WaitOptions? wait = null,
            string? idempotencyKey = null, CancellationToken cancellationToken = default)
        {
            var started = await CreateAsync(request, idempotencyKey, cancellationToken).ConfigureAwait(false);
            wait ??= new WaitOptions { Interval = TimeSpan.FromSeconds(10), Timeout = TimeSpan.FromMinutes(20) };
            return await _c.Jobs.WaitAsync(started.Id, wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Read, wait for, follow and cancel jobs.</summary>
    public sealed class JobsResource
    {
        private readonly MnmlClient _c;
        internal JobsResource(MnmlClient client) => _c = client;

        /// <summary>
        /// Reads a job. With <paramref name="waitSeconds"/> (1–90) the API holds the read and answers
        /// as soon as the job settles.
        /// </summary>
        public Task<Job> GetAsync(string id, int? waitSeconds = null, CancellationToken cancellationToken = default) =>
            _c.RequestAsync<Job>(HttpMethod.Get, MnmlClient.WaitQuery($"/v2/jobs/{Uri.EscapeDataString(id)}", waitSeconds),
                null, null, _c.HeldTimeout(waitSeconds), cancellationToken);

        /// <summary>Cancels a job; one cancelled before it produced anything is refunded.</summary>
        public Task<JobCanceled> CancelAsync(string id, string? idempotencyKey = null,
            CancellationToken cancellationToken = default) =>
            _c.RequestAsync<JobCanceled>(HttpMethod.Post, $"/v2/jobs/{Uri.EscapeDataString(id)}/cancel", null,
                idempotencyKey, _c.HeldTimeout(null), cancellationToken);

        /// <summary>
        /// Reads the job until it succeeds, fails or is cancelled, and returns it. Each read asks the
        /// API to hold it until the job settles, so a render usually takes one read. Throws
        /// <see cref="MnmlTimeoutException"/> when the time is up; the job keeps running.
        /// </summary>
        public async Task<Job> WaitAsync(string id, WaitOptions? wait = null, CancellationToken cancellationToken = default)
        {
            wait ??= new WaitOptions();
            var deadline = DateTimeOffset.UtcNow + wait.Timeout;
            while (true)
            {
                var left = (int)Math.Floor((deadline - DateTimeOffset.UtcNow).TotalSeconds);
                var began = DateTimeOffset.UtcNow;
                var job = await GetAsync(id, left >= 1 ? Math.Min(MnmlClient.MaxWaitSeconds, left) : (int?)null,
                    cancellationToken).ConfigureAwait(false);
                if (job.IsSettled) return job;
                if (DateTimeOffset.UtcNow + wait.Interval > deadline) throw new MnmlTimeoutException(id);
                // Answered early, still running: space the next read.
                var pause = wait.Interval - (DateTimeOffset.UtcNow - began);
                if (pause > TimeSpan.Zero) await _c.Delay(pause, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary><see cref="WaitAsync"/> for several jobs at once, such as every id a render with <c>Count</c> started.</summary>
        public async Task<IReadOnlyList<Job>> WaitAllAsync(IEnumerable<string> ids, WaitOptions? wait = null,
            CancellationToken cancellationToken = default) =>
            await Task.WhenAll(ids.Select(id => WaitAsync(id, wait, cancellationToken))).ConfigureAwait(false);

        /// <summary>
        /// Follows a job as it runs, over one connection (Server-Sent Events): each change of the job,
        /// then one last event (<c>done</c>, <c>timeout</c> or <c>error</c>), after which the
        /// enumeration ends. Stopping the loop, or the token, hangs up; the job keeps running.
        /// <code>
        /// await foreach (var e in mnml.Jobs.StreamAsync(id))
        ///     if (e.Type == "done") Console.WriteLine(e.Job!.Outputs[0].Url);
        /// </code>
        /// </summary>
        public async IAsyncEnumerable<JobStreamEvent> StreamAsync(string id,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var path = $"/v2/jobs/{Uri.EscapeDataString(id)}/events";
            using var res = await _c.SendAsync(() => _c.StreamRequest(path), _c.HeldTimeout(null),
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                throw MnmlClient.ErrorFrom(res, await MnmlClient.ReadErrorAsync(res).ConfigureAwait(false));

            using var stream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
            // ReadLineAsync takes no token before .NET 7: hang up by closing the stream.
            using var hangUp = cancellationToken.Register(() => stream.Dispose());
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var block = new List<string>();
            while (true)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (line is null) break;
                if (line.Length > 0)
                {
                    block.Add(line);
                    continue;
                }

                var ev = JobStream.Parse(block);
                block.Clear();
                if (ev is null) continue;
                yield return ev;
                if (ev.IsFinal) yield break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            string? requestId = null;
            if (res.Headers.TryGetValues("X-Request-Id", out var ids)) requestId = ids.FirstOrDefault();
            throw new MnmlException("STREAM_ENDED",
                "The job stream ended before the job settled. Read the job, or open the stream again.",
                (int)res.StatusCode, requestId);
        }
    }

    /// <summary>A job's outputs. Their links are signed and expire, so download what you keep.</summary>
    public sealed class FilesResource
    {
        private readonly MnmlClient _c;
        internal FilesResource(MnmlClient client) => _c = client;

        /// <summary>The file behind an output.</summary>
        public Task<DownloadedFile> DownloadAsync(JobOutput output, CancellationToken cancellationToken = default) =>
            DownloadAsync(output.Url, cancellationToken);

        /// <summary>
        /// The file behind an output's link. The signature in the link is the credential, so your key
        /// is not sent with it. An expired link throws <see cref="MnmlException"/> with
        /// <c>NOT_FOUND</c>: read the job again for a fresh one.
        /// </summary>
        public async Task<DownloadedFile> DownloadAsync(string url, CancellationToken cancellationToken = default)
        {
            HttpRequestMessage Build()
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                _c.AddHeaders(req, authorized: false, accept: "*/*");
                return req;
            }

            using var res = await _c.SendAsync(Build, _c.HeldTimeout(null), HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                throw MnmlClient.ErrorFrom(res, await MnmlClient.ReadErrorAsync(res).ConfigureAwait(false));
            var data = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return new DownloadedFile(data, res.Content.Headers.ContentType?.MediaType);
        }
    }

    /// <summary>The key's account: balance, tier and limits.</summary>
    public sealed class AccountResource
    {
        private readonly MnmlClient _c;
        internal AccountResource(MnmlClient client) => _c = client;

        /// <summary>Reads the account.</summary>
        public Task<Account> GetAsync(CancellationToken cancellationToken = default) =>
            _c.RequestAsync<Account>(HttpMethod.Get, "/v2/account", null, null, _c.HeldTimeout(null), cancellationToken);
    }

    /// <summary>The engines and video models, with their prices and capabilities.</summary>
    public sealed class EnginesResource
    {
        private readonly MnmlClient _c;
        internal EnginesResource(MnmlClient client) => _c = client;

        /// <summary>Lists them.</summary>
        public Task<EngineCatalog> ListAsync(CancellationToken cancellationToken = default) =>
            _c.RequestAsync<EngineCatalog>(HttpMethod.Get, "/v2/engines", null, null, _c.HeldTimeout(null),
                cancellationToken);
    }

    internal static class JobStream
    {
        /// <summary>One Server-Sent Events block as an event; null for a comment (<c>: ping</c>) or an unknown event.</summary>
        internal static JobStreamEvent? Parse(IReadOnlyList<string> lines)
        {
            var type = "message";
            var data = new List<string>();
            foreach (var line in lines)
            {
                if (line.StartsWith(":", StringComparison.Ordinal)) continue;
                var colon = line.IndexOf(':');
                var field = colon < 0 ? line : line.Substring(0, colon);
                var value = colon < 0 ? "" : line.Substring(colon + 1);
                if (value.StartsWith(" ", StringComparison.Ordinal)) value = value.Substring(1);
                if (field == "event") type = value;
                else if (field == "data") data.Add(value);
            }

            if (data.Count == 0) return null;
            var payload = string.Join("\n", data);
            switch (type)
            {
                case "job":
                case "done":
                case "timeout":
                    return new JobStreamEvent(type, JsonSerializer.Deserialize<Job>(payload, MnmlClient.Json), null);
                case "error":
                    return new JobStreamEvent(type, null, JsonSerializer.Deserialize<JobError>(payload, MnmlClient.Json));
                default:
                    return null;
            }
        }
    }
}
