using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Application.Queries.ListUsers;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Application.Tests.Queries;

/// <summary>
/// Tests for ListUsersQueryHandler.
/// IReadDbConnection is mocked — no real DB. Validates pagination math,
/// empty result handling, and filter passthrough.
/// </summary>
public sealed class ListUsersQueryHandlerTests
{
    private readonly IReadDbConnection _db;
    private readonly ListUsersQueryHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();

    public ListUsersQueryHandlerTests()
    {
        _db = Substitute.For<IReadDbConnection>();
        // Passthrough decryptor — these tests use plaintext mock rows; decryption is covered by the
        // EncryptedFieldDecryptor unit tests and the ListUsers integration test (real key resolver).
        var decryptor = Substitute.For<IEncryptedFieldDecryptor>();
        decryptor.Decrypt(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());
        _sut = new ListUsersQueryHandler(_db, decryptor);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_WithResults_ReturnsSuccessPagedResult()
    {
        _db.QueryAsync<UserRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(BuildRows(3, totalCount: 3));

        var result = await _sut.Handle(
            new ListUsersQuery(TenantId, null, null, null, Page: 1, PageSize: 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);
        result.Value.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_SecondPage_PaginationMetaReflectsPage()
    {
        // 25 total, page size 10 → 3 pages; page 2 has next
        _db.QueryAsync<UserRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(BuildRows(10, totalCount: 25));

        var result = await _sut.Handle(
            new ListUsersQuery(TenantId, null, null, null, Page: 2, PageSize: 10),
            CancellationToken.None);

        result.Value.Meta.Page.Should().Be(2);
        result.Value.Meta.TotalPages.Should().Be(3);
        result.Value.Meta.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_LastPage_HasNextPageIsFalse()
    {
        _db.QueryAsync<UserRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(BuildRows(5, totalCount: 15));

        var result = await _sut.Handle(
            new ListUsersQuery(TenantId, null, null, null, Page: 3, PageSize: 5),
            CancellationToken.None);

        result.Value.Meta.HasNextPage.Should().BeFalse();
    }

    // --- Empty result ---

    [Fact]
    public async Task Handle_NoRows_ReturnsTotalCountZeroAndEmptyData()
    {
        _db.QueryAsync<UserRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sut.Handle(
            new ListUsersQuery(TenantId, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_NoRows_TotalPagesIsZero()
    {
        _db.QueryAsync<UserRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sut.Handle(
            new ListUsersQuery(TenantId, null, null, null, Page: 1, PageSize: 20),
            CancellationToken.None);

        result.Value.Meta.TotalPages.Should().Be(0);
    }

    // --- DTO mapping ---

    [Fact]
    public async Task Handle_WithRows_DtoFieldsMappedFromRow()
    {
        var userId = Guid.NewGuid();
        var row = new UserRow(
            userId, "Nguyen Van A", "a@gov.vn", "IT",
            ClassificationLevel.Internal, true, TenantId,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            null, null, TotalCount: 1);

        _db.QueryAsync<UserRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns([row]);

        var result = await _sut.Handle(
            new ListUsersQuery(TenantId, null, null, null),
            CancellationToken.None);

        var dto = result.Value.Items.Single();
        dto.Id.Should().Be(userId);
        dto.FullName.Should().Be("Nguyen Van A");
        dto.Email.Should().Be("a@gov.vn");
        dto.DepartmentCode.Should().Be("IT");
        dto.IsActive.Should().BeTrue();
        dto.TenantId.Should().Be(TenantId);
    }

    // --- Pagination clamp (ARCH-FU2.3) ---

    [Fact]
    public async Task Handle_PageSizeAboveAdminMax_ClampsToAdminMax()
    {
        _db.QueryAsync<UserRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(BuildRows(100, totalCount: 500));

        var result = await _sut.Handle(
            new ListUsersQuery(TenantId, null, null, null, Page: 1, PageSize: 9999),
            CancellationToken.None);

        result.Value.Meta.PageSize.Should().Be(PaginationDefaults.AdminMax,
            "pageSize above AdminMax must clamp via PaginationDefaults.Normalize");
    }

    [Fact]
    public async Task Handle_PageBelowOne_ClampsToOne()
    {
        _db.QueryAsync<UserRow>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sut.Handle(
            new ListUsersQuery(TenantId, null, null, null, Page: 0, PageSize: 20),
            CancellationToken.None);

        result.Value.Meta.Page.Should().Be(1, "page<1 must clamp to 1 via PaginationDefaults.Normalize");
    }

    // --- Helpers ---

    // UserRow is internal in the handler (InternalsVisibleTo allows access from this test assembly)
    private static IEnumerable<UserRow> BuildRows(int count, int totalCount) =>
        Enumerable.Range(1, count).Select(i => new UserRow(
            Guid.NewGuid(), $"User {i}", $"user{i}@gov.vn", null,
            ClassificationLevel.Internal, true, TenantId,
            DateTime.UtcNow, null, null, TotalCount: totalCount));
}
