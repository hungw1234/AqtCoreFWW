using AqtCoreFW.BusinessProcess.Application.DTOs;
using AqtCoreFW.BusinessProcess.Application.Queries.GetBusinessProcessById;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetBusinessProcessByIdQueryHandler — Pattern A tenant guard + NotFound path.
/// </summary>
public sealed class GetBusinessProcessByIdQueryHandlerTests
{
    private static readonly Guid CurrentTenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid BpPublicId = Guid.NewGuid();

    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IWorkflowModuleClient _workflowClient = Substitute.For<IWorkflowModuleClient>();
    private readonly IFormTemplateQueryClient _formTemplateClient = Substitute.For<IFormTemplateQueryClient>();
    private readonly ILogger<GetBusinessProcessByIdQueryHandler> _logger = Substitute.For<ILogger<GetBusinessProcessByIdQueryHandler>>();
    private readonly GetBusinessProcessByIdQueryHandler _handler;

    public GetBusinessProcessByIdQueryHandlerTests()
    {
        _tenantContext.TenantId.Returns(CurrentTenantId);
        _tenantContext.IsSystemAdmin.Returns(false);
        _handler = new GetBusinessProcessByIdQueryHandler(
            _db, _tenantContext, _workflowClient, _formTemplateClient, _logger);
    }

    [Fact]
    public async Task Handle_CrossTenant_NonSystemAdmin_ReturnsValidationError()
    {
        var query = new GetBusinessProcessByIdQuery(BpPublicId, OtherTenantId);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is ValidationError);
        result.Errors.First().Message.Should().Contain("Cross-tenant");
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFoundError()
    {
        var query = new GetBusinessProcessByIdQuery(BpPublicId, CurrentTenantId);

        // QueryFirstOrDefaultAsync<T> returns default(T) — for ref types this is null.
        // NSubstitute returns null by default; explicit setup not strictly required but readable.

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        result.Errors.First().Message.Should().Contain(BpPublicId.ToString());
    }

    // --- P7-FU1.2: WorkflowDefinitionPublicId resolution ---

    [Fact]
    public async Task Handle_CrossTenant_DoesNotCallWorkflowClient()
    {
        var query = new GetBusinessProcessByIdQuery(BpPublicId, OtherTenantId);

        await _handler.Handle(query, CancellationToken.None);

        await _workflowClient.DidNotReceiveWithAnyArgs().TryGetDefinitionPublicIdAsync(
            default, default, default);
    }

    [Fact]
    public async Task Handle_NotFound_DoesNotCallWorkflowClient()
    {
        var query = new GetBusinessProcessByIdQuery(BpPublicId, CurrentTenantId);

        await _handler.Handle(query, CancellationToken.None);

        await _workflowClient.DidNotReceiveWithAnyArgs().TryGetDefinitionPublicIdAsync(
            default, default, default);
    }

    [Fact]
    public async Task Handle_HappyPath_ResolvesWorkflowPublicId_AndPropagatesToDto()
    {
        const long internalBpId = 42L;
        const long workflowInternalId = 100L;
        var workflowPublicId = Guid.NewGuid();

        ArrangeRoot(internalBpId, workflowInternalId);
        _workflowClient.TryGetDefinitionPublicIdAsync(
                workflowInternalId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns(workflowPublicId);

        var result = await _handler.Handle(
            new GetBusinessProcessByIdQuery(BpPublicId, CurrentTenantId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkflowDefinitionPublicId.Should().Be(workflowPublicId);
        await _workflowClient.Received(1).TryGetDefinitionPublicIdAsync(
            workflowInternalId, CurrentTenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HappyPath_OrphanWorkflowFk_ReturnsNullPublicId()
    {
        const long internalBpId = 42L;
        const long workflowInternalId = 100L;

        ArrangeRoot(internalBpId, workflowInternalId);
        _workflowClient.TryGetDefinitionPublicIdAsync(
                workflowInternalId, CurrentTenantId, Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var result = await _handler.Handle(
            new GetBusinessProcessByIdQuery(BpPublicId, CurrentTenantId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.WorkflowDefinitionPublicId.Should().BeNull();
    }

    private void ArrangeRoot(long internalBpId, long workflowInternalId)
    {
        _db.QueryFirstOrDefaultAsync<BusinessProcessRootRow>(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new BusinessProcessRootRow
            {
                InternalId = internalBpId,
                Id = BpPublicId,
                Code = "BP-001",
                Name = "Test BP",
                WorkflowDefinitionId = workflowInternalId,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                RowVersion = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
            });
        _db.QueryAsync<BusinessProcessStateFormDto>(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<BusinessProcessStateFormDto>());
        _db.QueryAsync<BusinessProcessTransitionFormDto>(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<BusinessProcessTransitionFormDto>());
    }
}
