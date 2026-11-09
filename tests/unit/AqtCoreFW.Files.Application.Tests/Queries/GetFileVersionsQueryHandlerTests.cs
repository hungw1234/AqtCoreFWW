using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Application.Queries.GetFileVersions;
using AqtCoreFW.SharedKernel.Application.Data;
using NSubstitute;

namespace AqtCoreFW.Files.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetFileVersionsQueryHandler.
/// Validates: returns file versions, ordered by version number, empty result.
/// </summary>
public sealed class GetFileVersionsQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly GetFileVersionsQueryHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    // FileRecordId in query is Guid (FileRecord PublicId); in FileVersionDto it is long (FK).
    private static readonly Guid FileRecordPublicId = Guid.NewGuid();
    private const long FileRecordInternalId = 42L;

    public GetFileVersionsQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        _handler = new GetFileVersionsQueryHandler(_db);
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_WithVersions_ReturnsList()
    {
        var versions = new List<FileVersionDto>
        {
            new(Guid.NewGuid(), FileRecordInternalId, 1, "/storage/v1", 1024, "hash1",
                Guid.NewGuid(), DateTime.UtcNow.AddDays(-2), "Initial", TenantId),
            new(Guid.NewGuid(), FileRecordInternalId, 2, "/storage/v2", 2048, "hash2",
                Guid.NewGuid(), DateTime.UtcNow.AddDays(-1), "Updated", TenantId)
        };

        _db.QueryAsync<FileVersionDto>(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<FileVersionDto>>(versions));

        var query = new GetFileVersionsQuery(FileRecordPublicId, TenantId);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_NoVersions_ReturnsEmpty()
    {
        var emptyList = new List<FileVersionDto>();

        _db.QueryAsync<FileVersionDto>(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<FileVersionDto>>(emptyList));

        var query = new GetFileVersionsQuery(FileRecordPublicId, TenantId);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PassesCorrectParameters()
    {
        var emptyList = new List<FileVersionDto>();

        _db.QueryAsync<FileVersionDto>(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<FileVersionDto>>(emptyList));

        var query = new GetFileVersionsQuery(FileRecordPublicId, TenantId);

        await _handler.Handle(query, CancellationToken.None);

        await _db.Received(1).QueryAsync<FileVersionDto>(
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OrderedByVersionNumber()
    {
        var versions = new List<FileVersionDto>
        {
            new(Guid.NewGuid(), FileRecordInternalId, 1, "/v1", 100, "h1",
                Guid.NewGuid(), DateTime.UtcNow, null, TenantId),
            new(Guid.NewGuid(), FileRecordInternalId, 2, "/v2", 200, "h2",
                Guid.NewGuid(), DateTime.UtcNow, null, TenantId),
            new(Guid.NewGuid(), FileRecordInternalId, 3, "/v3", 300, "h3",
                Guid.NewGuid(), DateTime.UtcNow, null, TenantId)
        };

        _db.QueryAsync<FileVersionDto>(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<FileVersionDto>>(versions));

        var query = new GetFileVersionsQuery(FileRecordPublicId, TenantId);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Value.Select(v => v.VersionNumber)
            .Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public async Task Handle_MultipleVersionsWithMetadata()
    {
        var uploader1 = Guid.NewGuid();
        var uploader2 = Guid.NewGuid();

        var versions = new List<FileVersionDto>
        {
            new(Guid.NewGuid(), FileRecordInternalId, 1, "/v1", 1024, "hash1",
                uploader1, DateTime.UtcNow.AddDays(-1), "Initial upload", TenantId),
            new(Guid.NewGuid(), FileRecordInternalId, 2, "/v2", 2048, "hash2",
                uploader2, DateTime.UtcNow, "Corrected version", TenantId)
        };

        _db.QueryAsync<FileVersionDto>(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<FileVersionDto>>(versions));

        var query = new GetFileVersionsQuery(FileRecordPublicId, TenantId);

        var result = await _handler.Handle(query, CancellationToken.None);

        result.Value.First().Comment.Should().Be("Initial upload");
        result.Value.Last().Comment.Should().Be("Corrected version");
    }

    [Fact]
    public async Task Handle_CancellationTokenPropagated()
    {
        var cts = new CancellationTokenSource();
        var emptyList = new List<FileVersionDto>();

        _db.QueryAsync<FileVersionDto>(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<FileVersionDto>>(emptyList));

        var query = new GetFileVersionsQuery(FileRecordPublicId, TenantId);

        await _handler.Handle(query, cts.Token);

        await _db.Received(1).QueryAsync<FileVersionDto>(
            Arg.Any<string>(),
            Arg.Any<object>(),
            cts.Token);
    }

    [Fact]
    public async Task Handle_DatabaseException_Propagates()
    {
        _db.QueryAsync<FileVersionDto>(
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IEnumerable<FileVersionDto>>(
                new InvalidOperationException("Query timeout")));

        var query = new GetFileVersionsQuery(FileRecordPublicId, TenantId);

        var act = async () => await _handler.Handle(query, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Query timeout*");
    }
}
