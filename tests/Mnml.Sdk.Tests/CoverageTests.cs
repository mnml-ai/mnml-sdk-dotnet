using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Mnml.Tests;

/// <summary>
/// The SDK against the API's own OpenAPI document (<c>spec/openapi.json</c>): every field of every
/// body and answer is a property here, no more and no fewer. A field the API adds fails here until
/// the SDK has it. <c>scripts/update-spec.sh</c> refreshes the document.
/// </summary>
public class CoverageTests
{
    private static readonly JsonElement Paths = LoadPaths();

    private static JsonElement LoadPaths()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "spec", "openapi.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("paths").Clone();
    }

    private static JsonElement Op(string path, string method) => Paths.GetProperty(path).GetProperty(method);

    private static string[] Keys(JsonElement schema) =>
        schema.TryGetProperty("properties", out var props)
            ? props.EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

    private static JsonElement Body(string path) =>
        Op(path, "post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");

    private static JsonElement Data(string path, string method)
    {
        foreach (var r in Op(path, method).GetProperty("responses").EnumerateObject())
            if (r.Name.StartsWith("2", StringComparison.Ordinal))
                return r.Value.GetProperty("content").GetProperty("application/json").GetProperty("schema")
                    .GetProperty("properties").GetProperty("data");
        throw new InvalidOperationException($"{method} {path} has no 2xx answer");
    }

    private static JsonElement Prop(JsonElement schema, string name)
    {
        var p = schema.GetProperty("properties").GetProperty(name);
        if (p.TryGetProperty("items", out var items)) return items;
        if (p.TryGetProperty("anyOf", out var any))
            foreach (var a in any.EnumerateArray())
                if (a.TryGetProperty("properties", out _)) return a;
        return p;
    }

    private static string[] Fields(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .Where(n => n != null)
            .Select(n => n!)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

    public static IEnumerable<object[]> Bodies() => new[]
    {
        new object[] { "/v2/renders", typeof(RenderRequest) },
        new object[] { "/v2/edits", typeof(EditRequest) },
        new object[] { "/v2/enhancements", typeof(EnhancementRequest) },
        new object[] { "/v2/videos", typeof(VideoRequest) },
    };

    [Theory]
    [MemberData(nameof(Bodies))]
    public void Every_request_field_is_a_property(string path, Type type) =>
        Assert.Equal(Keys(Body(path)), Fields(type));

    [Fact]
    public void Every_answer_field_is_a_property()
    {
        var render = Data("/v2/renders", "post");
        Assert.Equal(Keys(render), Fields(typeof(RenderStarted)));
        Assert.Equal(Keys(Data("/v2/edits", "post")), Fields(typeof(JobStarted)));

        var job = Data("/v2/jobs/{id}", "get");
        Assert.Equal(Keys(job), Fields(typeof(Job)));
        Assert.Equal(Keys(Prop(job, "outputs")), Fields(typeof(JobOutput)));
        Assert.Equal(Keys(Data("/v2/jobs/{id}/cancel", "post")), Fields(typeof(JobCanceled)));

        var account = Data("/v2/account", "get");
        Assert.Equal(Keys(account), Fields(typeof(Account)));
        Assert.Equal(Keys(Prop(account, "credits")), Fields(typeof(AccountCredits)));
        Assert.Equal(Keys(Prop(account, "key")), Fields(typeof(AccountKey)));
        Assert.Equal(Keys(Prop(account, "limits")), Fields(typeof(AccountLimits)));

        var engines = Data("/v2/engines", "get");
        Assert.Equal(Keys(engines), Fields(typeof(EngineCatalog)));
        var engine = Prop(engines, "engines");
        Assert.Equal(Keys(engine), Fields(typeof(Engine)));
        Assert.Equal(Keys(Prop(engine, "prices")), Fields(typeof(EnginePrices)));
        Assert.Equal(Keys(Prop(engine, "capabilities")), Fields(typeof(EngineCapabilities)));
        var video = Prop(engines, "video_models");
        Assert.Equal(Keys(video), Fields(typeof(VideoModel)));
        Assert.Equal(Keys(Prop(video, "prices")), Fields(typeof(VideoPrice)));
    }

    /// <summary>Every answer field the spec marks deprecated, as <c>operation: path</c>.</summary>
    private static string[] SpecDeprecated()
    {
        var found = new List<string>();
        void Walk(string op, JsonElement schema, string path)
        {
            var s = schema;
            var at = path;
            if (schema.TryGetProperty("items", out var items)) { s = items; at = path + "[]"; }
            else if (schema.TryGetProperty("anyOf", out var any))
                foreach (var a in any.EnumerateArray())
                    if (a.TryGetProperty("properties", out _)) { s = a; break; }
            if (!s.TryGetProperty("properties", out var props)) return;
            foreach (var p in props.EnumerateObject())
            {
                var name = at.Length == 0 ? p.Name : at + "." + p.Name;
                if (p.Value.TryGetProperty("deprecated", out var d) && d.ValueKind == JsonValueKind.True)
                    found.Add(op + ": " + name);
                Walk(op, p.Value, name);
            }
        }
        Walk("createRender", Data("/v2/renders", "post"), "");
        Walk("createEdit", Data("/v2/edits", "post"), "");
        Walk("createEnhancement", Data("/v2/enhancements", "post"), "");
        Walk("createVideo", Data("/v2/videos", "post"), "");
        Walk("getJob", Data("/v2/jobs/{id}", "get"), "");
        Walk("cancelJob", Data("/v2/jobs/{id}/cancel", "post"), "");
        Walk("getAccount", Data("/v2/account", "get"), "");
        Walk("listEngines", Data("/v2/engines", "get"), "");
        return found.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The <c>[Obsolete]</c> properties of a type, by their JSON names.</summary>
    private static string[] Obsolete(Type t, string prefix) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ObsoleteAttribute>() != null)
            .Select(p => prefix + p.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name)
            .ToArray();

    [Fact]
    public void The_obsolete_properties_are_the_fields_the_spec_deprecates()
    {
        var sdk = new[]
            {
                Obsolete(typeof(RenderStarted), "createRender: "),
                Obsolete(typeof(Job), "createRender: jobs[]."),
                Obsolete(typeof(JobStarted), "createEdit: "),
                Obsolete(typeof(JobStarted), "createEnhancement: "),
                Obsolete(typeof(JobStarted), "createVideo: "),
                Obsolete(typeof(Job), "getJob: "),
                Obsolete(typeof(JobCanceled), "cancelJob: "),
                Obsolete(typeof(Account), "getAccount: "),
                Obsolete(typeof(AccountCredits), "getAccount: credits."),
                Obsolete(typeof(AccountKey), "getAccount: key."),
                Obsolete(typeof(AccountLimits), "getAccount: limits."),
                Obsolete(typeof(EnginePrices), "listEngines: engines[].prices."),
                Obsolete(typeof(VideoPrice), "listEngines: video_models[].prices[]."),
            }
            .SelectMany(x => x)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(SpecDeprecated(), sdk);
    }

    [Fact]
    public void The_credits_low_webhook_marks_its_credit_fields_obsolete() =>
        Assert.Equal(new[] { "balance", "threshold" }, Obsolete(typeof(CreditsLow), "").OrderBy(k => k, StringComparer.Ordinal).ToArray());

    [Fact]
    public void The_constants_match_the_enums()
    {
        static string[] Enum(JsonElement s) =>
            s.GetProperty("enum").EnumerateArray().Select(e => e.GetString()!).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        static string[] Consts(Type t) =>
            t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral)
                .Select(f => (string)f.GetRawConstantValue()!).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        var render = Body("/v2/renders");
        Assert.Equal(Enum(render.GetProperty("properties").GetProperty("mode")), Consts(typeof(Modes)));
        Assert.Equal(Enum(render.GetProperty("properties").GetProperty("aspect_ratio")), Consts(typeof(AspectRatios)));
        Assert.Equal(Enum(Body("/v2/enhancements").GetProperty("properties").GetProperty("kind")), Consts(typeof(EnhancementKinds)));
        Assert.Equal(Enum(Data("/v2/jobs/{id}", "get").GetProperty("properties").GetProperty("status")), Consts(typeof(JobStatuses)));
    }
}
