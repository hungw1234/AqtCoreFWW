using AqtCoreFW.Reporting.Application.Commands.CreateReportDefinition;
using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.Reporting.Domain.Errors;
using AqtCoreFW.Reporting.Domain.Entities;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Reporting.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateReportDefinitionCommandHandler.
/// Validates SQL security checks: @TenantId required, must start with SELECT,
/// dangerous DDL/DML keywords blocked.
/// </summary>
public sealed class CreateReportDefinitionCommandHandlerTests
{
    private readonly IReportDefinitionRepository _repository = Substitute.For<IReportDefinitionRepository>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly CreateReportDefinitionCommandHandler _sut;

    public CreateReportDefinitionCommandHandlerTests()
    {
        // Provide deterministic Guid for IGuidGenerator stub
        _guidGen.NewIdMonotonic().Returns(Guid.Parse("22222222-2222-7222-8222-222222222222"));
        _sut = new CreateReportDefinitionCommandHandler(_repository, _guidGen);
    }

    [Fact]
    public async Task Handle_MissingTenantIdInSql_ReturnsError()
    {
        // Arrange — SQL lacks @TenantId parameter
        var command = BuildCommand(sqlTemplate: "SELECT * FROM forms.FormTemplates WHERE IsDeleted = 0");

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Metadata.ContainsKey("ModuleErrorCode") &&
            e.Metadata["ModuleErrorCode"].ToString() == ReportingErrors.Codes.MissingTenantFilter);

        // Repository must not be touched
        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_DangerousSqlKeyword_ReturnsError()
    {
        // Arrange — SQL has @TenantId and starts with SELECT but contains DROP
        var command = BuildCommand(
            sqlTemplate: "SELECT * FROM forms.FormTemplates WHERE TenantId = @TenantId; DROP TABLE forms.FormTemplates");

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert — blocked by dangerous keyword check (semicolon or DROP)
        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.GetType() == typeof(DangerousSqlError) ||
            (e.Metadata.ContainsKey("ModuleErrorCode") &&
             e.Metadata["ModuleErrorCode"].ToString() == ReportingErrors.Codes.DangerousSql));

        await _repository.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesDefinition()
    {
        // Arrange — well-formed SQL: starts with SELECT, has @TenantId, no dangerous keywords
        var command = BuildCommand(
            sqlTemplate: "SELECT Id, Title, Status FROM forms.FormTemplates WHERE TenantId = @TenantId AND IsDeleted = 0");

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert — success with a non-empty Guid
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        // Definition was persisted — inspect via ReceivedCalls to avoid Arg.Do matcher leak
        await _repository.Received(1).AddAsync(
            Arg.Any<ReportDefinition>(),
            Arg.Any<CancellationToken>());

        // Extract the captured definition from the recorded call (no Arg.Do side-effect needed)
        var captured = (ReportDefinition)_repository.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(_repository.AddAsync))
            .GetArguments()[0]!;

        captured.Name.Should().Be(command.Name);
        captured.IsActive.Should().BeTrue();
        captured.TenantId.Should().Be(command.TenantId);

        // T2: returned id matches the entity PublicId (Guid AK) — not the long internal PK
        result.Value.Should().Be(captured.PublicId);
    }

    // --- Helpers ---

    private static CreateReportDefinitionCommand BuildCommand(
        string? sqlTemplate = null,
        string name = "Forms Report",
        OutputFormat format = OutputFormat.Excel) =>
        new(Name: name,
            NameVi: "Báo cáo vụ việc",
            Description: "Test fixture",
            SqlTemplate: sqlTemplate ?? "SELECT * FROM forms.FormTemplates WHERE TenantId = @TenantId",
            ParametersSchema: "[]",
            OutputFormat: format,
            TenantId: "tenant-001");
}
