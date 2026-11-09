using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.MasterData.Application.Queries.GetExternalMappings;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.MasterData.Tests.Queries;

/// <summary>
/// Tests for GetExternalMappingsQueryHandler.
/// Verifies handler structure and query validation.
/// </summary>
public sealed class GetExternalMappingsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Handler_HasCorrectDependencies()
    {
        // Arrange & Act
        var mockDb = Substitute.For<IReadDbConnection>();
        var handler = new GetExternalMappingsQueryHandler(mockDb);

        // Assert
        handler.Should().NotBeNull();
    }

    [Fact]
    public void ValidQuery_CanBeCreated()
    {
        // Arrange & Act
        var query = new GetExternalMappingsQuery(
            TenantId,
            "EXTERNAL_SYSTEM",
            "INTERNAL_CODE",
            true,
            2,
            15);

        // Assert
        query.TenantId.Should().Be(TenantId);
        query.ExternalSystem.Should().Be("EXTERNAL_SYSTEM");
        query.InternalCode.Should().Be("INTERNAL_CODE");
        query.ActiveOnly.Should().BeTrue();
        query.Page.Should().Be(2);
        query.PageSize.Should().Be(15);
    }

    [Fact]
    public void QueryWithDefaults_CanBeCreated()
    {
        // Arrange & Act
        var query = new GetExternalMappingsQuery(TenantId);

        // Assert
        query.TenantId.Should().Be(TenantId);
        query.ExternalSystem.Should().BeNull();
        query.InternalCode.Should().BeNull();
        query.ActiveOnly.Should().BeTrue();
        query.Page.Should().Be(1);
        query.PageSize.Should().Be(20);
    }

    [Fact]
    public void QueryForAllMappings_CanBeCreated()
    {
        // Arrange & Act
        var query = new GetExternalMappingsQuery(TenantId, null, null, false, 1, 50);

        // Assert
        query.ExternalSystem.Should().BeNull();
        query.InternalCode.Should().BeNull();
        query.ActiveOnly.Should().BeFalse();
        query.PageSize.Should().Be(50);
    }
}
