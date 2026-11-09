using AqtCoreFW.Files.Application.Queries.GetFileSigningSteps;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Files.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetFileSigningStepsQueryHandler.
/// Verifies tenant-ownership guard, delegation to the signing client, and empty-list passthrough.
/// </summary>
public sealed class GetFileSigningStepsQueryHandlerTests
{
    private readonly IFileRepository _fileRepository;
    private readonly ISigningRequestClient _signingClient;
    private readonly GetFileSigningStepsQueryHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid FileId   = Guid.NewGuid();

    public GetFileSigningStepsQueryHandlerTests()
    {
        _fileRepository = Substitute.For<IFileRepository>();
        _signingClient  = Substitute.For<ISigningRequestClient>();
        _handler = new GetFileSigningStepsQueryHandler(_fileRepository, _signingClient);
    }

    [Fact]
    public async Task Handle_SignedFile_ReturnsStepsFromClient()
    {
        var steps = new List<SigningStepDto>
        {
            new(Guid.NewGuid(), 1, "Completed", Guid.NewGuid(), null, DateTime.UtcNow, true)
        };
        SetupFileFound(FileId, TenantId);
        _signingClient
            .GetStepsForFileAsync(TenantId, FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SigningStepDto>>(steps));

        var result = await _handler.Handle(
            new GetFileSigningStepsQuery(FileId, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Status.Should().Be("Completed");
    }

    [Fact]
    public async Task Handle_UnsignedFile_ReturnsEmptyList()
    {
        SetupFileFound(FileId, TenantId);
        _signingClient
            .GetStepsForFileAsync(TenantId, FileId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SigningStepDto>>(Array.Empty<SigningStepDto>()));

        var result = await _handler.Handle(
            new GetFileSigningStepsQuery(FileId, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("unsigned files have no signing steps but are not an error");
    }

    [Fact]
    public async Task Handle_FileNotFound_ReturnsNotFoundError()
    {
        _fileRepository
            .GetByIdAsync(Arg.Any<FileId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<FileRecord>("not found")));

        var result = await _handler.Handle(
            new GetFileSigningStepsQuery(FileId, TenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
    }

    [Fact]
    public async Task Handle_CrossTenantFileId_ReturnsNotFoundError()
    {
        // File exists but belongs to a different tenant
        SetupFileFound(FileId, tenantId: Guid.NewGuid());

        var result = await _handler.Handle(
            new GetFileSigningStepsQuery(FileId, TenantId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.GetType().Name == "NotFoundError");
        // Signing client must NOT be called (ownership guard failed before delegation)
        await _signingClient.DidNotReceive()
            .GetStepsForFileAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ─── Helper ──────────────────────────────────────────────────────────────────

    private void SetupFileFound(Guid fileId, Guid tenantId)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(fileId);
        var file = FileRecord.Create(
            gen, tenantId, "doc.pdf", $"{tenantId}/{fileId}.pdf",
            "application/pdf", 1024, "abc123", Guid.NewGuid(),
            caseId: null, uploaderDisplayName: null,
            classification: ClassificationLevel.Internal);

        _fileRepository
            .GetByIdAsync(Files.Domain.Entities.FileId.From(fileId), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(file)));
    }
}
