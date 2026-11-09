using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.Reporting.Infrastructure.Jobs;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Reporting.Infrastructure.Tests.Jobs;

/// <summary>
/// TC-RPT-I003: ExecuteReportJob idempotency guard skips terminal-state executions,
/// and handles missing execution/definition gracefully without throwing.
/// </summary>
public sealed class ExecuteReportJobTests
{
    private readonly IReportDefinitionRepository _definitionRepo = Substitute.For<IReportDefinitionRepository>();
    private readonly IReportExecutionRepository _executionRepo = Substitute.For<IReportExecutionRepository>();
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly IReportExcelGenerator _excelGen = Substitute.For<IReportExcelGenerator>();
    private readonly IReportPdfGenerator _pdfGen = Substitute.For<IReportPdfGenerator>();
    private readonly IReportStorageService _storage = Substitute.For<IReportStorageService>();
    private static readonly IGuidGenerator GuidGen = Substitute.For<IGuidGenerator>();

    static ExecuteReportJobTests()
    {
        // Deterministic Guid stub for test fixtures
        GuidGen.NewIdMonotonic().Returns(Guid.Parse("44444444-4444-7444-8444-444444444444"));
    }

    private ExecuteReportJob BuildSut() => new(
        _definitionRepo, _executionRepo, _db,
        _excelGen, _pdfGen, _storage,
        NullLogger<ExecuteReportJob>.Instance);

    [Fact]
    public async Task ExecuteAsync_ExecutionNotFound_ReturnsWithoutSaving()
    {
        // TC-RPT-I003: missing execution → log warning and return, no SaveChanges
        // T2: job receives PublicId (Guid) — uses GetByPublicIdAsync
        _executionRepo.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReportExecution?>(null));

        var sut = BuildSut();

        var act = async () => await sut.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _executionRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ExecutionAlreadyDone_SkipsWithoutSaving()
    {
        // TC-RPT-I003 idempotency guard: execution in Done state → skip (H-01)
        // T2: Create with IGuidGenerator + long definitionId
        var execution = ReportExecution.Create(GuidGen, 1L, "{}", Guid.NewGuid());
        execution.MarkRunning();
        execution.MarkDone("reports/file.xlsx");

        // T2: job looks up execution by PublicId (Guid)
        _executionRepo.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReportExecution?>(execution));

        var sut = BuildSut();

        // T2: pass PublicId to job
        await sut.ExecuteAsync(execution.PublicId, CancellationToken.None);

        // Idempotency guard: SaveChanges never called for terminal states
        await _executionRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _db.DidNotReceive().QueryAsync<dynamic>(
            Arg.Any<string>(), Arg.Any<object?>(),
            Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task ExecuteAsync_ExecutionAlreadyFailed_SkipsWithoutSaving()
    {
        // TC-RPT-I003 idempotency guard: execution in Failed state → skip (H-01)
        var execution = ReportExecution.Create(GuidGen, 1L, "{}", Guid.NewGuid());
        execution.MarkRunning();
        execution.MarkFailed("previous error");

        // T2: look up by PublicId
        _executionRepo.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReportExecution?>(execution));

        var sut = BuildSut();

        await sut.ExecuteAsync(execution.PublicId, CancellationToken.None);

        await _executionRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DefinitionNotFound_MarksExecutionFailed()
    {
        // TC-RPT-I003: execution exists but definition missing → marks Failed and saves
        var execution = ReportExecution.Create(GuidGen, 1L, "{}", Guid.NewGuid());

        // T2: execution by PublicId; definition by internal long FK
        _executionRepo.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReportExecution?>(execution));
        _definitionRepo.GetByInternalIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReportDefinition?>(null));

        var sut = BuildSut();

        await sut.ExecuteAsync(execution.PublicId, CancellationToken.None);

        execution.Status.Should().Be(ExecutionStatus.Failed);
        execution.ErrorMessage.Should().Contain(execution.ReportDefinitionId.ToString());
        await _executionRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DangerousSql_MarksExecutionFailed()
    {
        // TC-RPT-I003: SQL contains dangerous keyword → marks Failed, no DB query executed
        var execution = ReportExecution.Create(GuidGen, 1L, "{}", Guid.NewGuid());
        // T2: ReportDefinition.Create needs IGuidGenerator
        var definition = ReportDefinition.Create(
            GuidGen,
            "Test", "Test VN", "Desc",
            sqlTemplate: "DROP TABLE Users",  // dangerous keyword
            parametersSchema: "[]",
            format: OutputFormat.Excel,
            tenantId: null);

        // T2: execution by PublicId; definition by internal long FK
        _executionRepo.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReportExecution?>(execution));
        _definitionRepo.GetByInternalIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReportDefinition?>(definition));

        var sut = BuildSut();

        await sut.ExecuteAsync(execution.PublicId, CancellationToken.None);

        execution.Status.Should().Be(ExecutionStatus.Failed);
        execution.ErrorMessage.Should().Contain("SQL validation failed");
        await _db.DidNotReceive().QueryAsync<dynamic>(
            Arg.Any<string>(), Arg.Any<object?>(),
            Arg.Any<CancellationToken>(), Arg.Any<int?>());
    }

    [Fact]
    public async Task ExecuteAsync_NonSelectSql_MarksExecutionFailed()
    {
        // TC-RPT-I003: SQL does not start with SELECT (no dangerous keyword) → marks Failed
        // "SHOW TABLES" contains no blocked keywords but doesn't start with SELECT
        var execution = ReportExecution.Create(GuidGen, 1L, "{}", Guid.NewGuid());
        var definition = ReportDefinition.Create(
            GuidGen,
            "Test2", "Test2 VN", "Desc",
            sqlTemplate: "SHOW TABLES",
            parametersSchema: "[]",
            format: OutputFormat.Excel,
            tenantId: null);

        // T2: execution by PublicId; definition by internal long FK
        _executionRepo.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReportExecution?>(execution));
        _definitionRepo.GetByInternalIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReportDefinition?>(definition));

        var sut = BuildSut();

        await sut.ExecuteAsync(execution.PublicId, CancellationToken.None);

        execution.Status.Should().Be(ExecutionStatus.Failed);
        execution.ErrorMessage.Should().Contain("must start with SELECT");
    }
}
