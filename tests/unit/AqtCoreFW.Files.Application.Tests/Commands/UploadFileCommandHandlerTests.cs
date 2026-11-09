using AqtCoreFW.Files.Application.Commands.UploadFile;
using AqtCoreFW.Files.Application.Jobs;
using AqtCoreFW.Files.Application.Options;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Files.Application.Tests.Commands;

/// <summary>
/// Unit tests for UploadFileCommandHandler — 8-step upload pipeline.
/// All I/O dependencies mocked. MemoryStream used for stream-based tests.
/// </summary>
public sealed class UploadFileCommandHandlerTests
{
    private readonly IFileRepository _fileRepository;
    private readonly IFileStorageService _storageService;
    private readonly IFileSecurityService _securityService;
    private readonly IBackgroundJobService _backgroundJobService;
    private readonly IOptions<FilesOptions> _options;
    private readonly UploadFileCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UploadedBy = Guid.NewGuid();

    public UploadFileCommandHandlerTests()
    {
        _fileRepository = Substitute.For<IFileRepository>();
        _storageService = Substitute.For<IFileStorageService>();
        _securityService = Substitute.For<IFileSecurityService>();
        _backgroundJobService = Substitute.For<IBackgroundJobService>();
        _options = Microsoft.Extensions.Options.Options.Create(new FilesOptions { BucketName = "test-bucket" });

        // Default: all security checks pass
        _securityService.ValidateExtension(Arg.Any<string>())
            .Returns(FileSecurityResult.Ok());
        _securityService.ValidateMimeTypeAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(FileSecurityResult.Ok()));
        _securityService.CheckZipBombAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(FileSecurityResult.Ok()));
        _securityService.GenerateStorageKey(Arg.Any<Guid>(), Arg.Any<string>())
            .Returns($"{TenantId}/{Guid.NewGuid()}.pdf");

        _backgroundJobService.Enqueue(
                Arg.Any<string>(),
                Arg.Any<System.Linq.Expressions.Expression<Func<IClamAvScanJob, Task>>>())
            .Returns("job-id");

        // Phase-4b B3b: IGuidGenerator injected — NSubstitute returns stable Guid for PublicId
        var guidGen = Substitute.For<IGuidGenerator>();
        guidGen.NewIdMonotonic().Returns(Guid.NewGuid());

        _handler = new UploadFileCommandHandler(
            _fileRepository,
            _storageService,
            _securityService,
            _backgroundJobService,
            guidGen,
            _options,
            Substitute.For<ILogger<UploadFileCommandHandler>>());
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidFile_ReturnsSuccessWithFileId()
    {
        var command = BuildCommand();

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FileId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_ValidFile_StatusUrlContainsFileId()
    {
        var command = BuildCommand();

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.StatusUrl.Should().Contain(result.Value.FileId.ToString());
    }

    [Fact]
    public async Task Handle_ValidFile_PersistsFileRecordWithQuarantinedStatus()
    {
        var command = BuildCommand();

        await _handler.Handle(command, CancellationToken.None);

        await _fileRepository.Received(1).AddAsync(
            Arg.Is<FileRecord>(f => f.Status == FileStatus.Quarantined),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidFile_EnqueuesClamAvScanJob_OnVirusScanQueue()
    {
        var command = BuildCommand();

        await _handler.Handle(command, CancellationToken.None);

        // Must enqueue on the dedicated virus-scan lane (the queue the processing server lists), not the
        // default queue — routing to a queue no worker drains would leave files stuck Quarantined forever.
        _backgroundJobService.Received(1)
            .Enqueue(
                BackgroundJobQueues.VirusScan,
                Arg.Any<System.Linq.Expressions.Expression<Func<IClamAvScanJob, Task>>>());
    }

    [Fact]
    public async Task Handle_ValidFile_ComputesSha256Checksum()
    {
        var command = BuildCommand(content: new byte[] { 1, 2, 3, 4, 5 });

        await _handler.Handle(command, CancellationToken.None);

        // SHA-256 computed means AddAsync was called with a non-empty checksum
        await _fileRepository.Received(1).AddAsync(
            Arg.Is<FileRecord>(f => f.ChecksumSha256.Length == 64), // hex SHA-256 = 64 chars
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AllSecurityChecksPassed_UploadsToMinIO()
    {
        var command = BuildCommand();

        await _handler.Handle(command, CancellationToken.None);

        await _storageService.Received(1).UploadAsync(
            Arg.Any<Stream>(),
            "test-bucket",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // --- Security validation failures ---

    [Fact]
    public async Task Handle_BlockedExtension_ReturnsValidationError()
    {
        _securityService.ValidateExtension(Arg.Any<string>())
            .Returns(FileSecurityResult.Rejected("File extension '.exe' is not allowed."));
        var command = BuildCommand(fileName: "malware.exe");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains(".exe"));
    }

    [Fact]
    public async Task Handle_InvalidMimeType_ReturnsValidationError()
    {
        _securityService.ValidateMimeTypeAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(FileSecurityResult.Rejected("File too small to validate.")));
        var command = BuildCommand();

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("small"));
    }

    [Fact]
    public async Task Handle_ZipBomb_ReturnsValidationError()
    {
        _securityService.CheckZipBombAsync(Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(FileSecurityResult.Rejected("Archive expansion ratio exceeds 50x limit.")));
        var command = BuildCommand(fileName: "archive.zip");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("50x"));
    }

    [Fact]
    public async Task Handle_BlockedExtension_DoesNotCallRepository()
    {
        _securityService.ValidateExtension(Arg.Any<string>())
            .Returns(FileSecurityResult.Rejected("Blocked."));
        var command = BuildCommand(fileName: "script.bat");

        await _handler.Handle(command, CancellationToken.None);

        await _fileRepository.DidNotReceive().AddAsync(
            Arg.Any<FileRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RepositoryThrows_ExceptionPropagates()
    {
        _fileRepository
            .AddAsync(Arg.Any<FileRecord>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB unavailable")));
        var command = BuildCommand();

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DB unavailable*");
    }

    // --- Helpers ---

    private static UploadFileCommand BuildCommand(
        string fileName = "document.pdf",
        string contentType = "application/pdf",
        byte[]? content = null)
    {
        var bytes = content ?? new byte[] { 0x25, 0x50, 0x44, 0x46, 0x00 }; // %PDF magic bytes
        var stream = new MemoryStream(bytes) { Position = 0 };
        return new UploadFileCommand(
            FileStream: stream,
            OriginalFileName: fileName,
            ContentType: contentType,
            SizeBytes: bytes.Length,
            TenantId: TenantId,
            UploadedBy: UploadedBy,
            Classification: ClassificationLevel.Internal);
    }
}
