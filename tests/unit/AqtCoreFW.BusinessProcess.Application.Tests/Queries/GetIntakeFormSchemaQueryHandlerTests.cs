using AqtCoreFW.BusinessProcess.Application.Queries.GetIntakeFormSchema;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetIntakeFormSchemaQueryHandler — Pattern-A guard + NotFound + null-intake (204 path)
/// + snapshot-missing data-integrity NotFound + happy path + SystemAdmin bypass.
/// </summary>
public sealed class GetIntakeFormSchemaQueryHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid BpPublicId = Guid.NewGuid();
    private static readonly Guid TemplatePublicId = Guid.NewGuid();

    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IFormTemplateQueryClient _formClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly ILogger<GetIntakeFormSchemaQueryHandler> _logger =
        Substitute.For<ILogger<GetIntakeFormSchemaQueryHandler>>();
    private readonly GetIntakeFormSchemaQueryHandler _handler;

    public GetIntakeFormSchemaQueryHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);
        _handler = new GetIntakeFormSchemaQueryHandler(_db, _tenantContext, _formClient, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_NonSystemAdmin_RejectedBeforeAnyDbOrClientCall()
    {
        var result = await _handler.Handle(
            new GetIntakeFormSchemaQuery(BpPublicId, OtherTenantId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _formClient.DidNotReceiveWithAnyArgs()
            .GetVersionSchemaByInternalIdAsync(default, default, default, default);
    }

    [Fact]
    public async Task Handle_BpNotFound_ReturnsNotFound()
    {
        var result = await _handler.Handle(
            new GetIntakeFormSchemaQuery(BpPublicId, CurrentTenantId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        result.Errors.First().Message.Should().Contain(BpPublicId.ToString());
        await _formClient.DidNotReceiveWithAnyArgs()
            .GetVersionSchemaByInternalIdAsync(default, default, default, default);
    }

    [Fact]
    public async Task Handle_NoIntakeFormPinned_ReturnsSuccessNullForControllerNoContent()
    {
        ArrangeBp(intakeFormTemplateId: null, intakeFormTemplateVersion: null);

        var result = await _handler.Handle(
            new GetIntakeFormSchemaQuery(BpPublicId, CurrentTenantId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull("controller maps null to 204 No Content for BPs without intake form");
        await _formClient.DidNotReceiveWithAnyArgs()
            .GetVersionSchemaByInternalIdAsync(default, default, default, default);
    }

    [Fact]
    public async Task Handle_SnapshotMissing_ReturnsNotFoundDataIntegrity()
    {
        ArrangeBp(intakeFormTemplateId: 42L, intakeFormTemplateVersion: 3);
        _formClient.GetVersionSchemaByInternalIdAsync(42L, 3, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((FormTemplateVersionSchemaDto?)null);

        var result = await _handler.Handle(
            new GetIntakeFormSchemaQuery(BpPublicId, CurrentTenantId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        result.Errors.First().Message.Should().Contain("v3");
    }

    [Fact]
    public async Task Handle_HappyPath_ReturnsDtoWithSchema()
    {
        const string schemaJson = """{"fields":[{"key":"name","type":"text"}]}""";
        ArrangeBp(intakeFormTemplateId: 42L, intakeFormTemplateVersion: 3);
        _formClient.GetVersionSchemaByInternalIdAsync(42L, 3, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(TemplatePublicId, 3, schemaJson));

        var result = await _handler.Handle(
            new GetIntakeFormSchemaQuery(BpPublicId, CurrentTenantId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.FormTemplatePublicId.Should().Be(TemplatePublicId);
        result.Value.VersionNumber.Should().Be(3);
        result.Value.SchemaJson.Should().Be(schemaJson);
    }

    [Fact]
    public async Task Handle_SystemAdminCrossTenant_BypassesGuard()
    {
        _tenantContext.IsSystemAdmin.Returns(true);
        ArrangeBp(intakeFormTemplateId: null, intakeFormTemplateVersion: null);

        var result = await _handler.Handle(
            new GetIntakeFormSchemaQuery(BpPublicId, OtherTenantId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    private void ArrangeBp(long? intakeFormTemplateId, int? intakeFormTemplateVersion)
    {
        _db.QueryFirstOrDefaultAsync<GetIntakeFormSchemaRow>(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new GetIntakeFormSchemaRow
            {
                IntakeFormTemplateId = intakeFormTemplateId,
                IntakeFormTemplateVersion = intakeFormTemplateVersion
            });
    }
}
