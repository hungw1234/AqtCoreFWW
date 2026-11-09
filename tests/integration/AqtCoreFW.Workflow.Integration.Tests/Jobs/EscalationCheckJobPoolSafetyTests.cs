using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Jobs;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Integration.Tests.Jobs;

/// <summary>
/// G5 — EscalationCheckJob pool-safety gate (S9 RT2-7 steal-fix + RT3-M3 pool-aging).
///
/// A1: Pool task (AssigneeId=null) survives job run even with matching escalation rule → no steal,
///     no TaskAssignment ledger row. Proves SQL filter + belt-and-braces guard both active.
/// A2: Single-assigned task with matching rule is escalated normally → regression bar for legacy path.
/// A3: Pool task unclaimed ≥ 60 min emits structured LogWarning → aging pass works.
/// A4: Pool task unclaimed &lt; 60 min emits no aging log → threshold SQL predicate correct.
/// A5: Both passes run in same job invocation on different tasks without crossover.
///
/// S5 META-L (c) invariant: every test has explicit precondition asserts pinning WHICH code path fires.
/// Logger capture: hand-rolled CapturingLogger&lt;T&gt; (no new NuGet package; NSubstitute generic
/// method resolution is fragile for ILogger&lt;T&gt;.Log&lt;TState&gt; — planner-authorised fallback).
/// Back-dating: raw SQL UPDATE on CreatedAt/DueDate after EF seed (private setter cannot be set via EF).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class EscalationCheckJobPoolSafetyTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _fixture;

    public EscalationCheckJobPoolSafetyTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    // ── CapturingLogger ─────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal in-process logger that captures (Level, Message) pairs.
    /// Planner-authorised fallback: NSubstitute's generic Arg matchers for ILogger&lt;T&gt;.Log&lt;TState&gt;
    /// are fragile; this avoids the dependency entirely. Under 20 LOC, zero new NuGet packages.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];
        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    // ── Seed helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a minimal WorkflowDefinition + WorkflowInstance. Returns (definitionId, initialStateId, instanceId).
    /// Uses a unique DefinitionKey per call to avoid unique-index collision across tests.
    /// </summary>
    private async Task<(long DefinitionId, long InitialStateId, long InstanceId)> SeedDefinitionAndInstanceAsync(
        Guid tenantId,
        Guid createdBy)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db  = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var definitionKey = $"esc-test-{Guid.NewGuid():N}";
        var definition = WorkflowDefinition.Create(
            gen: gen,
            name: $"EscalationTest {definitionKey}",
            description: "Auto-seeded for EscalationCheckJob pool-safety tests",
            tenantId: tenantId,
            createdBy: createdBy,
            definitionKey: definitionKey,
            entityType: "IntegrationTest");

        db.WorkflowDefinitions.Add(definition);
        await db.SaveChangesAsync();

        var initialState = WorkflowState.Create(
            definitionId: definition.Id,
            name: "Open",
            displayNameVi: "Mở",
            displayNameEn: "Open",
            isInitial: true,
            isFinal: false,
            color: "#808080",
            sortOrder: 0);

        db.Set<WorkflowState>().Add(initialState);
        await db.SaveChangesAsync();

        var instance = WorkflowInstance.Create(
            gen: gen,
            definitionId: definition.Id,
            initialStateId: initialState.Id,
            entityType: "IntegrationTest",
            entityId: Guid.NewGuid(),
            tenantId: tenantId,
            createdBy: createdBy);

        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();

        return (definition.Id, initialState.Id, instance.Id);
    }

    /// <summary>
    /// Seeds a WorkflowTask. After seeding, back-dates CreatedAt and optionally DueDate
    /// via raw SQL (CreatedAt has private setter — only path for back-dating in tests).
    /// Returns the persisted task Id.
    /// </summary>
    private async Task<long> SeedTaskAndBackdateAsync(
        long workflowInstanceId,
        Guid tenantId,
        Guid createdBy,
        Guid? assigneeId,
        int createdAtMinutesAgo,
        int? dueDateMinutesAgo)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var task = WorkflowTask.Create(
            workflowInstanceId: workflowInstanceId,
            title: "Pool-Safety Test Task",
            description: null,
            assigneeId: assigneeId,
            priority: WorkflowTaskPriority.Normal,
            // DueDate will be overwritten by raw SQL below; seed null or future for now
            dueDate: dueDateMinutesAgo.HasValue ? DateTime.UtcNow.AddMinutes(-dueDateMinutesAgo.Value) : null,
            tenantId: tenantId,
            createdBy: createdBy);

        db.WorkflowTasks.Add(task);
        await db.SaveChangesAsync();

        // Back-date CreatedAt (and optionally DueDate) via raw SQL — private setters
        // cannot be set via EF change tracking from outside the entity. This is the only
        // correct path for setting precise time-in-the-past values for aging tests.
        var taskId = task.Id;

        if (dueDateMinutesAgo.HasValue)
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE workflow.WorkflowTasks SET CreatedAt = @p0, DueDate = @p1 WHERE Id = @p2",
                DateTime.UtcNow.AddMinutes(-createdAtMinutesAgo),
                DateTime.UtcNow.AddMinutes(-dueDateMinutesAgo.Value),
                taskId);
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE workflow.WorkflowTasks SET CreatedAt = @p0 WHERE Id = @p1",
                DateTime.UtcNow.AddMinutes(-createdAtMinutesAgo),
                taskId);
        }

        return taskId;
    }

    /// <summary>
    /// Seeds an EscalationRule for a definition. EscalateToValue must be a parsable Guid string
    /// so that EscalationCheckJob line 67 resolves escalateToId != Guid.Empty → task.Assign() fires.
    /// This is the S5 META-L (c) invariant: the test pins the code path where task.Assign() WOULD
    /// have been called for a single-assigned task, so the steal-fix assertion is non-tautological.
    /// </summary>
    private async Task<(long RuleId, Guid EscalateToUserId)> SeedEscalationRuleAsync(
        long definitionId,
        Guid tenantId,
        Guid createdBy,
        int escalationAfterMinutes = 30)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var escalateToUserId = Guid.NewGuid(); // valid Guid → TryParse succeeds → Assign() fires
        var rule = EscalationRule.Create(
            workflowDefinitionId: definitionId,
            stateId: null,                      // null = applies to any state
            escalationAfterMinutes: escalationAfterMinutes,
            escalateTo: EscalationTarget.SpecificUser,
            escalateToValue: escalateToUserId.ToString(),
            notifyVia: string.Empty,
            tenantId: tenantId,
            createdBy: createdBy);

        db.EscalationRules.Add(rule);
        await db.SaveChangesAsync();

        return (rule.Id, escalateToUserId);
    }

    /// <summary>
    /// Creates and returns a job SUT directly constructed from DI-resolved deps + capturing logger.
    /// Bypasses Hangfire DI registration question (R6 in planner risk table).
    /// </summary>
    private EscalationCheckJob CreateJobSut(IServiceProvider sp, CapturingLogger<EscalationCheckJob> logger)
    {
        var db  = sp.GetRequiredService<WorkflowDbContext>();
        var dbc = sp.GetRequiredService<IReadDbConnection>();
        return new EscalationCheckJob(dbc, db, logger);
    }

    // ── A1: pool task steal-fix proof ────────────────────────────────────────────

    /// <summary>
    /// A1 — RT2-7 steal-fix.
    /// A pool task (AssigneeId=null) that is overdue and has a matching escalation rule
    /// MUST NOT be stolen: AssigneeId stays null and no TaskAssignment ledger row is written.
    ///
    /// The escalation rule uses a parsable Guid as EscalateToValue so that — without the SQL
    /// filter AND the belt-and-braces guard — task.Assign() WOULD have been called, changing
    /// AssigneeId from null to the escalation target. This makes the post-assert non-tautological
    /// (S5 META-L (c) invariant).
    ///
    /// Expected state BEFORE Edit 1+2: AssigneeId set → FAIL (RED proves test catches the bug).
    /// Expected state AFTER Edit 1+2:  AssigneeId null → PASS (GREEN).
    /// </summary>
    [Fact]
    public async Task PoolTask_OverdueAndEscalationRuleMatches_StaysUnclaimedAndNoEscalationLedger()
    {
        // ── Arrange ─────────────────────────────────────────────────────────────
        var tenantId  = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        var (definitionId, _, instanceId) = await SeedDefinitionAndInstanceAsync(tenantId, createdBy);

        // Pool task: AssigneeId=null, 90 min ago (well past 30-min rule threshold), DueDate overdue
        var taskId = await SeedTaskAndBackdateAsync(
            workflowInstanceId: instanceId,
            tenantId: tenantId,
            createdBy: createdBy,
            assigneeId: null,              // ← pool task
            createdAtMinutesAgo: 90,
            dueDateMinutesAgo: 60);        // DueDate 60 min ago → overdue

        // EscalationRule: active, threshold=30min, EscalateToValue=parsable Guid
        // (Guid.TryParse succeeds → escalateToId != Guid.Empty → task.Assign() WOULD fire without fix)
        var (_, escalateToUserId) = await SeedEscalationRuleAsync(
            definitionId: definitionId,
            tenantId: tenantId,
            createdBy: createdBy,
            escalationAfterMinutes: 30);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var logger = new CapturingLogger<EscalationCheckJob>();
        var sut = CreateJobSut(scope.ServiceProvider, logger);

        // ── PRECONDITION asserts (S5 META-L (c): pin WHICH code path fires) ────
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var taskBefore = await db.WorkflowTasks.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId);
        taskBefore.AssigneeId.Should().BeNull(
            "precondition: task is a pool task before job runs");

        // Verify rule WOULD have fired on a single-assigned task (EscalationAfterMinutes < MinutesElapsed)
        var ruleBefore = await db.EscalationRules.IgnoreQueryFilters()
            .FirstAsync(r => r.WorkflowDefinitionId == definitionId);
        ruleBefore.IsActive.Should().BeTrue(
            "precondition: escalation rule must be active");
        ruleBefore.EscalationAfterMinutes.Should().Be(30,
            "precondition: rule threshold 30 min < 90 min elapsed → WOULD match");
        Guid.TryParse(ruleBefore.EscalateToValue, out var preParseResult).Should().BeTrue(
            "precondition: EscalateToValue must be parsable Guid so task.Assign() WOULD fire without steal-fix");
        preParseResult.Should().NotBe(Guid.Empty,
            "precondition: escalateToId != Guid.Empty → task.Assign() branch WOULD execute without steal-fix");

        var ledgerCountBefore = await db.TaskAssignments.IgnoreQueryFilters()
            .CountAsync(a => a.WorkflowTaskId == taskId && a.Type == TaskAssignmentType.Escalation);
        ledgerCountBefore.Should().Be(0, "precondition: no escalation ledger rows before job runs");

        // ── Act ─────────────────────────────────────────────────────────────────
        await sut.ExecuteAsync(CancellationToken.None);

        // ── Assert (steal-fix) ───────────────────────────────────────────────────
        // Re-query on a FRESH context (EF tracking may cache stale state)
        await using var assertScope = _fixture.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var taskAfter = await assertDb.WorkflowTasks.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId);
        taskAfter.AssigneeId.Should().BeNull(
            "RT2-7 steal-fix: pool task must NOT be stolen by EscalationCheckJob");
        taskAfter.AssigneeId.Should().NotBe(escalateToUserId,
            "RT2-7: AssigneeId must not equal the rule's escalation target");

        var ledgerCountAfter = await assertDb.TaskAssignments.IgnoreQueryFilters()
            .CountAsync(a => a.WorkflowTaskId == taskId && a.Type == TaskAssignmentType.Escalation);
        ledgerCountAfter.Should().Be(0,
            "RT2-7: pool task must not receive a TaskAssignment.Escalation ledger row");

        // Logger: no "Escalated task {taskId} " message for this pool task.
        // Use trailing space in the pattern to avoid matching "Escalated task 1..." when other
        // tests have seeded tasks with overlapping IDs in the shared DB.
        var escalationLogEntries = logger.Entries
            .Where(e => e.Level == LogLevel.Information
                     && e.Message.Contains($"Escalated task {taskId} "))
            .ToList();
        escalationLogEntries.Should().BeEmpty(
            "RT2-7: no escalation log should be emitted for pool task");
    }

    // ── A2: single-assigned regression bar ──────────────────────────────────────

    /// <summary>
    /// A2 — single-assigned escalation path regression bar.
    /// A task with AssigneeId != null that is overdue and has a matching escalation rule
    /// MUST still be escalated (Edit 1+2 must not break the existing path).
    ///
    /// PRECONDITION asserts verify:
    ///   - task.AssigneeId != null (single-assigned, not pool)
    ///   - rule.IsActive, rule threshold passes, EscalateToValue parsable
    ///   - no pre-existing TaskAssignment ledger row
    /// </summary>
    [Fact]
    public async Task SingleAssignedTask_OverdueAndEscalationRuleMatches_EscalatesNormally()
    {
        // ── Arrange ─────────────────────────────────────────────────────────────
        var tenantId  = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var initialAssignee = Guid.NewGuid();

        var (definitionId, _, instanceId) = await SeedDefinitionAndInstanceAsync(tenantId, createdBy);

        var taskId = await SeedTaskAndBackdateAsync(
            workflowInstanceId: instanceId,
            tenantId: tenantId,
            createdBy: createdBy,
            assigneeId: initialAssignee,   // ← single-assigned
            createdAtMinutesAgo: 90,
            dueDateMinutesAgo: 60);

        var (_, escalateToUserId) = await SeedEscalationRuleAsync(
            definitionId: definitionId,
            tenantId: tenantId,
            createdBy: createdBy,
            escalationAfterMinutes: 30);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var logger = new CapturingLogger<EscalationCheckJob>();
        var sut = CreateJobSut(scope.ServiceProvider, logger);

        // ── PRECONDITION asserts ─────────────────────────────────────────────────
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var taskBefore = await db.WorkflowTasks.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId);
        taskBefore.AssigneeId.Should().NotBeNull("precondition: task is single-assigned");
        taskBefore.AssigneeId.Should().Be(initialAssignee, "precondition: task has expected initial assignee");

        var ruleBefore = await db.EscalationRules.IgnoreQueryFilters()
            .FirstAsync(r => r.WorkflowDefinitionId == definitionId);
        ruleBefore.IsActive.Should().BeTrue("precondition: rule is active");
        ruleBefore.EscalationAfterMinutes.Should().Be(30, "precondition: rule threshold < elapsed minutes");
        Guid.TryParse(ruleBefore.EscalateToValue, out _).Should().BeTrue(
            "precondition: EscalateToValue parsable → escalateToId != Guid.Empty → task.Assign() fires");

        var ledgerCountBefore = await db.TaskAssignments.IgnoreQueryFilters()
            .CountAsync(a => a.WorkflowTaskId == taskId && a.Type == TaskAssignmentType.Escalation);
        ledgerCountBefore.Should().Be(0, "precondition: no escalation ledger rows before job runs");

        // ── Act ─────────────────────────────────────────────────────────────────
        await sut.ExecuteAsync(CancellationToken.None);

        // ── Assert (regression bar) ───────────────────────────────────────────────
        await using var assertScope = _fixture.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        // TaskAssignment ledger row must exist
        var ledgerCountAfter = await assertDb.TaskAssignments.IgnoreQueryFilters()
            .CountAsync(a => a.WorkflowTaskId == taskId && a.Type == TaskAssignmentType.Escalation);
        ledgerCountAfter.Should().Be(1,
            "A2 regression: escalation must create exactly 1 TaskAssignment.Escalation row for single-assigned task");

        var assignment = await assertDb.TaskAssignments.IgnoreQueryFilters()
            .FirstAsync(a => a.WorkflowTaskId == taskId && a.Type == TaskAssignmentType.Escalation);
        assignment.AssignedById.Should().Be(Guid.Empty, "system actor id for escalation = Guid.Empty");
        assignment.AssignedToId.Should().Be(escalateToUserId, "escalation target must match EscalateToValue");

        // AssigneeId updated to escalation target
        var taskAfter = await assertDb.WorkflowTasks.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId);
        taskAfter.AssigneeId.Should().Be(escalateToUserId,
            "A2 regression: task.Assign() must have been called for single-assigned task");

        // Log message emitted for escalation. Use trailing-space boundary pattern
        // to avoid substring false positives in the shared DB environment.
        var escalationLogEntries = logger.Entries
            .Where(e => e.Level == LogLevel.Information
                     && e.Message.Contains($"Escalated task {taskId} "))
            .ToList();
        escalationLogEntries.Should().HaveCount(1,
            "A2 regression: exactly 1 escalation log entry expected for single-assigned task");
    }

    // ── A3: aging pass emits structured log warning ──────────────────────────────

    /// <summary>
    /// A3 — RT3-M3 pool-aging: a pool task unclaimed for &gt;= 60 min emits exactly 1 LogWarning.
    /// No EscalationRule needed (aging is independent of escalation pass).
    /// No TaskAssignment written. AssigneeId stays null. No DB mutation.
    ///
    /// Expected state BEFORE aging code added: no LogWarning → FAIL (RED proves test catches absence).
    /// Expected state AFTER Edit 2 (aging pass added): LogWarning emitted → PASS (GREEN).
    /// </summary>
    [Fact]
    public async Task PoolTask_OverduePastAgingThreshold_EmitsAgingLogWarning_NoLedger_NoAssign()
    {
        // ── Arrange ─────────────────────────────────────────────────────────────
        var tenantId  = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        var (_, _, instanceId) = await SeedDefinitionAndInstanceAsync(tenantId, createdBy);

        // Pool task 90 min old (past 60-min threshold). No DueDate → aging measured from CreatedAt only.
        var taskId = await SeedTaskAndBackdateAsync(
            workflowInstanceId: instanceId,
            tenantId: tenantId,
            createdBy: createdBy,
            assigneeId: null,
            createdAtMinutesAgo: 90,      // 90 min > 60 min threshold → aging MUST fire
            dueDateMinutesAgo: null);     // DueDate null → task NOT in escalation pass scope

        await using var scope = _fixture.Services.CreateAsyncScope();
        var logger = new CapturingLogger<EscalationCheckJob>();
        var sut = CreateJobSut(scope.ServiceProvider, logger);

        // ── PRECONDITION asserts ─────────────────────────────────────────────────
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var taskBefore = await db.WorkflowTasks.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId);
        taskBefore.AssigneeId.Should().BeNull("precondition: task is a pool task");
        (DateTime.UtcNow - taskBefore.CreatedAt).TotalMinutes.Should().BeGreaterThan(60.0,
            "precondition: task is older than 60 min → aging threshold WILL be exceeded by SQL predicate");

        // No EscalationRule for this definition — proves aging fires independently of escalation
        var instanceForTask = await db.WorkflowInstances.IgnoreQueryFilters()
            .FirstAsync(i => i.Id == instanceId);
        var escalationRuleCount = await db.EscalationRules.IgnoreQueryFilters()
            .CountAsync(r => r.WorkflowDefinitionId == instanceForTask.DefinitionId);
        escalationRuleCount.Should().Be(0,
            "precondition: no EscalationRule for this definition → aging fires independently");

        var agingLogCountPreAct = logger.Entries
            .Count(e => e.Level == LogLevel.Warning && e.Message.Contains("Pool task"));
        agingLogCountPreAct.Should().Be(0, "precondition: no aging log entries before job runs");

        // ── Act ─────────────────────────────────────────────────────────────────
        await sut.ExecuteAsync(CancellationToken.None);

        // ── Assert (aging proof) ─────────────────────────────────────────────────
        // Use "Pool task {id} " (trailing space) to avoid substring false positives from short IDs.
        var agingLogEntries = logger.Entries
            .Where(e => e.Level == LogLevel.Warning
                     && e.Message.Contains($"Pool task {taskId} "))
            .ToList();
        agingLogEntries.Should().HaveCount(1,
            "RT3-M3: exactly 1 LogWarning must be emitted for the aged pool task");

        var logMessage = agingLogEntries[0].Message;
        logMessage.Should().Contain("unclaimed for",
            "RT3-M3: log message must contain 'unclaimed for' structured text");
        logMessage.Should().Contain("60 minutes",
            "RT3-M3: log message must contain the threshold value '60 minutes'");

        // No DB side-effects
        await using var assertScope = _fixture.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var ledgerCount = await assertDb.TaskAssignments.IgnoreQueryFilters()
            .CountAsync(a => a.WorkflowTaskId == taskId);
        ledgerCount.Should().Be(0, "RT3-M3: aging must write zero TaskAssignment rows");

        var taskAfter = await assertDb.WorkflowTasks.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId);
        taskAfter.AssigneeId.Should().BeNull("RT3-M3: aging must not assign the task");
        taskAfter.UpdatedAt.Should().BeNull("RT3-M3: aging must not trigger any audit-update side-effect");
    }

    // ── A4: aging below threshold is no-op ───────────────────────────────────────

    /// <summary>
    /// A4 — below-threshold no-op.
    /// A pool task unclaimed for &lt; 60 min emits no aging LogWarning.
    ///
    /// PRECONDITION asserts verify task.CreatedAt is recent enough to be below threshold.
    /// Expected: GREEN immediately (SQL predicate excludes it); no prod change needed for this test.
    /// </summary>
    [Fact]
    public async Task PoolTask_OverdueButBelowAgingThreshold_EmitsNoAgingLog()
    {
        // ── Arrange ─────────────────────────────────────────────────────────────
        var tenantId  = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        var (_, _, instanceId) = await SeedDefinitionAndInstanceAsync(tenantId, createdBy);

        // Pool task only 30 min old — BELOW 60-min aging threshold
        var taskId = await SeedTaskAndBackdateAsync(
            workflowInstanceId: instanceId,
            tenantId: tenantId,
            createdBy: createdBy,
            assigneeId: null,
            createdAtMinutesAgo: 30,      // 30 min < 60 min → aging SQL predicate excludes it
            dueDateMinutesAgo: null);     // no DueDate → not in escalation scope either

        await using var scope = _fixture.Services.CreateAsyncScope();
        var logger = new CapturingLogger<EscalationCheckJob>();
        var sut = CreateJobSut(scope.ServiceProvider, logger);

        // ── PRECONDITION asserts ─────────────────────────────────────────────────
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var taskBefore = await db.WorkflowTasks.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId);
        taskBefore.AssigneeId.Should().BeNull("precondition: task is a pool task");
        (DateTime.UtcNow - taskBefore.CreatedAt).TotalMinutes.Should().BeLessThan(60.0,
            "precondition: task is younger than 60 min → aging threshold NOT exceeded");

        // ── Act ─────────────────────────────────────────────────────────────────
        await sut.ExecuteAsync(CancellationToken.None);

        // ── Assert ───────────────────────────────────────────────────────────────
        // Use "Pool task {id} " exact-boundary filter — short IDs are substrings of UUIDs in message body.
        var agingLogForTask = logger.Entries
            .Where(e => e.Level == LogLevel.Warning
                     && e.Message.Contains($"Pool task {taskId} "))
            .ToList();
        agingLogForTask.Should().BeEmpty(
            "A4: pool task below aging threshold must emit no aging LogWarning");

        await using var assertScope = _fixture.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var ledgerCount = await assertDb.TaskAssignments.IgnoreQueryFilters()
            .CountAsync(a => a.WorkflowTaskId == taskId);
        ledgerCount.Should().Be(0, "A4: no TaskAssignment row for below-threshold pool task");

        var taskAfter = await assertDb.WorkflowTasks.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == taskId);
        taskAfter.AssigneeId.Should().BeNull("A4: AssigneeId unchanged for below-threshold pool task");
    }

    // ── A5: co-existence — pool + single-assigned in same run ────────────────────

    /// <summary>
    /// A5 — co-existence smoke test.
    /// Both passes run in the same job invocation on different tasks without crossover:
    ///   - Pool task P4 (AssigneeId=null, 90 min old, no DueDate): aging LogWarning emitted, no ledger.
    ///   - Single-assigned task S2 (AssigneeId=u2, overdue DueDate): escalated, 1 ledger row, AssigneeId changed.
    /// Proves the two passes are disjoint as designed (RT2-7 + RT3-M3 orthogonality).
    /// </summary>
    [Fact]
    public async Task BothPasses_PoolAndSingleAssigned_ActOnRespectiveTargetsWithoutCrossover()
    {
        // ── Arrange ─────────────────────────────────────────────────────────────
        var tenantId  = Guid.NewGuid();
        var createdBy = Guid.NewGuid();
        var u2        = Guid.NewGuid(); // assignee for single-assigned task

        var (definitionId, _, instanceId) = await SeedDefinitionAndInstanceAsync(tenantId, createdBy);

        // Task P4: pool (null), 90 min old, no DueDate — only aging pass touches this
        var poolTaskId = await SeedTaskAndBackdateAsync(
            workflowInstanceId: instanceId,
            tenantId: tenantId,
            createdBy: createdBy,
            assigneeId: null,
            createdAtMinutesAgo: 90,
            dueDateMinutesAgo: null);

        // Task S2: single-assigned (u2), 90 min old, overdue — only escalation pass touches this
        var singleTaskId = await SeedTaskAndBackdateAsync(
            workflowInstanceId: instanceId,
            tenantId: tenantId,
            createdBy: createdBy,
            assigneeId: u2,
            createdAtMinutesAgo: 90,
            dueDateMinutesAgo: 60);

        // EscalationRule: covers this definition (fires for S2, must NOT fire for P4)
        var (_, escalateToUserId) = await SeedEscalationRuleAsync(
            definitionId: definitionId,
            tenantId: tenantId,
            createdBy: createdBy,
            escalationAfterMinutes: 30);

        await using var scope = _fixture.Services.CreateAsyncScope();
        var logger = new CapturingLogger<EscalationCheckJob>();
        var sut = CreateJobSut(scope.ServiceProvider, logger);

        // ── PRECONDITION asserts ─────────────────────────────────────────────────
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var poolBefore = await db.WorkflowTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == poolTaskId);
        poolBefore.AssigneeId.Should().BeNull("precondition: P4 is a pool task");

        var singleBefore = await db.WorkflowTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == singleTaskId);
        singleBefore.AssigneeId.Should().Be(u2, "precondition: S2 is single-assigned to u2");

        var rule = await db.EscalationRules.IgnoreQueryFilters()
            .FirstAsync(r => r.WorkflowDefinitionId == definitionId);
        rule.IsActive.Should().BeTrue("precondition: rule is active");

        // ── Act ─────────────────────────────────────────────────────────────────
        await sut.ExecuteAsync(CancellationToken.None);

        // ── Assert ───────────────────────────────────────────────────────────────
        await using var assertScope = _fixture.Services.CreateAsyncScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        // P4 (pool): no steal, no ledger row, aging LogWarning emitted
        var poolAfter = await assertDb.WorkflowTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == poolTaskId);
        poolAfter.AssigneeId.Should().BeNull("A5: pool task P4 must NOT be stolen");

        var poolLedger = await assertDb.TaskAssignments.IgnoreQueryFilters()
            .CountAsync(a => a.WorkflowTaskId == poolTaskId && a.Type == TaskAssignmentType.Escalation);
        poolLedger.Should().Be(0, "A5: no escalation ledger row for pool task P4");

        // Use "Pool task {id} " (with trailing space) to avoid substring-match false positives
        // (e.g., id=1 matching "Pool task 10" or a UUID containing "1" in the message body).
        var poolAgingLog = logger.Entries
            .Where(e => e.Level == LogLevel.Warning
                     && e.Message.Contains($"Pool task {poolTaskId} "))
            .ToList();
        poolAgingLog.Should().HaveCount(1,
            "A5: exactly 1 aging LogWarning for pool task P4 (90 min old, past threshold)");

        // S2 (single-assigned): escalated, 1 ledger row, AssigneeId changed
        var singleLedger = await assertDb.TaskAssignments.IgnoreQueryFilters()
            .CountAsync(a => a.WorkflowTaskId == singleTaskId && a.Type == TaskAssignmentType.Escalation);
        singleLedger.Should().Be(1, "A5: exactly 1 escalation ledger row for single-assigned task S2");

        var singleAfter = await assertDb.WorkflowTasks.IgnoreQueryFilters().FirstAsync(t => t.Id == singleTaskId);
        singleAfter.AssigneeId.Should().Be(escalateToUserId, "A5: S2 AssigneeId must be reassigned to escalation target");

        // No aging LogWarning for S2 (assigned task → not in aging SQL scope).
        // Use "Pool task {id} " for exact-boundary match (same reasoning as poolAgingLog filter above).
        var singleAgingLog = logger.Entries
            .Where(e => e.Level == LogLevel.Warning
                     && e.Message.Contains($"Pool task {singleTaskId} "))
            .ToList();
        singleAgingLog.Should().BeEmpty("A5: no aging log for single-assigned task S2 (it has an assignee)");
    }
}
