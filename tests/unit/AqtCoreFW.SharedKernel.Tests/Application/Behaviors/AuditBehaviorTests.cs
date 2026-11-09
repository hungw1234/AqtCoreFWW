using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Behaviors;
using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.SharedKernel.Tests.Application.Behaviors;

// Must be public/internal (not nested private) so NSubstitute can proxy ILogger<AuditBehavior<T,R>>
public sealed record AuditTestCommand : IBaseCommand, IRequest<Result>;
public sealed record AuditTestCommandWithDto : IBaseCommand, IRequest<Result<AuditTestDto>>;
public sealed record AuditTestDto(Guid Id, string Name);
public sealed record AuditTestDtoNoId(string Name);
public sealed record AuditTestCommandWithDtoNoId : IBaseCommand, IRequest<Result<AuditTestDtoNoId>>;
// Verb-coverage probes — every new verb added to CommandVerbs needs a paired test so we don't
// regress the "long type name gets truncated + resource = entire name" bug.
public sealed record ActivateProbeCommand : IBaseCommand, IRequest<Result>;
public sealed record DeactivateProbeCommand : IBaseCommand, IRequest<Result>;
// BP MVP P7-FU1 Phase B (D-B-4): "Replace" verb added to CommandVerbs
public sealed record ReplaceProbeCommand : IBaseCommand, IRequest<Result>;

[AuditCategory(AuditCategory.AppAccess)]
[SuppressAutoAudit]
public sealed record SuppressedAuditCommand : IBaseCommand, IRequest<Result>;

/// <summary>
/// Tests AuditBehavior: logs on success, skips failed results, skips suppressed commands,
/// never breaks pipeline on audit failure.
/// TC-SK-A005
/// </summary>
public sealed class AuditBehaviorTests
{
    private static (AuditBehavior<T, TResponse> sut, IAuditLogger auditLogger)
        CreateSut<T, TResponse>() where T : IBaseCommand
    {
        var auditLogger = Substitute.For<IAuditLogger>();
        // Use NullLogger to avoid Castle proxy accessibility issue with private nested types
        var logger = NullLogger<AuditBehavior<T, TResponse>>.Instance;
        var sut = new AuditBehavior<T, TResponse>(auditLogger, logger);
        return (sut, auditLogger);
    }

