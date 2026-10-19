# mnml .NET SDK

The official C# and .NET client for the [mnml API](https://developers.mnml.ai): architecture
renders, edits, enhancements and video, from your own product or add-in.

- Every endpoint of API v2: renders, edits, enhancements, video, jobs, account, engines
- Typed requests and answers, checked against the API's OpenAPI document
- Retries, timeouts and idempotency keys handled for you
- `CreateAndWaitAsync` and `Jobs.WaitAsync` to wait for a job, `Jobs.StreamAsync` to follow one,
  `Files.DownloadAsync` to keep its output
- Webhook signature verification
- `netstandard2.0` and `net8.0`: Revit, Rhino and Grasshopper, Dynamo, AutoCAD and Unity add-ins
  on .NET Framework 4.8, and any modern .NET app

```bash
dotnet add package Mnml.Sdk
```

## Quick start

Create an API key in the [console](https://developers.mnml.ai/console/keys), then:

```csharp
using Mnml;

using var mnml = new MnmlClient(); // reads MNML_API_KEY

var jobs = await mnml.Renders.CreateAndWaitAsync(new RenderRequest
{
    Mode = Modes.Exterior,
    Image = ImageInput.FromFile("massing.png"), // or a public https:// link
    Prompt = "Timber facade, late afternoon light, olive trees",
});
Console.WriteLine($"{jobs[0].Status} {jobs[0].Outputs[0].Url}");
```

Every call spends credits from your prepaid API balance. Top it up in dollars on
[Billing](https://developers.mnml.ai/console/billing).

## Configuration

```csharp
using var mnml = new MnmlClient(new MnmlClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("MNML_API_KEY"), // the default
    MaxRetries = 2,                       // retries for a 429, a 5xx or a dropped connection
    Timeout = TimeSpan.FromSeconds(60),   // per request
});
```

Make one client and reuse it. It holds an `HttpClient`; pass your own as the second argument (for
`IHttpClientFactory`, a proxy or a test handler) and the SDK leaves its lifetime to you.

## Methods

| Method                                   | Endpoint                    | What it does                                        |
| ---------------------------------------- | --------------------------- | --------------------------------------------------- |
| `Renders.CreateAsync(request)`           | `POST /v2/renders`          | Render from an image, or from a prompt alone        |
| `Edits.CreateAsync(request)`             | `POST /v2/edits`            | Edit or erase, over the whole image or a region     |
| `Enhancements.CreateAsync(request)`      | `POST /v2/enhancements`     | Upscale, enhance, remove the background, outpaint   |
| `Videos.CreateAsync(request)`            | `POST /v2/videos`           | Video from a still                                  |
| `Jobs.GetAsync(id)`                      | `GET /v2/jobs/{id}`         | Read a job                                          |
| `Jobs.CancelAsync(id)`                   | `POST /v2/jobs/{id}/cancel` | Cancel a job (refunded if it had not produced yet)  |
| `Account.GetAsync()`                     | `GET /v2/account`           | Balance, tier and limits for this key               |
| `Engines.ListAsync()`                    | `GET /v2/engines`           | Engines, video models, prices and capabilities      |
| `Files.DownloadAsync(output)`            | `GET /v2/files/{id}`        | A job output's bytes, from its signed link          |
| `Jobs.WaitAsync(id, wait?)`              | `GET /v2/jobs/{id}`         | Read a job until it succeeds, fails or is cancelled |
| `Jobs.WaitAllAsync(ids, wait?)`          | `GET /v2/jobs/{id}`         | `WaitAsync` for several jobs at once                |
| `Jobs.StreamAsync(id)`                   | `GET /v2/jobs/{id}/events`  | Follow a job as it changes, over one connection     |
| `<resource>.CreateAndWaitAsync(request)` | the create, then the job    | Start a job and wait for it (a render: every job)   |

Every method takes a `CancellationToken`, and every create takes an optional `idempotencyKey`.
The client makes one per call when you pass none and keeps it across its own retries, so a
retried call is never charged twice.

### Images

An image goes in the same call that uses it, up to 15 MB:

```csharp
Image = ImageInput.FromFile("view.jpg");          // a file
Image = ImageInput.FromBytes(bytes);              // bytes (an implicit conversion works too)
Image = "https://example.com/view.jpg";           // a public link, a data URI or base64
JobId = "1234";                                   // or a finished job's output
```

References, an edit's `Mask` and a video's `EndFrame` take an image the same way:
`Reference.Of(ImageInput.FromFile("oak.jpg"), ReferenceModes.Material)`.

### Engines, modes and settings

`Modes`, `AspectRatios`, `ReferenceModes`, `EnhancementKinds` and `CameraMoves` hold the values
the API takes. `Engines.ListAsync()` is the live list, with each engine's price and what it can do:

```csharp
var catalog = await mnml.Engines.ListAsync();
foreach (var e in catalog.Engines.Where(e => e.Available))
    Console.WriteLine($"{e.Name}: {e.Prices.Render} credits a render");
```

### Edits and regions

```csharp
var job = await mnml.Edits.CreateAndWaitAsync(new EditRequest
{
    JobId = render.Id,
    Prompt = "Replace the lawn with a reflecting pool",
    Region = Region.Rectangle(x: 0, y: 0.6, width: 1, height: 0.4),
});
```

### Waiting and following a job

A render is usually done within one held read: `CreateAndWaitAsync` asks the API to hold its
answer for up to 90 seconds. For a video, or for many jobs, read on with `Jobs.WaitAsync`, follow
the job as a stream, or take a [webhook](https://developers.mnml.ai/docs/webhooks):

```csharp
await foreach (var e in mnml.Jobs.StreamAsync(started.Id))
{
    if (e.Type == "done") Console.WriteLine(e.Job!.Outputs[0].Url);
}
```

### Downloading outputs

Output links are signed and expire within hours. Keep what you need:

```csharp
var file = await mnml.Files.DownloadAsync(job.Outputs[0]);
File.WriteAllBytes("render.jpg", file.Data);
```

## Errors

A refusal throws `MnmlException` with the API's `Code` (`INSUFFICIENT_CREDITS`,
`VALIDATION_FAILED`, `RATE_LIMITED` …), the HTTP `Status`, the `RequestId` to quote to support,
and, on `VALIDATION_FAILED`, each refused field in `Issues`. `Jobs.WaitAsync` throws
`MnmlTimeoutException` when its time is up; the job keeps running. The codes:
[developers.mnml.ai/docs/errors](https://developers.mnml.ai/docs/errors).

```csharp
try { await mnml.Renders.CreateAsync(request); }
catch (MnmlException e) when (e.Code == "INSUFFICIENT_CREDITS") { /* top up */ }
```

## Webhooks

Verify each delivery against your endpoint's `whsec_` secret, with the raw body:

```csharp
// ASP.NET Core minimal API
app.MapPost("/mnml", async (HttpRequest req) =>
{
    var body = await new StreamReader(req.Body).ReadToEndAsync();
    var ev = Webhooks.Verify(body, name => req.Headers[name].ToString(), secret);
    if (ev.Type == "job.succeeded") Console.WriteLine(ev.AsJob()!.Outputs[0].Url);
    return Results.Ok();
});
```

## In an add-in

Revit, Rhino and AutoCAD run add-ins on the UI thread. Await the SDK's calls from an async
handler, or run them on a worker and marshal the result back, so the host stays responsive while
a render runs. On .NET Framework 4.8, the package brings `System.Text.Json` with it.

## Versioning

This SDK follows [Semantic Versioning](https://semver.org/). The API is versioned in its path
(`/v2`); a release of this package never changes which API version it calls without a major
version. See [CHANGELOG.md](./CHANGELOG.md).

## License

MIT
