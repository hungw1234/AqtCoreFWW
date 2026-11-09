using AqtCoreFW.Architecture.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Architecture guard (ARCH-FU2): every paginated query handler reading
/// <c>request.PageSize</c> (or a recognized parameter alias) must defend against
/// crafted oversized PageSize requests via one of:
///
/// <list type="bullet">
///   <item><description><c>Math.Clamp(request.PageSize, 1, Max)</c> — inline clamp (bounds both directions)</description></item>
///   <item><description><see cref="AqtCoreFW.SharedKernel.Application.Pagination.PaginationDefaults"/> usage</description></item>
///   <item><description>FluentValidation <c>InclusiveBetween(1, Max)</c> on the PageSize rule</description></item>
/// </list>
/// <c>Math.Min</c> is intentionally rejected (ARCH-FU2.1): it caps the upper bound but leaks
/// non-positive PageSize through to SQL. Prefer <c>Math.Clamp(_, 1, max)</c>.
///
/// Existing unclamped handlers are grandfathered via <see cref="PaginationClampScanner.GrandfatheredFiles"/>.
/// New entries to the allowlist require code-review justification; preferred remediation
/// is to add a defense token instead. Per-module sweeps (carry-over FUs) shrink the
/// allowlist over time until it can be removed entirely.
/// </summary>
public sealed class PaginationClampScannerTests
{
    [Fact]
    public void Handlers_Reading_PageSize_MustClamp_OrBeGrandfathered()
    {
        var repoRoot = FindRepoRoot();
        var violations = PaginationClampScanner.Scan(repoRoot);

        violations.Should().BeEmpty(
            "every paginated query handler must clamp PageSize via Math.Clamp / " +
            "PaginationDefaults / FluentValidation InclusiveBetween, OR be listed in " +
            "PaginationClampScanner.GrandfatheredFiles with justification. " +
            "Found:\n{0}",
            string.Join("\n", violations.Select(v => $"  {v.RelativePath}:{v.LineNumber} → {v.Snippet}")));
    }

    [Fact]
    public void Grandfathered_List_DoesNotShrink_Silently()
    {
        // Snapshot test: ensures intentional reductions to the allowlist
        // surface as a test failure (forcing PR review) rather than silently
        // expanding the rule's coverage. When a sweep FU fixes a handler,
        // update both the handler AND the expected count here.
        const int expectedCount = 0;
        PaginationClampScanner.GrandfatheredFiles.Should().HaveCount(expectedCount,
            "if you fixed an unclamped handler, decrement this count. " +
            "If you added a new grandfathered entry — STOP and prefer fixing the handler.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(
            Directory.Exists(Path.Combine(dir.FullName, "src")) &&
            Directory.Exists(Path.Combine(dir.FullName, "tests"))))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("could not locate repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
