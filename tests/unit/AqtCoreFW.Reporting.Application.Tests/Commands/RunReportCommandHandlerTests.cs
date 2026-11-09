using AqtCoreFW.Reporting.Application.Commands.RunReport;
using AqtCoreFW.Reporting.Application.Jobs;
using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using NSubstitute;
using System.Linq.Expressions;

namespace AqtCoreFW.Reporting.Application.Tests.Commands;

/// <summary>
/// Unit tests for RunReportCommandHandler.
/// Verifies: definition-not-found error, inactive-definition error,
/// and happy-path creation + job enqueue.
/// </summary>
public sealed class RunReportCommandHandlerTests
{
    private readonly IReportDefinitionRepository _definitionRepo = Substitute.For<IReportDefinitionRepository>();
    private readonly IReportExecutionRepository _executionRepo = Substitute.For<IReportExecutionRepository>();
    private readonly IBackgroundJobService _backgroundJobs = Substitute.For<IBackgroundJobService>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly RunReportCommandHandler _sut;

    private static readonly Guid DefinitionId = Guid.NewGuid();
    private static readonly Guid RequestedBy = Guid.NewGuid();

    public RunReportCommandHandlerTests()
    {
        // Stub IGuidGenerator to return a deterministic Guid for ReportExecution.PublicId
        _guidGen.NewIdMonotonic().Returns(Guid.Parse("33333333-3333-7333-8333-333333333333"));
        _sut = new RunReportCommandHandler(_definitionRepo, _executionRepo, _backgroundJobs, _guidGen);
    }

    [Fact]
    public async Task Handle_DefinitionNotFound_ReturnsError()
    {
        // Arrange — repo returns null (definition does not exist)
        // T2: look up by PublicId (Guid)
        _definitionRepo.GetByPublicIdAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns((ReportDefinition?)null);

        var command = BuildCommand();

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.GetType() == typeof(ReportDefinitionNotFoundError));

        // No execution should be created
        await _executionRepo.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_InactiveDefinition_ReturnsError()
    {
        // Arrange — definition exists but is inactive
        var inactiveDefinition = CreateDefinition(active: false);
        // T2: look up by PublicId (Guid)
        _definitionRepo.GetByPublicIdAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(inactiveDefinition);

        var command = BuildCommand();

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("inactive", StringComparison.OrdinalIgnoreCase));

        // No execution should be created
        await _executionRepo.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesExecutionAndEnqueuesJob()
    {
        // Arrange — active definition; ReportDefinition.Id is long (0 before EF SaveChanges)
        // ReportDefinitionId on execution will be 0 (definition.Id pre-persist).
        var activeDefinition = CreateDefinition(active: true);
        var actualDefinitionId = activeDefinition.Id; // long, = 0 before EF SaveChanges

        // T2: look up by PublicId (Guid)
        _definitionRepo.GetByPublicIdAsync(DefinitionId, Arg.Any<CancellationToken>())
            .Returns(activeDefinition);

        ReportExecution? capturedExecution = null;
        await _executionRepo.AddAsync(
            Arg.Do<ReportExecution>(e => capturedExecution = e),
            Arg.Any<CancellationToken>());

        // IBackgroundJobService.Enqueue<IExecuteReportJob> — stub the exact closed generic
        _backgroundJobs.Enqueue<IExecuteReportJob>(Arg.Any<Expression<Func<IExecuteReportJob, Task>>>())
            .Returns("job-id-123");

        var command = BuildCommand(parametersJson: """{"FromDate":"2025-01-01"}""");

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert — success with a valid executionId (T2: PublicId Guid)
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        // Execution was persisted
        await _executionRepo.Received(1)
            .AddAsync(Arg.Any<ReportExecution>(), Arg.Any<CancellationToken>());

        capturedExecution.Should().NotBeNull();
        // ReportDefinitionId on the execution is long FK = definition.Id (0 before EF SaveChanges)
        capturedExecution!.ReportDefinitionId.Should().Be(actualDefinitionId);
        capturedExecution.Status.Should().Be(ExecutionStatus.Queued);
        capturedExecution.RequestedBy.Should().Be(RequestedBy);

        // T2: returned id matches the execution's PublicId (Guid AK) — not the long internal PK
        result.Value.Should().Be(capturedExecution.PublicId);

        // Background job was enqueued with the exact IExecuteReportJob closed generic
        _backgroundJobs.Received(1)
            .Enqueue<IExecuteReportJob>(Arg.Any<Expression<Func<IExecuteReportJob, Task>>>());
    }

    // --- Helpers ---

    private static RunReportCommand BuildCommand(string parametersJson = "{}") =>
        new(DefinitionId, parametersJson, TenantId: "tenant-001", RequestedBy: RequestedBy);

    /// <summary>
    /// Creates a ReportDefinition via the public factory, then optionally deactivates it.
    /// T2 entity: IGuidGenerator stub injected for PublicId generation.
    /// </summary>
    private ReportDefinition CreateDefinition(bool active)
    {
        var def = ReportDefinition.Create(
            gen: _guidGen,
            name: "Test Report",
            nameVi: "Báo cáo thử nghiệm",
            description: "Unit test fixture",
            sqlTemplate: "SELECT * FROM forms.FormTemplates WHERE TenantId = @TenantId",
            parametersSchema: "[]",
            format: OutputFormat.Excel,
            tenantId: "tenant-001");

        // ReportDefinition.Create sets IsActive = true; deactivate if needed
        if (!active)
            def.Deactivate();

        return def;
    }
}
