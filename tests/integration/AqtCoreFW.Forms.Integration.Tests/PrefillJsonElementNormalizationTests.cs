using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Clients;
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

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// P6-FU3 (2026-05-16): integration round-trip proving <see cref="JsonElement"/>-vs-CLR asymmetry is
/// closed at the storage boundary by <c>JsonValueNormalizer</c>.
///
/// <para>
/// Scope clarification — memory referenced "4-mode storage matrix (Json/Table × source/target)" but
/// the *target* template's StorageMode is irrelevant to prefill resolution (only the source
/// submission's data is read; the target's data is never touched at prefill time). The real axis is
/// the source backend = 2 modes:
/// <list type="bullet">
///   <item><description><b>Json mode</b>: <c>SubmissionData</c> column → <c>JsonSerializer.Deserialize</c>
///         used to return values as <see cref="JsonElement"/> until P6-FU2 fix.</description></item>
///   <item><description><b>Table mode</b>: dynamic table via Dapper → already native CLR (regression-only).</description></item>
/// </list>
/// </para>
///
/// <para>
/// Strategy: submit a form via the public API with a typed payload (string + integer + boolean),
/// then resolve <see cref="IFormSubmissionRunClient"/> from the test host's DI scope and call
/// <c>GetFullSubmissionAsync</c> directly. Each returned value must be the native CLR type, not
/// <see cref="JsonElement"/>.
/// </para>
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class PrefillJsonElementNormalizationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId  = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    public PrefillJsonElementNormalizationTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task GetFullSubmission_JsonMode_ReturnsNativeClrTypes_NotJsonElement()
    {
        var admin = CreateClientWithPermissions(AdminId, TenantId, PermCodes.Forms.Template.Manage);
        var user  = CreateClientWithPermissions(
            UserId, TenantId, PermCodes.Forms.Submit.Create, PermCodes.Forms.Submit.View);
        var code  = $"N{Guid.NewGuid():N}"[..9];

        var templatePublicId = await CreatePublishedTemplate(admin, code, storageMode: 0);

        // Submit with typed values — int, string, bool. PostAsJsonAsync serializes each at its native
        // JSON type, so server stores them as the matching JSON tokens. Without FU2, the deserialize
        // round-trip would box every value as JsonElement.
        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data = new Dictionary<string, object?>
            {
                ["name"]   = "Nguyen Van A",
                ["age"]    = 42,
                ["active"] = true,
            }
        });
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissionId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        // Resolve IFormSubmissionRunClient directly to bypass any DTO mapping that may already
        // unwrap JsonElement. This pins the boundary contract: the contract method returns native CLR.
        using var scope = _app.Services.CreateScope();
        var runClient = scope.ServiceProvider.GetRequiredService<IFormSubmissionRunClient>();
        var dict = await runClient.GetFullSubmissionAsync(submissionId, TenantId);

        dict.Should().NotBeNull();

        // No value may be a JsonElement — that's the whole point of FU2.
        foreach (var kv in dict!)
            kv.Value.Should().NotBeOfType<JsonElement>(
                $"P6-FU2 boundary must normalize JsonElement for key {kv.Key}");

        dict["name"].Should().BeOfType<string>().And.Be("Nguyen Van A");
        dict["age"].Should().BeOfType<long>().And.Be(42L);
        dict["active"].Should().BeOfType<bool>().And.Be(true);
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> CreatePublishedTemplate(HttpClient admin, string code, int storageMode)
    {
        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId    = TenantId,
            name        = $"Prefill FU3 {code}",
            nameVi      = $"Prefill FU3 {code}",
            code,
            storageMode,
            createdBy   = AdminId
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        await AddField(admin, templatePublicId, "name",   fieldType: 0, displayOrder: 1);
        await AddField(admin, templatePublicId, "age",    fieldType: 1, displayOrder: 2);
        await AddField(admin, templatePublicId, "active", fieldType: 4, displayOrder: 3);

        var publishResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId = TenantId, updatedBy = AdminId });
        publishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return templatePublicId;
    }

    private async Task AddField(
        HttpClient admin, Guid templatePublicId, string key, int fieldType, int displayOrder)
    {
        var resp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId  = TenantId,
                fieldKey  = key,
                type      = fieldType,
                labelEn   = key,
                labelVi   = key,
                displayOrder,
                required  = false,
                updatedBy = AdminId
            });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
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
        return client;
    }
}
