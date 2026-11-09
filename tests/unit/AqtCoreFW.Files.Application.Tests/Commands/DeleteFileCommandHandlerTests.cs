using AqtCoreFW.Files.Application.Commands.DeleteFile;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using FluentResults;

namespace AqtCoreFW.Files.Application.Tests.Commands;

/// <summary>
/// Unit tests for DeleteFileCommandHandler.
/// Verifies tenant isolation, soft-delete, not-found and forbidden error paths.
/// </summary>
public sealed class DeleteFileCommandHandlerTests
{
    private readonly IFileRepository _fileRepository;
    private readonly DeleteFileCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DeletedBy = Guid.NewGuid();

    public DeleteFileCommandHandlerTests()
    {
        _fileRepository = Substitute.For<IFileRepository>();
        _handler = new DeleteFileCommandHandler(_fileRepository);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ExistingFile_ReturnsSuccess()
    {
        var fileId = Guid.NewGuid();
        SetupFileFound(fileId, TenantId);

        var result = await _handler.Handle(
            new DeleteFileCommand(fileId, TenantId, DeletedBy), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExistingFile_CallsSoftDelete()
    {
        var fileId = Guid.NewGuid();
        var file = SetupFileFound(fileId, TenantId);

        await _handler.Handle(
            new DeleteFileCommand(fileId, TenantId, DeletedBy), CancellationToken.None);

        file.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExistingFile_CallsRepositoryUpdate()
    {
        var fileId = Guid.NewGuid();
        SetupFileFound(fileId, TenantId);

        await _handler.Handle(
            new DeleteFileCommand(fileId, TenantId, DeletedBy), CancellationToken.None);

        await _fileRepository.Received(1).UpdateAsync(
            Arg.Any<FileRecord>(), Arg.Any<CancellationToken>());
    }

    // --- Error paths ---

    [Fact]
    public async Task Handle_NonExistentFile_ReturnsNotFoundError()
    {
        _fileRepository
            .GetByIdAsync(Arg.Any<FileId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<FileRecord>("not found")));

        var result = await _handler.Handle(
            new DeleteFileCommand(Guid.NewGuid(), TenantId, DeletedBy), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
    }

    [Fact]
    public async Task Handle_WrongTenant_ReturnsForbiddenError()
    {
        var fileId = Guid.NewGuid();
        SetupFileFound(fileId, tenantId: Guid.NewGuid()); // different tenant owns the file

        var result = await _handler.Handle(
            new DeleteFileCommand(fileId, TenantId, DeletedBy), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "ForbiddenError");
    }

    [Fact]
    public async Task Handle_WrongTenant_DoesNotCallRepositoryUpdate()
    {
        var fileId = Guid.NewGuid();
        SetupFileFound(fileId, tenantId: Guid.NewGuid());

        await _handler.Handle(
            new DeleteFileCommand(fileId, TenantId, DeletedBy), CancellationToken.None);

        await _fileRepository.DidNotReceive().UpdateAsync(
            Arg.Any<FileRecord>(), Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    private FileRecord SetupFileFound(Guid fileId, Guid tenantId)
    {
        // Phase-4b B3b: FileRecord.Create requires IGuidGenerator as first arg
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(fileId); // use fileId as PublicId for stable test assertions
        var file = FileRecord.Create(
            gen,
            tenantId,
            originalFileName: "report.pdf",
            storageKey: $"{tenantId}/{fileId}.pdf",
            contentType: "application/pdf",
            sizeBytes: 2048,
            checksumSha256: "deadbeef",
            uploadedBy: DeletedBy,
            classification: ClassificationLevel.Internal);

        _fileRepository
            .GetByIdAsync(FileId.From(fileId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(file)));

        return file;
    }
}
