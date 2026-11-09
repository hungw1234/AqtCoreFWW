using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// Integration tests for POST /api/v1/public/forms/{code}/evaluate-formulas
/// (Forms Phase-4A P2 — anonymous public batch formula compute endpoint).
/// Exercises the real HTTP path end-to-end: create -> add Formula field -> publish ->
/// set IsPublic=1 -> anonymous evaluate, through the actual NCalc sandbox
/// (IFormulaEvaluationService), no stubbing. Rate-limit test mirrors
/// <see cref="PublicFormSubmitRateLimitTests"/> (limiter runs as middleware ahead of MediatR,
/// so no seeded template is required for the 429 assertion).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class EvaluatePublicFormulasEndpointTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public EvaluatePublicFormulasEndpointTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task PublicEvaluate_ExceedingPerIpQuota_Returns429()
    {
        // Testing environment resolves the non-Development quota (30/min); fire well past it from
        // one client so the same-IP partition is exhausted. Requests fire CONCURRENTLY — the
        // per-request handler round-trip (~2-3s observed) means a sequential loop spans well past
        // the 60s fixed window and never accumulates enough permits in-window to trip the limiter
        // (same class of pitfall as PublicFormSubmitRateLimitTests, but that test's much lower quota
        // of 5 trips within a handful of sequential calls; 30 does not). Pre-limit responses may be
        // 404 (no such template) which is fine — asserting on the limiter, not the handler.
        var client = _app.CreateClient();
        var code = $"rlf-{Guid.NewGuid():N}"[..12];
        var body = new { data = new Dictionary<string, object?>() };

        var tasks = Enumerable.Range(0, 60)
            .Select(_ => client.PostAsJsonAsync($"/api/v1/public/forms/{code}/evaluate-formulas", body));
        var responses = await Task.WhenAll(tasks);
        var statuses = responses.Select(r => r.StatusCode).ToList();

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "the formula-evaluate limiter caps anonymous evaluate calls at 30/min per IP — firing 60 " +
            "concurrently from one client must trip a 429; if none appears the limiter is no longer " +
            "enforced on this endpoint");
    }

    [Fact]
    public async Task PublicEvaluate_UnknownCode_Returns404()
    {
        var client = _app.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/public/forms/{Guid.NewGuid():N}/evaluate-formulas",
            new { data = new Dictionary<string, object?>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublicEvaluate_ActivePublicTemplateWithFormulaField_Returns200WithComputedValue()
    {
        var tenantId = Guid.NewGuid();
        var adminId  = Guid.NewGuid();
        var admin = CreateClientWithPermissions(adminId, tenantId, PermCodes.Forms.Template.Manage);
        var code = $"PF{Guid.NewGuid():N}"[..9];

        // 1. Create draft template
        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId,
            name = $"Public Formula Test {code}",
            nameVi = $"Public Formula Test {code}",
            code,
            storageMode = 0, // StorageMode.Json
            createdBy = adminId
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // 2. Add a Number field ("amount") the formula depends on
        var addAmountResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId,
                fieldKey = "amount",
                type = 1, // FormFieldType.Number
                labelEn = "Amount",
                labelVi = "Số tiền",
                displayOrder = 1,
                required = false,
                updatedBy = adminId
            });
        addAmountResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Add the Formula field itself
        var addFormulaResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId,
                fieldKey = "total",
                type = 30, // FormFieldType.Formula
                labelEn = "Total",
                labelVi = "Tổng",
                displayOrder = 2,
                required = false,
                formulaJson = """{"expression":"[amount]*2"}""",
                updatedBy = adminId
            });
        addFormulaResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Publish -> Active (Json mode)
        var publishResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId, updatedBy = adminId });
        publishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 5. Set IsPublic=1 for public bypass read (by-Code resolution requires IsPublic +
        // public_forms_bypass flag — same targeted UPDATE pattern as FormSubmissionConsentAtomicityTests
        // since the HTTP create/fields/publish path has no request field to set it directly).
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var formsDb = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            var internalId = await formsDb.FormTemplates
                .Where(t => t.PublicId == templatePublicId).Select(t => t.Id).FirstAsync();
            await formsDb.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE forms.FormTemplates SET IsPublic = 1 WHERE Id = {internalId}");
        }

        // 6. Evaluate anonymously. Real HTTP JSON body binds "amount" as a numeric JsonElement —
        // the endpoint must normalize it before handing it to the NCalc sandbox (same regression
        // guard FormulaBatchEvaluator already covers for the authed P1 path).
        var anon = _app.CreateClient();
        var evalResp = await anon.PostAsJsonAsync(
            $"/api/v1/public/forms/{code}/evaluate-formulas",
            new { data = new Dictionary<string, object?> { ["amount"] = 100 } });

        var rawBody = await evalResp.Content.ReadAsStringAsync();
        evalResp.StatusCode.Should().Be(HttpStatusCode.OK, "body was: " + rawBody);
        var responseBody = JsonSerializer.Deserialize<JsonElement>(rawBody);
        responseBody.GetProperty("success").GetBoolean().Should().BeTrue("body was: " + rawBody);
        var results = responseBody.GetProperty("data").GetProperty("results");
        results.TryGetProperty("total", out var totalEl).Should().BeTrue("body was: " + rawBody);
        totalEl.GetDecimal().Should().Be(200m, "body was: " + rawBody);
    }

    // --- Helpers ---

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
