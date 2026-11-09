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
using Xunit;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// Integration tests for POST /api/v1/forms/templates/{publicId}/evaluate-formulas
/// (Forms Phase-4A P1 — authed batch formula compute endpoint).
/// Exercises the real HTTP path end-to-end: create -> add Formula field -> publish -> evaluate,
/// through the actual NCalc sandbox (IFormulaEvaluationService), no stubbing.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class EvaluateFormulasEndpointTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId  = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    public EvaluateFormulasEndpointTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task EvaluateFormulas_UnknownPublicId_Returns404()
    {
        var client = _app.CreateAuthenticatedClient(UserId, TenantId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/forms/templates/{Guid.NewGuid()}/evaluate-formulas",
            new { values = new Dictionary<string, object?>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task EvaluateFormulas_Unauthenticated_Returns401()
    {
        var client = _app.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/forms/templates/{Guid.NewGuid()}/evaluate-formulas",
            new { values = new Dictionary<string, object?>() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EvaluateFormulas_ActiveTemplateWithFormulaField_Returns200WithComputedValue()
    {
        var admin = _app.WithWebHostBuilder(builder =>
                builder.ConfigureTestServices(services =>
                    services.AddScoped(_ => BuildPermissionService(AdminId, PermCodes.Forms.Template.Manage))))
            .CreateClient();
        admin.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, AdminId.ToString());
        admin.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, TenantId.ToString());

        var code = $"F{Guid.NewGuid():N}"[..9];

        // 1. Create draft template
        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId    = TenantId,
            name        = $"Formula Test {code}",
            nameVi      = $"Formula Test {code}",
            code,
            storageMode = 0, // StorageMode.Json
            createdBy   = AdminId
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // 2. Add a Number field ("amount") the formula depends on
        var addAmountResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId     = TenantId,
                fieldKey     = "amount",
                type         = 1, // FormFieldType.Number
                labelEn      = "Amount",
                labelVi      = "Số tiền",
                displayOrder = 1,
                required     = false,
                updatedBy    = AdminId
            });
        addAmountResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Add the Formula field itself
        var addFormulaResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId     = TenantId,
                fieldKey     = "total",
                type         = 30, // FormFieldType.Formula
                labelEn      = "Total",
                labelVi      = "Tổng",
                displayOrder = 2,
                required     = false,
                formulaJson  = """{"expression":"[amount]*2"}""",
                updatedBy    = AdminId
            });
        addFormulaResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Publish -> Active (Json mode)
        var publishResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId = TenantId, updatedBy = AdminId });
        publishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 5. Evaluate — any authenticated user (no manage permission required). Real HTTP JSON
        // body binds "amount" as a numeric JsonElement — the endpoint must normalize it before
        // handing it to the NCalc sandbox (a raw JsonElement operand throws inside NCalc).
        var filler = _app.CreateAuthenticatedClient(UserId, TenantId);
        var evalResp = await filler.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/evaluate-formulas",
            new { values = new Dictionary<string, object?> { ["amount"] = 100 } });

        var rawBody = await evalResp.Content.ReadAsStringAsync();
        evalResp.StatusCode.Should().Be(HttpStatusCode.OK, "body was: " + rawBody);
        var body = JsonSerializer.Deserialize<JsonElement>(rawBody);
        body.GetProperty("success").GetBoolean().Should().BeTrue("body was: " + rawBody);
        var results = body.GetProperty("data").GetProperty("results");
        results.TryGetProperty("total", out var totalEl).Should().BeTrue("body was: " + rawBody);
        totalEl.GetDecimal().Should().Be(200m, "body was: " + rawBody);
    }

    // --- Helpers ---

    private static IEffectivePermissionService BuildPermissionService(
        Guid userId, params string[] permissions)
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
        return permService;
    }
}
