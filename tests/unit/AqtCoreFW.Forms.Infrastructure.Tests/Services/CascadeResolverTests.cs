using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Infrastructure.Services;
using FluentAssertions;

namespace AqtCoreFW.Forms.Infrastructure.Tests.Services;

/// <summary>
/// P3 (cascade v1) — parent-value filtering in the data-source resolvers, exercised via the pure
/// helpers so the semantics are tested without HTTP/reflection.
///
/// External (spike §3): parent values become UriBuilder-escaped query params (NOT string-concat), and
/// the cache-key MUST carry the parent values (else a filtered child returns another parent's cached
/// list — the quietest failure). No parents ⇒ URL + cache-key byte-identical to today (backward guard).
///
/// Enum (spike §3, user 2026-08-05): a config-driven parentFilterMap narrows the option-set;
/// multi-parent ⇒ intersection; unmapped parentValue ⇒ fail-safe empty; no parents ⇒ full set.
/// </summary>
public sealed class CascadeResolverTests
{
    private const string ApiUrl = "https://api.example.com/wards";
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Dictionary<string, string> P(params (string k, string v)[] kv) =>
        kv.ToDictionary(x => x.k, x => x.v, StringComparer.Ordinal);

    // ---------- External: BuildRequestUrl ----------

    [Fact]
    public void BuildRequestUrl_NoParents_ReturnsUrlUnchanged()
    {
        ExternalDataSourceResolver.BuildRequestUrl(ApiUrl, null)
            .Should().Be(ApiUrl, "no parents = backward static path");
        ExternalDataSourceResolver.BuildRequestUrl(ApiUrl, new Dictionary<string, string>())
            .Should().Be(ApiUrl);
    }

    [Fact]
    public void BuildRequestUrl_WithParent_AppendsEscapedQueryParam()
    {
        var url = ExternalDataSourceResolver.BuildRequestUrl(ApiUrl, P(("provinceCode", "HN")));

        url.Should().Contain("provinceCode=HN");
        url.Should().StartWith("https://api.example.com/wards?");
    }

    [Fact]
    public void BuildRequestUrl_EscapesInjectionCharacters()
    {
        // A parent value with query-breaking chars must be percent-escaped, not concatenated raw.
        var url = ExternalDataSourceResolver.BuildRequestUrl(ApiUrl, P(("q", "a&b=c d")));

        url.Should().NotContain("a&b=c d");
        url.Should().Contain("a%26b%3Dc%20d");
    }

    [Fact]
    public void BuildRequestUrl_PreservesExistingQuery()
    {
        var url = ExternalDataSourceResolver.BuildRequestUrl(
            "https://api.example.com/x?fixed=1", P(("provinceCode", "HN")));

        url.Should().Contain("fixed=1");
        url.Should().Contain("provinceCode=HN");
    }

    [Fact]
    public void BuildRequestUrl_DoesNotAlterHostOrScheme()
    {
        var url = ExternalDataSourceResolver.BuildRequestUrl(ApiUrl, P(("provinceCode", "HN")));
        var uri = new Uri(url);

        uri.Scheme.Should().Be("https");
        uri.Host.Should().Be("api.example.com");
    }

    // ---------- External: BuildCacheKey ----------

    [Fact]
    public void BuildCacheKey_NoParents_ByteIdenticalToLegacyFormat()
    {
        var key = ExternalDataSourceResolver.BuildCacheKey(ApiUrl, Tenant, null);

        key.Should().Be($"forms:ext-ds:{Uri.EscapeDataString(ApiUrl)}:{Tenant}",
            "legacy cache-key format must be preserved when there are no parents (no cache-miss storm)");
    }

    [Fact]
    public void BuildCacheKey_DifferentParentValues_ProduceDifferentKeys()
    {
        var keyHn  = ExternalDataSourceResolver.BuildCacheKey(ApiUrl, Tenant, P(("provinceCode", "HN")));
        var keyHcm = ExternalDataSourceResolver.BuildCacheKey(ApiUrl, Tenant, P(("provinceCode", "HCM")));

        keyHn.Should().NotBe(keyHcm, "each parent value must map to its own cached list");
    }

    [Fact]
    public void BuildCacheKey_SameParentsDifferentOrder_ProduceSameKey()
    {
        var a = ExternalDataSourceResolver.BuildCacheKey(ApiUrl, Tenant, P(("a", "1"), ("b", "2")));
        var b = ExternalDataSourceResolver.BuildCacheKey(ApiUrl, Tenant, P(("b", "2"), ("a", "1")));

        a.Should().Be(b, "deterministic ordinal-key ordering");
    }

    // ---------- Enum: ApplyParentFilter ----------

    private static List<DataSourceOptionDto> Opts(params string[] values) =>
        values.Select(v => new DataSourceOptionDto(v, $"L{v}")).ToList();

    private static Dictionary<string, List<string>> Map(params (string parent, string[] allowed)[] m) =>
        m.ToDictionary(x => x.parent, x => x.allowed.ToList(), StringComparer.Ordinal);

    [Fact]
    public void ApplyParentFilter_NoParents_ReturnsFullSet()
    {
        var result = EnumDataSourceResolver.ApplyParentFilter(
            Opts("1", "2", "3"), Map(("HN", new[] { "1" })), parentValues: null);

        result.Should().HaveCount(3, "no parent selected = full enum set (backward)");
    }

    [Fact]
    public void ApplyParentFilter_MappedParent_KeepsOnlyAllowedValues()
    {
        var result = EnumDataSourceResolver.ApplyParentFilter(
            Opts("1", "2", "3"),
            Map(("HN", new[] { "1", "2" })),
            P(("provinceCode", "HN")));

        result.Select(o => o.Value).Should().BeEquivalentTo(new[] { "1", "2" });
    }

    [Fact]
    public void ApplyParentFilter_MultiParent_IntersectsAllowedSets()
    {
        var result = EnumDataSourceResolver.ApplyParentFilter(
            Opts("1", "2", "3"),
            Map(("HN", new[] { "1", "2" }), ("Q1", new[] { "2", "3" })),
            P(("provinceCode", "HN"), ("districtCode", "Q1")));

        result.Select(o => o.Value).Should().BeEquivalentTo(new[] { "2" }, "intersection of {1,2} and {2,3}");
    }

    [Fact]
    public void ApplyParentFilter_UnmappedParentValue_ReturnsEmptyFailSafe()
    {
        var result = EnumDataSourceResolver.ApplyParentFilter(
            Opts("1", "2", "3"),
            Map(("HN", new[] { "1" })),
            P(("provinceCode", "UNKNOWN")));

        result.Should().BeEmpty("a parentValue with no map entry must fail-safe to empty, never leak the full set");
    }

    [Fact]
    public void ApplyParentFilter_ParentsButNoMap_ReturnsEmptyFailSafe()
    {
        var result = EnumDataSourceResolver.ApplyParentFilter(
            Opts("1", "2", "3"), parentFilterMap: null, P(("provinceCode", "HN")));

        result.Should().BeEmpty("filter intended but no map configured = fail-safe empty");
    }
}
