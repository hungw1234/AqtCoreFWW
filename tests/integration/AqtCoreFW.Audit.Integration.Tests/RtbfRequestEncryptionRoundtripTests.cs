using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Audit.Integration.Tests;

/// <summary>
/// Wave-4 roundtrip proof: RtbfRequest PII columns (SubjectEmail, CitizenNationalId) are
/// AES-256-GCM encrypted at rest. Verifies the complete cycle:
///   1. EF insert stores ciphertext (v1: prefix) in raw SQL
///   2. EF load decrypts back to original plaintext
///
/// Also covers <see cref="PlaintextToEncryptedUpgrader"/>:
///   - PkColumn = "Id" (default) — regression guard for the original hardcoded-'Id' bug
///   - PkColumn = "InternalId" override — covers audit.RtbfRequests bigint PK shape
///   - Idempotency: second run encrypts 0 rows (already-v{N}: rows skipped)
///   - Mixed batch: plaintext sibling encrypted, pre-encrypted sibling skipped
///
/// Requires SQL Server (uses AES-GCM + raw SqlConnection). Runs in SqlServerCollection fixture.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class RtbfRequestEncryptionRoundtripTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public RtbfRequestEncryptionRoundtripTests(WebAppFixture app)
    {
        _app = app;
    }

    // ── Test 1: EF insert encrypts PII columns at rest ────────────────────────

    [Fact]
    public async Task Insert_SubjectEmailAndCitizenNationalId_StoredEncrypted()
    {
        const string email = "nguyen.van.a@example.vn";
        const string nationalId = "001099001234";

        var messageId = await InsertRtbfRequestAsync(email, nationalId);
        var (rawEmail, rawNationalId) = await ReadRawColumnsAsync(messageId);

        // Raw SQL must show v1: prefix (not plaintext) — encryption happened via EncryptedStringConverter
        rawEmail.Should().StartWith("v1:",
            because: "SubjectEmail must be AES-256-GCM ciphertext with v1: prefix, not plaintext");
        rawNationalId.Should().StartWith("v1:",
            because: "CitizenNationalId must be AES-256-GCM ciphertext with v1: prefix, not plaintext");

        // Ciphertext must be longer than plaintext (overhead: nonce 12 + tag 16 = 28 bytes + base64 expansion)
        rawEmail.Length.Should().BeGreaterThan(email.Length,
            because: "encrypted email must be longer than plaintext email");
        rawNationalId.Length.Should().BeGreaterThan(nationalId.Length,
            because: "encrypted national ID must be longer than plaintext");
    }

    // ── Test 2: EF load decrypts PII columns back to original plaintext ───────

    [Fact]
    public async Task Load_AfterInsert_ReturnsOriginalPlaintext()
    {
        const string email = "tran.thi.b@test.gov.vn";
        const string nationalId = "079200001234";

        var messageId = await InsertRtbfRequestAsync(email, nationalId);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        // EF decrypts via EncryptedStringConverter when loading
        var loaded = await db.RtbfRequests
            .AsNoTracking()
            .FirstAsync(r => r.MessageId == messageId);

        loaded.SubjectEmail.Should().Be(email,
            because: "EF must decrypt SubjectEmail back to original plaintext via EncryptedStringConverter");
        loaded.CitizenNationalId.Should().Be(nationalId,
            because: "EF must decrypt CitizenNationalId back to original plaintext via EncryptedStringConverter");
    }

    // ── Test 3: PlaintextToEncryptedUpgrader — InternalId (bigint) PK shape ──

    [Fact]
    public async Task PlaintextToEncryptedUpgrader_InternalIdPkColumn_EncryptsPlaintextRows()
    {
        // Regression guard: the original bug hardcoded PK column as 'Id', which doesn't exist
        // in audit.RtbfRequests (PK is 'InternalId'). This test creates a real table in the
        // test DB that mirrors the InternalId PK shape, then drops it after the test.
        // Real table (not temp table) is required because PlaintextToEncryptedUpgrader opens
        // a new SqlConnection per run — temp tables are session-scoped and would be invisible.

        var connStr = GetConnectionString();
        var key = new byte[32]; // all-zero test key — matches EncryptedStringConverter test pattern
        const int version = 1;
        // Unique table name per test run to avoid conflicts with parallel test runs
        var tableName = $"PiiUpgTest_InternalId_{Guid.NewGuid():N}";

        await using (var conn = new SqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var createCmd = conn.CreateCommand();
            createCmd.CommandText = $"""
                CREATE TABLE [dbo].[{tableName}]
                (
                    InternalId  BIGINT IDENTITY(1,1) PRIMARY KEY,
                    DataCol     NVARCHAR(1024) NOT NULL
                );
                INSERT INTO [dbo].[{tableName}] (DataCol) VALUES (N'plaintext-row-one');
                INSERT INTO [dbo].[{tableName}] (DataCol) VALUES (N'plaintext-row-two@example.com');
                """;
            await createCmd.ExecuteNonQueryAsync();
        }

        try
        {
            var target = new PlaintextToEncryptedUpgrader.ColumnTarget(
                Schema: "dbo",
                Table: tableName,
                Column: "DataCol",
                PkColumn: "InternalId");

            var result = await PlaintextToEncryptedUpgrader.EncryptColumnAsync(
                connStr, target, _ => key, version);

            result.Encrypted.Should().Be(2, because: "both plaintext rows must be encrypted");
            result.Skipped.Should().Be(0, because: "no rows had v{N}: prefix before upgrader ran");

            // Verify raw values now carry v1: prefix
            await using var conn2 = new SqlConnection(connStr);
            await conn2.OpenAsync();
            await using var selectCmd = conn2.CreateCommand();
            selectCmd.CommandText = $"SELECT DataCol FROM [dbo].[{tableName}] ORDER BY InternalId";
            await using var reader = await selectCmd.ExecuteReaderAsync();
            var ciphers = new List<string>();
            while (await reader.ReadAsync()) ciphers.Add(reader.GetString(0));

            ciphers.Should().HaveCount(2, because: "two rows were inserted");
            ciphers.Should().AllSatisfy(c => c.Should().StartWith("v1:",
                because: "all rows must carry v1: prefix after upgrader run"));
        }
        finally
        {
            await DropTestTableAsync(connStr, tableName);
        }
    }

    // ── Test 4: PlaintextToEncryptedUpgrader — Id (Guid NVARCHAR) PK shape ───

    [Fact]
    public async Task PlaintextToEncryptedUpgrader_DefaultIdPkColumn_EncryptsPlaintextRows()
    {
        // Covers default PkColumn = "Id" path (AspNetUsers.Email, NormalizedEmail pattern).
        // Uses a real table with NVARCHAR(50) Id PK (Guid string representation).

        var connStr = GetConnectionString();
        var key = new byte[32]; // all-zero test key
        const int version = 1;
        var tableName = $"PiiUpgTest_GuidId_{Guid.NewGuid():N}";

        var id1 = Guid.NewGuid().ToString();
        var id2 = Guid.NewGuid().ToString();

        await using (var conn = new SqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var createCmd = conn.CreateCommand();
            createCmd.CommandText = $"""
                CREATE TABLE [dbo].[{tableName}]
                (
                    Id      NVARCHAR(50)    NOT NULL PRIMARY KEY,
                    DataCol NVARCHAR(1024)  NOT NULL
                );
                INSERT INTO [dbo].[{tableName}] (Id, DataCol) VALUES (N'{id1}', N'user1@domain.example');
                INSERT INTO [dbo].[{tableName}] (Id, DataCol) VALUES (N'{id2}', N'USER1@DOMAIN.EXAMPLE');
                """;
            await createCmd.ExecuteNonQueryAsync();
        }

        try
        {
            // Run upgrader with default PkColumn = "Id"
            var target = new PlaintextToEncryptedUpgrader.ColumnTarget(
                Schema: "dbo",
                Table: tableName,
                Column: "DataCol");  // PkColumn defaults to "Id"

            var result = await PlaintextToEncryptedUpgrader.EncryptColumnAsync(
                connStr, target, _ => key, version);

            result.Encrypted.Should().Be(2, because: "two plaintext rows must be encrypted");
            result.Skipped.Should().Be(0);

            await using var conn2 = new SqlConnection(connStr);
            await conn2.OpenAsync();
            await using var selectCmd = conn2.CreateCommand();
            selectCmd.CommandText = $"SELECT DataCol FROM [dbo].[{tableName}] ORDER BY Id";
            await using var reader = await selectCmd.ExecuteReaderAsync();
            var ciphers = new List<string>();
            while (await reader.ReadAsync()) ciphers.Add(reader.GetString(0));

            ciphers.Should().HaveCount(2);
            ciphers.Should().AllSatisfy(c => c.Should().StartWith("v1:"));
        }
        finally
        {
            await DropTestTableAsync(connStr, tableName);
        }
    }

    // ── Test 5: PlaintextToEncryptedUpgrader idempotency ─────────────────────

    [Fact]
    public async Task PlaintextToEncryptedUpgrader_SecondRun_EncryptsZeroRows()
    {
        var connStr = GetConnectionString();
        var key = new byte[32];
        const int version = 1;
        var tableName = $"PiiUpgTest_Idempotent_{Guid.NewGuid():N}";

        await using (var conn = new SqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var createCmd = conn.CreateCommand();
            createCmd.CommandText = $"""
                CREATE TABLE [dbo].[{tableName}]
                (
                    InternalId  BIGINT IDENTITY PRIMARY KEY,
                    DataCol     NVARCHAR(1024) NOT NULL
                );
                INSERT INTO [dbo].[{tableName}] (DataCol) VALUES (N'some-plaintext');
                """;
            await createCmd.ExecuteNonQueryAsync();
        }

        try
        {
            var target = new PlaintextToEncryptedUpgrader.ColumnTarget(
                Schema: "dbo",
                Table: tableName,
                Column: "DataCol",
                PkColumn: "InternalId");

            // First run — should encrypt 1 row
            var firstRun = await PlaintextToEncryptedUpgrader.EncryptColumnAsync(
                connStr, target, _ => key, version);
            firstRun.Encrypted.Should().Be(1, because: "first run must encrypt the plaintext row");

            // Second run — should encrypt 0 rows (WHERE NOT LIKE 'v[0-9]%:%' skips them)
            var secondRun = await PlaintextToEncryptedUpgrader.EncryptColumnAsync(
                connStr, target, _ => key, version);
            secondRun.Encrypted.Should().Be(0, because: "second run is idempotent — already-encrypted rows skipped");
        }
        finally
        {
            await DropTestTableAsync(connStr, tableName);
        }
    }

    // ── Test 6: Mixed batch — pre-encrypted row skipped, plaintext row encrypted ──

    [Fact]
    public async Task PlaintextToEncryptedUpgrader_MixedBatch_SkipsPreEncryptedRows()
    {
        var connStr = GetConnectionString();
        var key = new byte[32];
        const int version = 1;
        var tableName = $"PiiUpgTest_Mixed_{Guid.NewGuid():N}";

        // A synthetic v1: cipher — passes the NOT LIKE 'v[0-9]%:%' guard so upgrader skips it.
        var fakeAlreadyEncrypted = "v1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";

        await using (var conn = new SqlConnection(connStr))
        {
            await conn.OpenAsync();
            await using var createCmd = conn.CreateCommand();
            createCmd.CommandText = $"""
                CREATE TABLE [dbo].[{tableName}]
                (
                    InternalId  BIGINT IDENTITY PRIMARY KEY,
                    DataCol     NVARCHAR(1024) NULL
                );
                INSERT INTO [dbo].[{tableName}] (DataCol) VALUES (N'plaintext-row');
                INSERT INTO [dbo].[{tableName}] (DataCol) VALUES (N'{fakeAlreadyEncrypted}');
                """;
            await createCmd.ExecuteNonQueryAsync();
        }

        try
        {
            var target = new PlaintextToEncryptedUpgrader.ColumnTarget(
                Schema: "dbo",
                Table: tableName,
                Column: "DataCol",
                PkColumn: "InternalId");

            var result = await PlaintextToEncryptedUpgrader.EncryptColumnAsync(
                connStr, target, _ => key, version);

            // Only the plaintext row must be encrypted; the v1: row must be skipped
            result.Encrypted.Should().Be(1,
                because: "only the plaintext row must be encrypted — the v1: row is skipped by WHERE NOT LIKE 'v[0-9]%:%'");
        }
        finally
        {
            await DropTestTableAsync(connStr, tableName);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> InsertRtbfRequestAsync(string? subjectEmail = null, string? citizenNationalId = null)
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var request = RtbfRequest.Create(
            gen,
            tenantId: Guid.NewGuid(),
            dataSubjectId: Guid.NewGuid(),
            citizenNationalId: citizenNationalId,
            subjectEmail: subjectEmail);

        db.RtbfRequests.Add(request);
        await db.SaveChangesAsync();
        return request.MessageId;
    }

    private async Task<(string Email, string NationalId)> ReadRawColumnsAsync(Guid messageId)
    {
        var connStr = GetConnectionString();
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT SubjectEmail, CitizenNationalId
            FROM [audit].[RtbfRequests]
            WHERE MessageId = @msgId
            """;
        cmd.Parameters.AddWithValue("@msgId", messageId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.GetString(1));
    }

    private string GetConnectionString()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return db.Database.GetConnectionString()!;
    }

    private static async Task DropTestTableAsync(string connStr, string tableName)
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"IF OBJECT_ID(N'[dbo].[{tableName}]', N'U') IS NOT NULL DROP TABLE [dbo].[{tableName}]";
        await cmd.ExecuteNonQueryAsync();
    }
}
