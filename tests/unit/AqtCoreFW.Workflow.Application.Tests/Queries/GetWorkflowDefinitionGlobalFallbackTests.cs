using System.Reflection;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Workflow.Application.Queries.GetWorkflowDefinition;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

/// <summary>
/// Regression tests for 2026-04-21 fix: detail handler must accept global templates
/// (TenantId=Guid.Empty) — mirrors WorkflowDefinitionRepository.GetLatestByKeyAsync
/// fallback semantics so admin UI can GET a global workflow by ID.
///
/// DefinitionRow is a private nested record, so we inspect <see cref="ISubstitute.ReceivedCalls"/>
/// reflectively rather than mocking the generic return.
/// </summary>
public sealed class GetWorkflowDefinitionGlobalFallbackTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly GetWorkflowDefinitionQueryHandler _sut;

    public GetWorkflowDefinitionGlobalFallbackTests()
    {
        _sut = new GetWorkflowDefinitionQueryHandler(_db);
    }

    [Fact]
    public async Task Handle_EmitsSqlWithGlobalTenantFallback()
    {
        var query = new GetWorkflowDefinitionQuery(Guid.NewGuid(), Guid.NewGuid());
        await _sut.Handle(query, CancellationToken.None);

        var sql = FindFirstSqlArg();
        sql.Should().NotBeNull();
        sql.Should().Contain("TenantId = @TenantId").And.Contain("TenantId = @GlobalTenantId");
    }

    [Fact]
    public async Task Handle_PassesGuidEmptyAsGlobalTenantParameter()
    {
        var query = new GetWorkflowDefinitionQuery(Guid.NewGuid(), Guid.NewGuid());
        await _sut.Handle(query, CancellationToken.None);

        var paramObj = FindFirstParamArg();
        paramObj.Should().NotBeNull("handler must pass an anonymous param object");
        var globalProp = paramObj!.GetType().GetProperty("GlobalTenantId");
        globalProp.Should().NotBeNull("handler must pass GlobalTenantId param");
        globalProp!.GetValue(paramObj).Should().Be(Guid.Empty);
    }

    private string? FindFirstSqlArg() =>
        _db.ReceivedCalls()
            .SelectMany(c => c.GetArguments())
            .OfType<string>()
            .FirstOrDefault(s => s.Contains("WorkflowDefinitions", StringComparison.Ordinal));

    private object? FindFirstParamArg() =>
        _db.ReceivedCalls()
            .SelectMany(c => c.GetArguments())
            .FirstOrDefault(a =>
                a is not null
                && a is not string
                && a is not CancellationToken
                && a.GetType().GetProperty("GlobalTenantId") is not null);
}
