using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Mnml.Tests;

public class WebhookTests
{
    // A throwaway secret made fresh for every run: no secret, real or sample, lives in this repo.
    private static readonly byte[] Key = RandomKey();
    private static readonly string Secret = "whsec_" + Convert.ToBase64String(Key);
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000);
    private static readonly string Body = $$"""{"type":"job.succeeded","timestamp":"2026-10-18T09:00:40Z","data":{{Fixtures.Job.Replace("\n", "")}}}""";

    private static Dictionary<string, string> Headers(string signature, long? ts = null) => new()
    {
        ["Webhook-Id"] = "msg_1",
        ["Webhook-Timestamp"] = (ts ?? Now.ToUnixTimeSeconds()).ToString(),
        ["Webhook-Signature"] = signature,
    };

    [Fact]
    public void A_signed_delivery_verifies_and_reads_as_a_job()
    {
        var sig = Webhooks.Sign(Secret, "msg_1", Now.ToUnixTimeSeconds(), Body);

        var ev = Webhooks.Verify(Body, Headers(sig), Secret, Now);

        Assert.Equal("job.succeeded", ev.Type);
        Assert.Equal("42", ev.AsJob()!.Id);
        Assert.Null(ev.AsCreditsLow());
    }

    [Fact]
    public void Any_of_several_signatures_may_match()
    {
        var sig = Webhooks.Sign(Secret, "msg_1", Now.ToUnixTimeSeconds(), Body);

        var ev = Webhooks.Verify(Body, Headers("v1,c29tZXRoaW5nIGVsc2U= " + sig), Secret, Now);

        Assert.Equal("job.succeeded", ev.Type);
    }

    [Fact]
    public void A_changed_body_is_refused()
    {
        var sig = Webhooks.Sign(Secret, "msg_1", Now.ToUnixTimeSeconds(), Body);

        Assert.Throws<WebhookVerificationException>(() =>
            Webhooks.Verify(Body.Replace("42", "43"), Headers(sig), Secret, Now));
    }

    [Fact]
    public void An_old_delivery_is_refused()
    {
        var old = Now.ToUnixTimeSeconds() - 600;
        var sig = Webhooks.Sign(Secret, "msg_1", old, Body);

        Assert.Throws<WebhookVerificationException>(() => Webhooks.Verify(Body, Headers(sig, old), Secret, Now));
    }

    [Fact]
    public void Missing_headers_are_refused()
    {
        Assert.Throws<WebhookVerificationException>(() =>
            Webhooks.Verify(Body, new Dictionary<string, string>(), Secret, Now));
    }

    [Fact]
    public void The_signature_is_the_standard_webhooks_hmac()
    {
        // HMAC-SHA256 of id.timestamp.body under the decoded secret; the whsec_ prefix is optional.
        using var hmac = new HMACSHA256(Key);
        var expected = "v1," + Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes("""msg_1.1614265330.{"test":1}""")));

        Assert.Equal(expected, Webhooks.Sign(Secret, "msg_1", 1614265330, """{"test":1}"""));
        Assert.Equal(expected, Webhooks.Sign(Convert.ToBase64String(Key), "msg_1", 1614265330, """{"test":1}"""));
    }

    private static byte[] RandomKey()
    {
        var b = new byte[24];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(b);
        return b;
    }
}
