using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Mnml.Tests;

public class ClientTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

    [Fact]
    public async Task A_render_posts_its_body_with_the_key_an_idempotency_key_and_the_image_as_a_data_uri()
    {
        var api = new FakeApi().Ok("""{"id":"42","ids":["42"],"status":"queued","credits_charged":30,"replayed":false,"notes":[]}""");
        using var mnml = api.Client();

        var started = await mnml.Renders.CreateAsync(new RenderRequest
        {
            Prompt = "Timber facade, dusk",
            Mode = Modes.Exterior,
            Image = Png,
            References = new List<Reference> { Reference.Of("https://x.test/ref.jpg", ReferenceModes.Material) },
        });

        Assert.Equal("42", started.Id);
        Assert.Equal(JobStatuses.Queued, started.Status);
        var seen = api.Requests.Single();
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.Equal("https://api.test/v2/renders", seen.Url);
        Assert.Equal("Bearer test-key", seen.Headers["authorization"]);
        Assert.StartsWith("mnml-sdk-dotnet/", seen.Headers["user-agent"]);
        Assert.True(Guid.TryParse(seen.Headers["idempotency-key"], out _));
        using var body = JsonDocument.Parse(seen.Body!);
        Assert.Equal("Timber facade, dusk", body.RootElement.GetProperty("prompt").GetString());
        Assert.StartsWith("data:image/png;base64,", body.RootElement.GetProperty("image").GetString());
        Assert.Equal("material", body.RootElement.GetProperty("references")[0].GetProperty("mode").GetString());
        // Nothing unset is sent.
        Assert.False(body.RootElement.TryGetProperty("engine", out _));
        Assert.False(body.RootElement.TryGetProperty("seed", out _));
    }

    [Fact]
    public async Task A_retry_after_a_503_sends_the_same_idempotency_key_after_retry_after()
    {
        var api = new FakeApi()
            .Fail(503, "SERVICE_UNAVAILABLE", edit: r => r.Headers.TryAddWithoutValidation("Retry-After", "5"))
            .Ok("""{"id":"7","status":"queued","credits_charged":2,"replayed":false,"notes":[]}""");
        using var mnml = api.Client();

        await mnml.Edits.CreateAsync(new EditRequest { Prompt = "Add trees", JobId = "41" });

        Assert.Equal(2, api.Requests.Count);
        Assert.Equal(api.Requests[0].Headers["idempotency-key"], api.Requests[1].Headers["idempotency-key"]);
        Assert.Equal(TimeSpan.FromSeconds(5), api.Delays.Single());
    }

    [Fact]
    public async Task A_dropped_connection_is_retried_with_a_backoff()
    {
        var api = new FakeApi().Drop().Ok(Fixtures.Job);
        using var mnml = api.Client();

        var job = await mnml.Jobs.GetAsync("42");

        Assert.Equal("42", job.Id);
        Assert.Equal(2, api.Requests.Count);
        Assert.InRange(api.Delays.Single().TotalMilliseconds, 500, 750);
    }

    [Fact]
    public async Task A_long_retry_after_is_the_callers_to_handle()
    {
        var api = new FakeApi().Fail(429, "RATE_LIMITED", "Daily limit reached.",
            r => r.Headers.TryAddWithoutValidation("Retry-After", "3600"));
        using var mnml = api.Client();

        var e = await Assert.ThrowsAsync<MnmlException>(() => mnml.Account.GetAsync());

        Assert.Equal("RATE_LIMITED", e.Code);
        Assert.Equal(429, e.Status);
        Assert.Equal("req_test", e.RequestId);
        Assert.Single(api.Requests);
        Assert.Empty(api.Delays);
    }

    [Fact]
    public async Task A_refusal_carries_the_apis_code_message_and_issues()
    {
        var api = new FakeApi().Answer(400, """
            {"success":false,"error":{"code":"VALIDATION_FAILED","message":"Check the fields.",
             "issues":[{"path":"prompt","message":"Required."}],"details":{"reason":"x"}}}
            """);
        using var mnml = api.Client();

        var e = await Assert.ThrowsAsync<MnmlException>(() => mnml.Renders.CreateAsync(new RenderRequest()));

        Assert.Equal("VALIDATION_FAILED", e.Code);
        Assert.Equal("Check the fields.", e.Message);
        Assert.Equal("prompt", e.Issues.Single().Path);
        Assert.Equal("x", e.Details!.Value.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task An_answer_that_is_not_the_envelope_is_an_http_error()
    {
        var api = new FakeApi().Answer(502, "<html>Bad gateway</html>", contentType: "text/html");
        using var mnml = api.Client(maxRetries: 0);

        var e = await Assert.ThrowsAsync<MnmlException>(() => mnml.Engines.ListAsync());

        Assert.Equal("HTTP_ERROR", e.Code);
        Assert.Equal(502, e.Status);
    }

    [Fact]
    public async Task Wait_holds_each_read_and_returns_the_settled_job()
    {
        var api = new FakeApi().Ok(Fixtures.JobWith("processing")).Ok(Fixtures.Job);
        using var mnml = api.Client();

        var job = await mnml.Jobs.WaitAsync("42");

        Assert.True(job.IsSettled);
        Assert.Equal("https://api.test/v2/files/42?exp=1&sig=x", job.Outputs.Single().Url);
        Assert.All(api.Requests, r => Assert.Contains("/v2/jobs/42?wait=", r.Url));
    }

    [Fact]
    public async Task Wait_gives_up_after_its_timeout()
    {
        var api = new FakeApi().Ok(Fixtures.JobWith("processing"));
        using var mnml = api.Client();

        var e = await Assert.ThrowsAsync<MnmlTimeoutException>(() =>
            mnml.Jobs.WaitAsync("42", new WaitOptions { Timeout = TimeSpan.FromMilliseconds(10) }));

        Assert.Equal("42", e.JobId);
    }

    [Fact]
    public async Task A_render_that_settled_in_the_held_answer_needs_no_poll()
    {
        var api = new FakeApi().Ok($$"""{"id":"42","ids":["42"],"status":"succeeded","credits_charged":30,"replayed":false,"notes":[],"jobs":[{{Fixtures.Job}}]}""");
        using var mnml = api.Client();

        var jobs = await mnml.Renders.CreateAndWaitAsync(new RenderRequest { Prompt = "A", Mode = Modes.TextToRender });

        Assert.Single(jobs);
        Assert.Equal("https://api.test/v2/renders?wait=90", api.Requests.Single().Url);
    }

    [Fact]
    public void A_wait_outside_1_to_90_seconds_is_refused_before_any_call()
    {
        var api = new FakeApi();
        using var mnml = api.Client();

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mnml.Jobs.GetAsync("42", 91)).GetAwaiter().GetResult();
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task Cancel_posts_with_an_idempotency_key()
    {
        var api = new FakeApi().Ok("""{"id":"42","outcome":"refunded","credits_refunded":30}""");
        using var mnml = api.Client();

        var canceled = await mnml.Jobs.CancelAsync("42");

        Assert.Equal("refunded", canceled.Outcome);
        Assert.Equal("https://api.test/v2/jobs/42/cancel", api.Requests.Single().Url);
        Assert.True(api.Requests.Single().Headers.ContainsKey("idempotency-key"));
    }

    [Fact]
    public async Task A_download_sends_no_key()
    {
        var api = new FakeApi().Answer(200, "JPEGBYTES", contentType: "image/jpeg");
        using var mnml = api.Client();

        var file = await mnml.Files.DownloadAsync(new JobOutput { Url = "https://api.test/v2/files/42?exp=1&sig=x" });

        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal("JPEGBYTES", System.Text.Encoding.UTF8.GetString(file.Data));
        Assert.False(api.Requests.Single().Headers.ContainsKey("authorization"));
    }

    [Fact]
    public async Task A_stream_yields_each_change_then_the_last_event()
    {
        var sse = $$"""
            : ping

            event: job
            data: {{Fixtures.JobWith("processing").Replace("\n", "")}}

            event: done
            data: {{Fixtures.Job.Replace("\n", "")}}

            event: job
            data: {"never":"read"}


            """;
        var api = new FakeApi().Answer(200, sse, contentType: "text/event-stream");
        using var mnml = api.Client();

        var events = new List<JobStreamEvent>();
        await foreach (var e in mnml.Jobs.StreamAsync("42")) events.Add(e);

        Assert.Equal(new[] { "job", "done" }, events.Select(e => e.Type));
        Assert.Equal("succeeded", events[1].Job!.Status);
        Assert.Equal("text/event-stream", api.Requests.Single().Headers["accept"]);
    }

    [Fact]
    public async Task A_stream_that_ends_early_says_so()
    {
        var api = new FakeApi().Answer(200, $"event: job\ndata: {Fixtures.JobWith("processing").Replace("\n", "")}\n\n",
            contentType: "text/event-stream");
        using var mnml = api.Client();

        var e = await Assert.ThrowsAsync<MnmlException>(async () =>
        {
            await foreach (var _ in mnml.Jobs.StreamAsync("42")) { }
        });

        Assert.Equal("STREAM_ENDED", e.Code);
    }

    [Fact]
    public void Bytes_are_labelled_by_their_first_bytes()
    {
        Assert.StartsWith("data:image/png;base64,", ImageInput.FromBytes(Png).Value);
        Assert.StartsWith("data:image/jpeg;base64,", ImageInput.FromBytes(new byte[] { 0xFF, 0xD8, 0xFF, 0 }).Value);
        Assert.Equal("https://x.test/a.jpg", ((ImageInput)"https://x.test/a.jpg").Value);
    }

    [Fact]
    public void A_region_is_sent_as_the_api_reads_it()
    {
        var json = JsonSerializer.Serialize(Region.Outline((0.1, 0.2), (0.5, 0.2), (0.3, 0.6)), MnmlClient.Json);
        // Compared as numbers: .NET Framework writes 0.1 in its round-trip form, 0.10000000000000001, the same double.
        var polygon = JsonDocument.Parse(json).RootElement.GetProperty("polygon");
        Assert.Equal(new[] { 0.1, 0.2, 0.5, 0.2, 0.3, 0.6 },
            polygon.EnumerateArray().SelectMany(p => p.EnumerateArray()).Select(n => n.GetDouble()));
        Assert.Equal("""{"box":{"x":0,"y":0.5,"width":1,"height":0.5}}""",
            JsonSerializer.Serialize(Region.Rectangle(0, 0.5, 1, 0.5), MnmlClient.Json));
    }

    [Fact]
    public void A_client_needs_a_key()
    {
        var before = Environment.GetEnvironmentVariable("MNML_API_KEY");
        Environment.SetEnvironmentVariable("MNML_API_KEY", null);
        try
        {
            Assert.Throws<ArgumentException>(() => new MnmlClient(new MnmlClientOptions()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MNML_API_KEY", before);
        }
    }
}
