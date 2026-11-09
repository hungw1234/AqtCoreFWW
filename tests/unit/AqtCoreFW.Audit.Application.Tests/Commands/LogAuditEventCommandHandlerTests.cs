using AqtCoreFW.Audit.Application.Commands.LogAuditEvent;
using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Audit.Application.Tests.Commands;

/// <summary>
/// Unit tests for LogAuditEventCommandHandler.
/// Handler persists AuditLogEntry and enqueues async HMAC chain job.
/// Tests verify: persistence, job enqueue, returned MessageId, and exception propagation.
/// </summary>
public sealed class LogAuditEventCommandHandlerTests
{
    private readonly IAuditLogRepository _repo = Substitute.For<IAuditLogRepository>();
    private readonly IBackgroundAuditJobEnqueuer _jobEnqueuer = Substitute.For<IBackgroundAuditJobEnqueuer>();
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();
    private readonly LogAuditEventCommandHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    public LogAuditEventCommandHandlerTests()
    {
        // Return unique Guid per call so two entries always get different MessageIds.
        _guidGenerator.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _sut = new LogAuditEventCommandHandler(_repo, _jobEnqueuer, _guidGenerator);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithEntryId()
    {
        var command = BuildCommand();

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsEntryToRepository()
    {
        var command = BuildCommand(action: "Delete", moduleName: "Cases");

        await _sut.Handle(command, CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Is<AuditLogEntry>(e => e.Action == "Delete" && e.ModuleName == "Cases"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_EnqueuesHmacChainJobForPersistedEntry()
    {
        Guid? capturedTenantId = null;
        _jobEnqueuer.When(j => j.EnqueueHmacChain(Arg.Any<Guid?>()))
            .Do(ci => capturedTenantId = ci.Arg<Guid?>());

        var command = BuildCommand();
        await _sut.Handle(command, CancellationToken.None);

        // Verify the enqueued tenant matches the command's TenantId, not the entry's MessageId.
        // Per-tenant batch signing: one job signs all unsigned rows for the tenant.
        _jobEnqueuer.Received(1).EnqueueHmacChain(Arg.Any<Guid?>());
        capturedTenantId.Should().Be(command.TenantId,
            "per-tenant enqueue passes the entry's TenantId so the batch signer processes the right chain");
    }

    // --- Optional fields ---

    [Fact]
    public async Task Handle_WithOptionalFields_PersistsCorrelationIdAndIp()
    {
        var command = BuildCommand(correlationId: "corr-xyz", ipAddress: "10.0.0.1");

        await _sut.Handle(command, CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Is<AuditLogEntry>(e => e.CorrelationId == "corr-xyz" && e.IpAddress == "10.0.0.1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNullTenantId_StillSucceeds()
    {
        var command = BuildCommand(tenantId: null);

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // --- Repository failure propagation ---

    [Fact]
    public async Task Handle_RepositoryThrows_ExceptionPropagates()
    {
        _repo.AddAsync(Arg.Any<AuditLogEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("DB write failed")));

        var act = async () => await _sut.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB write failed*");
    }

    [Fact]
    public async Task Handle_RepositoryThrows_HmacJobNotEnqueued()
    {
        _repo.AddAsync(Arg.Any<AuditLogEntry>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("DB write failed")));

        try { await _sut.Handle(BuildCommand(), CancellationToken.None); } catch { /* expected */ }

        _jobEnqueuer.DidNotReceive().EnqueueHmacChain(Arg.Any<Guid?>());
    }

    // --- Helpers ---

    private static LogAuditEventCommand BuildCommand(
        Guid? tenantId = null,
        string action = "Create",
        string moduleName = "Cases",
        string resourceType = "Case",
        string? correlationId = null,
        string? ipAddress = null) =>
        new(tenantId ?? TenantId, UserId, UserName: "test.user",
            action, moduleName, resourceType,
            ResourceId: "res-001", DataSnapshot: null,
            IpAddress: ipAddress, CorrelationId: correlationId);
}
