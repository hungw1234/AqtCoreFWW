using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Persistence;

/// <summary>
/// Guards the G2 invariant of the streaming read path: <see cref="DapperReadDbConnection.QueryUnbufferedAsync{T}"/>
/// must refuse to run inside a cross-module unit of work. A long-lived streaming reader on the shared
/// write connection would hold the transaction open row-by-row, so the method fails loud instead of
/// silently sharing <c>uow.Connection</c>. This is a pure guard test — it trips before any connection
/// is opened, so no database is required.
/// </summary>
public sealed class DapperReadDbConnectionUnbufferedGuardTests
{
    [Fact]
    public async Task QueryUnbufferedAsync_WhenUnitOfWorkActive_ThrowsBeforeOpeningConnection()
    {
        var dbOptions = Substitute.For<IOptionsMonitor<DatabaseOptions>>();
        dbOptions.CurrentValue.Returns(new DatabaseOptions { ConnectionString = "Server=unused;" });

        var tenantContext = Substitute.For<ITenantContext>();

        var uow = Substitute.For<ICrossModuleUnitOfWork>();
        uow.IsActive.Returns(true); // inside a write scope → streaming read is forbidden

        var sut = new DapperReadDbConnection(dbOptions, tenantContext, uow);

        // The iterator throws on first MoveNextAsync (enumeration start), not at call time.
        var act = async () =>
        {
            await foreach (var _ in sut.QueryUnbufferedAsync<int>("SELECT 1")) { }
        };

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*unit of work*");
    }
}
