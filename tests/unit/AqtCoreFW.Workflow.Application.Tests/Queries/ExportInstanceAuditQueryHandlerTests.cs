using System.Text;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.ExportInstanceAudit;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Queries;

using AuditHeaderRow = ExportInstanceAuditQueryHandler.AuditHeaderRow;
using AuditTransitionRow = ExportInstanceAuditQueryHandler.AuditTransitionRow;
using AuditTaskAssignmentRow = ExportInstanceAuditQueryHandler.AuditTaskAssignmentRow;

/// <summary>
/// Tests for ExportInstanceAuditQueryHandler (JSON + CSV formats).
/// PDF format deferred to a separate composer — tested in infrastructure layer.
/// </summary>
public sealed class ExportInstanceAuditQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly ExportInstanceAuditQueryHandler _handler;

    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WrongTenantId = Guid.NewGuid();

    public ExportInstanceAuditQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _handler = new ExportInstanceAuditQueryHandler(_db);
    }

    private void SetupHeader(AuditHeaderRow? row = null) =>
        _db.QueryFirstOrDefaultAsync<AuditHeaderRow>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(row ?? DefaultHeader());

    private void SetupNoHeader() =>
        _db.QueryFirstOrDefaultAsync<AuditHeaderRow>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns((AuditHeaderRow?)null);

    private void SetupHeaderTenantAware(AuditHeaderRow row) =>
        // Use reflection on anonymous param object to check TenantId match — mimics SQL WHERE clause
        _db.QueryFirstOrDefaultAsync<AuditHeaderRow>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var param = ci.ArgAt<object?>(1);
                var prop = param?.GetType().GetProperty("TenantId");
                var tenantInParam = prop?.GetValue(param) as Guid?;
                return tenantInParam == row.TenantId ? row : null;
            });

    private void SetupTransitions(IEnumerable<AuditTransitionRow>? rows = null) =>
        _db.QueryAsync<AuditTransitionRow>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows ?? [DefaultTransition()]));

    private void SetupAssignments(IEnumerable<AuditTaskAssignmentRow>? rows = null) =>
        _db.QueryAsync<AuditTaskAssignmentRow>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(rows ?? Enumerable.Empty<AuditTaskAssignmentRow>()));

    private static AuditHeaderRow DefaultHeader() => new()
    {
        InstanceId = InstanceId,
        TenantId = TenantId,
        DefinitionName = "Case Review",
        DefinitionKey = "case-review",
        EntityType = "Case",
        EntityId = Guid.NewGuid(),
        StartedAt = DateTimeOffset.UtcNow.AddDays(-3),
        CompletedAt = null,
        CurrentStateName = "Pending",
        DueAt = DateTimeOffset.UtcNow.AddDays(4),
    };

    private static AuditTransitionRow DefaultTransition() => new()
    {
        FromStateName = "Draft",
        ToStateName = "Pending",
        ActionName = "Submit",
        TransitionedBy = Guid.NewGuid(),
        Comment = "Submitted for review",
        TransitionedAt = DateTimeOffset.UtcNow.AddDays(-2),
        AttachmentCount = 2,
    };

    [Fact]
    public async Task Handle_InstanceNotFound_ReturnsNotFound()
    {
        SetupNoHeader();

        var result = await _handler.Handle(
            new ExportInstanceAuditQuery(InstanceId, TenantId, AuditExportFormat.Json),
            CancellationToken.None);

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, e => e is NotFoundError);
    }

    [Fact]
    public async Task Handle_WrongTenant_ReturnsNotFound()
    {
        // Header row exists but for a different tenant → handler must enforce isolation
        SetupHeaderTenantAware(DefaultHeader());

        var result = await _handler.Handle(
            new ExportInstanceAuditQuery(InstanceId, WrongTenantId, AuditExportFormat.Json),
            CancellationToken.None);

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, e => e is NotFoundError);
    }

    [Fact]
    public async Task Handle_JsonFormat_ReturnsSerializedDto()
    {
        SetupHeader();
        SetupTransitions();
        SetupAssignments();

        var result = await _handler.Handle(
            new ExportInstanceAuditQuery(InstanceId, TenantId, AuditExportFormat.Json),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuditExportFormat.Json, result.Value.Format);
        Assert.Equal("application/json", result.Value.ContentType);
        Assert.EndsWith(".json", result.Value.FileName);

        // Verify JSON is well-formed and contains key fields
        var json = Encoding.UTF8.GetString(result.Value.Data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(InstanceId, doc.RootElement.GetProperty("instanceId").GetGuid());
        Assert.Equal("Case Review", doc.RootElement.GetProperty("definitionName").GetString());
        Assert.True(doc.RootElement.GetProperty("transitions").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Handle_CsvFormat_ReturnsCsvWithHeader()
    {
        SetupHeader();
        SetupTransitions([DefaultTransition(), DefaultTransition()]);
        SetupAssignments();

        var result = await _handler.Handle(
            new ExportInstanceAuditQuery(InstanceId, TenantId, AuditExportFormat.Csv),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuditExportFormat.Csv, result.Value.Format);
        Assert.Equal("text/csv", result.Value.ContentType);
        Assert.EndsWith(".csv", result.Value.FileName);

        var csv = Encoding.UTF8.GetString(result.Value.Data);
        // Must start with a header row
        Assert.StartsWith("FromState,ToState,Action,TransitionedBy,TransitionedAt,AttachmentCount,Comment", csv);
        // Must have 2 data rows
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length); // header + 2 transitions
    }

    [Fact]
    public async Task Handle_CsvFormat_EscapesCommasAndQuotesInComments()
    {
        SetupHeader();
        var trickyTransition = DefaultTransition() with
        {
            Comment = "Approved, with note \"urgent\""
        };
        SetupTransitions([trickyTransition]);
        SetupAssignments();

        var result = await _handler.Handle(
            new ExportInstanceAuditQuery(InstanceId, TenantId, AuditExportFormat.Csv),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var csv = Encoding.UTF8.GetString(result.Value.Data);
        // Quoted field containing comma + escaped inner quotes: "Approved, with note ""urgent"""
        Assert.Contains("\"Approved, with note \"\"urgent\"\"\"", csv);
    }

    [Fact]
    public async Task Handle_JsonFormat_IncludesTaskAssignments()
    {
        SetupHeader();
        SetupTransitions();
        var assignee = Guid.NewGuid();
        SetupAssignments([
            new() {
                TaskTitle = "Review case",
                AssignedToId = assignee,
                AssignedById = Guid.NewGuid(),
                Type = "Initial",
                AssignedAt = DateTimeOffset.UtcNow.AddDays(-2),
                Reason = null,
            }
        ]);

        var result = await _handler.Handle(
            new ExportInstanceAuditQuery(InstanceId, TenantId, AuditExportFormat.Json),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var json = Encoding.UTF8.GetString(result.Value.Data);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetProperty("taskAssignments").GetArrayLength());
    }

    [Fact]
    public async Task Handle_FileName_IncludesInstanceIdAndFormat()
    {
        SetupHeader();
        SetupTransitions();
        SetupAssignments();

        var result = await _handler.Handle(
            new ExportInstanceAuditQuery(InstanceId, TenantId, AuditExportFormat.Csv),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // File name uses compact :N format (no hyphens) — suitable for filenames
        Assert.Contains(InstanceId.ToString("N"), result.Value.FileName);
        Assert.Contains("audit", result.Value.FileName, StringComparison.OrdinalIgnoreCase);
    }
}
