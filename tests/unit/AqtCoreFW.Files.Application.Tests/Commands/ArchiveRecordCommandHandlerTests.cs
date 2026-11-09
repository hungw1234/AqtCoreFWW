using AqtCoreFW.Files.Application.Commands.ArchiveRecord;
using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Files.Application.Tests.Commands;

/// <summary>
/// Unit tests for ArchiveRecordCommandHandler.
/// Validates: success archive, lifecycle creation, status transition.
/// Phase-4b B3b: handler now requires IFileRepository (PublicId→long FK resolution),
/// IRepository&lt;RecordLifecycle, long&gt; (T3 InternalId PK), and IGuidGenerator.
/// ArchiveRecordCommand.FileRecordId is Guid (PublicId); handler resolves to long internally.
/// RecordLifecycleDto.FileRecordId is now long; MessageId (Guid) replaces Id (Guid).
/// </summary>
public sealed class ArchiveRecordCommandHandlerTests
{
    private readonly IFileRepository _fileRepository;
    private readonly IRepository<RecordLifecycle, long> _lifecycleRepository;
    private readonly IGuidGenerator _guidGen;
    private readonly ArchiveRecordCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid FileRecordPublicId = Guid.NewGuid();
    private static readonly long FileRecordInternalId = 42L;
    private static readonly Guid ArchivedBy = Guid.NewGuid();
    private static readonly Guid StableMessageId = Guid.NewGuid();

    public ArchiveRecordCommandHandlerTests()
    {
        _fileRepository = Substitute.For<IFileRepository>();
        _lifecycleRepository = Substitute.For<IRepository<RecordLifecycle, long>>();
        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(StableMessageId);

        // Default: FileRepository resolves PublicId → real FileRecord
        SetupFileRecord(FileRecordPublicId, FileRecordInternalId);

        _handler = new ArchiveRecordCommandHandler(_fileRepository, _lifecycleRepository, _guidGen);
    }

    private void SetupFileRecord(Guid publicId, long internalId)
    {
        // FileRecord is sealed — create a real instance, not a mock
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(publicId);
        var realFile = FileRecord.Create(gen, TenantId, "file.pdf", "key/file.pdf",
            "application/pdf", 1024, "hash", ArchivedBy);

        _fileRepository
            .GetByIdAsync(FileId.From(publicId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(realFile)));
    }

    private ArchiveRecordCommand BuildCommand(Guid? fileRecordId = null) =>
        new(
            FileRecordId: fileRecordId ?? FileRecordPublicId,
            ArchiveStoragePath: "/archive/file",
            OriginalStoragePath: "/storage/file",
            ArchivedBy: ArchivedBy,
            TenantId: TenantId,
            ArchiveReason: "Retention period reached");

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithDto()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsViaRepository()
    {
        await _handler.Handle(BuildCommand(), CancellationToken.None);

        await _lifecycleRepository.Received(1).AddAsync(
            Arg.Is<RecordLifecycle>(l => l.CurrentStatus == RecordLifecycleStatus.Archived),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CreatesLifecycleEntry()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.Value.ArchivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_SetsArchivedStatus()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.Value.CurrentStatus.Should().Be(RecordLifecycleStatus.Archived);
    }

    [Fact]
    public async Task Handle_WithArchiveReason()
    {
        var command = BuildCommand() with { ArchiveReason = "Retention compliance - 7 year requirement" };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithoutArchiveReason()
    {
        var command = BuildCommand() with { ArchiveReason = null };

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_RecordsArchivedAtTimestamp()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.Value.ArchivedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_FileRecordNotFound_ReturnsFailed()
    {
        var unknownId = Guid.NewGuid();
        _fileRepository
            .GetByIdAsync(FileId.From(unknownId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<FileRecord>("Not found")));

        var command = BuildCommand(fileRecordId: unknownId);
        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_RepositoryException_Propagates()
    {
        _lifecycleRepository
            .AddAsync(Arg.Any<RecordLifecycle>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB unavailable")));

        var act = async () => await _handler.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB unavailable*");
    }

    [Fact]
    public async Task Handle_CancellationTokenRespected()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _lifecycleRepository
            .AddAsync(Arg.Any<RecordLifecycle>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromCanceled(cts.Token));

        var act = async () => await _handler.Handle(BuildCommand(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
