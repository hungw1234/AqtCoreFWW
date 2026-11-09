using System.Transactions;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests.Repositories;

/// <summary>
/// H2 fix — exercises the REAL <c>WorkflowInstanceRepository.HasAmbientTransaction()</c> logic.
///
/// The production implementation at WorkflowInstanceRepository.cs:159 is:
///   <code>return Transaction.Current is not null || Ctx.Database.CurrentTransaction is not null;</code>
///
/// These tests verify BOTH OR-branches independently, using precondition asserts to prove
/// that the correct branch fired — not just that the method returned the expected boolean.
/// Without precondition asserts, any single-branch "simplification" of the production code
/// could still pass both tests (reproducing the S5 META-L (c) tautological-mock smell).
///
/// Fixture: same <see cref="WebAppFixture"/> + <see cref="SqlServerCollection"/> as
/// WorkflowInstanceBranchFanOutRollbackTests — uses the real SQL Server integration DB.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowInstanceRepositoryAmbientTransactionTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _fixture;

    public WorkflowInstanceRepositoryAmbientTransactionTests(WebAppFixture fixture)
    {
        _fixture = fixture;
    }

    // ── Fact 1: baseline — no scope, no db tx → false ───────────────────────────

    /// <summary>
    /// Neither <c>Transaction.Current</c> nor <c>Ctx.Database.CurrentTransaction</c> is active.
    /// HasAmbientTransaction must return false — baseline / no false-positive guard.
    /// </summary>
    [Fact]
    public async Task HasAmbientTransaction_NoScope_NoDbTx_ReturnsFalse()
    {
        // Arrange: resolve real repo + db from integration fixture — no tx active
        await using var scope = _fixture.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceRepository>();
        var db   = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        // Pre-condition: NEITHER tx branch is active — any true return would be a false-positive
        Transaction.Current.Should().BeNull(
            "no TransactionScope should be active before the call");
        db.Database.CurrentTransaction.Should().BeNull(
            "no EF db transaction should be active before the call");

        // Act + Assert
        repo.HasAmbientTransaction().Should().BeFalse(
            "no ambient transaction of any kind is active");
    }

    // ── Fact 2: TransactionScope path (Transaction.Current branch) ───────────────

    /// <summary>
    /// A <see cref="TransactionScope"/> is active → <c>Transaction.Current</c> is non-null.
    ///
    /// Critical: EF Core's <c>Ctx.Database.CurrentTransaction</c> remains NULL inside an
    /// MSDTC/TransactionScope until EF auto-enlists on first I/O. The precondition assert
    /// verifies that the <c>true</c> answer came from the <c>Transaction.Current</c> branch
    /// specifically — not from an EF enlistment side-effect.
    /// This is the scout §4 "must check BOTH" warning verified empirically.
    /// </summary>
    [Fact]
    public async Task HasAmbientTransaction_TransactionScopeActive_ReturnsTrue()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceRepository>();
        var db   = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        using var txScope = new TransactionScope(
            TransactionScopeOption.Required,
            TransactionScopeAsyncFlowOption.Enabled);

        // Pre-condition: Transaction.Current IS set but EF db tx is still null
        // (no EF I/O performed — auto-enlistment has NOT occurred)
        Transaction.Current.Should().NotBeNull(
            "TransactionScope must activate Transaction.Current before HasAmbientTransaction is called");
        db.Database.CurrentTransaction.Should().BeNull(
            "EF CurrentTransaction must be null before any I/O — proves Transaction.Current branch fires");

        // Act + Assert — must return true via Transaction.Current branch
        repo.HasAmbientTransaction().Should().BeTrue(
            "Transaction.Current is non-null → ambient tx active");

        // Dispose without Complete() — roll back the scope (no data touched)
    }

    // ── Fact 3: EF db transaction path (Ctx.Database.CurrentTransaction branch) ──

    /// <summary>
    /// An explicit EF <c>BeginTransactionAsync</c> is active → <c>Ctx.Database.CurrentTransaction</c>
    /// is non-null.
    ///
    /// Precondition assert verifies that the <c>true</c> answer came from the EF-db branch —
    /// not from a stray <c>TransactionScope</c> ambient tx. Confirms the second OR-clause
    /// of the production check works independently.
    /// </summary>
    [Fact]
    public async Task HasAmbientTransaction_DbContextTransactionActive_ReturnsTrue()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IWorkflowInstanceRepository>();
        var db   = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        await using var dbTx = await db.Database.BeginTransactionAsync();

        // Pre-condition: EF tx IS set but no ambient TransactionScope
        Transaction.Current.Should().BeNull(
            "no TransactionScope should wrap this test — proves Ctx.Database.CurrentTransaction branch fires");
        db.Database.CurrentTransaction.Should().NotBeNull(
            "EF CurrentTransaction must be non-null after BeginTransactionAsync");

        // Act + Assert — must return true via Ctx.Database.CurrentTransaction branch
        repo.HasAmbientTransaction().Should().BeTrue(
            "Ctx.Database.CurrentTransaction is non-null → ambient tx active");

        await dbTx.RollbackAsync(); // clean up
    }
}
