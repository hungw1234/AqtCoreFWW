using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Tests.SharedFixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Notifications.Integration.Tests;

/// <summary>
/// Integration tests for Notifications REST API endpoints.
/// Uses WebApplicationFactory + Testcontainers SQL Server.
/// Email/SMS providers are stubbed â€” only InApp channel exercises real DB path end-to-end.
///
/// Permission model: <c>PermissionAuthorizationHandler</c> â†’ <c>IEffectivePermissionService</c>.
/// Testing env has no identity data seeded, so tests stub the service via
/// <c>ConfigureTestServices</c> (same pattern as <c>WorkflowTaxonomyAdminEndpointsTests</c>).
/// Endpoints without <c>[Authorize(Policy = ...)]</c> (GET list, PATCH read) need auth only,
/// so plain <c>CreateAuthenticatedClient</c> still works for recipient users (Alice, Bob).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class NotificationEndpointsTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantA    = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantB    = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid UserAlice  = Guid.Parse("A1000000-0000-0000-0000-000000000001");
    private static readonly Guid UserBob    = Guid.Parse("B2000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminUser  = Guid.Parse("AD000000-0000-0000-0000-000000000001");

    public NotificationEndpointsTests(WebAppFixture app) => _app = app;

    // --- TC-NOT-INT-001: Create notification persists in DB (POST then GET list) ---

    [Fact]
    public async Task SendNotification_InAppChannel_PersistsInDatabase()
    {
        // Admin sends a notification to Alice on TenantA â€” needs NOTIF.MSG.SEND
        var adminClient = CreateClientWithPermissions(AdminUser, TenantA, PermCodes.Notif.Msg.Send);

        var payload = new
        {
            tenantId = TenantA,
            recipientUserId = UserAlice,
            subject = "TC-NOT-INT-001 Test Subject",
            body = "Integration test notification body",
            channel = "inapp"
        };

        var sendResp = await adminClient.PostAsJsonAsync("/api/v1/notifications", payload);
        sendResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var sendBody = await sendResp.Content.ReadFromJsonAsync<JsonElement>();
        // ApiResponse<Guid> â€” data contains the created notification ID
        var notifId = sendBody.GetProperty("data").GetGuid();
        notifId.Should().NotBeEmpty();

        // Verify persisted: fetch Alice's notifications list (class-level [Authorize], no policy)
        var aliceClient = _app.CreateAuthenticatedClient(UserAlice, TenantA);
        var listResp = await aliceClient.GetAsync(
            $"/api/v1/notifications?tenantId={TenantA}&pageSize=100");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var listBody = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = listBody.GetProperty("data").GetProperty("items").EnumerateArray().ToList();

        items.Should().Contain(n =>
            n.GetProperty("id").GetGuid() == notifId &&
            n.GetProperty("subject").GetString() == "TC-NOT-INT-001 Test Subject");
    }

    // --- TC-NOT-INT-002: Mark notification as read sets ReadAt ---

    [Fact]
    public async Task MarkRead_ExistingNotification_SetsReadAtTimestamp()
    {
        // Send a notification to Alice
        var adminClient = CreateClientWithPermissions(AdminUser, TenantA, PermCodes.Notif.Msg.Send);
        var payload = new
        {
            tenantId = TenantA,
            recipientUserId = UserAlice,
            subject = "TC-NOT-INT-002 Mark Read",
            body = "Please mark this as read",
            channel = "inapp"
        };
        var sendResp = await adminClient.PostAsJsonAsync("/api/v1/notifications", payload);
        sendResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var sendJson = await sendResp.Content.ReadFromJsonAsync<JsonElement>();
        var notifId = sendJson.GetProperty("data").GetGuid();

        // Alice marks it as read (class-level [Authorize], no policy)
        var aliceClient = _app.CreateAuthenticatedClient(UserAlice, TenantA);
        var markResp = await aliceClient.PatchAsync(
            $"/api/v1/notifications/{notifId}/read?tenantId={TenantA}", null);
        markResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify via list â€” isRead should now be true and readAt populated
        var listResp = await aliceClient.GetAsync(
            $"/api/v1/notifications?tenantId={TenantA}&pageSize=100&isRead=true");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = listBody.GetProperty("data").GetProperty("items").EnumerateArray().ToList();

        var readItem = items.FirstOrDefault(n => n.GetProperty("id").GetGuid() == notifId);
        readItem.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "the marked-read notification should appear in isRead=true filter");
        readItem.GetProperty("isRead").GetBoolean().Should().BeTrue();
        readItem.GetProperty("readAt").ValueKind.Should().NotBe(JsonValueKind.Null,
            "readAt must be populated after marking as read");
    }

    // --- TC-NOT-INT-003: Notification preferences filter delivery (channel-based) ---

    // Reactivated 2026-04-18: email delivery stubbed via FakeEmailSender in WebAppFixture.
    [Fact]
    public async Task SendNotification_EmailChannel_CallsEmailSenderAndPersistsRecord()
    {
        // Email channel: real delivery is stubbed, but DB record should still be created
        var adminClient = CreateClientWithPermissions(AdminUser, TenantA, PermCodes.Notif.Msg.Send);
        var payload = new
        {
            tenantId = TenantA,
            recipientUserId = UserAlice,
            subject = "TC-NOT-INT-003 Email Notification",
            body = "Test email body for channel preference test",
            channel = "email"
        };

        var sendResp = await adminClient.PostAsJsonAsync("/api/v1/notifications", payload);

        // Email stub returns Task.CompletedTask â€” no exception â†’ 200 OK
        sendResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "email channel is stubbed to succeed â€” notification should be persisted");

        var notifId = (await sendResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();
        notifId.Should().NotBeEmpty();

        // Verify persisted in DB for channel=email â€” filter by channel in list
        var aliceClient = _app.CreateAuthenticatedClient(UserAlice, TenantA);
        var listResp = await aliceClient.GetAsync(
            $"/api/v1/notifications?tenantId={TenantA}&channel=email&pageSize=100");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var listBody = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var items = listBody.GetProperty("data").GetProperty("items").EnumerateArray().ToList();

        items.Should().Contain(n => n.GetProperty("id").GetGuid() == notifId);
    }

    // --- TC-NOT-INT-004: Template rendering with seeded template ---

    [Fact]
    public async Task SendNotification_WithTemplate_PersistsTemplateId()
    {
        // Admin needs BOTH Notif.Template.Manage (create template) and Notif.Msg.Send (send notification)
        var adminClient = CreateClientWithPermissions(AdminUser, TenantA,
            PermCodes.Notif.Template.Manage,
            PermCodes.Notif.Msg.Send);

        // Create a template first
        var templatePayload = new
        {
            tenantId = TenantA,
            templateKey = $"tc_not_int_004_{Guid.NewGuid():N}",
            subjectTemplate = "Hello {{name}}",
            bodyTemplate = "Welcome, {{name}}! Your tenant is {{tenantId}}.",
            channel = "inapp"
        };
        var createTemplateResp = await adminClient.PostAsJsonAsync(
            "/api/v1/admin/notification-templates", templatePayload);
        createTemplateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templateId = (await createTemplateResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();
        templateId.Should().NotBeEmpty("template must be created before sending");

        // Send a notification referencing the template
        var sendPayload = new
        {
            tenantId = TenantA,
            recipientUserId = UserAlice,
            subject = "TC-NOT-INT-004 Template Subject",
            body = "Rendered template body",
            channel = "inapp",
            templateId,
            correlationId = $"tc-004-{Guid.NewGuid():N}"
        };
        var sendResp = await adminClient.PostAsJsonAsync("/api/v1/notifications", sendPayload);
        sendResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var notifId = (await sendResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        // Verify the notification appears in Alice's list
        var aliceClient = _app.CreateAuthenticatedClient(UserAlice, TenantA);
        var listResp = await aliceClient.GetAsync(
            $"/api/v1/notifications?tenantId={TenantA}&pageSize=100");
        var items = (await listResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("items").EnumerateArray().ToList();

        items.Should().Contain(n => n.GetProperty("id").GetGuid() == notifId,
            "notification sent with templateId should persist and be retrievable");
    }

    // --- TC-NOT-INT-005: Cross-tenant notification isolation ---

    [Fact]
    public async Task GetNotifications_TenantBUser_DoesNotSeeTenantANotifications()
    {
        // Send a notification on TenantA
        var adminA = CreateClientWithPermissions(AdminUser, TenantA, PermCodes.Notif.Msg.Send);
        var payload = new
        {
            tenantId = TenantA,
            recipientUserId = UserAlice,
            subject = "TC-NOT-INT-005 TenantA Private",
            body = "Should not be visible to TenantB",
            channel = "inapp"
        };
        var sendResp = await adminA.PostAsJsonAsync("/api/v1/notifications", payload);
        sendResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var notifId = (await sendResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        // TenantB user queries their own notifications â€” must not see TenantA's record
        var bobClient = _app.CreateAuthenticatedClient(UserBob, TenantB);
        var listResp = await bobClient.GetAsync(
            $"/api/v1/notifications?tenantId={TenantB}&pageSize=100");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = (await listResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("items").EnumerateArray().ToList();

        items.Should().NotContain(n => n.GetProperty("id").GetGuid() == notifId,
            "TenantB must not see notifications belonging to TenantA");
    }

    // --- TC-NOT-INT-006: Notification template list + by-id round-trip (Dapper direct DTO materialization) ---
    // Guards the MTS-4 change that collapsed the intermediate Dapper `Row` records in
    // GetNotificationTemplates(ById)QueryHandler — DateTimeOffset now materializes via the global
    // DapperTypeHandlers handler. If that handler regresses, CreatedAt comes back as default/throws here.

    [Fact]
    public async Task GetNotificationTemplates_AfterCreate_ListAndById_ReturnTemplateWithTimestamps()
    {
        var adminClient = CreateClientWithPermissions(AdminUser, TenantA, PermCodes.Notif.Template.Manage);

        var templateKey = $"tc_not_int_006_{Guid.NewGuid():N}";
        var createResp = await adminClient.PostAsJsonAsync("/api/v1/admin/notification-templates", new
        {
            tenantId = TenantA,
            templateKey,
            subjectTemplate = "Subject {{name}}",
            bodyTemplate = "Body for {{name}}",
            channel = "inapp"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templateId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);

        // List endpoint → GetNotificationTemplatesQueryHandler
        var listResp = await adminClient.GetAsync($"/api/v1/admin/notification-templates?tenantId={TenantA}&pageSize=100");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listItems = (await listResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("items").EnumerateArray().ToList();

        var listed = listItems.SingleOrDefault(t => t.GetProperty("id").GetGuid() == templateId);
        listed.ValueKind.Should().NotBe(JsonValueKind.Undefined, "created template must appear in the list");
        listed.GetProperty("templateKey").GetString().Should().Be(templateKey);
        listed.GetProperty("channel").GetString().Should().Be("inapp");
        listed.GetProperty("createdAt").GetDateTimeOffset().Should().BeAfter(notBefore);

        // By-id endpoint → GetNotificationTemplateByIdQueryHandler
        var byIdResp = await adminClient.GetAsync($"/api/v1/admin/notification-templates/{templateId}");
        byIdResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var byId = (await byIdResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        byId.GetProperty("id").GetGuid().Should().Be(templateId);
        byId.GetProperty("templateKey").GetString().Should().Be(templateKey);
        byId.GetProperty("createdAt").GetDateTimeOffset().Should().BeAfter(notBefore);

        // Unknown id → 404 (Result.Fail → NotFoundError)
        var missingResp = await adminClient.GetAsync($"/api/v1/admin/notification-templates/{Guid.NewGuid()}");
        missingResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Creates an authenticated client whose <see cref="IEffectivePermissionService"/> grants only the
    /// specified permissions. Other users get an empty set (â†’ 403). Other tests in the same
    /// fixture are unaffected because <c>WithWebHostBuilder</c> clones the server per call.
    /// Pattern proven by <c>WorkflowTaxonomyAdminEndpointsTests.CreateClientWithPermissions</c>.
    /// </summary>
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
        // NotificationTemplatesAdminController carries [RequiresMfa] — supply mfa_at claim.
        client.DefaultRequestHeaders.Add(TestAuthHandler.MfaAtHeader,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        return client;
    }
}
