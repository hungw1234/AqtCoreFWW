using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.MasterData.Application.Queries.GetDictionaryItems;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.MasterData.Tests.Queries;

/// <summary>
/// Tests for GetDictionaryItemsQueryHandler.
/// Verifies handler structure and query validation.
/// </summary>
public sealed class GetDictionaryItemsQueryHandlerTests
{
    private static readonly Guid DictionaryId = Guid.NewGuid();

    [Fact]
    public void Handler_HasCorrectDependencies()
    {
        // Arrange & Act
        var mockDb = Substitute.For<IReadDbConnection>();
        var handler = new GetDictionaryItemsQueryHandler(mockDb);

        // Assert
        handler.Should().NotBeNull();
    }

    [Fact]
    public void ValidQuery_CanBeCreated()
    {
        // Arrange & Act
        var query = new GetDictionaryItemsQuery(DictionaryId, true, 2, 25);

        // Assert
        query.DictionaryId.Should().Be(DictionaryId);
        query.ActiveOnly.Should().BeTrue();
        query.Page.Should().Be(2);
        query.PageSize.Should().Be(25);
    }

    [Fact]
    public void QueryWithDefaults_CanBeCreated()
    {
        // Arrange & Act
        var query = new GetDictionaryItemsQuery(DictionaryId);

        // Assert
        query.DictionaryId.Should().Be(DictionaryId);
        query.ActiveOnly.Should().BeTrue();
        query.Page.Should().Be(1);
        query.PageSize.Should().Be(100);
    }

    [Fact]
    public void QueryIncludingInactive_CanBeCreated()
    {
        // Arrange & Act
        var query = new GetDictionaryItemsQuery(DictionaryId, false, 1, 50);

        // Assert
        query.ActiveOnly.Should().BeFalse();
        query.PageSize.Should().Be(50);
    }
}
