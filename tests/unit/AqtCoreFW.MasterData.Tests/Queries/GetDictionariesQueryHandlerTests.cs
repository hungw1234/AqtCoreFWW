using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.MasterData.Application.Queries.GetDictionaries;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.MasterData.Tests.Queries;

/// <summary>
/// Tests for GetDictionariesQueryHandler.
/// Verifies handler structure and query validation.
/// </summary>
public sealed class GetDictionariesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Handler_HasCorrectDependencies()
    {
        // Arrange & Act
        var mockDb = Substitute.For<IReadDbConnection>();
        var handler = new GetDictionariesQueryHandler(mockDb);

        // Assert
        handler.Should().NotBeNull();
    }

    [Fact]
    public void ValidQuery_CanBeCreated()
    {
        // Arrange & Act
        var query = new GetDictionariesQuery(
            TenantId,
            DictionaryStatus.Published,
            "SEARCH_TERM",
            2,
            10);

        // Assert
        query.TenantId.Should().Be(TenantId);
        query.Status.Should().Be(DictionaryStatus.Published);
        query.Search.Should().Be("SEARCH_TERM");
        query.Page.Should().Be(2);
        query.PageSize.Should().Be(10);
    }

    [Fact]
    public void QueryWithDefaults_CanBeCreated()
    {
        // Arrange & Act
        var query = new GetDictionariesQuery(TenantId);

        // Assert
        query.TenantId.Should().Be(TenantId);
        query.Status.Should().BeNull();
        query.Search.Should().BeNull();
        query.Page.Should().Be(1);
        query.PageSize.Should().Be(20);
    }
}
