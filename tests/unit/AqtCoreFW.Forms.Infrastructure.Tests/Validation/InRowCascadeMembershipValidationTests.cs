using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Text.Json;

namespace AqtCoreFW.Forms.Infrastructure.Tests.Validation;

/// <summary>
/// M1 Phase-06 slice 6b: in-row cascade option-membership guard at submit time. When a TableField
/// child-column carries cascade dependencies, the submitted child value in each row MUST belong to
/// the option-set that the row's parent value resolves to on the server (client-supplied option-set
/// is never trusted). Enum/Internal resolvers fail 422; External resolvers soft-log warnings and let
/// the submit through (per Phase-06 user decision — N-rows × outbound HTTP = DoS surface).
/// </summary>
public sealed class InRowCascadeMembershipValidationTests
{
    private const long TemplateId = 1L;
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Fake enum resolver returning a fixed option-set for a fixed parent value.</summary>
    private sealed class FakeResolver : IDataSourceResolver
    {
        private readonly Dictionary<string, string[]> _byParentValue;
        public FakeResolver(Dictionary<string, string[]> byParentValue) => _byParentValue = byParentValue;

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<DataSourceOptionDto>> ResolveAsync(
            string dataSourceJson, Guid tenantId,
            IReadOnlyDictionary<string, string>? parentValues = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var pv = parentValues?.Values.FirstOrDefault() ?? string.Empty;
            var vals = _byParentValue.TryGetValue(pv, out var xs) ? xs : [];
            IReadOnlyList<DataSourceOptionDto> options =
                vals.Select(v => new DataSourceOptionDto(v, v)).ToList();
            return Task.FromResult(options);
        }
    }

    /// <summary>Resolver that always throws — proves External path fails soft (submit not blocked).</summary>
    private sealed class ThrowingExternalResolver : IDataSourceResolver
    {
        public Task<IReadOnlyList<DataSourceOptionDto>> ResolveAsync(
            string dataSourceJson, Guid tenantId,
            IReadOnlyDictionary<string, string>? parentValues = null,
            CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("upstream 500 — must NOT block the submit under external soft-log");
    }

    private static IDataSourceResolverFactory Factory(
        FormFieldType type, IDataSourceResolver resolver)
    {
        var f = Substitute.For<IDataSourceResolverFactory>();
        f.Create(type).Returns(resolver);
        return f;
    }

    private static FormValidationService Sut(
        IDataSourceResolverFactory factory,
        ILogger<FormValidationService>? logger = null) =>
        new(factory, logger ?? NullLogger<FormValidationService>.Instance);

    private static FormField TableParent(string key, long fieldId) =>
        MakeField(key, FormFieldType.TableField, id: fieldId);

    private static FormField ChildColumn(
        string key, FormFieldType type, long tableFieldId, string? dsJson = null) =>
        MakeField(key, type, id: 0, parentFieldId: tableFieldId, dataSourceJson: dsJson);

    /// <summary>Build a FormField with a caller-set Id (Entity.Id is protected-set — reflection here
    /// mirrors DB-assigned identity in-memory, needed so ParentFieldId lookups match a real parent).</summary>
    private static FormField MakeField(
        string key, FormFieldType type, long id, long? parentFieldId = null, string? dataSourceJson = null)
    {
        var f = FormField.Create(
            TemplateId, key, type, key, key, displayOrder: 1, required: false,
            addedInVersion: 1, dataSourceJson: dataSourceJson, parentFieldId: parentFieldId);
        typeof(SharedKernel.Domain.Entity)
            .GetProperty(nameof(SharedKernel.Domain.Entity.Id))!
            .SetValue(f, id);
        return f;
    }

    /// <summary>DataSourceJson carrying a `dependencies:[]` list — the cascade discriminator (non-empty ⇒ cascade path).</summary>
    private static string DsWithDeps(string dependsOnFieldKey, string filterParamName) =>
        $$"""{"Values":[{"code":"A"},{"code":"B"}],"dependencies":[{"dependsOnFieldKey":"{{dependsOnFieldKey}}","filterParamName":"{{filterParamName}}"}]}""";

    private static IDictionary<string, object?> TableData(string key, params object[] rows) =>
        new Dictionary<string, object?> { [key] = rows.ToList() };

