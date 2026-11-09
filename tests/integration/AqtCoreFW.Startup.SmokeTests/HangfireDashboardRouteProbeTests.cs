using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AqtCoreFW.Startup.SmokeTests;

/// <summary>
/// Route-probe guard for the Hangfire dashboard relocation. The predicate unit test
/// (HangfireAuthorizationFilterTests) covers the role logic on a hand-built principal; this proves the
/// behavior in the REAL booted pipeline — specifically the two facts the unit test cannot:
///   1. AuthServer hosts /hangfire but the SystemAdmin filter has NO dev bypass, so an anonymous request
///      is denied even in the Development environment (the regression the dev-open removal must hold).
///   2. The API host no longer mounts /hangfire → 404.
/// Each fixture boots its host (Program.cs) against its own ephemeral Testcontainers SQL Server.
/// </summary>
[Collection(SmokeTestCollection.CollectionName)]
public sealed class HangfireAuthServerRouteProbeTests : IClassFixture<AuthServerHostFixture>
{
    private readonly AuthServerHostFixture _fixture;

    public HangfireAuthServerRouteProbeTests(AuthServerHostFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task AuthServer_Hangfire_AnonymousInDevelopment_IsDenied()
    {
        // AllowAutoRedirect=false so a deny isn't masked by following a login redirect.
        var client = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/hangfire");

        // dev-open removed → no anonymous bypass even in Development. The filter denies → not 200.
        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "the SystemAdmin dashboard filter has no Development bypass — anonymous must be denied in every env");
    }
}

[Collection(SmokeTestCollection.CollectionName)]
public sealed class HangfireApiHostRouteProbeTests : IClassFixture<ApiHostFixture>
{
    private readonly ApiHostFixture _fixture;

    public HangfireApiHostRouteProbeTests(ApiHostFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Smoke")]
    public async Task ApiHost_Hangfire_IsNotMounted_Returns404()
    {
        var client = _fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/hangfire");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the dashboard was moved to the AuthServer; the API host must no longer serve /hangfire");
    }
}
