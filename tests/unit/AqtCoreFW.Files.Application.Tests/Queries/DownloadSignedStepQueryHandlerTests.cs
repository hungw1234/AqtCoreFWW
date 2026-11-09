using AqtCoreFW.Files.Application.Queries.DownloadSignedStep;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using FluentResults;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Files.Application.Tests.Queries;

/// <summary>
/// Unit tests for DownloadSignedStepQueryHandler.
/// Verifies ownership guard, server-side key resolution, and storage streaming.
/// </summary>
public sealed class DownloadSignedStepQueryHandlerTests
{
    private readonly IFileRepository _fileRepository;
    private readonly ISigningRequestClient _signingClient;
    private readonly IFileStorageService _storageService;
    private readonly DownloadSignedStepQueryHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid FileId   = Guid.NewGuid();
    private static readonly byte[] PdfMagicBytes = [0x25, 0x50, 0x44, 0x46];

    public DownloadSignedStepQueryHandlerTests()
    {
        _fileRepository = Substitute.For<IFileRepository>();
        _signingClient  = Substitute.For<ISigningRequestClient>();
        _storageService = Substitute.For<IFileStorageService>();
        _handler = new DownloadSignedStepQueryHandler(_fileRepository, _signingClient, _storageService);
    }

    [Fact]
    public async Task Handle_CompletedStep_StreamsSignedPdf()
    {
        SetupFileFound(FileId, TenantId);
        SetupResolution(Found: true, bucket: "signing-docflow", key: "key.pdf", fileName: "signed.pdf");
        _storageService
            .DownloadAsync("signing-docflow", "key.pdf", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(PdfMagicBytes)));

        var result = await _handler.Handle(
            new DownloadSignedStepQuery(FileId, TenantId, 1), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ContentType.Should().Be("application/pdf");
        result.Value.OriginalFileName.Should().Be("signed.pdf");
        result.Value.FileStream.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_FileNotFound_ReturnsNotFoundError_BeforeSigningClientCalled()
    {
        _fileRepository
            .GetByIdAsync(Arg.Any<FileId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<FileRecord>("not found")));

        var result = await _handler.Handle(
            new DownloadSignedStepQuery(FileId, TenantId, 1), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
        // Key security invariant: client must NEVER reach storage resolution on unknown file
        await _signingClient.DidNotReceive()
            .ResolveSignedStepForFileAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CrossTenantFileId_ReturnsNotFoundError_BeforeSigningClientCalled()
    {
        SetupFileFound(FileId, tenantId: Guid.NewGuid()); // file belongs to different tenant

        var result = await _handler.Handle(
            new DownloadSignedStepQuery(FileId, TenantId, 1), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
        await _signingClient.DidNotReceive()
            .ResolveSignedStepForFileAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StepNotCompleted_ReturnsNotFoundError()
    {
        SetupFileFound(FileId, TenantId);
        SetupResolution(Found: false, errorCode: "NOT_COMPLETED");

        var result = await _handler.Handle(
            new DownloadSignedStepQuery(FileId, TenantId, 1), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
        await _storageService.DidNotReceive()
            .DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StorageKeyNotFound_ReturnsNotFoundError()
    {
        SetupFileFound(FileId, TenantId);
        SetupResolution(Found: false, errorCode: "NOT_FOUND");

        var result = await _handler.Handle(
            new DownloadSignedStepQuery(FileId, TenantId, 1), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
    }

    [Fact]
    public async Task Handle_StorageThrowsFileNotFoundException_ReturnsNotFoundError()
    {
        SetupFileFound(FileId, TenantId);
        SetupResolution(Found: true, bucket: "signing-docflow", key: "key.pdf", fileName: "signed.pdf");
        _storageService
            .DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new FileNotFoundException("blob gone"));

        var result = await _handler.Handle(
            new DownloadSignedStepQuery(FileId, TenantId, 1), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
    }

    [Fact]
    public async Task Handle_ResolutionHasNoFileName_UsesFallbackName()
    {
        SetupFileFound(FileId, TenantId);
        SetupResolution(Found: true, bucket: "signing-docflow", key: "key.pdf", fileName: null);
        _storageService
            .DownloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(PdfMagicBytes)));

        var result = await _handler.Handle(
            new DownloadSignedStepQuery(FileId, TenantId, SequenceNumber: 2), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.OriginalFileName.Should().Contain(FileId.ToString())
            .And.Contain("seq2");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private void SetupFileFound(Guid fileId, Guid tenantId)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(fileId);
        var file = FileRecord.Create(
            gen, tenantId, "doc.pdf", $"{tenantId}/{fileId}.pdf",
            "application/pdf", 2048, "deadbeef", Guid.NewGuid(),
            caseId: null, uploaderDisplayName: null,
            classification: ClassificationLevel.Internal);

        _fileRepository
            .GetByIdAsync(Files.Domain.Entities.FileId.From(fileId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(file)));
    }

    private void SetupResolution(
        bool Found,
        string? bucket = null,
        string? key = null,
        string? fileName = null,
        string? errorCode = null)
    {
        _signingClient
            .ResolveSignedStepForFileAsync(TenantId, FileId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SignedStepResolution(Found, bucket, key, fileName, errorCode)));
    }
}
