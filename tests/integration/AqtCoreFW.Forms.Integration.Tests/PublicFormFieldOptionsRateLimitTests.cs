using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using System.Net;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// C1 regression lock: the anonymous public-form field-options endpoint is rate-limited to
/// 15 req/min per IP (<c>public-field-options</c> policy, <c>RateLimiterConfiguration.cs</c>) — stricter
/// than the 30/min formula-evaluate policy because resolving a reference field can trigger an outbound
/// external data-source fetch. It is a security control on an unauthenticated surface, so it must stay
/// enforced.
///
/// The limiter runs as MIDDLEWARE ahead of MediatR, so an over-quota request is rejected with 429 BEFORE
/// the handler resolves the template — the test needs no seeded template and does not depend on the field
/// existing. It fires more requests than the non-Development quota (15) from one client (same
/// RemoteIpAddress partition) and asserts at least one 429 appears; the pre-limit responses may be 404
/// (no such form) which is fine — the assertion is on the limiter, not the handler. If the policy is
/// removed or the <c>[EnableRateLimiting("public-field-options")]</c> attribute dropped, every response
/// becomes a handler status and no 429 ever appears → this test fails.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class PublicFormFieldOptionsRateLimitTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public PublicFormFieldOptionsRateLimitTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task PublicFieldOptions_ExceedingPerIpQuota_Returns429()
    {
        // Testing environment resolves the non-Development quota (15/min); fire well past it from one
        // client so the same-IP partition is exhausted regardless of any concurrent global-limiter budget.
        var client = _app.CreateClient();
        var code = $"rl-{Guid.NewGuid():N}"[..12];

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 25; i++)
        {
            var response = await client.GetAsync($"/api/v1/public/forms/{code}/fields/somekey/options");
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "the public-field-options limiter caps anonymous option resolves at 15/min per IP — firing 25 " +
            "from one client must trip a 429; if none appears the limiter is no longer enforced on this endpoint");
    }
}
