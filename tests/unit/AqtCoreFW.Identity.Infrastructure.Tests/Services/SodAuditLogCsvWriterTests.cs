using AqtCoreFW.Identity.Infrastructure.Services;
using FluentAssertions;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for SodAuditLogCsvWriter — validates RFC 4180 escape + OWASP formula-injection defense.
/// Matrix covers all 6 OWASP payload families + RFC 4180 edge cases.
/// </summary>
public class SodAuditLogCsvWriterTests
{
    // ─── OWASP Formula Injection Tests ─────────────────────────────────────────

    [Theory]
    // Formula with quotes and comma needs RFC 4180 wrapping after OWASP prefix
    [InlineData("=HYPERLINK(\"http://evil\", \"click\")", "\"'=HYPERLINK(\"\"http://evil\"\", \"\"click\"\")\"")]
    [InlineData("=cmd|' /c calc'!A1", "'=cmd|' /c calc'!A1")]
    [InlineData("=SUM(A1:A10)", "'=SUM(A1:A10)")]
    public void EscapeCell_FormulaStartingWithEquals_PrependsSingleQuote(string input, string expected)
    {
        var result = SodAuditLogCsvWriter.EscapeCell(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("+1+1=3", "'+1+1=3")]
    [InlineData("+cmd", "'+cmd")]
    public void EscapeCell_FormulaStartingWithPlus_PrependsSingleQuote(string input, string expected)
    {
        var result = SodAuditLogCsvWriter.EscapeCell(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("-10+calc()", "'-10+calc()")]
    [InlineData("-cmd", "'-cmd")]
    public void EscapeCell_FormulaStartingWithMinus_PrependsSingleQuote(string input, string expected)
    {
        var result = SodAuditLogCsvWriter.EscapeCell(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("@import", "'@import")]
    public void EscapeCell_FormulaStartingWithAt_PrependsSingleQuote(string input, string expected)
    {
        var result = SodAuditLogCsvWriter.EscapeCell(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void EscapeCell_FormulaStartingWithTab_PrependsSingleQuote()
    {
        var input = "\tmalicious";
        var result = SodAuditLogCsvWriter.EscapeCell(input);
        result.Should().Be("'	malicious"); // ' + tab + malicious
    }

    [Fact]
    public void EscapeCell_FormulaStartingWithCarriageReturn_PrependsSingleQuote()
    {
        var input = "\rmalicious";
        var result = SodAuditLogCsvWriter.EscapeCell(input);
        // CR needs RFC 4180 wrapping too
        result.Should().Be("\"'\rmalicious\"");
    }

    // ─── RFC 4180 Tests ────────────────────────────────────────────────────────

    [Fact]
    public void EscapeCell_ValueWithComma_WrapsInDoubleQuotes()
    {
        var result = SodAuditLogCsvWriter.EscapeCell("value,with,commas");
        result.Should().Be("\"value,with,commas\"");
    }

    [Fact]
    public void EscapeCell_ValueWithQuote_WrapsAndEscapesQuotes()
    {
        var result = SodAuditLogCsvWriter.EscapeCell("value\"with\"quotes");
        result.Should().Be("\"value\"\"with\"\"quotes\"");
    }

    [Fact]
    public void EscapeCell_ValueWithNewline_WrapsInDoubleQuotes()
    {
        var result = SodAuditLogCsvWriter.EscapeCell("value\nwith\nnewlines");
        result.Should().Be("\"value\nwith\nnewlines\"");
    }

    [Fact]
    public void EscapeCell_ValueWithCarriageReturn_WrapsInDoubleQuotes()
    {
        var result = SodAuditLogCsvWriter.EscapeCell("line1\rline2");
        result.Should().Be("\"line1\rline2\"");
    }

    [Fact]
    public void EscapeCell_CombinedQuoteAndComma_EscapesBothCorrectly()
    {
        var result = SodAuditLogCsvWriter.EscapeCell("\"quoted,value\"");
        result.Should().Be("\"\"\"quoted,value\"\"\"");
    }

    // ─── Edge Cases ────────────────────────────────────────────────────────────

    [Fact]
    public void EscapeCell_Null_ReturnsEmpty()
    {
        var result = SodAuditLogCsvWriter.EscapeCell(null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void EscapeCell_EmptyString_ReturnsEmpty()
    {
        var result = SodAuditLogCsvWriter.EscapeCell(string.Empty);
        result.Should().BeEmpty();
    }

    [Fact]
    public void EscapeCell_NormalValue_ReturnsUnchanged()
    {
        var result = SodAuditLogCsvWriter.EscapeCell("IDENTITY.USER.VIEW");
        result.Should().Be("IDENTITY.USER.VIEW");
    }

    // ─── WriteHeader + WriteRow Integration ────────────────────────────────────

    [Fact]
    public void WriteHeader_WritesCorrectColumnNames()
    {
        using var sw = new StringWriter();
        SodAuditLogCsvWriter.WriteHeader(sw);

        var header = sw.ToString().TrimEnd();
        header.Should().Be("DetectedAt,UserId,UserName,RoleId,RoleName,RuleId,PermissionCodeA,PermissionCodeB,Severity,ActorId,ActorName,Justification");
    }

    [Fact]
    public void WriteRow_FormatsCorrectly_WithAllValues()
    {
        using var sw = new StringWriter();
        var detectedAt = new DateTime(2026, 4, 21, 10, 30, 0, DateTimeKind.Utc);
        Guid? userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid? roleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        // SodConflictRule.Id is long (T1 bigint PK) — ruleId column in CSV is long?
        long? ruleId = 42L;
        var actorId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        SodAuditLogCsvWriter.WriteRow(
            sw,
            detectedAt,
            userId,
            "Test User",
            roleId,
            "Admin",
            ruleId,
            "IDENTITY.USER.CREATE",
            "IDENTITY.USER.DELETE",
            "High",
            actorId,
            "Admin User",
            "Override for testing");

        var row = sw.ToString().TrimEnd();
        row.Should().Contain("2026-04-21T10:30:00.0000000Z");
        row.Should().Contain("11111111-1111-1111-1111-111111111111");
        row.Should().Contain("Test User");
        row.Should().Contain("IDENTITY.USER.CREATE");
        row.Should().Contain("IDENTITY.USER.DELETE");
        row.Should().Contain("High");
        row.Should().Contain("Override for testing");
    }

    [Fact]
    public void WriteRow_HandlesNullJustification()
    {
        using var sw = new StringWriter();
        var detectedAt = DateTime.UtcNow;
        Guid? userId = Guid.NewGuid();
        Guid? roleId = Guid.NewGuid();
        // SodConflictRule.Id is long T1 PK
        long? ruleId = 100L;
        var actorId = Guid.NewGuid();

        SodAuditLogCsvWriter.WriteRow(
            sw,
            detectedAt,
            userId,
            null,
            roleId,
            null,
            ruleId,
            "PERM.A",
            "PERM.B",
            "Low",
            actorId,
            null,
            null);

        // Should not throw, row ends with newline
        var row = sw.ToString();
        row.Should().NotBeNullOrEmpty();
        // Verify row has correct number of commas (11 commas for 12 columns)
        row.Count(c => c == ',').Should().Be(11);
    }

    [Fact]
    public void WriteRow_HandlesNullGuids()
    {
        using var sw = new StringWriter();
        var detectedAt = DateTime.UtcNow;
        var actorId = Guid.NewGuid();

        SodAuditLogCsvWriter.WriteRow(
            sw,
            detectedAt,
            null, // userId
            null,
            null, // roleId
            null,
            null, // ruleId
            "PERM.A",
            "PERM.B",
            "Low",
            actorId,
            null,
            null);

        // Should not throw, nullable guids render as empty strings
        var row = sw.ToString();
        row.Should().NotBeNull();
    }

    [Fact]
    public void WriteRow_EscapesFormulaInJustification()
    {
        using var sw = new StringWriter();
        var detectedAt = DateTime.UtcNow;

        SodAuditLogCsvWriter.WriteRow(
            sw,
            detectedAt,
            Guid.NewGuid(),
            "User",
            Guid.NewGuid(),
            "Role",
            // ruleId is long? (SodConflictRule.Id is T1 bigint PK)
            (long?)200L,
            "PERM.A",
            "PERM.B",
            "Medium",
            Guid.NewGuid(),
            "Actor",
            "=HYPERLINK(\"http://evil\")");

        var row = sw.ToString();
        // Should contain the escaped formula (single quote prefix + RFC 4180 wrapping)
        row.Should().Contain("'=HYPERLINK");
    }
}