    [Fact]
    public async Task Handle_SuccessfulCommand_CallsAuditLogger()
    {
        // TC-SK-A005: AuditBehavior logs command execution on success
        var (sut, auditLogger) = CreateSut<AuditTestCommand, Result>();

        await sut.Handle(
            new AuditTestCommand(),
            ct => Task.FromResult(Result.Ok()),
            CancellationToken.None);

        await auditLogger.Received(1).LogCommandAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Type?>(),
            dataSnapshot: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FailedResult_DoesNotCallAuditLogger()
    {
        // Audit only logs success — failed commands are not audited
        var (sut, auditLogger) = CreateSut<AuditTestCommand, Result>();

        await sut.Handle(
            new AuditTestCommand(),
            ct => Task.FromResult(Result.Fail("something went wrong")),
            CancellationToken.None);

        await auditLogger.DidNotReceive().LogCommandAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Type?>(),
            dataSnapshot: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AuditLoggerThrows_DoesNotPropagateException()
    {
        // Audit failure must never crash the main pipeline
        var (sut, auditLogger) = CreateSut<AuditTestCommand, Result>();
        auditLogger
            .LogCommandAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<Type?>(), dataSnapshot: Arg.Any<string?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new Exception("audit store unavailable")));

        var act = async () => await sut.Handle(
            new AuditTestCommand(),
            ct => Task.FromResult(Result.Ok()),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_SuccessfulCommand_ReturnsOriginalResponse()
    {
        var (sut, _) = CreateSut<AuditTestCommand, Result>();

        var result = await sut.Handle(
            new AuditTestCommand(),
            ct => Task.FromResult(Result.Ok()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ResponseValueHasIdProperty_PassesResourceIdToLogger()
    {
        // F2: When response is Result<TDto> and TDto has an Id property, the behavior
        // should extract it via reflection and propagate it as resourceId.
        var (sut, auditLogger) = CreateSut<AuditTestCommandWithDto, Result<AuditTestDto>>();
        var expectedId = Guid.NewGuid();

        await sut.Handle(
            new AuditTestCommandWithDto(),
            ct => Task.FromResult(Result.Ok(new AuditTestDto(expectedId, "n"))),
            CancellationToken.None);

        await auditLogger.Received(1).LogCommandAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<string?>(s => s == expectedId.ToString()),
            Arg.Is<Type?>(t => t == typeof(AuditTestCommandWithDto)),
            dataSnapshot: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ResponseValueWithoutIdProperty_PassesNullResourceIdToLogger()
    {
        // F2: When response Value has no Id property, resourceId should be null (best-effort).
        var (sut, auditLogger) = CreateSut<AuditTestCommandWithDtoNoId, Result<AuditTestDtoNoId>>();

        await sut.Handle(
            new AuditTestCommandWithDtoNoId(),
            ct => Task.FromResult(Result.Ok(new AuditTestDtoNoId("n"))),
            CancellationToken.None);

        await auditLogger.Received(1).LogCommandAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<string?>(s => s == null),
            Arg.Is<Type?>(t => t == typeof(AuditTestCommandWithDtoNoId)),
            dataSnapshot: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SuppressedCommand_DoesNotCallAuditLogger()
    {
        // [SuppressAutoAudit] commands skip AuditBehavior — handler emits structured audit.
        var (sut, auditLogger) = CreateSut<SuppressedAuditCommand, Result>();

        await sut.Handle(
            new SuppressedAuditCommand(),
            ct => Task.FromResult(Result.Ok()),
            CancellationToken.None);

        await auditLogger.DidNotReceive().LogCommandAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<Type?>(),
            dataSnapshot: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActivateVerb_LogsActionActivate_ResourceWithoutVerbPrefix()
    {
        // Regression guard for BP MVP P4 Slice 2A-Write code-review M-1: without "Activate" in
        // CommandVerbs, action was the truncated 20-char "ActivateProbeCommand" prefix and resource
        // was the entire type name — both useless for compliance filtering.
        var (sut, auditLogger) = CreateSut<ActivateProbeCommand, Result>();

        await sut.Handle(new ActivateProbeCommand(), ct => Task.FromResult(Result.Ok()), CancellationToken.None);

        await auditLogger.Received(1).LogCommandAsync(
            Arg.Is<string>(a => a == "Activate"),
            Arg.Any<string>(),
            Arg.Is<string>(r => r == "Probe"),
            Arg.Any<string?>(),
            Arg.Any<Type?>(),
            dataSnapshot: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DeactivateVerb_LogsActionDeactivate_ResourceWithoutVerbPrefix()
    {
        var (sut, auditLogger) = CreateSut<DeactivateProbeCommand, Result>();

        await sut.Handle(new DeactivateProbeCommand(), ct => Task.FromResult(Result.Ok()), CancellationToken.None);

        await auditLogger.Received(1).LogCommandAsync(
            Arg.Is<string>(a => a == "Deactivate"),
            Arg.Any<string>(),
            Arg.Is<string>(r => r == "Probe"),
            Arg.Any<string?>(),
            Arg.Any<Type?>(),
            dataSnapshot: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReplaceVerb_LogsActionReplace_ResourceWithoutVerbPrefix()
    {
        // Regression guard for BP MVP P7-FU1 Phase B D-B-4: "Replace" added to CommandVerbs.
        // Without it, "ReplaceFormMapsCommand" would truncate to 20-char action + full type-name resource.
        var (sut, auditLogger) = CreateSut<ReplaceProbeCommand, Result>();

        await sut.Handle(new ReplaceProbeCommand(), ct => Task.FromResult(Result.Ok()), CancellationToken.None);

        await auditLogger.Received(1).LogCommandAsync(
            Arg.Is<string>(a => a == "Replace"),
            Arg.Any<string>(),
            Arg.Is<string>(r => r == "Probe"),
            Arg.Any<string?>(),
            Arg.Any<Type?>(),
            dataSnapshot: Arg.Any<string?>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }
}
