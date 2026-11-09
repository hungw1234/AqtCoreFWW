using AqtCoreFW.SharedKernel.Tests.Architecture.Helpers;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.SharedKernel.Tests.Architecture.Helpers;

/// <summary>
/// Unit tests for SourceCodeScanner.IsSuppressed — the per-line suppress directive check.
/// Phase-07 PR-D1 (2026-05-07). These tests exercise the helper directly with crafted strings
/// (no I/O, no repo scan). Directive grammar: // arch:suppress &lt;RuleCode&gt; &lt;Entity&gt;: &lt;reason≥3chars&gt;
/// </summary>
[Trait("Category", "Architecture")]
public sealed class SourceCodeScannerSuppressTests
{
    private const string RuleCode = "R_T2FK";
    private const string Entity = "FileRecord";

    // ──────────────────────────────────────────────────────────────────────────
    // Case 1: same-line trailing directive → suppressed
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void SameLineTrailingDirective_MatchingRuleAndEntity_ReturnsSuppressed()
    {
        var currentLine = "    long FileRecordId, // arch:suppress R_T2FK FileRecord: T3 cross-aggregate FK by design";
        var prevLine    = "    // unrelated comment";

        var result = SourceCodeScanner.IsSuppressed(currentLine, prevLine, RuleCode, Entity);

        result.Should().BeTrue("a trailing same-line suppress directive with matching rule+entity must suppress the violation");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Case 2: preceding-line directive (pure comment) → suppressed
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void PrecedingLineDirective_MatchingRuleAndEntity_ReturnsSuppressed()
    {
        var prevLine    = "    // arch:suppress R_T2FK FileRecord: T3 cross-aggregate FK by design (Phase-4b B3b)";
        var currentLine = "    long FileRecordId,";

        var result = SourceCodeScanner.IsSuppressed(currentLine, prevLine, RuleCode, Entity);

        result.Should().BeTrue("a preceding-line pure-comment suppress directive must suppress the violation");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Case 3: wrong rule code → NOT suppressed
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void WrongRuleCode_DoesNotSuppress()
    {
        // Directive has R_OTHER instead of R_T2FK
        var currentLine = "    long FileRecordId, // arch:suppress R_OTHER FileRecord: some reason here";
        var prevLine    = (string?)null;

        var result = SourceCodeScanner.IsSuppressed(currentLine, prevLine, RuleCode, Entity);

        result.Should().BeFalse("a directive with a different rule code must not suppress this rule's violations");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Case 4: wrong entity name → NOT suppressed
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void WrongEntityName_DoesNotSuppress()
    {
        // Directive mentions OrgUnit, not FileRecord
        var currentLine = "    long FileRecordId, // arch:suppress R_T2FK OrgUnit: some reason here";
        var prevLine    = (string?)null;

        var result = SourceCodeScanner.IsSuppressed(currentLine, prevLine, RuleCode, Entity);

        result.Should().BeFalse("a directive whose entity name does not match the extracted entity must not suppress");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Case 5: directive missing colon + reason (forge-resistance) → NOT suppressed
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void MissingReasonText_DoesNotSuppress()
    {
        // No colon + reason at all — incomplete directive
        var currentLine = "    long FileRecordId, // arch:suppress R_T2FK FileRecord";
        var prevLine    = "    // arch:suppress R_T2FK FileRecord";

        var result = SourceCodeScanner.IsSuppressed(currentLine, prevLine, RuleCode, Entity);

        result.Should().BeFalse("a directive without ':' and a ≥3-char reason must not suppress (forge-resistance)");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Case 5b: directive has colon but reason is only 1-2 chars → NOT suppressed
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TooShortReason_DoesNotSuppress()
    {
        // Reason is only 2 chars "ok" — below ≥3-char threshold
        var currentLine = "    long FileRecordId, // arch:suppress R_T2FK FileRecord: ok";
        var prevLine    = "    // arch:suppress R_T2FK FileRecord: ok";

        var result = SourceCodeScanner.IsSuppressed(currentLine, prevLine, RuleCode, Entity);

        result.Should().BeFalse("a directive with a reason shorter than 3 chars must not suppress");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Case 6: directive is two lines above (only N-1 is checked) → NOT suppressed
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void DirectiveTwoLinesAbove_DoesNotSuppress()
    {
        // prevLine is a blank/non-directive line; the directive is "further up" (not passed in)
        var currentLine = "    long FileRecordId,";
        var prevLine    = "    public sealed record FileVersionDto("; // intervening non-directive line

        var result = SourceCodeScanner.IsSuppressed(currentLine, prevLine, RuleCode, Entity);

        result.Should().BeFalse("only the immediately preceding line is checked; a directive two lines up must not suppress");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Case 7 (M2 PR-D2): `///` doc-comment preceding line → NOT suppressed
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TripleSlashDocCommentPreceding_DoesNotSuppress()
    {
        // A `///` XML doc-comment line containing the directive text must NOT activate suppression.
        // Defends against accidentally pasting suppress text into a doc-comment.
        var prevLine    = "    /// arch:suppress R_T2FK FileRecord: T3 cross-aggregate FK by design";
        var currentLine = "    long FileRecordId,";

        var result = SourceCodeScanner.IsSuppressed(currentLine, prevLine, RuleCode, Entity);

        result.Should().BeFalse("`///` doc-comment lines must not activate the suppress directive (M2 PR-D2 precision)");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Case 8 (M2 PR-D2): `////` quad-slash preceding line → NOT suppressed
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void QuadSlashCommentPreceding_DoesNotSuppress()
    {
        // Edge case: `////` is technically still a comment, but lookbehind rejects it.
        var prevLine    = "    //// arch:suppress R_T2FK FileRecord: edge case quad slash";
        var currentLine = "    long FileRecordId,";

        var result = SourceCodeScanner.IsSuppressed(currentLine, prevLine, RuleCode, Entity);

        result.Should().BeFalse("`////` comment lines must not activate the suppress directive");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Case 9 (M2 PR-D2): `///` doc-comment text mid-line trailing → NOT suppressed
    // ──────────────────────────────────────────────────────────────────────────
    [Fact]
    public void TripleSlashTrailingMidLine_DoesNotSuppress()
    {
        // Hybrid: code on left, `///` arch:suppress text on right. Lookbehind rejects (preceding char is `/`).
        var currentLine = "    long FileRecordId, /// arch:suppress R_T2FK FileRecord: hybrid form";
        var prevLine    = (string?)null;

        var result = SourceCodeScanner.IsSuppressed(currentLine, prevLine, RuleCode, Entity);

        result.Should().BeFalse("`///` text appearing trailing on the same line must not activate the suppress directive");
    }
}