    private static Dictionary<string, object?> Row((string k, object? v)[] cells) =>
        cells.ToDictionary(c => c.k, c => (object?)c.v);

    // ============================================================================
    // Enum/Internal: hard-reject (422) when child value ∉ parent's option-set.
    // ============================================================================

    [Fact]
    public async Task Enum_InRow_ChildValueInSet_NoError()
    {
        // parent=HN → child options {01001, 01002}. Row(HN, 01001) is valid.
        var resolver = new FakeResolver(new()
        {
            ["HN"] = new[] { "01001", "01002" },
            ["HCM"] = new[] { "79001" },
        });
        var factory = Factory(FormFieldType.EnumRef, resolver);

        var tbl   = TableParent("addresses", fieldId: 100);
        var tinh  = ChildColumn("tinh", FormFieldType.EnumRef, tableFieldId: 100);
        var xa    = ChildColumn("xa",   FormFieldType.EnumRef, tableFieldId: 100,
            dsJson: DsWithDeps("tinh", "provinceCode"));

        var data = TableData("addresses",
            Row([("tinh", "HN"), ("xa", "01001")]));

        var result = await Sut(factory).ValidateAsync([tbl, tinh, xa], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        resolver.CallCount.Should().Be(1, "one row × one cascade child = one resolve call");
    }

    [Fact]
    public async Task Enum_InRow_ChildValueNotInSet_ReturnsValidationError()
    {
        // parent=HN → options {01001}. Row(HN, 99999) is INVALID — child value not in set.
        var resolver = new FakeResolver(new() { ["HN"] = new[] { "01001" } });
        var factory = Factory(FormFieldType.EnumRef, resolver);

        var tbl  = TableParent("addresses", fieldId: 100);
        var tinh = ChildColumn("tinh", FormFieldType.EnumRef, tableFieldId: 100);
        var xa   = ChildColumn("xa",   FormFieldType.EnumRef, tableFieldId: 100,
            dsJson: DsWithDeps("tinh", "provinceCode"));

        var data = TableData("addresses",
            Row([("tinh", "HN"), ("xa", "99999")]));

        var result = await Sut(factory).ValidateAsync([tbl, tinh, xa], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e =>
            e.Metadata["Property"].Equals("xa") && e.Message.Contains("cascade"));
    }

    [Fact]
    public async Task Enum_InRow_MultipleRows_EachRowResolvedIndependently()
    {
        // Row 1 parent=HN valid; Row 2 parent=HCM child not in HCM's set → row-2 fails, row-1 passes.
        var resolver = new FakeResolver(new()
        {
            ["HN"]  = new[] { "01001" },
            ["HCM"] = new[] { "79001" },
        });
        var factory = Factory(FormFieldType.EnumRef, resolver);

        var tbl  = TableParent("addresses", fieldId: 100);
        var tinh = ChildColumn("tinh", FormFieldType.EnumRef, tableFieldId: 100);
        var xa   = ChildColumn("xa",   FormFieldType.EnumRef, tableFieldId: 100,
            dsJson: DsWithDeps("tinh", "provinceCode"));

        var data = TableData("addresses",
            Row([("tinh", "HN"),  ("xa", "01001")]),
            Row([("tinh", "HCM"), ("xa", "01001")])); // xa doesn't belong to HCM set

        var result = await Sut(factory).ValidateAsync([tbl, tinh, xa], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => e.Metadata["Property"].Equals("xa"));
        resolver.CallCount.Should().Be(2, "each row is resolved independently — server-side re-check");
    }

    // ============================================================================
    // External: soft-log — resolver failure MUST NOT block the submit.
    // ============================================================================

    [Fact]
    public async Task External_InRow_ResolverThrows_SubmitPassesWithoutError()
    {
        // External resolver throws (upstream 5xx) — Phase-06 rule: soft-log, do not block submit.
        var resolver = new ThrowingExternalResolver();
        var factory = Factory(FormFieldType.ExternalRef, resolver);

        var tbl  = TableParent("addresses", fieldId: 100);
        var tinh = ChildColumn("tinh", FormFieldType.ExternalRef, tableFieldId: 100);
        var xa   = ChildColumn("xa",   FormFieldType.ExternalRef, tableFieldId: 100,
            dsJson: DsWithDeps("tinh", "provinceCode"));

        var data = TableData("addresses",
            Row([("tinh", "HN"), ("xa", "any-value")]));

        var result = await Sut(factory).ValidateAsync([tbl, tinh, xa], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("external cascade membership check is soft-log per Phase-06 decision");
    }

    // ============================================================================
    // Backward guards.
    // ============================================================================

    [Fact]
    public async Task ChildColumn_NoDependencies_MembershipGuardSkipped()
    {
        // Backward path: child with no cascade `dependencies` = old flat option-set, not gated.
        var resolver = new FakeResolver(new()); // would return empty if called
        var factory = Factory(FormFieldType.EnumRef, resolver);

        var tbl  = TableParent("addresses", fieldId: 100);
        var tinh = ChildColumn("tinh", FormFieldType.EnumRef, tableFieldId: 100);
        var xa   = ChildColumn("xa",   FormFieldType.EnumRef, tableFieldId: 100); // no ds

        var data = TableData("addresses",
            Row([("tinh", "HN"), ("xa", "anything")]));

        var result = await Sut(factory).ValidateAsync([tbl, tinh, xa], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        resolver.CallCount.Should().Be(0, "no dependencies → skip cascade re-resolve");
    }

    [Fact]
    public async Task NullTenantId_MembershipGuardSkipped_BackwardSafe()
    {
        // Backward guard for legacy callers that never pass tenantId — skip the guard, keep old shape.
        var resolver = new FakeResolver(new() { ["HN"] = new[] { "01001" } });
        var factory = Factory(FormFieldType.EnumRef, resolver);

        var tbl  = TableParent("addresses", fieldId: 100);
        var tinh = ChildColumn("tinh", FormFieldType.EnumRef, tableFieldId: 100);
        var xa   = ChildColumn("xa",   FormFieldType.EnumRef, tableFieldId: 100,
            dsJson: DsWithDeps("tinh", "provinceCode"));

        var data = TableData("addresses",
            Row([("tinh", "HN"), ("xa", "99999")])); // would fail if guard ran

        var result = await Sut(factory).ValidateAsync([tbl, tinh, xa], data, tenantId: null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("null tenantId (legacy caller) skips the membership guard");
        resolver.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task NoTableFieldInFields_MembershipGuardNotInvoked()
    {
        // No TableField at all → the guard's outer loop finds nothing → resolver never called.
        var factoryStrict = Substitute.For<IDataSourceResolverFactory>();
        factoryStrict.Create(default).ReturnsForAnyArgs(_ =>
            throw new InvalidOperationException("no TableField → factory.Create should not be called"));

        var topEnum = MakeField("simple", FormFieldType.EnumRef, id: 42);
        var data    = new Dictionary<string, object?> { ["simple"] = "A" };

        var result = await Sut(factoryStrict).ValidateAsync([topEnum], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task NonRefChildType_WithSpuriousDependencies_SkipsGuardAndDoesNotCrash()
    {
        // C-1 fix: publish-time CascadeDependencyValidator does not gate on FormFieldType, so a
        // hand-crafted DataSourceJson with `dependencies:[]` on a Text child would reach the guard
        // and previously threw ArgumentException from DataSourceResolverFactory.Create (non-Ref).
        // Defense-in-depth: guard skips unsupported types instead of crashing the whole submit.
        var factoryStrict = Substitute.For<IDataSourceResolverFactory>();
        factoryStrict.Create(default).ReturnsForAnyArgs(_ =>
            throw new InvalidOperationException("factory.Create must NOT be called for non-Ref child type"));

        var tbl  = TableParent("addresses", fieldId: 100);
        var tinh = ChildColumn("tinh", FormFieldType.EnumRef, tableFieldId: 100);
        var text = ChildColumn("note", FormFieldType.Text, tableFieldId: 100,
            dsJson: DsWithDeps("tinh", "provinceCode")); // Text + dependencies = admin misconfig

        var data = TableData("addresses",
            Row([("tinh", "HN"), ("note", "anything")]));

        var result = await Sut(factoryStrict).ValidateAsync([tbl, tinh, text], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("non-Ref child with cascade deps skips guard, does not crash");
    }

    [Fact]
    public async Task Enum_InRow_MultiParentDependency_AllParentsPassedToResolver()
    {
        // Multi-parent cascade: child depends on TWO parents. BuildParentValues must pack both into
        // the resolver call keyed by each dependency's filterParamName.
        Dictionary<string, string>? captured = null;
        var capturingResolver = new CapturingResolver(pv => { captured = pv?.ToDictionary(kv => kv.Key, kv => kv.Value); });
        var factory = Factory(FormFieldType.EnumRef, capturingResolver);

        var tbl    = TableParent("rows", fieldId: 100);
        var parent1 = ChildColumn("country", FormFieldType.EnumRef, tableFieldId: 100);
        var parent2 = ChildColumn("region",  FormFieldType.EnumRef, tableFieldId: 100);
        var child   = ChildColumn("city", FormFieldType.EnumRef, tableFieldId: 100,
            dsJson: $$"""{"dependencies":[{"dependsOnFieldKey":"country","filterParamName":"c"},{"dependsOnFieldKey":"region","filterParamName":"r"}]}""");

        var data = TableData("rows",
            Row([("country", "VN"), ("region", "N"), ("city", "HN")]));

        var result = await Sut(factory).ValidateAsync([tbl, parent1, parent2, child], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Should().ContainKey("c").WhoseValue.Should().Be("VN");
        captured.Should().ContainKey("r").WhoseValue.Should().Be("N");
    }

    /// <summary>Resolver that captures the parentValues dict it receives - proves multi-parent packing.</summary>
    private sealed class CapturingResolver : IDataSourceResolver
    {
        private readonly Action<IReadOnlyDictionary<string, string>?> _capture;
        public CapturingResolver(Action<IReadOnlyDictionary<string, string>?> capture) => _capture = capture;

        public Task<IReadOnlyList<DataSourceOptionDto>> ResolveAsync(
            string dataSourceJson, Guid tenantId,
            IReadOnlyDictionary<string, string>? parentValues = null,
            CancellationToken cancellationToken = default)
        {
            _capture(parentValues);
            // Return the submitted child value inside the set so no false-fail masks the capture check.
            IReadOnlyList<DataSourceOptionDto> opts = new List<DataSourceOptionDto> { new("HN", "HN") };
            return Task.FromResult(opts);
        }
    }

    // ============================================================================
    // Dedup (v1.1 FORMS-CASCADE-INROW-GUARD-N-PLUS-1-DEDUP): resolver called once per
    // distinct (child, parent-tuple) combination — not once per row. Guard is O(distinct
    // combos), not O(rows × children). Semantic verdict unchanged: same input → same errors.
    // ============================================================================

    [Fact]
    public async Task Enum_InRow_DuplicateParentAcrossRows_ResolvedOnce_VerdictPerRow()
    {
        // Row 1 (HN, 01001) valid; Row 2 (HN, 99999) invalid. Same parent tuple (HN) → 1 resolve call
        // regardless of row count; per-row membership decision still applied independently.
        var resolver = new FakeResolver(new() { ["HN"] = new[] { "01001" } });
        var factory = Factory(FormFieldType.EnumRef, resolver);

        var tbl  = TableParent("addresses", fieldId: 100);
        var tinh = ChildColumn("tinh", FormFieldType.EnumRef, tableFieldId: 100);
        var xa   = ChildColumn("xa",   FormFieldType.EnumRef, tableFieldId: 100,
            dsJson: DsWithDeps("tinh", "provinceCode"));

        var data = TableData("addresses",
            Row([("tinh", "HN"), ("xa", "01001")]),
            Row([("tinh", "HN"), ("xa", "99999")]));

        var result = await Sut(factory).ValidateAsync([tbl, tinh, xa], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(e => e.Metadata["Property"].Equals("xa"));
        resolver.CallCount.Should().Be(1,
            "duplicate parent-tuples across rows resolve once (dedup); membership check re-runs per row");
    }

    [Fact]
    public async Task Enum_InRow_AdversarialNRows_ResolverCalledOncePerDistinctParentTuple()
    {
        // Adversarial N-rows-per-DoS scenario: 500 rows, 3 cascade children, but only 2 distinct
        // parent-tuples across all rows. Pre-dedup = 1500 resolver calls; post-dedup = 6 calls
        // (2 distinct parents × 3 cascade children). Assert the O(distinct-combos) bound directly.
        var resolver = new FakeResolver(new()
        {
            ["HN"]  = new[] { "01001" },
            ["HCM"] = new[] { "79001" },
        });
        var factory = Factory(FormFieldType.EnumRef, resolver);

        var tbl    = TableParent("addresses", fieldId: 100);
        var tinh   = MakeField("tinh", FormFieldType.EnumRef, id: 101, parentFieldId: 100);
        // Each cascade child must carry a DB-unique Id — dedup key = (childFieldId, parent-tuple).
        var xa1    = MakeField("xa1", FormFieldType.EnumRef, id: 102, parentFieldId: 100,
            dataSourceJson: DsWithDeps("tinh", "provinceCode"));
        var xa2    = MakeField("xa2", FormFieldType.EnumRef, id: 103, parentFieldId: 100,
            dataSourceJson: DsWithDeps("tinh", "provinceCode"));
        var xa3    = MakeField("xa3", FormFieldType.EnumRef, id: 104, parentFieldId: 100,
            dataSourceJson: DsWithDeps("tinh", "provinceCode"));

        var rows = new List<object>();
        for (int i = 0; i < 250; i++)
        {
            rows.Add(Row([("tinh", "HN"),  ("xa1", "01001"), ("xa2", "01001"), ("xa3", "01001")]));
            rows.Add(Row([("tinh", "HCM"), ("xa1", "79001"), ("xa2", "79001"), ("xa3", "79001")]));
        }
        var data = new Dictionary<string, object?> { ["addresses"] = rows };

        var result = await Sut(factory).ValidateAsync([tbl, tinh, xa1, xa2, xa3], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        resolver.CallCount.Should().Be(6,
            "2 distinct parent-tuples × 3 cascade children = 6 resolver calls, independent of the 500-row count");
    }

    [Fact]
    public async Task Enum_InRow_MultiParent_DelimiterInValue_NoBucketCollision()
    {
        // C-1 ORACLE (blocking-fixed in-slice): before the percent-encoding fix, two rows whose
        // multi-parent values happened to canonicalize identically under a naive `key=value|key=value`
        // scheme would silently share the FIRST row's option-set. This is the same bypass class the
        // guard exists to prevent. Post-fix (Uri.EscapeDataString on both key and value), each
        // semantically-distinct parent-tuple gets its own bucket → its own resolver call → its own
        // membership decision.
        //
        // Repro (staff-eng review): rowA = { "a": "foo|b=bar", "b": "baz" }; rowB = { "a": "foo", "b": "bar|b=baz" }.
        // Under the ambiguous scheme both stringify to "a=foo|b=bar|b=baz" (collision). Post-fix they
        // resolve to distinct canonical strings.
        //
        // We use a capturing resolver that returns DIFFERENT option-sets keyed by the raw tuple string
        // so we can prove: (a) resolver was called twice (not once — bucket did NOT collide),
        // (b) the row-B child value is checked against row-B's true option-set (not row-A's).
        var rowATuple = "a=foo|b=bar,b=baz";
        var rowBTuple = "a=foo,b=bar|b=baz";
        var callsByTuple = new Dictionary<string, int>();
        var resolver = new TupleDrivenResolver(
            capture: pv =>
            {
                var raw = string.Join(",", pv!.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                             .Select(kv => $"{kv.Key}={kv.Value}"));
                callsByTuple[raw] = callsByTuple.GetValueOrDefault(raw) + 1;
                // Row A tuple → allowed set { "X" }; Row B tuple → allowed set { "Y" }. Values swapped
                // on purpose so a bucket collision (rowA's set applied to rowB) would fail rowB's "Y".
                return raw == rowATuple ? new[] { "X" }
                     : raw == rowBTuple ? new[] { "Y" }
                     : Array.Empty<string>();
            });
        var factory = Factory(FormFieldType.EnumRef, resolver);

        var tbl     = TableParent("rows", fieldId: 100);
        var parentA = MakeField("a", FormFieldType.EnumRef, id: 101, parentFieldId: 100);
        var parentB = MakeField("b", FormFieldType.EnumRef, id: 102, parentFieldId: 100);
        var child   = MakeField("c", FormFieldType.EnumRef, id: 103, parentFieldId: 100,
            dataSourceJson: $$"""{"dependencies":[{"dependsOnFieldKey":"a","filterParamName":"a"},{"dependsOnFieldKey":"b","filterParamName":"b"}]}""");

        var data = TableData("rows",
            Row([("a", "foo|b=bar"), ("b", "baz"),        ("c", "X")]),
            Row([("a", "foo"),        ("b", "bar|b=baz"), ("c", "Y")]));

        var result = await Sut(factory).ValidateAsync([tbl, parentA, parentB, child], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("both rows submit the value that belongs to their own resolved set");
        callsByTuple.Should().HaveCount(2, "distinct multi-parent tuples must NOT share a dedup bucket");
        callsByTuple[rowATuple].Should().Be(1);
        callsByTuple[rowBTuple].Should().Be(1);
    }

    /// <summary>Resolver that maps captured parentValues (raw, per-row) to an option-set — proves each
    /// row's true tuple was passed and used, not a bucket-collided predecessor's.</summary>
    private sealed class TupleDrivenResolver : IDataSourceResolver
    {
        private readonly Func<IReadOnlyDictionary<string, string>?, string[]> _capture;
        public TupleDrivenResolver(Func<IReadOnlyDictionary<string, string>?, string[]> capture) => _capture = capture;

        public Task<IReadOnlyList<DataSourceOptionDto>> ResolveAsync(
            string dataSourceJson, Guid tenantId,
            IReadOnlyDictionary<string, string>? parentValues = null,
            CancellationToken cancellationToken = default)
        {
            var vals = _capture(parentValues);
            IReadOnlyList<DataSourceOptionDto> opts = vals.Select(v => new DataSourceOptionDto(v, v)).ToList();
            return Task.FromResult(opts);
        }
    }

    [Fact]
    public async Task External_InRow_DedupSharedFailure_SoftLogOncePerKey_NoRowBlocked()
    {
        // External throws once per (child, parent-tuple) key; with dedup, 500 rows sharing 1 parent
        // = 1 exception (soft-log once), all 500 rows pass through.
        var resolver = new ThrowingExternalResolver();
        var factory = Factory(FormFieldType.ExternalRef, resolver);

        var tbl  = TableParent("addresses", fieldId: 100);
        var tinh = ChildColumn("tinh", FormFieldType.ExternalRef, tableFieldId: 100);
        var xa   = ChildColumn("xa",   FormFieldType.ExternalRef, tableFieldId: 100,
            dsJson: DsWithDeps("tinh", "provinceCode"));

        var rows = new List<object>();
        for (int i = 0; i < 500; i++)
            rows.Add(Row([("tinh", "HN"), ("xa", "any")]));
        var data = new Dictionary<string, object?> { ["addresses"] = rows };

        var result = await Sut(factory).ValidateAsync([tbl, tinh, xa], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty("external cascade failures soft-log; dedup collapses N-row bursts");
    }

    [Fact]
    public async Task JsonElementRows_AreAcceptedByGuard()
    {
        // Incoming data may already be JsonElement (System.Text.Json binding path) — guard must
        // handle both List<Dictionary> and JsonElement array shapes (delegates to ParseTableFieldRows).
        var resolver = new FakeResolver(new() { ["HN"] = new[] { "01001" } });
        var factory = Factory(FormFieldType.EnumRef, resolver);

        var tbl  = TableParent("addresses", fieldId: 100);
        var tinh = ChildColumn("tinh", FormFieldType.EnumRef, tableFieldId: 100);
        var xa   = ChildColumn("xa",   FormFieldType.EnumRef, tableFieldId: 100,
            dsJson: DsWithDeps("tinh", "provinceCode"));

        var json = """[{"tinh":"HN","xa":"01001"}]""";
        var rowsElement = JsonSerializer.Deserialize<JsonElement>(json);
        var data = new Dictionary<string, object?> { ["addresses"] = rowsElement };

        var result = await Sut(factory).ValidateAsync([tbl, tinh, xa], data, Tenant);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        resolver.CallCount.Should().Be(1);
    }
}
