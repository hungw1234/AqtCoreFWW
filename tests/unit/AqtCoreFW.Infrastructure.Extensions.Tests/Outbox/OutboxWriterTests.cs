using AqtCoreFW.Infrastructure.Persistence.Outbox;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Outbox;

/// <summary>
/// Unit tests for <see cref="OutboxWriter"/>'s guard clauses — the "must ride an active
/// transaction" and "schema must be registered" invariants. Both are pure control-flow checks
/// that fail BEFORE any SQL is issued, so they need no real connection/transaction.
///
/// The actual INSERT (row lands in the real DB, rides the CrossModuleUnitOfWork transaction,
/// rolls back with it) is proven by the Forms integration suite (real SQL Server via
/// WebAppFixture) — Dapper's CommandDefinition needs a genuine ADO.NET connection to execute
/// against, which an NSubstitute fake DbConnection cannot provide.
/// </summary>
public sealed class OutboxWriterTests
{
    private sealed record TestExternalEvent(Guid MessageId) : IExternalDomainEvent
    {
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    private readonly ICrossModuleUnitOfWork _uow = Substitute.For<ICrossModuleUnitOfWork>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly IOutboxSchemaRegistry _schemaRegistry = Substitute.For<IOutboxSchemaRegistry>();

    public OutboxWriterTests()
    {
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
    }

    private OutboxWriter BuildWriter() => new(_uow, _guidGen, _schemaRegistry);

    [Fact]
    public async Task EnqueueAsync_UowNotActive_ThrowsAndNeverTouchesRegistry()
    {
        _uow.IsActive.Returns(false);
        _schemaRegistry.Schemas.Returns(new[] { "forms" });

        var writer = BuildWriter();
        var evt = new TestExternalEvent(Guid.NewGuid());

        var act = async () => await writer.EnqueueAsync(evt, "forms", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active*");

        // Fail-fast on the "must be inside a transaction" invariant — schema allowlist is never
        // even consulted, proving the two checks are independent guard clauses in the right order.
        _ = _schemaRegistry.DidNotReceive().Schemas;
    }

    [Fact]
    public async Task EnqueueAsync_SchemaNotRegistered_Throws()
    {
        _uow.IsActive.Returns(true);
        _schemaRegistry.Schemas.Returns(new[] { "notifications" }); // "forms" NOT in the allowlist

        var writer = BuildWriter();
        var evt = new TestExternalEvent(Guid.NewGuid());

        var act = async () => await writer.EnqueueAsync(evt, "forms", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not registered*");
    }

    [Fact]
    public async Task EnqueueAsync_SchemaRegisteredCaseInsensitive_PassesSchemaGuard()
    {
        // Registry stores lower-cased names; caller may pass any casing — EnqueueAsync
        // normalizes before the containment check, so "Forms" must match "forms".
        _uow.IsActive.Returns(true);
        _schemaRegistry.Schemas.Returns(new[] { "forms" });

        var writer = BuildWriter();
        var evt = new TestExternalEvent(Guid.NewGuid());

        // Past the guard clauses, the writer attempts a real Dapper ExecuteAsync against
        // uow.Connection — which is null on this bare substitute, so it throws for an unrelated
        // reason (NullReferenceException), not the schema guard. That distinction IS the proof
        // the schema guard passed.
        var act = async () => await writer.EnqueueAsync(evt, "Forms", CancellationToken.None);

        await act.Should().NotThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task EnqueueAsync_NullEvent_ThrowsArgumentNullException()
    {
        _uow.IsActive.Returns(true);
        var writer = BuildWriter();

        var act = async () => await writer.EnqueueAsync(null!, "forms", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EnqueueAsync_BlankSchemaName_ThrowsArgumentException(string schemaName)
    {
        _uow.IsActive.Returns(true);
        var writer = BuildWriter();
        var evt = new TestExternalEvent(Guid.NewGuid());

        var act = async () => await writer.EnqueueAsync(evt, schemaName, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
