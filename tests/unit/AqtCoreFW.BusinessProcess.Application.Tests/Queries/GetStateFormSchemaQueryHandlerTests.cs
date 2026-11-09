using AqtCoreFW.BusinessProcess.Application.Queries.GetStateFormSchema;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Queries;

/// <summary>
/// P8-FU1: unit tests for GetStateFormSchemaQueryHandler — Pattern-A guard, NotFound on missing
/// state-form map, NotFound on missing pinned snapshot (data-integrity), happy path, and
/// SystemAdmin bypass.
/// </summary>
public sealed class GetStateFormSchemaQueryHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid BpPublicId = Guid.NewGuid();
    private static readonly Guid StateFormMapPublicId = Guid.NewGuid();
    private static readonly Guid TemplatePublicId = Guid.NewGuid();

    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IFormTemplateQueryClient _formClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly ILogger<GetStateFormSchemaQueryHandler> _logger =
        Substitute.For<ILogger<GetStateFormSchemaQueryHandler>>();
    private readonly GetStateFormSchemaQueryHandler _handler;

    public GetStateFormSchemaQueryHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);
        _handler = new GetStateFormSchemaQueryHandler(_db, _tenantContext, _formClient, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_NonSystemAdmin_RejectedBeforeAnyDbOrClientCall()
    {
        var result = await _handler.Handle(
            new GetStateFormSchemaQuery(BpPublicId, StateFormMapPublicId, OtherTenantId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        await _formClient.DidNotReceiveWithAnyArgs()
            .GetVersionSchemaByInternalIdAsync(default, default, default, default);
    }

    [Fact]
    public async Task Handle_StateFormMapNotFound_ReturnsNotFound()
    {
        var result = await _handler.Handle(
            new GetStateFormSchemaQuery(BpPublicId, StateFormMapPublicId, CurrentTenantId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        result.Errors.First().Message.Should().Contain(StateFormMapPublicId.ToString());
        await _formClient.DidNotReceiveWithAnyArgs()
            .GetVersionSchemaByInternalIdAsync(default, default, default, default);
    }

    [Fact]
    public async Task Handle_SnapshotMissing_ReturnsNotFoundDataIntegrity()
    {
        ArrangeMap(formTemplateId: 42L, formTemplateVersion: 3);
        _formClient.GetVersionSchemaByInternalIdAsync(42L, 3, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((FormTemplateVersionSchemaDto?)null);

        var result = await _handler.Handle(
            new GetStateFormSchemaQuery(BpPublicId, StateFormMapPublicId, CurrentTenantId),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        result.Errors.First().Message.Should().Contain("v3");
    }

    [Fact]
    public async Task Handle_HappyPath_ReturnsDtoWithSchema()
    {
        const string schemaJson = """{"fields":[{"key":"comment","type":"text"}]}""";
        ArrangeMap(formTemplateId: 42L, formTemplateVersion: 3);
        _formClient.GetVersionSchemaByInternalIdAsync(42L, 3, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(TemplatePublicId, 3, schemaJson));

        var result = await _handler.Handle(
            new GetStateFormSchemaQuery(BpPublicId, StateFormMapPublicId, CurrentTenantId),
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
        ArrangeMap(formTemplateId: 42L, formTemplateVersion: 1);
        _formClient.GetVersionSchemaByInternalIdAsync(42L, 1, OtherTenantId, Arg.Any<CancellationToken>())
            .Returns(new FormTemplateVersionSchemaDto(TemplatePublicId, 1, """{"fields":[]}"""));

        var result = await _handler.Handle(
            new GetStateFormSchemaQuery(BpPublicId, StateFormMapPublicId, OtherTenantId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.FormTemplatePublicId.Should().Be(TemplatePublicId);
    }

    private void ArrangeMap(long formTemplateId, int formTemplateVersion)
    {
        _db.QueryFirstOrDefaultAsync<GetStateFormSchemaRow>(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new GetStateFormSchemaRow
            {
                FormTemplateId = formTemplateId,
                FormTemplateVersion = formTemplateVersion
            });
    }
}
