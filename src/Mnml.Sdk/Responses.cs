using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mnml
{
    /// <summary>The job statuses. <c>succeeded</c>, <c>failed</c> and <c>canceled</c> are final.</summary>
    public static class JobStatuses
    {
#pragma warning disable CS1591
        public const string Queued = "queued";
        public const string Processing = "processing";
        public const string Succeeded = "succeeded";
        public const string Failed = "failed";
        public const string Canceled = "canceled";
#pragma warning restore CS1591

        /// <summary>Whether a job in this status is done: succeeded, failed or canceled.</summary>
        public static bool IsSettled(string? status) => status == Succeeded || status == Failed || status == Canceled;
    }

    /// <summary>What a create call answers: the job it started.</summary>
    public class JobStarted
    {
        /// <summary>The job's id: read it with <see cref="JobsResource.GetAsync"/>.</summary>
        [JsonPropertyName("id")] public string Id { get; set; } = "";

        /// <summary>One of <see cref="JobStatuses"/>.</summary>
        [JsonPropertyName("status")] public string Status { get; set; } = "";

        /// <summary>Credits charged for the call.</summary>
        [JsonPropertyName("credits_charged")] public long CreditsCharged { get; set; }

        /// <summary>True when this answers an earlier identical request: nothing new was charged.</summary>
        [JsonPropertyName("replayed")] public bool Replayed { get; set; }

        /// <summary>What the API changed or dropped from the request, said back instead of silently.</summary>
        [JsonPropertyName("notes")] public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>What a render answers: every job it started (one per <c>Count</c>).</summary>
    public sealed class RenderStarted : JobStarted
    {
        /// <summary>Every job the call started; <see cref="JobStarted.Id"/> is the first.</summary>
        [JsonPropertyName("ids")] public List<string> Ids { get; set; } = new List<string>();

        /// <summary>Only with <c>wait</c>: every job as it stood when the wait ended, outputs included.</summary>
        [JsonPropertyName("jobs")] public List<Job>? Jobs { get; set; }
    }

    /// <summary>A job: its status and, once it succeeded, its outputs.</summary>
    public sealed class Job
    {
        /// <summary>The job's id.</summary>
        [JsonPropertyName("id")] public string Id { get; set; } = "";

        /// <summary>One of <see cref="JobStatuses"/>.</summary>
        [JsonPropertyName("status")] public string Status { get; set; } = "";

        /// <summary>What the job does: <c>render</c>, <c>edit</c>, <c>upscale</c>, <c>video</c> and so on.</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";

        /// <summary>The engine or video model it ran on.</summary>
        [JsonPropertyName("engine")] public string? Engine { get; set; }

        /// <summary>The results. Their links are signed and expire: download what you keep.</summary>
        [JsonPropertyName("outputs")] public List<JobOutput> Outputs { get; set; } = new List<JobOutput>();

        /// <summary>Credits charged.</summary>
        [JsonPropertyName("credits_charged")] public long CreditsCharged { get; set; }

        /// <summary>Credits given back (a failed or cancelled job).</summary>
        [JsonPropertyName("credits_refunded")] public long CreditsRefunded { get; set; }

        /// <summary>Why it failed, or null.</summary>
        [JsonPropertyName("error")] public JobError? Error { get; set; }

        /// <summary>When it was started.</summary>
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }

        /// <summary>When it settled, or null while it runs.</summary>
        [JsonPropertyName("completed_at")] public DateTimeOffset? CompletedAt { get; set; }

        /// <summary>Whether the job is done: succeeded, failed or canceled.</summary>
        [JsonIgnore] public bool IsSettled => JobStatuses.IsSettled(Status);
    }

    /// <summary>One result of a job.</summary>
    public sealed class JobOutput
    {
        /// <summary>A signed link, served by the API. Download it before <see cref="ExpiresAt"/>.</summary>
        [JsonPropertyName("url")] public string Url { get; set; } = "";

        /// <summary><c>image</c> or <c>video</c>.</summary>
        [JsonPropertyName("media")] public string Media { get; set; } = "";

        /// <summary>When the link stops working.</summary>
        [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
    }

    /// <summary>Why a job failed.</summary>
    public sealed class JobError
    {
        /// <summary><c>UNSAFE_CONTENT</c>, <c>NO_CHANGE</c>, <c>CANCELED</c> or <c>RENDER_FAILED</c>.</summary>
        [JsonPropertyName("code")] public string Code { get; set; } = "";

        /// <summary>In plain words.</summary>
        [JsonPropertyName("message")] public string Message { get; set; } = "";
    }

    /// <summary>What a cancel answers.</summary>
    public sealed class JobCanceled
    {
        /// <summary>The job's id.</summary>
        [JsonPropertyName("id")] public string Id { get; set; } = "";

        /// <summary><c>refunded</c>, <c>requested</c>, <c>too-late</c> or <c>already-settled</c>.</summary>
        [JsonPropertyName("outcome")] public string Outcome { get; set; } = "";

        /// <summary>Credits given back.</summary>
        [JsonPropertyName("credits_refunded")] public long CreditsRefunded { get; set; }
    }

    /// <summary>The key's account: balance, tier and limits.</summary>
    public sealed class Account
    {
#pragma warning disable CS1591
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
#pragma warning restore CS1591

        /// <summary><c>paid</c> once the account has topped up its API wallet; it sets the limits.</summary>
        [JsonPropertyName("tier")] public string Tier { get; set; } = "";

        /// <summary>The balance.</summary>
        [JsonPropertyName("credits")] public AccountCredits Credits { get; set; } = new AccountCredits();

        /// <summary>The key this call used.</summary>
        [JsonPropertyName("key")] public AccountKey Key { get; set; } = new AccountKey();

        /// <summary>The limits this key works under.</summary>
        [JsonPropertyName("limits")] public AccountLimits Limits { get; set; } = new AccountLimits();
    }

    /// <summary>The balance.</summary>
    public sealed class AccountCredits
    {
        /// <summary>What the next call can spend.</summary>
        [JsonPropertyName("spendable")] public long Spendable { get; set; }
    }

    /// <summary>The key a call used.</summary>
    public sealed class AccountKey
    {
#pragma warning disable CS1591
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("allowed_origins")] public List<string> AllowedOrigins { get; set; } = new List<string>();
#pragma warning restore CS1591

        /// <summary>Your own daily limit for this key, or null for none.</summary>
        [JsonPropertyName("daily_credit_limit")] public long? DailyCreditLimit { get; set; }
    }

    /// <summary>The limits a key works under.</summary>
    public sealed class AccountLimits
    {
        /// <summary><c>account</c> (shared by every key, free) or <c>key</c> (this key's own, paid).</summary>
        [JsonPropertyName("scope")] public string Scope { get; set; } = "";

#pragma warning disable CS1591
        [JsonPropertyName("requests_per_minute")] public int RequestsPerMinute { get; set; }
#pragma warning restore CS1591

        /// <summary>GET requests (job polls included), counted apart from the rest.</summary>
        [JsonPropertyName("reads_per_minute")] public int ReadsPerMinute { get; set; }

        /// <summary>Jobs that may run at once.</summary>
        [JsonPropertyName("concurrent_jobs")] public int ConcurrentJobs { get; set; }

        /// <summary>Credits this key may still spend per UTC day; null is no limit.</summary>
        [JsonPropertyName("daily_credits")] public long? DailyCredits { get; set; }
    }

    /// <summary>The engines and video models, with their prices and capabilities.</summary>
    public sealed class EngineCatalog
    {
#pragma warning disable CS1591
        [JsonPropertyName("engines")] public List<Engine> Engines { get; set; } = new List<Engine>();
        [JsonPropertyName("video_models")] public List<VideoModel> VideoModels { get; set; } = new List<VideoModel>();
#pragma warning restore CS1591
    }

    /// <summary>A render engine.</summary>
    public sealed class Engine
    {
#pragma warning disable CS1591
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("tier")] public string Tier { get; set; } = "";
        [JsonPropertyName("default")] public bool Default { get; set; }
        [JsonPropertyName("paid_only")] public bool PaidOnly { get; set; }
#pragma warning restore CS1591

        /// <summary>Whether this account may use it now (a paid-only engine needs a paid account).</summary>
        [JsonPropertyName("available")] public bool Available { get; set; }

        /// <summary>Credits per render and per edit.</summary>
        [JsonPropertyName("prices")] public EnginePrices Prices { get; set; } = new EnginePrices();

        /// <summary>What it can do.</summary>
        [JsonPropertyName("capabilities")] public EngineCapabilities Capabilities { get; set; } = new EngineCapabilities();
    }

    /// <summary>An engine's prices, in credits.</summary>
    public sealed class EnginePrices
    {
#pragma warning disable CS1591
        [JsonPropertyName("render")] public long Render { get; set; }
        [JsonPropertyName("edit")] public long Edit { get; set; }
#pragma warning restore CS1591
    }

    /// <summary>What an engine can do.</summary>
    public sealed class EngineCapabilities
    {
#pragma warning disable CS1591
        [JsonPropertyName("text_to_render")] public bool TextToRender { get; set; }
        [JsonPropertyName("masks")] public bool Masks { get; set; }
        [JsonPropertyName("edit_references")] public bool EditReferences { get; set; }
        [JsonPropertyName("max_references")] public int MaxReferences { get; set; }
#pragma warning restore CS1591
    }

    /// <summary>A video model.</summary>
    public sealed class VideoModel
    {
#pragma warning disable CS1591
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("default")] public bool Default { get; set; }
        [JsonPropertyName("available")] public bool Available { get; set; }
        [JsonPropertyName("prices")] public List<VideoPrice> Prices { get; set; } = new List<VideoPrice>();
