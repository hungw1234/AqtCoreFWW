using AqtCoreFW.Notifications.Application.DTOs;
using AqtCoreFW.Notifications.Application.Queries.GetUserNotifications;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Notifications.Application.Tests.Queries;

/// <summary>
/// Unit tests for GetUserNotificationsQueryHandler.
/// Handler uses IReadDbConnection (Dapper read-side) — two calls: COUNT then data rows.
/// </summary>
public sealed class GetUserNotificationsQueryHandlerTests
{
    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly GetUserNotificationsQueryHandler _sut;

    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    public GetUserNotificationsQueryHandlerTests()
    {
        _sut = new GetUserNotificationsQueryHandler(_db);
    }

    // --- Happy path: returns paged results ---

    [Fact]
    public async Task Handle_WithResults_ReturnsSuccessPagedResult()
    {
        var dtos = BuildDtos(3);
        SetupDb(totalCount: 3, dtos);

        var query = new GetUserNotificationsQuery(UserId, TenantId, Page: 1, PageSize: 20);
        var result = await _sut.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);
        result.Value.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Handle_WithResults_MetaPageIsCorrect()
    {
        SetupDb(totalCount: 25, BuildDtos(10));

        var query = new GetUserNotificationsQuery(UserId, TenantId, Page: 2, PageSize: 10);
        var result = await _sut.Handle(query, CancellationToken.None);

        result.Value.Meta.Page.Should().Be(2);
        result.Value.Meta.PageSize.Should().Be(10);
        result.Value.Meta.TotalPages.Should().Be(3);
        result.Value.Meta.HasNextPage.Should().BeTrue();
    }

    // --- Empty result ---

    [Fact]
    public async Task Handle_NoNotifications_ReturnsEmptyPagedResult()
    {
        SetupDb(totalCount: 0, []);

        var query = new GetUserNotificationsQuery(UserId, TenantId);
        var result = await _sut.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_NoNotifications_HasNextPageIsFalse()
    {
        SetupDb(totalCount: 0, []);

        var query = new GetUserNotificationsQuery(UserId, TenantId, Page: 1, PageSize: 20);
        var result = await _sut.Handle(query, CancellationToken.None);

        result.Value.Meta.HasNextPage.Should().BeFalse();
    }

    // --- Last page has no next page ---

    [Fact]
    public async Task Handle_LastPage_HasNextPageIsFalse()
    {
        SetupDb(totalCount: 15, BuildDtos(5));

        // page 3 of 3 (pageSize=5, total=15)
        var query = new GetUserNotificationsQuery(UserId, TenantId, Page: 3, PageSize: 5);
        var result = await _sut.Handle(query, CancellationToken.None);

        result.Value.Meta.HasNextPage.Should().BeFalse();
    }

    // --- DB queried exactly twice (COUNT + data) ---

    [Fact]
    public async Task Handle_Always_QueriesDbTwice()
    {
        SetupDb(totalCount: 1, BuildDtos(1));

        var query = new GetUserNotificationsQuery(UserId, TenantId);
        await _sut.Handle(query, CancellationToken.None);

        await _db.Received(1).QueryFirstOrDefaultAsync<int>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await _db.Received(1).QueryAsync<NotificationDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    private void SetupDb(int totalCount, IEnumerable<NotificationDto> dtos)
    {
        _db.QueryFirstOrDefaultAsync<int>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(totalCount);
        _db.QueryAsync<NotificationDto>(Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(dtos);
    }

    private static List<NotificationDto> BuildDtos(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new NotificationDto
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                RecipientUserId = UserId,
                Subject = "Subj",
                Body = "Body",
                Channel = "inapp",
                IsRead = false,
                ReadAt = null,
                CreatedAt = DateTimeOffset.UtcNow,
            })
            .ToList();
}
