using AqtCoreFW.Audit.Application.Commands.ProcessRtbfRequest;
using NSubstitute.ExceptionExtensions;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Rtbf;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Application.Tests.Commands;

/// <summary>
/// Unit tests for ProcessRtbfRequestCommandHandler.
/// Orchestrates best-effort RTBF anonymization across modules (Law 91/2025 Art.9).
/// Handler always returns Result.Ok() — failures are recorded in RtbfRequest.FailureLog.
/// </summary>
public sealed class ProcessRtbfRequestCommandHandlerTests
{
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SubjectId = Guid.NewGuid();
    private static readonly Guid ProcessedBy = Guid.NewGuid();

    private readonly IRtbfRequestRepository _repo = Substitute.For<IRtbfRequestRepository>();
    private readonly ILogger<ProcessRtbfRequestCommandHandler> _logger =
        Substitute.For<ILogger<ProcessRtbfRequestCommandHandler>>();

    // Shared gen stub: unique Guid per call so two RtbfRequest instances have distinct MessageIds.
    private static IGuidGenerator NewGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return gen;
    }

    private ProcessRtbfRequestCommandHandler BuildSut(params IModulePiiAnonymizer[] anonymizers) =>
        new(_repo, anonymizers, _logger);

    private static ProcessRtbfRequestCommand BuildCommand() =>
        new(RequestId, ProcessedBy);

    // --- Not found ---

    [Fact]
    public async Task Handle_RequestNotFound_ReturnsNotFoundError()
    {
        _repo.GetByIdAsync(RequestId, default).Returns((RtbfRequest?)null);
        var sut = BuildSut();

        var result = await sut.Handle(BuildCommand(), default);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(RequestId.ToString()));
    }

    // --- All succeed ---

    [Fact]
    public async Task Handle_AllAnonymizersSucceed_CompletesRequestAndReturnsOk()
    {
        var rtbf = RtbfRequest.Create(NewGen(), TenantId, SubjectId);
        _repo.GetByIdAsync(RequestId, default).Returns(rtbf);

        var anonymizerA = Substitute.For<IModulePiiAnonymizer>();
        anonymizerA.ModuleName.Returns("ModA");
        anonymizerA.AnonymizeAsync(SubjectId, TenantId, null, default)
            .Returns(RtbfAnonymizationResult.Ok("ModA", 5));

        var anonymizerB = Substitute.For<IModulePiiAnonymizer>();
        anonymizerB.ModuleName.Returns("ModB");
        anonymizerB.AnonymizeAsync(SubjectId, TenantId, null, default)
            .Returns(RtbfAnonymizationResult.Ok("ModB", 3));

        var sut = BuildSut(anonymizerA, anonymizerB);

        var result = await sut.Handle(BuildCommand(), default);

        result.IsSuccess.Should().BeTrue();
        rtbf.Status.Should().Be(RtbfStatus.Completed);
        rtbf.ProcessedBy.Should().Be(ProcessedBy);
        rtbf.FailureLog.Should().BeNull();
        await _repo.Received(2).SaveChangesAsync(default); // StartProcessing + Complete
    }

    // --- Partial failure via Fail result ---

    [Fact]
    public async Task Handle_AnonymizerReturnsFailResult_PartiallyCompletesAndReturnsOk()
    {
        var rtbf = RtbfRequest.Create(NewGen(), TenantId, SubjectId);
        _repo.GetByIdAsync(RequestId, default).Returns(rtbf);

        var failAnonymizer = Substitute.For<IModulePiiAnonymizer>();
        failAnonymizer.ModuleName.Returns("Cases");
        failAnonymizer.AnonymizeAsync(SubjectId, TenantId, null, default)
            .Returns(RtbfAnonymizationResult.Fail("Cases", "DB timeout"));

        var sut = BuildSut(failAnonymizer);

        var result = await sut.Handle(BuildCommand(), default);

        result.IsSuccess.Should().BeTrue();
        rtbf.Status.Should().Be(RtbfStatus.PartiallyCompleted);
        rtbf.FailureLog.Should().Contain("Cases").And.Contain("DB timeout");
    }

    // --- Partial failure via exception ---

    [Fact]
    public async Task Handle_AnonymizerThrowsException_PartiallyCompletesAndReturnsOk()
    {
        var rtbf = RtbfRequest.Create(NewGen(), TenantId, SubjectId);
        _repo.GetByIdAsync(RequestId, default).Returns(rtbf);

        var crashAnonymizer = Substitute.For<IModulePiiAnonymizer>();
        crashAnonymizer.ModuleName.Returns("Identity");
        crashAnonymizer.AnonymizeAsync(SubjectId, TenantId, null, default)
            .Throws(new InvalidOperationException("Connection lost"));

        var sut = BuildSut(crashAnonymizer);

        var result = await sut.Handle(BuildCommand(), default);

        result.IsSuccess.Should().BeTrue();
        rtbf.Status.Should().Be(RtbfStatus.PartiallyCompleted);
        rtbf.FailureLog.Should().Contain("Identity").And.Contain("Connection lost");
    }

    // --- No anonymizers ---

    [Fact]
    public async Task Handle_NoAnonymizersRegistered_CompletesWithZeroRecordsAndReturnsOk()
    {
        var rtbf = RtbfRequest.Create(NewGen(), TenantId, SubjectId);
        _repo.GetByIdAsync(RequestId, default).Returns(rtbf);
        var sut = BuildSut(); // empty anonymizer list

        var result = await sut.Handle(BuildCommand(), default);

        result.IsSuccess.Should().BeTrue();
        rtbf.Status.Should().Be(RtbfStatus.Completed);
    }

    // --- Multiple failures collected ---

    [Fact]
    public async Task Handle_MultipleModulesFail_FailureLogContainsAllModuleNames()
    {
        var rtbf = RtbfRequest.Create(NewGen(), TenantId, SubjectId);
        _repo.GetByIdAsync(RequestId, default).Returns(rtbf);

        IModulePiiAnonymizer MakeFail(string name, string error)
        {
            var a = Substitute.For<IModulePiiAnonymizer>();
            a.ModuleName.Returns(name);
            a.AnonymizeAsync(default, default, null, default)
                .ReturnsForAnyArgs(RtbfAnonymizationResult.Fail(name, error));
            return a;
        }

        var sut = BuildSut(MakeFail("Forms", "err1"), MakeFail("Audit", "err2"));

        await sut.Handle(BuildCommand(), default);

        rtbf.FailureLog.Should().Contain("Forms").And.Contain("Audit");
        rtbf.FailureLog.Should().Contain("err1").And.Contain("err2");
    }

    // --- CitizenNationalId forwarded ---

    [Fact]
    public async Task Handle_CitizenNationalIdPresent_ForwardsToAllAnonymizers()
    {
        const string nid = "123456789012";
        var rtbf = RtbfRequest.Create(NewGen(), TenantId, SubjectId, citizenNationalId: nid);
        _repo.GetByIdAsync(RequestId, default).Returns(rtbf);

        var anonymizer = Substitute.For<IModulePiiAnonymizer>();
        anonymizer.ModuleName.Returns("Forms");
        anonymizer.AnonymizeAsync(SubjectId, TenantId, nid, default)
            .Returns(RtbfAnonymizationResult.Ok("Forms", 2));

        var sut = BuildSut(anonymizer);
        await sut.Handle(BuildCommand(), default);

        await anonymizer.Received(1)
            .AnonymizeAsync(SubjectId, TenantId, nid, Arg.Any<CancellationToken>());
    }
}
