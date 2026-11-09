using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.Queries.GetWorkflowDefinitions;
using Dapper;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Regression tests for 2026-04-21 fix: list handler must include global templates
/// (TenantId=Guid.Empty) alongside tenant-specific rows — mirrors
/// WorkflowDefinitionRepository.GetLatestByKeyAsync fallback semantics.
/// </summary>
public sealed class GetWorkflowDefinitionsGlobalFallbackTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly GetWorkflowDefinitionsQueryHandler _sut;

    public GetWorkflowDefinitionsGlobalFallbackTests()
    {
        _sut = new GetWorkflowDefinitionsQueryHandler(_db);
    }

    [Fact]
    public async Task Handle_EmitsSqlWithGlobalTenantFallback()
    {
        string? capturedCountSql = null;
        string? capturedDataSql = null;
        _db.QuerySingleAsync<int>(
                Arg.Do<string>(sql => capturedCountSql = sql),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        _db.QueryAsync<WorkflowDefinitionSummaryDto>(
                Arg.Do<string>(sql => capturedDataSql = sql),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var query = new GetWorkflowDefinitionsQuery(
            TenantId: Guid.NewGuid(), IsActive: null, SearchTerm: null,
            Page: 1, PageSize: 20);

        var result = await _sut.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedCountSql.Should().Contain("TenantId = @TenantId").And.Contain("TenantId = @GlobalTenantId");
        capturedDataSql.Should().Contain("TenantId = @TenantId").And.Contain("TenantId = @GlobalTenantId");
    }

    [Fact]
    public async Task Handle_PassesGuidEmptyAsGlobalTenantParameter()
    {
        DynamicParameters? captured = null;
        _db.QuerySingleAsync<int>(
                Arg.Any<string>(),
                Arg.Do<object?>(p => captured ??= p as DynamicParameters),
                Arg.Any<CancellationToken>())
            .Returns(0);
        _db.QueryAsync<WorkflowDefinitionSummaryDto>(
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var query = new GetWorkflowDefinitionsQuery(
            TenantId: Guid.NewGuid(), IsActive: null, SearchTerm: null,
            Page: 1, PageSize: 20);

        await _sut.Handle(query, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ParameterNames.Should().Contain("GlobalTenantId");
        captured.Get<Guid>("GlobalTenantId").Should().Be(Guid.Empty);
    }
}
