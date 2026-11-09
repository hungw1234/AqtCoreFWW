using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Audit.Integration.Tests;

[Collection(SqlServerCollection.CollectionName)]
public sealed class RtbfEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private static readonly Guid TenantId  = Guid.NewGuid();
    private static readonly Guid SubjectId = Guid.NewGuid();
    private static readonly Guid AdminId   = Guid.NewGuid();

    public RtbfEndpointsTests(WebAppFixture app) { _app = app; }

    [Fact]
    public async Task GetRtbfRequests_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();
        var response = await client.GetAsync("/api/v1/admin/rtbf-requests");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRtbfRequests_NonAdminRole_Returns403()
    {
        var client = _app.CreateAuthenticatedClient(AdminId, TenantId, "GovOfficer");
        var response = await client.GetAsync("/api/v1/admin/rtbf-requests");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetRtbfRequests_SystemAdmin_Returns200()
    {
        var client = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Audit.Rtbf.View);
        var response = await client.GetAsync("/api/v1/admin/rtbf-requests");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRtbfRequests_ResponseBody_WrappedInApiResponseEnvelope()
    {
        var client = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Audit.Rtbf.View);
        var response = await client.GetAsync("/api/v1/admin/rtbf-requests");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("success", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ProcessRtbfRequest_NonExistentId_Returns404()
    {
        var client = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Audit.Rtbf.View, PermCodes.Audit.Rtbf.Execute);
        var payload = new { processedBy = AdminId };
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/rtbf-requests/{Guid.NewGuid()}/process", payload);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProcessRtbfRequest_ExistingRequest_Returns204AndSetsStatus()
    {
        var rtbf = await SeedRtbfRequestAsync();
        var client = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Audit.Rtbf.View, PermCodes.Audit.Rtbf.Execute);
        var payload = new { processedBy = AdminId };
        // T3 entity: controller route takes {id:guid} which maps to MessageId
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/rtbf-requests/{rtbf.MessageId}/process", payload);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RejectRtbfRequest_NonExistentId_Returns404()
    {
        var client = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Audit.Rtbf.View, PermCodes.Audit.Rtbf.Execute);
        var payload = new { processedBy = AdminId, reason = "Invalid request" };
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/rtbf-requests/{Guid.NewGuid()}/reject", payload);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RejectRtbfRequest_ExistingRequest_Returns204()
    {
        var rtbf = await SeedRtbfRequestAsync();
        var client = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Audit.Rtbf.View, PermCodes.Audit.Rtbf.Execute);
        var payload = new { processedBy = AdminId, reason = "Data subject cannot be identified" };
        // T3 entity: controller route takes {id:guid} which maps to MessageId
        var response = await client.PostAsJsonAsync(
            $"/api/v1/admin/rtbf-requests/{rtbf.MessageId}/reject", payload);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<RtbfRequest> SeedRtbfRequestAsync()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        // T3 entity: IGuidGenerator required to assign MessageId
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        var rtbf = RtbfRequest.Create(gen, TenantId, SubjectId);
        db.Set<RtbfRequest>().Add(rtbf);
        await db.SaveChangesAsync();
        return rtbf;
    }

    private HttpClient CreateClientWithPermissions(Guid userId, Guid tenantId, params string[] permissions)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService
            .GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string>(permissions)
            });
        permService
            .GetSummaryAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = Guid.Empty,
                PermissionCodes = new HashSet<string>()
            });
        var client = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped(_ => permService)))
            .CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        // RtbfController carries [RequiresMfa] — supply a fresh mfa_at claim so the
        // MfaRequirementHandler is satisfied (the SystemAdmin bypass handler only
        // shortcuts PermissionRequirement, not MfaRequirement).
        client.DefaultRequestHeaders.Add(TestAuthHandler.MfaAtHeader, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        return client;
    }
}