#pragma warning restore CS1591
    }

    /// <summary>A video price, per length.</summary>
    public sealed class VideoPrice
    {
        /// <summary>The clip length, or null for the model's one fixed length.</summary>
        [JsonPropertyName("duration_seconds")] public int? DurationSeconds { get; set; }

        /// <summary>Credits.</summary>
        [JsonPropertyName("credits")] public long Credits { get; set; }
    }

    /// <summary>A job output's bytes.</summary>
    public sealed class DownloadedFile
    {
        internal DownloadedFile(byte[] data, string? contentType)
        {
            Data = data;
            ContentType = contentType;
        }

        /// <summary>The file.</summary>
        public byte[] Data { get; }

        /// <summary><c>image/jpeg</c>, <c>image/png</c>, <c>image/webp</c> or <c>video/mp4</c>.</summary>
        public string? ContentType { get; }
    }

    /// <summary>
    /// One event of <see cref="JobsResource.StreamAsync"/>: the job as it stands (<c>job</c>), then
    /// exactly one last event: <c>done</c> (the job settled), <c>timeout</c> (still running after ten
    /// minutes: open the stream again) or <c>error</c> (the API could not read the job; it still runs).
    /// </summary>
    public sealed class JobStreamEvent
    {
        internal JobStreamEvent(string type, Job? job, JobError? error)
        {
            Type = type;
            Job = job;
            Error = error;
        }

        /// <summary><c>job</c>, <c>done</c>, <c>timeout</c> or <c>error</c>.</summary>
        public string Type { get; }

        /// <summary>The job, on every event but <c>error</c>.</summary>
        public Job? Job { get; }

        /// <summary>On <c>error</c>: <c>{ code, message }</c>.</summary>
        public JobError? Error { get; }

        /// <summary>Whether this is the stream's last event.</summary>
        public bool IsFinal => Type != "job";
    }

    /// <summary>One field the API refused, on a <c>VALIDATION_FAILED</c> answer.</summary>
    public sealed class MnmlIssue
    {
        /// <summary>The field, as a dotted path (<c>references.0.mode</c>).</summary>
        [JsonPropertyName("path")] public string Path { get; set; } = "";

        /// <summary>Why.</summary>
        [JsonPropertyName("message")] public string Message { get; set; } = "";
    }

    internal sealed class Envelope<T>
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("data")] public T? Data { get; set; }
        [JsonPropertyName("error")] public EnvelopeError? Error { get; set; }
    }

    internal sealed class EnvelopeError
    {
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("details")] public JsonElement? Details { get; set; }
        [JsonPropertyName("issues")] public List<MnmlIssue>? Issues { get; set; }
    }
}
