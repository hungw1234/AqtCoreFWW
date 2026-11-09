using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using System.Net;
using System.Net.Http.Json;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// Phase-2 (2D) regression lock: the anonymous public-form submit endpoint is rate-limited to
/// 5 req/min per IP (<c>public-form-submit</c> policy, <c>RateLimiterConfiguration.cs</c>). This is a
/// spam-prevention control on the only unauthenticated write surface, so it must stay enforced.
///
/// The limiter runs as MIDDLEWARE ahead of MediatR, so an over-quota request is rejected with 429
/// BEFORE the handler resolves the template — the test therefore needs no seeded template and does not
/// depend on the submit body being valid. It fires more requests than the non-Development quota (5)
/// from a single client (same RemoteIpAddress partition) and asserts at least one 429 appears; the
/// pre-limit responses may be 404/422 (no such template) which is fine — we assert on the limiter, not
/// the handler. If the policy is removed or its wiring attribute dropped, every response becomes a
/// handler status and no 429 ever appears → this test fails.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class PublicFormSubmitRateLimitTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public PublicFormSubmitRateLimitTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task PublicSubmit_ExceedingPerIpQuota_Returns429()
    {
        // Testing environment resolves the non-Development quota (5/min); fire well past it from one
        // client so the same-IP partition is exhausted regardless of any concurrent global-limiter budget.
        var client = _app.CreateClient();
        var code = $"rl-{Guid.NewGuid():N}"[..12];
        var body = new { Data = new Dictionary<string, object?>(), ConsentGiven = true };

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync($"/api/v1/public/forms/{code}/submit", body);
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "the public-form-submit limiter caps anonymous submits at 5/min per IP — firing 10 from one " +
            "client must trip a 429; if none appears the limiter is no longer enforced on this endpoint");
    }
}
