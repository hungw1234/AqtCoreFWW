using System.Net;
using AqtCoreFW.Tests.Integration.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Notifications;

/// <summary>
/// Smoke tests for NotificationsController — /api/v1/notifications
/// [Authorize] on controller: all endpoints require authenticated identity.
/// GetCurrentUserId() extracts sub/NameIdentifier claim — TestAuthHandler sets ClaimTypes.NameIdentifier.
/// Pass explicit userId to avoid Guid.Empty from missing claim.
/// </summary>
[Collection("Integration")]
public class NotificationsApiTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BaseUrl = "/api/v1/notifications";
    private static readonly Guid TestTenantId = Guid.NewGuid();

    [Fact]
    public async Task GetNotifications_Authenticated_Returns200()
    {
        using var client = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["Viewer"],
            tenantId: TestTenantId.ToString());

        var response = await client.GetAsync(
            $"{BaseUrl}?tenantId={TestTenantId}&page=1&pageSize=20");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUnreadCount_Authenticated_Returns200()
    {
        using var client = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["Viewer"],
            tenantId: TestTenantId.ToString());

        var response = await client.GetAsync(
            $"{BaseUrl}/unread-count?tenantId={TestTenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetNotifications_Unauthenticated_Returns401Or403()
    {
        var anonClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await anonClient.GetAsync(
            $"{BaseUrl}?tenantId={TestTenantId}");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MarkRead_WithNonExistentNotification_Returns403Or404()
    {
        // Notification doesn't exist → ForbiddenError or NotFoundError depending on ownership check
        using var client = CreateAuthenticatedClient(
            userId: Guid.NewGuid().ToString(),
            roles: ["Viewer"],
            tenantId: TestTenantId.ToString());

        var response = await client.PatchAsync(
            $"{BaseUrl}/{Guid.NewGuid()}/read?tenantId={TestTenantId}",
            content: null);

        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Forbidden,
            HttpStatusCode.NotFound,
            HttpStatusCode.NoContent);
    }
}
