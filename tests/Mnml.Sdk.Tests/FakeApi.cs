using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mnml.Tests;

/// <summary>An HTTP handler that answers from a queue and records every request it saw.</summary>
internal sealed class FakeApi : HttpMessageHandler
{
    // A class, not a record: records need IsExternalInit, which .NET Framework 4.8 lacks.
    public sealed class Seen
    {
        public Seen(HttpMethod method, string url, Dictionary<string, string> headers, string? body)
        {
            Method = method;
            Url = url;
            Headers = headers;
            Body = body;
        }

        public HttpMethod Method { get; }
        public string Url { get; }
        public Dictionary<string, string> Headers { get; }
        public string? Body { get; }
    }

    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();
    public List<Seen> Requests { get; } = new();
    public List<TimeSpan> Delays { get; } = new();

    public FakeApi Answer(int status, string body, Action<HttpResponseMessage>? edit = null, string contentType = "application/json")
    {
        _answers.Enqueue(_ =>
        {
            var res = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, contentType) };
            res.Headers.TryAddWithoutValidation("X-Request-Id", "req_test");
            edit?.Invoke(res);
            return res;
        });
        return this;
    }

    public FakeApi Ok(string dataJson) => Answer(200, $"{{\"success\":true,\"data\":{dataJson}}}");

    public FakeApi Fail(int status, string code, string message = "No.", Action<HttpResponseMessage>? edit = null) =>
        Answer(status, $"{{\"success\":false,\"error\":{{\"code\":\"{code}\",\"message\":\"{message}\"}}}}", edit);

    public FakeApi Drop()
    {
        _answers.Enqueue(_ => throw new HttpRequestException("connection reset"));
        return this;
    }

    public MnmlClient Client(int maxRetries = 2)
    {
        var client = new MnmlClient(new MnmlClientOptions { ApiKey = "test-key", BaseUrl = "https://api.test", MaxRetries = maxRetries },
            new HttpClient(this));
        client.Delay = (t, _) =>
        {
            Delays.Add(t);
            return Task.CompletedTask;
        };
        return client;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value));
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync();
        Requests.Add(new Seen(request.Method, request.RequestUri!.ToString(), headers, body));
        if (_answers.Count == 0) throw new InvalidOperationException($"No answer queued for {request.Method} {request.RequestUri}");
        return _answers.Dequeue()(request);
    }
}

internal static class Fixtures
{
    public const string Job = """
        {"id":"42","status":"succeeded","kind":"render","engine":"v4.6-ultra",
         "outputs":[{"url":"https://api.test/v2/files/42?exp=1&sig=x","media":"image","expires_at":"2026-10-18T10:00:00Z"}],
         "credits_charged":30,"credits_refunded":0,"error":null,
         "created_at":"2026-10-18T09:00:00Z","completed_at":"2026-10-18T09:00:40Z"}
        """;

    public static string JobWith(string status) => Job.Replace("\"succeeded\"", $"\"{status}\"");
}
