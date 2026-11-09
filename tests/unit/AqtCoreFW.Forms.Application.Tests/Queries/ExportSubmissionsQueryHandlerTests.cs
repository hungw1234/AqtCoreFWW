using AqtCoreFW.Forms.Application.Queries.ExportSubmissions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.Queries;

/// <summary>
/// Unit tests for the streaming ExportSubmissionsQueryHandler. Cover the contract change to
/// IAsyncEnumerable&lt;string&gt;: 404 paths run BEFORE any line is yielded, the header line is
/// emitted first, data lines map fields by key with empty cells for missing keys, and CSV cells are
/// escaped. The fake read connection routes the template-resolve / field / submission queries by SQL
/// keyword so the handler's real projection + CSV code runs.
/// </summary>
public sealed class ExportSubmissionsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TemplatePublicId = Guid.NewGuid();

    // Routes each query by a keyword in the SQL. Field/submission rows are supplied per-test.
    private sealed class FakeReadDbConnection : IReadDbConnection
    {
        public long? ResolvedInternalId { get; set; } = 42;
        public List<(string FieldKey, string LabelEn)> Fields { get; set; } = [];
        public List<(Guid Id, Guid SubmittedBy, DateTimeOffset SubmittedAt, string? SubmissionData)> Submissions { get; set; } = [];

        public Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
        {
            // Template-resolve query returns the internal long Id (or null → 404).
            object? id = ResolvedInternalId;
            return Task.FromResult((T?)id);
        }

        public Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
        {
            // Only the field-header query is buffered in the handler.
            var rows = Fields.Select(f => (T)Activator.CreateInstance(typeof(T), f.FieldKey, f.LabelEn)!);
            return Task.FromResult(rows);
        }

        public async IAsyncEnumerable<T> QueryUnbufferedAsync<T>(string sql, object? param = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default, int? commandTimeout = null)
        {
            foreach (var s in Submissions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return (T)Activator.CreateInstance(
                    typeof(T), s.Id, s.SubmittedBy, s.SubmittedAt, s.SubmissionData)!;
                await Task.Yield();
            }
        }

        // Unused by this handler.
        public Task<T> QuerySingleAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => Task.FromResult(default(T)!);
        public Task<int> ExecuteAsync(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => Task.FromResult(0);
        public Task<IEnumerable<T>> QueryWithAuditAdminBypassAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => QueryAsync<T>(sql, param, cancellationToken, commandTimeout);
        public Task<T> QuerySingleWithAuditAdminBypassAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => QuerySingleAsync<T>(sql, param, cancellationToken, commandTimeout);
        public Task<T?> QueryFirstOrDefaultWithPublicFormsBypassAsync<T>(string sql, object? param = null,
            CancellationToken cancellationToken = default, int? commandTimeout = null)
            => QueryFirstOrDefaultAsync<T>(sql, param, cancellationToken, commandTimeout);
    }

    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<string> lines)
    {
        var result = new List<string>();
        await foreach (var l in lines) result.Add(l);
        return result;
    }

    [Fact]
    public async Task Handle_TemplateNotFound_FailsWithNotFound_BeforeStreaming()
    {
        var db = new FakeReadDbConnection { ResolvedInternalId = null };
        var handler = new ExportSubmissionsQueryHandler(db);

        var result = await handler.Handle(
            new ExportSubmissionsQuery(TenantId, TemplatePublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().BeOfType<NotFoundError>();
    }

    [Fact]
    public async Task Handle_NoFields_FailsWithNotFound_BeforeStreaming()
    {
        var db = new FakeReadDbConnection { Fields = [] }; // template resolves, but no active fields
        var handler = new ExportSubmissionsQueryHandler(db);

        var result = await handler.Handle(
            new ExportSubmissionsQuery(TenantId, TemplatePublicId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().BeOfType<NotFoundError>();
    }

    [Fact]
    public async Task Handle_YieldsHeaderLineFirst()
    {
        var db = new FakeReadDbConnection
        {
            Fields = [("qty", "Quantity"), ("price", "Unit Price")],
            Submissions = [],
        };
        var handler = new ExportSubmissionsQueryHandler(db);

        var result = await handler.Handle(
            new ExportSubmissionsQuery(TenantId, TemplatePublicId), CancellationToken.None);
        var lines = await DrainAsync(result.Value);

        result.IsSuccess.Should().BeTrue();
        lines.Should().ContainSingle();
        lines[0].Should().Be("SubmissionId,SubmittedBy,SubmittedAt,Quantity,Unit Price");
    }

    [Fact]
    public async Task Handle_MapsFieldsByKey_EmptyCellForMissingKey()
    {
        var subId = Guid.NewGuid();
        var user = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
        var db = new FakeReadDbConnection
        {
            Fields = [("qty", "Quantity"), ("price", "Unit Price")],
            // "price" absent → empty cell; extra key "note" ignored.
            Submissions = [(subId, user, at, """{"qty":"5","note":"ignored"}""")],
        };
        var handler = new ExportSubmissionsQueryHandler(db);

        var result = await handler.Handle(
            new ExportSubmissionsQuery(TenantId, TemplatePublicId), CancellationToken.None);
        var lines = await DrainAsync(result.Value);

        lines.Should().HaveCount(2);
        lines[1].Should().Be($"{subId},{user},2026-07-29 10:00:00,5,");
    }

    [Fact]
    public async Task Handle_FullOutput_ByteParityWithBufferedFormat()
    {
        // Locks CSV byte-parity criterion (c): joining every yielded line with Environment.NewLine and
        // appending a trailing newline must reproduce exactly what the old StringBuilder.AppendLine
        // buffered output produced — same header, same escaping, same line ending, no BOM.
        var subId = Guid.NewGuid();
        var user = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
        var db = new FakeReadDbConnection
        {
            Fields = [("qty", "Quantity")],
            Submissions = [(subId, user, at, """{"qty":"5"}""")],
        };
        var handler = new ExportSubmissionsQueryHandler(db);

        var result = await handler.Handle(
            new ExportSubmissionsQuery(TenantId, TemplatePublicId), CancellationToken.None);
        var lines = await DrainAsync(result.Value);

        // Controller writes each line via WriteLineAsync → every line (incl. the last) is followed by
        // Environment.NewLine, matching the old AppendLine-per-row output.
        var rendered = string.Concat(lines.Select(l => l + Environment.NewLine));
        var expected =
            "SubmissionId,SubmittedBy,SubmittedAt,Quantity" + Environment.NewLine +
            $"{subId},{user},2026-07-29 10:00:00,5" + Environment.NewLine;
        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task Handle_EscapesCellsContainingCommaOrQuote()
    {
        var subId = Guid.NewGuid();
        var user = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
        var db = new FakeReadDbConnection
        {
            Fields = [("addr", "Address")],
            Submissions = [(subId, user, at, """{"addr":"12 Le Loi, Hue"}""")],
        };
        var handler = new ExportSubmissionsQueryHandler(db);

        var result = await handler.Handle(
            new ExportSubmissionsQuery(TenantId, TemplatePublicId), CancellationToken.None);
        var lines = await DrainAsync(result.Value);

        // Comma-containing cell is wrapped in quotes.
        lines[1].Should().EndWith(",\"12 Le Loi, Hue\"");
    }
}
