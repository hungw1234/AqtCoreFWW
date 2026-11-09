using AqtCoreFW.Files.Application.Commands.StageFileForSigning;
using AqtCoreFW.Files.Application.Options;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using FluentResults;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Files.Application.Tests.Commands;

/// <summary>
/// Unit tests for StageFileForSigningCommandHandler.
/// Verifies tenant isolation, status gating, PDF content-type enforcement,
/// and the bucket-copy semantics (Files bucket → signing-original bucket).
/// </summary>
public sealed class StageFileForSigningCommandHandlerTests
{
    private readonly IFileRepository _fileRepository;
    private readonly IFileStorageService _storageService;
    private readonly IOptions<FilesOptions> _options;
    private readonly IGuidGenerator _guidGen;
    private readonly StageFileForSigningCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();

    public StageFileForSigningCommandHandlerTests()
    {
        _fileRepository = Substitute.For<IFileRepository>();
        _storageService = Substitute.For<IFileStorageService>();
        _options = Microsoft.Extensions.Options.Options.Create(
            new FilesOptions { BucketName = "aqtcorefw-files" });

        // Default: DownloadAsync returns a non-empty stream so UploadAsync has bytes to copy
        _storageService
            .DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 })));

        // Default: UploadAsync returns the key unchanged
        _storageService
            .UploadAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.ArgAt<string>(2)));

        _guidGen = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdMonotonic().Returns(_ => Guid.CreateVersion7());

        _handler = new StageFileForSigningCommandHandler(_fileRepository, _storageService, _options, _guidGen);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_AvailablePdf_ReturnsDocIdAndKey()
    {
        var fileId = Guid.NewGuid();
        SetupAvailablePdf(fileId, TenantId);

        var result = await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DocId.Should().NotBeEmpty();
        result.Value.OriginalStorageKey.Should().StartWith($"{TenantId}/");
        result.Value.OriginalStorageKey.Should().EndWith(".pdf");
    }

    [Fact]
    public async Task Handle_AvailablePdf_DownloadsFromFilesBucket()
    {
        var fileId = Guid.NewGuid();
        var file = SetupAvailablePdf(fileId, TenantId);

        await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        // Must read from the Files upload bucket using the file's storage key
        await _storageService.Received(1).DownloadAsync(
            "aqtcorefw-files", file.StorageKey, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AvailablePdf_UploadsToSigningOriginalBucket()
    {
        var fileId = Guid.NewGuid();
        SetupAvailablePdf(fileId, TenantId);

        var result = await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        // Must write to the signing-original bucket under the minted tenant-prefixed key
        await _storageService.Received(1).UploadAsync(
            Arg.Any<Stream>(),
            "signing-original",
            result.Value.OriginalStorageKey,
            "application/pdf",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AvailablePdf_KeyStartsWithTenantId()
    {
        var fileId = Guid.NewGuid();
        SetupAvailablePdf(fileId, TenantId);

        var result = await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        // DocFlow validator requires key to start with "{tenantId}/" for tenant isolation
        result.Value.OriginalStorageKey.Should().Be($"{TenantId}/{result.Value.DocId}.pdf");
    }

    // --- Tenant isolation ---

    [Fact]
    public async Task Handle_WrongTenant_ReturnsForbiddenError()
    {
        var fileId = Guid.NewGuid();
        SetupAvailablePdf(fileId, tenantId: Guid.NewGuid()); // owned by a different tenant

        var result = await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "ForbiddenError");
    }

    [Fact]
    public async Task Handle_WrongTenant_DoesNotCopyBytes()
    {
        var fileId = Guid.NewGuid();
        SetupAvailablePdf(fileId, tenantId: Guid.NewGuid());

        await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        await _storageService.DidNotReceive().DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Status gating ---

    [Fact]
    public async Task Handle_NonExistentFile_ReturnsNotFoundError()
    {
        _fileRepository
            .GetByIdAsync(Arg.Any<FileId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<FileRecord>("not found")));

        var result = await _handler.Handle(
            new StageFileForSigningCommand(Guid.NewGuid(), TenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
    }

    [Fact]
    public async Task Handle_QuarantinedFile_ReturnsNotFoundError()
    {
        var fileId = Guid.NewGuid();
        SetupFileWithContentType(fileId, TenantId, FileStatus.Quarantined, "application/pdf");

        var result = await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
    }

    [Fact]
    public async Task Handle_RejectedFile_ReturnsNotFoundError()
    {
        var fileId = Guid.NewGuid();
        SetupFileWithContentType(fileId, TenantId, FileStatus.Rejected, "application/pdf");

        var result = await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
    }

    // --- PDF enforcement ---

    [Fact]
    public async Task Handle_NonPdfContentType_ReturnsValidationError()
    {
        var fileId = Guid.NewGuid();
        SetupFileWithContentType(fileId, TenantId, FileStatus.Available, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

        var result = await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "ValidationError");
    }

    [Fact]
    public async Task Handle_NonPdfContentType_DoesNotCopyBytes()
    {
        var fileId = Guid.NewGuid();
        SetupFileWithContentType(fileId, TenantId, FileStatus.Available, "image/png");

        await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        await _storageService.DidNotReceive().DownloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PdfContentTypeCaseInsensitive_Accepted()
    {
        // ContentType may arrive with uppercase letters depending on the upload client
        var fileId = Guid.NewGuid();
        SetupFileWithContentType(fileId, TenantId, FileStatus.Available, "Application/PDF");

        var result = await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // --- Encryption gate ---

    [Fact]
    public async Task Handle_EncryptedPdf_ReturnsValidationError()
    {
        var fileId = Guid.NewGuid();
        SetupAvailablePdf(fileId, TenantId);
        SetDownloadBytes(EncryptedPdfBytes());

        var result = await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "ValidationError");
    }

    [Fact]
    public async Task Handle_EncryptedPdf_DoesNotStageToSigningBucket()
    {
        var fileId = Guid.NewGuid();
        SetupAvailablePdf(fileId, TenantId);
        SetDownloadBytes(EncryptedPdfBytes());

        await _handler.Handle(
            new StageFileForSigningCommand(fileId, TenantId), CancellationToken.None);

        // An encrypted document must never reach the signing-original bucket — it could never be signed.
        await _storageService.DidNotReceive().UploadAsync(
            Arg.Any<Stream>(), "signing-original",
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // A trailer carrying an /Encrypt indirect reference marks the PDF as encrypted/password-protected.
    private static byte[] EncryptedPdfBytes() =>
        "%PDF-1.7\n1 0 obj<<>>endobj\ntrailer<< /Root 1 0 R /Encrypt 5 0 R >>\n%%EOF"u8.ToArray();

    private void SetDownloadBytes(byte[] bytes) =>
        _storageService
            .DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(bytes)));

    // --- Helpers ---

    private FileRecord SetupAvailablePdf(Guid fileId, Guid tenantId)
        => SetupFileWithContentType(fileId, tenantId, FileStatus.Available, "application/pdf");

    private FileRecord SetupFileWithContentType(
        Guid fileId, Guid tenantId, FileStatus status, string contentType)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(fileId);
        var file = FileRecord.Create(
            gen,
            tenantId,
            originalFileName: "document.pdf",
            storageKey: $"{tenantId}/{fileId}.pdf",
            contentType: contentType,
            sizeBytes: 8192,
            checksumSha256: "cafebabe",
            uploadedBy: Guid.NewGuid(),
            classification: ClassificationLevel.Internal);

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

        return file;
    }
}
