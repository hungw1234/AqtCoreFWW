using AqtCoreFW.Files.Application.Options;
using AqtCoreFW.Files.Application.Queries.DownloadFile;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using FluentResults;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Files.Application.Tests.Queries;

/// <summary>
/// Unit tests for DownloadFileQueryHandler.
/// Covers status gating (Quarantined/Rejected = 404), tenant isolation, and clearance-level
/// access control for SystemAdmin, GovOfficer, and default roles.
/// </summary>
public sealed class DownloadFileQueryHandlerTests
{
    private readonly IFileRepository _fileRepository;
    private readonly IFileStorageService _storageService;
    private readonly ICurrentUser _currentUser;
    private readonly IOptions<FilesOptions> _options;
    private readonly DownloadFileQueryHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RequestedBy = Guid.NewGuid();

    public DownloadFileQueryHandlerTests()
    {
        _fileRepository = Substitute.For<IFileRepository>();
        _storageService = Substitute.For<IFileStorageService>();
        _currentUser = Substitute.For<ICurrentUser>();
        _options = Microsoft.Extensions.Options.Options.Create(new FilesOptions { BucketName = "test-bucket" });

        // Default: standard user (no elevated roles)
        _currentUser.Roles.Returns(Array.Empty<string>());

        // Default: storage returns an empty stream
        _storageService
            .DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream()));

        _handler = new DownloadFileQueryHandler(
            _fileRepository, _storageService, _currentUser, _options);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_AvailableFile_ReturnsStreamResult()
    {
        var fileId = Guid.NewGuid();
        SetupAvailableFile(fileId, TenantId, ClassificationLevel.Internal);

        var result = await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FileStream.Should().NotBeNull();
        result.Value.OriginalFileName.Should().Be("document.pdf");
    }

    [Fact]
    public async Task Handle_StorageObjectMissing_ReturnsNotFoundError()
    {
        // Metadata row says Available, but the bytes are gone (orphaned record / storage reset).
        // The storage layer signals this with FileNotFoundException — the handler must map it to a
        // 404, never let it surface as a 500 or a silent empty 200.
        var fileId = Guid.NewGuid();
        SetupAvailableFile(fileId, TenantId, ClassificationLevel.Internal);
        _storageService
            .DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new FileNotFoundException("object gone"));

        var result = await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
    }

    [Fact]
    public async Task Handle_StorageUnreachable_PropagatesException()
    {
        // A down storage backend must fail loudly (exception → 5xx upstream), NOT be swallowed into a
        // 404/empty-200. Only FileNotFoundException (missing object) is caught; everything else bubbles.
        var fileId = Guid.NewGuid();
        SetupAvailableFile(fileId, TenantId, ClassificationLevel.Internal);
        _storageService
            .DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ => throw new IOException("storage unreachable"));

        var act = async () => await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
    }

    // --- Status gating ---

    [Fact]
    public async Task Handle_QuarantinedFile_ReturnsNotFoundError()
    {
        var fileId = Guid.NewGuid();
        SetupFileWithStatus(fileId, TenantId, FileStatus.Quarantined, ClassificationLevel.Internal);

        var result = await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
    }

    [Fact]
    public async Task Handle_RejectedFile_ReturnsNotFoundError()
    {
        var fileId = Guid.NewGuid();
        SetupFileWithStatus(fileId, TenantId, FileStatus.Rejected, ClassificationLevel.Internal);

        var result = await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
    }

    // --- Tenant isolation ---

    [Fact]
    public async Task Handle_NonExistentFile_ReturnsNotFoundError()
    {
        _fileRepository
            .GetByIdAsync(Arg.Any<FileId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<FileRecord>("not found")));

        var result = await _handler.Handle(
            new DownloadFileQuery(Guid.NewGuid(), TenantId, RequestedBy), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
    }

    [Fact]
    public async Task Handle_WrongTenant_ReturnsForbiddenError()
    {
        var fileId = Guid.NewGuid();
        SetupAvailableFile(fileId, tenantId: Guid.NewGuid(), ClassificationLevel.Internal);

        var result = await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "ForbiddenError");
    }

    // --- Clearance level enforcement ---

    [Fact]
    public async Task Handle_AdminUser_CanDownloadTopSecretFile()
    {
        _currentUser.Roles.Returns(["Admin"]);
        var fileId = Guid.NewGuid();
        SetupAvailableFile(fileId, TenantId, ClassificationLevel.TopSecret);

        var result = await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_SystemAdminUser_CanDownloadTopSecretFile()
    {
        _currentUser.Roles.Returns(["SystemAdmin"]);
        var fileId = Guid.NewGuid();
        SetupAvailableFile(fileId, TenantId, ClassificationLevel.TopSecret);

        var result = await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_GovOfficer_CanDownloadConfidentialFile()
    {
        _currentUser.Roles.Returns(["GovOfficer"]);
        var fileId = Guid.NewGuid();
        SetupAvailableFile(fileId, TenantId, ClassificationLevel.Confidential);

        var result = await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_GovOfficer_CannotDownloadTopSecretFile()
    {
        _currentUser.Roles.Returns(["GovOfficer"]);
        var fileId = Guid.NewGuid();
        SetupAvailableFile(fileId, TenantId, ClassificationLevel.TopSecret);

        var result = await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "ForbiddenError");
    }

    [Fact]
    public async Task Handle_DefaultUser_CanDownloadInternalFile()
    {
        _currentUser.Roles.Returns(Array.Empty<string>());
        var fileId = Guid.NewGuid();
        SetupAvailableFile(fileId, TenantId, ClassificationLevel.Internal);

        var result = await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_DefaultUser_CanOnlyDownloadInternalFile_NotConfidential()
    {
        _currentUser.Roles.Returns(Array.Empty<string>());
        var fileId = Guid.NewGuid();
        SetupAvailableFile(fileId, TenantId, ClassificationLevel.Confidential);

        var result = await _handler.Handle(
            new DownloadFileQuery(fileId, TenantId, RequestedBy), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "ForbiddenError");
    }

    // --- Helpers ---

    private void SetupAvailableFile(Guid fileId, Guid tenantId, ClassificationLevel classification)
        => SetupFileWithStatus(fileId, tenantId, FileStatus.Available, classification);

    private void SetupFileWithStatus(
        Guid fileId, Guid tenantId, FileStatus status, ClassificationLevel classification)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(fileId);
        var file = FileRecord.Create(
            gen,
            tenantId,
            originalFileName: "document.pdf",
            storageKey: $"{tenantId}/{fileId}.pdf",
            contentType: "application/pdf",
            sizeBytes: 4096,
            checksumSha256: "cafebabe",
            uploadedBy: RequestedBy,
            classification: classification);

        // Drive to the required status. scanGen provides MessageId for domain events (Rule1 compliant).
        var scanGen = Substitute.For<IGuidGenerator>();
        scanGen.NewIdRandom().Returns(Guid.NewGuid());
        if (status == FileStatus.Available)
            file.MarkAvailable(scanGen);
        else if (status == FileStatus.Rejected)
            file.MarkRejected(scanGen);
        // FileStatus.Quarantined is the default from Create()

        _fileRepository
            .GetByIdAsync(FileId.From(fileId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(file)));
    }
}
