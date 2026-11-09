using AqtCoreFW.AssetManagement.Application.DTOs;
using AqtCoreFW.AssetManagement.Application.Queries.GetAssetManagement;
using AqtCoreFW.AssetManagement.Application.Queries.ListAssetManagement;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.AssetManagement.Application.Tests.Queries;

public sealed class AssetManagementQueryHandlerTests
{
    [Fact]
    public async Task GetHandler_ReturnsAssetWhenFound()
    {
        var tenantId = Guid.NewGuid();
        var asset = new AssetManagementDto(
            7, tenantId, "Laptop", "Office device", DateTimeOffset.UtcNow);
        var db = Substitute.For<IReadDbConnection>();
        db.QueryFirstOrDefaultAsync<AssetManagementDto>(
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<int?>())
            .Returns(asset);
        var handler = new GetAssetManagementQueryHandler(db);
        using var cancellationSource = new CancellationTokenSource();

        var result = await handler.Handle(
            new GetAssetManagementQuery(asset.Id, tenantId),
            cancellationSource.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(asset);
        await db.Received(1).QueryFirstOrDefaultAsync<AssetManagementDto>(
            Arg.Is<string>(sql =>
                sql.Contains("TenantId = @TenantId") &&
                sql.Contains("IsDeleted = 0")),
            Arg.Is<object?>(parameters =>
                HasParameter(parameters, "Id", asset.Id) &&
                HasParameter(parameters, "TenantId", tenantId)),
            cancellationSource.Token,
            Arg.Any<int?>());
    }

    [Fact]
    public async Task GetHandler_ReturnsNotFoundWhenAssetDoesNotExist()
    {
        var db = Substitute.For<IReadDbConnection>();
        db.QueryFirstOrDefaultAsync<AssetManagementDto>(
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<int?>())
            .Returns((AssetManagementDto?)null);
        var handler = new GetAssetManagementQueryHandler(db);
        var query = new GetAssetManagementQuery(99, Guid.NewGuid());

        var result = await handler.Handle(query, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("99");
    }

    [Fact]
    public async Task ListHandler_ReturnsAssetsForTenant()
    {
        var tenantId = Guid.NewGuid();
        var assets = new[]
        {
            new AssetManagementDto(1, tenantId, "Laptop", null, DateTimeOffset.UtcNow),
            new AssetManagementDto(2, tenantId, "Monitor", "Office", DateTimeOffset.UtcNow),
        };
        var db = Substitute.For<IReadDbConnection>();
        db.QueryAsync<AssetManagementDto>(
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<int?>())
            .Returns(assets);
        var handler = new ListAssetManagementQueryHandler(db);
        using var cancellationSource = new CancellationTokenSource();

        var result = await handler.Handle(
            new ListAssetManagementQuery(tenantId),
            cancellationSource.Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(assets);
        await db.Received(1).QueryAsync<AssetManagementDto>(
            Arg.Is<string>(sql =>
                sql.Contains("TenantId = @TenantId") &&
                sql.Contains("IsDeleted = 0") &&
                sql.Contains("ORDER BY Id")),
            Arg.Is<object?>(parameters => HasParameter(parameters, "TenantId", tenantId)),
            cancellationSource.Token,
            Arg.Any<int?>());
    }

    [Fact]
    public async Task ListHandler_ReturnsEmptyListWhenNoAssetsExist()
    {
        var db = Substitute.For<IReadDbConnection>();
        db.QueryAsync<AssetManagementDto>(
                Arg.Any<string>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<int?>())
            .Returns(Enumerable.Empty<AssetManagementDto>());
        var handler = new ListAssetManagementQueryHandler(db);

        var result = await handler.Handle(
            new ListAssetManagementQuery(Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    private static bool HasParameter(object? parameters, string name, object expected)
    {
        if (parameters is null)
            return false;

        var value = parameters.GetType().GetProperty(name)?.GetValue(parameters);
        return Equals(value, expected);
    }
}
