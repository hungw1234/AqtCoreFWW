using AqtCoreFW.Files.Application.Commands.UploadFileVersion;
using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Files.Application.Tests.Commands;

/// <summary>
/// Unit tests for UploadFileVersionCommandHandler.
/// Validates: success version creation, version numbering, persistence.
/// Phase-4b B3b: handler requires IFileRepository (PublicId→long FK resolution),
/// IRepository&lt;FileVersion, long&gt; (T3 InternalId PK), and IGuidGenerator.
/// UploadFileVersionCommand.FileRecordId is Guid (PublicId); handler resolves internally.
/// FileVersionDto uses MessageId (Guid AK) and long FileRecordId.
/// </summary>
public sealed class UploadFileVersionCommandHandlerTests
{
    private readonly IFileRepository _fileRepository;
    private readonly IRepository<FileVersion, long> _repository;
    private readonly IGuidGenerator _guidGen;
    private readonly IReadDbConnection _db;
    private readonly UploadFileVersionCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid FileRecordPublicId = Guid.NewGuid();
    private static readonly Guid UploadedBy = Guid.NewGuid();
    private static readonly Guid StableMessageId = Guid.NewGuid();

    public UploadFileVersionCommandHandlerTests()
    {
        _fileRepository = Substitute.For<IFileRepository>();
        _repository = Substitute.For<IRepository<FileVersion, long>>();
        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(StableMessageId);
        _db = Substitute.For<IReadDbConnection>();

        // Default: FileRepository resolves PublicId → FileRecord with internal long Id
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(FileRecordPublicId);
        var fakeFile = FileRecord.Create(gen, TenantId, "file.pdf", "key/file.pdf",
            "application/pdf", 1024, "hash", UploadedBy);

        _fileRepository
            .GetByIdAsync(FileId.From(FileRecordPublicId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(fakeFile)));

        _handler = new UploadFileVersionCommandHandler(_fileRepository, _repository, _guidGen, _db);
    }

    private UploadFileVersionCommand BuildCommand(int? versionNumber = null, string? storagePath = null) =>
        new(
            FileRecordId: FileRecordPublicId,
            StoragePath: storagePath ?? "/storage/file-v1.pdf",
            FileSize: 1024000,
            ContentHash: "abc123def456",
            UploadedBy: UploadedBy,
            TenantId: TenantId,
            Comment: "Initial upload");

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithDto()
    {
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task Handle_CalculatesNextVersionNumber()
    {
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(3));

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.Value.VersionNumber.Should().Be(3);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsViaRepository()
    {
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        await _handler.Handle(BuildCommand(), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<FileVersion>(v => v.VersionNumber == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MapsAllProperties()
    {
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(2));

        var storagePath = "/storage/document-v2.pdf";
        var fileSize = 2048000L;
        var contentHash = "sha256-hash-value";
        var comment = "Updated document";

        var command = new UploadFileVersionCommand(
            FileRecordId: FileRecordPublicId,
            StoragePath: storagePath,
            FileSize: fileSize,
            ContentHash: contentHash,
            UploadedBy: UploadedBy,
            TenantId: TenantId,
            Comment: comment);

        var result = await _handler.Handle(command, CancellationToken.None);

        // Phase-4b B3b: FileRecordId is long (internal FK); DTO uses MessageId (Guid AK).
        result.Value.FileRecordId.Should().BeGreaterThanOrEqualTo(0); // long, 0 pre-SaveChanges
        result.Value.StoragePath.Should().Be(storagePath);
        result.Value.FileSize.Should().Be(fileSize);
        result.Value.ContentHash.Should().Be(contentHash);
        result.Value.UploadedBy.Should().Be(UploadedBy);
        result.Value.TenantId.Should().Be(TenantId);
        result.Value.Comment.Should().Be(comment);
    }

    [Fact]
    public async Task Handle_WithoutComment_CreatesVersionSuccessfully()
    {
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        var command = new UploadFileVersionCommand(
            FileRecordId: FileRecordPublicId,
            StoragePath: "/storage/file-v1.pdf",
            FileSize: 1024000,
            ContentHash: "hash",
            UploadedBy: UploadedBy,
            TenantId: TenantId,
            Comment: null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Comment.Should().BeNull();
    }

    [Fact]
    public async Task Handle_SetsUploadedAtTimestamp()
    {
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.Value.UploadedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_FileRecordNotFound_ReturnsFailed()
    {
        var unknownId = Guid.NewGuid();
        _fileRepository
            .GetByIdAsync(FileId.From(unknownId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<FileRecord>("Not found")));

        var command = new UploadFileVersionCommand(
            FileRecordId: unknownId,
            StoragePath: "/storage/file.pdf",
            FileSize: 1024,
            ContentHash: "hash",
            UploadedBy: UploadedBy,
            TenantId: TenantId,
            Comment: null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Failure scenarios ---

    [Fact]
    public async Task Handle_RepositoryException_Propagates()
    {
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(1));

        _repository
            .AddAsync(Arg.Any<FileVersion>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB error")));

        var act = async () => await _handler.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB error*");
    }

    [Fact]
    public async Task Handle_VersionNumberQueryException_Propagates()
    {
        _db.QuerySingleAsync<int>(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("Query failed")));

        var act = async () => await _handler.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Query failed*");
    }
}
