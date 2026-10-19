using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Mnml
{
    /// <summary>
    /// An answer that was not a success: the API's own code, message and request id. The codes are
    /// listed at https://developers.mnml.ai/docs/errors.
    /// </summary>
    public class MnmlException : Exception
    {
        /// <summary>An answer the API refused.</summary>
        public MnmlException(string code, string message, int status, string? requestId,
            JsonElement? details = null, IReadOnlyList<MnmlIssue>? issues = null)
            : base(message)
        {
            Code = code;
            Status = status;
            RequestId = requestId;
            Details = details;
            Issues = issues ?? Array.Empty<MnmlIssue>();
        }

        /// <summary>The API's error code, such as <c>INSUFFICIENT_CREDITS</c> or <c>RATE_LIMITED</c>.</summary>
        public string Code { get; }

        /// <summary>The HTTP status.</summary>
        public int Status { get; }

        /// <summary>The <c>X-Request-Id</c> to quote to support.</summary>
        public string? RequestId { get; }

        /// <summary>More about the refusal, when the API sent any (<c>details.reason</c> on a 429, say).</summary>
        public JsonElement? Details { get; }

        /// <summary>On <c>VALIDATION_FAILED</c>, each field that was refused.</summary>
        public IReadOnlyList<MnmlIssue> Issues { get; }
    }

    /// <summary><see cref="JobsResource.WaitAsync"/> ran out of time before the job settled; the job keeps running.</summary>
    public sealed class MnmlTimeoutException : Exception
    {
        /// <summary>The job that is still running.</summary>
        public MnmlTimeoutException(string jobId)
            : base($"Job {jobId} did not finish in time. It is still running; read it again later.")
        {
            JobId = jobId;
        }

        /// <summary>The job, still running.</summary>
        public string JobId { get; }
    }
}
