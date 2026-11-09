using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AqtCoreFW.Workflow.Infrastructure.Tests.Persistence;

/// <summary>
/// Phase 5b — TDD fact for <see cref="WorkflowInstanceRepository.Stage"/> semantics.
///
/// Verifies the new repo primitive (mirror of <c>IWorkflowTaskRepository.Stage</c>) introduced
/// by Phase 5b to escape <c>GenericRepository.AddAsync</c>'s auto-save behavior.
/// <list type="bullet">
///   <item>Stage → EntityState.Added (enrolled in ChangeTracker)</item>
///   <item>Stage → DbContext has buffered Adds (ChangeTracker.HasChanges() == true)</item>
///   <item>SaveChangesAsync → ChangeTracker drains (Added → Unchanged)</item>
/// </list>
/// Uses EF Core in-memory provider — sufficient for Stage semantics (ChangeTracker behavior is
/// provider-agnostic). FK / SQL-constraint testing is out of scope for this fact (S7 invariant
/// proven; production integration covered separately by I1-I4 integration tests).
/// </summary>
public sealed class WorkflowInstanceRepositoryStageTests
{
    private static readonly Guid TestTenantId = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TestCreatedBy = new("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task Stage_AddsToChangeTracker_DoesNotPersistUntilSaveChangesAsync()
    {
        // Arrange: in-memory EF context (provider-agnostic ChangeTracker behavior)
        var options = new DbContextOptionsBuilder<WorkflowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns((Guid?)TestTenantId);
        await using var ctx = new WorkflowDbContext(options, tenantCtx);

        var repo = new WorkflowInstanceRepository(ctx);
        var gen = Helpers.TestGuidGenerator.Create();

        var instance = WorkflowInstance.Create(
            gen,
            definitionId: 100L,
            initialStateId: 200L,
            "Case",
            Guid.NewGuid(),
            TestTenantId,
            TestCreatedBy);

        // Precondition: ChangeTracker is clean
        ctx.ChangeTracker.HasChanges().Should().BeFalse(
            "precondition — ChangeTracker must start clean to prove Stage is what enrolled the entity");

        // Act 1: Stage
        repo.Stage(instance);

        // Assert: EntityState == Added
        ctx.Entry(instance).State.Should().Be(EntityState.Added,
            "Stage must enroll the entity in EntityState.Added");

        // Assert: ChangeTracker has pending changes (Stage did NOT flush)
        ctx.ChangeTracker.HasChanges().Should().BeTrue(
            "Stage must NOT execute SaveChanges — caller owns the unified flush (S7 invariant)");

        // Anti-tautology: exactly 1 Added entry for WorkflowInstance (no accidental cascade adds)
        var addedInstances = ctx.ChangeTracker.Entries<WorkflowInstance>()
            .Count(e => e.State == EntityState.Added);
        addedInstances.Should().Be(1,
            "exactly one WorkflowInstance entry in Added state — no cascade or duplicate");

        // Act 2: SaveChangesAsync (via repository abstraction)
        var saveResult = await repo.SaveChangesAsync();

        // Assert: flush succeeded
        saveResult.IsSuccess.Should().BeTrue("SaveChangesAsync must succeed for a valid staged entity");

        // Assert: post-flush state Unchanged (Added → Unchanged after SaveChanges)
        ctx.Entry(instance).State.Should().Be(EntityState.Unchanged,
            "post-flush entity state must transition Added → Unchanged");

        // Assert: ChangeTracker drained
        ctx.ChangeTracker.HasChanges().Should().BeFalse(
            "SaveChangesAsync must drain pending changes");
    }
}
