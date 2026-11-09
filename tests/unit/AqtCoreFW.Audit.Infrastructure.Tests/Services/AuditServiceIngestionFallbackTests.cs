using System.Net;
using AqtCoreFW.Audit.Application.Services;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Audit.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AqtCoreFW.Audit.Infrastructure.Tests.Services;

/// <summary>
/// Guards the cell #7 L1 fix: a synchronous audit-write failure on the standalone request path must
/// NOT be silently lost — <see cref="AuditService"/> must hand a fully-captured intent to the durable
/// Hangfire retry lane (<see cref="IBackgroundAuditJobEnqueuer.EnqueueIngestionRetry"/>) and swallow,
/// keeping the pipeline fail-open. These are pure wiring tests (no DB): the persist is forced to fail
/// by a repository substitute that throws, and the assertions ride the real
/// <see cref="AuditEntryPersister"/> path AuditService uses in production.
///
/// The A/B pair (persist fails → enqueue; persist succeeds → no enqueue) proves the tests are
/// non-vacuous: the fallback fires ONLY on failure, not unconditionally.
/// </summary>
public sealed class AuditServiceIngestionFallbackTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static (AuditService sut, IAuditLogRepository repo, IBackgroundAuditJobEnqueuer enqueuer)
        CreateSut()
    {
        var repo = Substitute.For<IAuditLogRepository>();
        var uow = Substitute.For<ICrossModuleUnitOfWork>();
        uow.IsActive.Returns(false); // standalone request path — the one that gets the durable fallback
        var outbox = Substitute.For<IOutboxWriter>();
        var enqueuer = Substitute.For<IBackgroundAuditJobEnqueuer>();
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        var persister = new AuditEntryPersister(repo, uow, outbox, enqueuer, gen);

        var httpCtx = new DefaultHttpContext();
        httpCtx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        httpCtx.TraceIdentifier = "trace-xyz";
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpCtx);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TenantId.Returns(TenantId);
        currentUser.UserId.Returns(UserId);
        currentUser.UserName.Returns("alice");

        // db is unused on the LogCommandAsync path (only LogPersonalDataAccess/LogLoginAttempt touch it).
        var sut = new AuditService(
            db: null!,
            jobEnqueuer: enqueuer,
            httpContextAccessor: accessor,
            currentUser: currentUser,
            uow: uow,
            guidGenerator: gen,
            persister: persister,
            logger: NullLogger<AuditService>.Instance);

        return (sut, repo, enqueuer);
    }

    [Fact]
    public async Task LogCommand_PersistFails_EnqueuesDurableRetry_WithCapturedAmbientState()
    {
        var (sut, repo, enqueuer) = CreateSut();
        repo.AddAsync(Arg.Any<Domain.Entities.AuditLogEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("audit store unavailable"));

        var act = async () => await sut.LogCommandAsync("Create", "Forms", "Template");

        // Fail-open: the swallow is preserved — but now the event is durable, not lost.
        await act.Should().NotThrowAsync();

        enqueuer.Received(1).EnqueueIngestionRetry(Arg.Is<AuditIngestionIntent>(i =>
            i.TenantId == TenantId &&
            i.UserId == UserId &&
            i.UserName == "alice" &&
            i.Action == "Create" &&
            i.ModuleName == "Forms" &&
            i.ResourceType == "Template" &&
            i.IpAddress == "203.0.113.7" &&
            i.CorrelationId == "trace-xyz" &&
            i.MessageId != Guid.Empty));
    }

    [Fact]
    public async Task LogCommand_PersistSucceeds_DoesNotEnqueueRetry()
    {
        // A/B non-vacuity: with no fault the sync path completes and the fallback must NOT fire.
        var (sut, _, enqueuer) = CreateSut();

        await sut.LogCommandAsync("Create", "Forms", "Template");

        enqueuer.DidNotReceive().EnqueueIngestionRetry(Arg.Any<AuditIngestionIntent>());
        // Normal path still enqueues the per-tenant HMAC sign.
        enqueuer.Received(1).EnqueueHmacChain(TenantId);
    }

    [Fact]
    public async Task LogCommand_PersistAndEnqueueBothFail_Rethrows()
    {
        // Double failure = genuine loss → AuditService must rethrow so AuditBehavior logs Critical
        // (the pipeline still does not break — AuditBehavior swallows at the top).
        var (sut, repo, enqueuer) = CreateSut();
        repo.AddAsync(Arg.Any<Domain.Entities.AuditLogEntry>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("audit store unavailable"));
        enqueuer.When(e => e.EnqueueIngestionRetry(Arg.Any<AuditIngestionIntent>()))
            .Do(_ => throw new InvalidOperationException("hangfire storage unavailable"));

        var act = async () => await sut.LogCommandAsync("Create", "Forms", "Template");

        await act.Should().ThrowAsync<AggregateException>();
    }
}
