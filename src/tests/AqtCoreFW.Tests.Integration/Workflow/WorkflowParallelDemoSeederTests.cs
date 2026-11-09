using System.Collections.Concurrent;
using AqtCoreFW.Api;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Phase 7 — the demo parallel-branch seeder produces an Active, fork-bearing definition.
///
/// Drives the real <see cref="E2ETestDataSeeder.SeedParallelWorkflowAsync"/> against a no-HttpContext DI
/// scope (the production seeder runs as an IHostedService with no request context) — proving the mediator
/// save-graph→activate path works under BackgroundJobTenantContext, the canonical-vocab graph passes the
/// branch-aware validator, and the ByPermission child pools survive RB5 activation. IsActive==true is the
/// load-bearing assertion: it is reachable only if BOTH save-graph and activate succeeded end-to-end.
/// </summary>
[Collection("Integration")]
public sealed class WorkflowParallelDemoSeederTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    [Fact]
    public async Task SeedParallelWorkflow_ProducesActiveAndForkDefinition_Idempotent()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var log = new CapturingLogger();
        // Run twice — the second call must hit the PublicId idempotency guard and create nothing new.
        await E2ETestDataSeeder.SeedParallelWorkflowAsync(sp, log, CancellationToken.None);
        await E2ETestDataSeeder.SeedParallelWorkflowAsync(sp, log, CancellationToken.None);

        var ctx = sp.GetRequiredService<WorkflowDbContext>();

        var defs = await ctx.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.PublicId == E2ETestDataSeeder.ParallelWorkflowDefId)
            .ToListAsync();
        defs.Should().ContainSingle("the seeder is idempotent — a second run must not duplicate the def");

        var def = defs[0];
        def.IsActive.Should().BeTrue(
            "save-graph + activate must both succeed through the branch-aware validator — IsActive proves the whole pipeline passed. Seeder log: "
            + string.Join(" | ", log.Messages));

        var branch = await ctx.ParallelBranches.IgnoreQueryFilters().AsNoTracking()
            .Include(b => b.Children)
            .FirstOrDefaultAsync(b => b.DefinitionId == def.Id);
        branch.Should().NotBeNull("the demo def must persist a parallel branch, not just a plain definition");
        branch!.BranchType.Should().Be(ParallelBranchType.And, "the demo fork is an AND join");
        branch.Children.Should().HaveCount(2, "the AND fork seeds two children (review-a, review-b)");
    }

    // Captures the seeder's log-and-continue error messages so a save/activate failure is surfaced
    // in the assertion message instead of being silently swallowed.
    private sealed class CapturingLogger : ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Messages.Enqueue(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
