using System.Text.RegularExpressions;
using FluentAssertions;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Architecture guard for the Helm NetworkPolicy egress rules
/// (infra/helm/aqtcorefw/templates/network-policy.yaml).
///
/// Kubernetes admits an egress list item to ANY destination when the item declares
/// <c>ports:</c> but no <c>to:</c> selector. The chart historically shipped one such
/// unscoped item covering 443 plus the OTEL ports 4317/4318 — an exfiltration path
/// that contradicts the default-deny posture. This guard freezes the narrowed shape:
/// <list type="bullet">
///   <item>OTEL ports (4317/4318) are emitted only behind <c>egress.otel.enabled</c> and
///   are always scoped to a namespaceSelector (never any-destination).</item>
///   <item>External HTTPS (443) is CIDR-narrowed when <c>egress.httpsCidrs</c> is populated,
///   with a single documented broad-443 fallback when it is empty.</item>
///   <item>The only intentionally unscoped egress ports are DNS (53) and that 443 fallback.</item>
/// </list>
///
/// Note: this asserts the Helm TEMPLATE text — <c>helm</c>/<c>conftest</c> are not part of
/// this repo's toolchain, so the test verifies the render guards, not a full render. Every
/// egress item in this template is written as either <c>    - to:</c> (scoped) or
/// <c>    - ports:</c> (unscoped), which lets the scan classify items structurally.
/// </summary>
public sealed class NetworkPolicyEgressGuardTests
{
    private const string TemplateRelativePath =
        "infra/helm/aqtcorefw/templates/network-policy.yaml";

    // 4-space list markers distinguish scoped (`- to:`) from unscoped (`- ports:`) egress items.
    private static readonly Regex UnscopedItemStart = new(@"^    - ports:\s*$", RegexOptions.Compiled);
    private static readonly Regex ScopedItemStart = new(@"^    - to:\s*$", RegexOptions.Compiled);
    private static readonly Regex PortLine = new(@"^\s*port:\s*(\d+)", RegexOptions.Compiled);

    [Fact]
    public void UnscopedEgressPorts_AreOnly_DnsAndHttpsFallback()
    {
        var egress = ReadEgressSection();
        var unscopedPorts = new List<int>();
        var insideUnscopedItem = false;

        foreach (var raw in egress.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (UnscopedItemStart.IsMatch(line)) { insideUnscopedItem = true; continue; }
            if (ScopedItemStart.IsMatch(line)) { insideUnscopedItem = false; continue; }

            var m = PortLine.Match(line);
            if (m.Success && insideUnscopedItem)
                unscopedPorts.Add(int.Parse(m.Groups[1].Value));
        }

        // DNS (53) + the CIDR-less 443 fallback are the only sanctioned any-destination ports.
        unscopedPorts.Should().OnlyContain(p => p == 53 || p == 443,
            "an egress item without a `to:` selector reaches ANY destination; only DNS and the " +
            "documented external-443 fallback may be unscoped. A DB/OTEL/other port appearing here " +
            "means a `to:` selector was dropped (exfil regression).");
        // Non-vacuous: a parse failure would leave the list empty, so anchor on the DNS rule.
        unscopedPorts.Should().Contain(53, "the DNS egress rule must remain present.");
    }

    [Fact]
    public void OtelPorts_AreGatedByEnabledFlag_AndScopedToNamespace()
    {
        var egress = ReadEgressSection();
        var otelBlock = ExtractGuardedBlock(egress, "networkPolicy.egress.otel.enabled");

        otelBlock.Should().NotBeNull(
            "OTEL ports must be emitted only behind the egress.otel.enabled guard.");
        otelBlock!.Should().Contain("- to:", "the OTEL egress rule must carry a `to:` selector.")
            .And.Contain("namespaceSelector")
            .And.Contain("otel.namespace");

        // Each OTEL port must be declared exactly where the gated, scoped rule lives — nowhere else.
        foreach (var port in new[] { 4317, 4318 })
        {
            var total = CountPortLines(egress, port);
            var inBlock = CountPortLines(otelBlock, port);
            total.Should().BeGreaterThan(0, $"OTEL port {port} must be declared in the egress rules.");
            total.Should().Be(inBlock,
                $"OTEL port {port} must appear only inside the enabled-gated, namespace-scoped OTEL rule " +
                "(any occurrence outside it is an unscoped any-destination hole).");
        }
    }

    [Fact]
    public void ExternalHttps_HasStrictIpBlockAllowlist_AndBroadFallback()
    {
        var egress = ReadEgressSection();

        egress.Should().Contain("{{- if .Values.networkPolicy.egress.httpsCidrs }}",
            "external 443 must be CIDR-narrowable via networkPolicy.egress.httpsCidrs.");

        var strict = ExtractGuardedBlock(egress, "networkPolicy.egress.httpsCidrs");
        strict.Should().NotBeNull();
        strict!.Should().Contain("ipBlock", "populating httpsCidrs must emit scoped ipBlock egress rules.")
            .And.Contain("cidr")
            .And.Contain("{{- else }}", "an empty httpsCidrs must fall back to the broad-443 rule.");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string ReadEgressSection()
    {
        var relative = TemplateRelativePath.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(FindRepoRoot(), relative);
        File.Exists(path).Should().BeTrue($"expected the NetworkPolicy template at {TemplateRelativePath}");

        var text = File.ReadAllText(path);
        var idx = text.IndexOf("\n  egress:", StringComparison.Ordinal);
        idx.Should().BeGreaterThan(-1, "the NetworkPolicy template must define an egress section.");
        return text[idx..];
    }

    // Helm control tokens that open (+1) or close (-1) a nesting level.
    private static readonly Regex ControlToken = new(@"\{\{-\s*(if|range|with|end)\b", RegexOptions.Compiled);

    /// <summary>
    /// Returns the text between the <c>{{- if .Values.&lt;flagPath&gt; }}</c> directive and its
    /// MATCHING <c>{{- end }}</c> (spanning any <c>{{- else }}</c>), or null when the guard is
    /// absent/unbalanced. Balance-aware: a nested <c>{{- range }}</c>/<c>{{- end }}</c> (e.g. the
    /// ipBlock loop) does not prematurely close the block. Anchors on the directive (not a bare
    /// flag substring) so doc comments do not false-match.
    /// </summary>
    private static string? ExtractGuardedBlock(string text, string flagPath)
    {
        var directive = $"{{{{- if .Values.{flagPath} }}}}";
        var ifIdx = text.IndexOf(directive, StringComparison.Ordinal);
        if (ifIdx < 0) return null;

        var depth = 0;
        foreach (Match t in ControlToken.Matches(text))
        {
            if (t.Index < ifIdx) continue; // skip guards that close before our block opens

            if (t.Groups[1].Value == "end")
            {
                if (--depth == 0) return text[ifIdx..t.Index];
            }
            else
            {
                depth++;
            }
        }
        return null; // unbalanced
    }

    private static int CountPortLines(string text, int port)
        => Regex.Matches(text, $@"port:\s*{port}\b").Count;

    /// <summary>
    /// Walks up from the test binary location until it finds a directory containing both
    /// <c>src/</c> and <c>tests/</c>. Mirrors the helper in <see cref="MarsConnectionStringGuardTests"/>.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(
            Directory.Exists(Path.Combine(dir.FullName, "src")) &&
            Directory.Exists(Path.Combine(dir.FullName, "tests"))))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull(
            $"could not locate repo root walking up from {AppContext.BaseDirectory} (expected src/ + tests/)");
        return dir!.FullName;
    }
}
