using System.Net;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Shared;

/// <summary>
/// Verifies that the test infrastructure boots correctly:
/// containers start, migrations apply, and the API responds.
/// </summary>
public class SmokeTest(DatabaseFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task HealthCheck_Live_Returns200()
    {
        // /health/live uses Predicate = _ => false — no dependency checks, always healthy
        var response = await Client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnauthenticatedRequest_ToProtectedEndpoint_Returns401Or403()
    {
        // Anonymous client — no X-Test-* headers, TestAuthHandler still builds a principal
        // but [Authorize] policies should reject requests without SystemAdmin role
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await anonClient.GetAsync("/api/v1/admin/users");
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden);
    }
}
