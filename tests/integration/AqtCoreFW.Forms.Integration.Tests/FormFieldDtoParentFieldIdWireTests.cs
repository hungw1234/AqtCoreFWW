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

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// M1 Phase-07 finalize - reviewer C-1 fix witness at the HTTP wire.
///
/// Before the fix, <c>FormFieldDto</c> DID NOT project <c>ParentFieldId</c>, so a TableField child
/// column created via <c>AddFormFieldCommand(..., ParentFieldId: tableFieldId)</c> appeared in the
/// GET template payload IDENTICAL to a top-level field - the FE had no wire signal to distinguish
/// a child from a scalar. That defect made the FE fall back to fieldKey-prefix discovery
/// (<c>parent.child</c>), which <c>AddFormFieldCommandValidator</c>'s regex forbids in production,
/// so the entire in-row cascade dispatch (RowRefFieldSelect wiring) was unreachable dead code.
///
/// This test creates a template with (a) a scalar top-level field, (b) a TableField parent, and
/// (c) a child column linked via <c>ParentFieldId</c>. It then GETs the template via the real
/// authed endpoint and asserts the wire response carries <c>parentFieldId</c> matching the
/// TableField's internal Id ONLY on the child, and null on the top-level fields. That symmetry
/// is what the FE <c>TableFieldWidget</c> now consumes to discover child columns.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormFieldDtoParentFieldIdWireTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId  = Guid.NewGuid();

    public FormFieldDtoParentFieldIdWireTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task GetTemplate_Returns_ParentFieldId_On_Child_Column_Only()
    {
        var admin = CreateClientWithPermissions(AdminId, TenantId,
            PermCodes.Forms.Template.Manage, PermCodes.Forms.Template.View);
        var code = $"PFID{Guid.NewGuid():N}"[..12];

        // 1. Create a JSON-mode template (no DDL side-effect - only the DTO shape is under test).
        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            tenantId    = TenantId,
            name        = $"ParentFieldId Wire {code}",
            nameVi      = $"ParentFieldId Wire {code}",
            code,
            storageMode = 0, // StorageMode.Json - no per-TableField DDL background service needed
            createdBy   = AdminId
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        // 2. Scalar top-level field (baseline - parentFieldId must be null on the wire).
        var addScalarResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId = TenantId,
                fieldKey = "fullName",
                type = 0, // FormFieldType.Text
                labelEn = "Full Name",
                labelVi = "Họ tên",
                displayOrder = 1,
                required = false,
                updatedBy = AdminId
            });
        addScalarResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. TableField parent - captures its internal (bigint) Id from the response for wiring
        //    the child's ParentFieldId below.
        var addTableResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId = TenantId,
                fieldKey = "items",
                type = 20, // FormFieldType.TableField
                labelEn = "Items",
                labelVi = "Danh sách",
                displayOrder = 2,
                required = false,
                updatedBy = AdminId
            });
        addTableResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tableFieldId = (await addTableResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetInt64();

        // 4. Child column - linked via parentFieldId=tableFieldId. This is what the FE needs to
        //    discover as a child of the TableField at render time.
        var addChildResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId = TenantId,
                fieldKey = "itemName",
                type = 0, // FormFieldType.Text
                labelEn = "Item Name",
                labelVi = "Tên mục",
                displayOrder = 1,
                required = false,
                updatedBy = AdminId,
                parentFieldId = tableFieldId
            });
        addChildResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. GET the template via the real authed endpoint and assert the wire projection.
        var getResp = await admin.GetAsync(
            $"/api/v1/forms/templates/{templatePublicId}?tenantId={TenantId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var fields = (await getResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("fields").EnumerateArray().ToList();

        // Top-level fields: parentFieldId is null (or absent - both are valid wire representations
        // for a nullable long. The FE type declares `parentFieldId?: number | null`, so absent and
        // explicit null are both consumed as "no parent").
        var scalarField = fields.Single(f => f.GetProperty("fieldKey").GetString() == "fullName");
        AssertParentFieldIdIsNull(scalarField, "scalar top-level field must NOT carry a parent link");

        var tableField = fields.Single(f => f.GetProperty("fieldKey").GetString() == "items");
        AssertParentFieldIdIsNull(tableField, "TableField itself is top-level - must NOT carry a parent link");

        // Child column: parentFieldId MUST be present and MUST equal the TableField's internal Id.
        // This is the wire contract the FE TableFieldWidget consumes to filter allFields down to
        // its own children (`f.parentFieldId === field.id`), replacing the broken prefix path.
        var childField = fields.Single(f => f.GetProperty("fieldKey").GetString() == "itemName");
        childField.TryGetProperty("parentFieldId", out var childParentIdProp).Should().BeTrue(
            "the wire contract for a child column MUST project parentFieldId");
        childParentIdProp.ValueKind.Should().Be(JsonValueKind.Number,
            "parentFieldId on the wire is JSON number (BE long?), not string or null");
        childParentIdProp.GetInt64().Should().Be(tableFieldId,
            "the child column's parentFieldId must equal the TableField's internal Id");
    }

    /// <summary>
    /// Asserts a JSON field's `parentFieldId` is either absent from the payload or explicitly null -
    /// both are valid wire representations for a nullable long and both consumed identically by the
    /// FE type (`parentFieldId?: number | null`).
    /// </summary>
    private static void AssertParentFieldIdIsNull(JsonElement field, string because)
    {
        if (field.TryGetProperty("parentFieldId", out var prop))
        {
            prop.ValueKind.Should().Be(JsonValueKind.Null, because);
        }
        // Absent property is also valid for a null nullable long on the JSON wire - no further assert.
    }

    /// <summary>
    /// Copied from <c>FormEndpointsTests.CreateClientWithPermissions</c> per repo convention (each
    /// integration test class carries its own helper because <c>WithWebHostBuilder</c> must clone
    /// the server per call to avoid cross-test bleed of the permission mock).
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
        return client;
    }
}
