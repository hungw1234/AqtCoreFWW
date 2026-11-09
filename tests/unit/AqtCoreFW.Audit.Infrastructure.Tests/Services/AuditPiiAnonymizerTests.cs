using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for AuditPiiAnonymizer.
/// SQL execution path requires a real SQL Server — tested via integration tests.
/// Unit tests here cover module identity and the SqlConnection guard.
/// HMAC chain safety: DataSnapshot is not part of BuildFingerprint() — anonymization
/// does not invalidate the HMAC chain.
/// </summary>
public sealed class AuditPiiAnonymizerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SubjectId = Guid.NewGuid();

    private readonly ILogger<AuditPiiAnonymizer> _logger =
        Substitute.For<ILogger<AuditPiiAnonymizer>>();

    private AuditDbContext BuildInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AuditDbContext(options);
    }

    [Fact]
    public void ModuleName_ReturnsAudit()
    {
        using var db = BuildInMemoryContext();
        var sut = new AuditPiiAnonymizer(db, _logger);
        sut.ModuleName.Should().Be("Audit");
    }

    [Fact]
    public async Task AnonymizeAsync_NonSqlServerConnection_ThrowsInvalidOperationException()
    {
        // InMemory provider returns an InMemoryDatabaseConnection, not SqlConnection
        using var db = BuildInMemoryContext();
        var sut = new AuditPiiAnonymizer(db, _logger);

        // InMemory provider either throws "Relational-specific methods..." or our cast guard —
        // both are InvalidOperationException; exact message depends on provider internals.
        await sut.Invoking(a => a.AnonymizeAsync(SubjectId, TenantId, null))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
