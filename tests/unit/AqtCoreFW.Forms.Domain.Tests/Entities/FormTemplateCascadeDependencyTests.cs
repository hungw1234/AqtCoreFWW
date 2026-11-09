using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// P2 (cascade v1) — cascade dependency-graph invariants enforced at PUBLISH (whole-graph chokepoint),
/// per spike contract §4. A field's DataSourceJson may carry a "dependencies":[{dependsOnFieldKey,filterParamName}]
/// list; the graph over top-level fields must be acyclic, every dependsOnFieldKey must resolve to an existing
/// active field, and no field may depend on itself. Empty/absent dependencies = old static path (backward guard).
///
/// Addressing is by fieldKey (string) — survives reorder and matches how FE addresses fields (spike §1).
/// Validation runs in FormTemplate.Publish/Republish so a bad graph never reaches the browser.
/// </summary>
public sealed class FormTemplateCascadeDependencyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId  = Guid.NewGuid();

    private static IGuidGenerator Gen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return g;
    }

    private static FormTemplate DraftTemplate() =>
        FormTemplate.Create(Gen(), TenantId, "Cascade Form", "Biểu mẫu cascade", "cascade_form", StorageMode.Json, ActorId);

    /// <summary>DataSourceJson carrying an external config + a dependencies[] list naming parent fieldKeys.</summary>
    private static string DsWithDeps(params string[] dependsOnFieldKeys)
    {
        var deps = string.Join(",", dependsOnFieldKeys.Select(k =>
            $$"""{"dependsOnFieldKey":"{{k}}","filterParamName":"{{k}}Code"}"""));
        return $$"""{"ApiUrl":"https://api.example.com/x","LabelJsonPath":"name","ValueJsonPath":"id","dependencies":[{{deps}}]}""";
    }

    private static FormField AddRef(FormTemplate t, string key, int order, string? dataSourceJson = null) =>
        t.AddField(key, FormFieldType.ExternalRef, key, key, order, required: false,
            validationRulesJson: null, updatedBy: ActorId, dataSourceJson: dataSourceJson);

    /// <summary>External-DS config carrying a Vault-authed marker (AuthMode present) — H2 Phase 05.</summary>
    private static string DsAuthedExternal(string authMode = "Bearer") =>
        $$"""{"ApiUrl":"https://api.example.com/x","LabelJsonPath":"name","ValueJsonPath":"id","AuthMode":"{{authMode}}","VaultKeyId":"ds-99"}""";

    /// <summary>External-DS config with no auth marker — the static/backward path.</summary>
    private static string DsStaticExternal() =>
        """{"ApiUrl":"https://api.example.com/x","LabelJsonPath":"name","ValueJsonPath":"id"}""";

    /// <summary>Duplicate AuthMode keys: guard-bypass craft. STJ (guard + resolver) is last-property-wins,
    /// so the effective AuthMode is "Bearer" (authed) — the guard MUST agree with the resolver here.</summary>
    private static string DsDuplicateAuthModeKeys() =>
        """{"ApiUrl":"https://api.example.com/x","LabelJsonPath":"name","ValueJsonPath":"id","AuthMode":"","AuthMode":"Bearer","VaultKeyId":"ds-99"}""";

    /// <summary>AuthMode typed as a JSON number — STJ cannot bind to string?, throws. Guard fails closed.</summary>
    private static string DsNumberTypedAuthMode() =>
        """{"ApiUrl":"https://api.example.com/x","LabelJsonPath":"name","ValueJsonPath":"id","AuthMode":1}""";

    // --- Backward guard: no dependencies = old path, publishes fine ---

    [Fact]
    public void Publish_FieldsWithoutDependencies_Succeeds()
    {
        var t = DraftTemplate();
        AddRef(t, "tinh", 1);
        AddRef(t, "xa", 2);

        var act = () => t.Publish(ActorId);

        act.Should().NotThrow();
        t.Status.Should().Be(FormTemplateStatus.Active);
    }

    // --- Valid chain: tỉnh → huyện → xã, acyclic, publishes fine ---

    [Fact]
    public void Publish_ValidAcyclicChain_Succeeds()
    {
        var t = DraftTemplate();
        AddRef(t, "tinh", 1);
        AddRef(t, "huyen", 2, DsWithDeps("tinh"));
        AddRef(t, "xa", 3, DsWithDeps("huyen"));

        var act = () => t.Publish(ActorId);

        act.Should().NotThrow("an acyclic parent→child chain is valid");
    }

    // --- Cycle A→B→A rejects at publish ---

    [Fact]
    public void Publish_CyclicDependencies_ThrowsNamingTheCycle()
    {
        var t = DraftTemplate();
        AddRef(t, "a", 1, DsWithDeps("b"));
        AddRef(t, "b", 2, DsWithDeps("a"));

        var act = () => t.Publish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>()
            .Which.Message.Should().ContainAny("a", "b");
    }

    // --- Self-dependency rejects ---

    [Fact]
    public void Publish_SelfDependency_Throws()
    {
        var t = DraftTemplate();
        AddRef(t, "self", 1, DsWithDeps("self"));

        var act = () => t.Publish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>()
            .Which.Message.Should().Contain("self");
    }

    // --- Dangling reference: dependsOnFieldKey names a non-existent field ---

    [Fact]
    public void Publish_DanglingParentReference_Throws()
    {
        var t = DraftTemplate();
        AddRef(t, "child", 1, DsWithDeps("ghost"));

        var act = () => t.Publish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>()
            .Which.Message.Should().Contain("ghost");
    }

    // --- Deleting a field a surviving child still depends on is rejected (spike §4: reject, not strip) ---

    [Fact]
    public void DeactivateField_StillDependedOnByChild_Throws()
    {
        var t = DraftTemplate();
        var parent = AddRef(t, "tinh", 1);
        AddRef(t, "xa", 2, DsWithDeps("tinh"));

        var act = () => t.DeactivateField(parent.Id, ActorId);

        act.Should().Throw<InvalidFormSchemaException>()
            .Which.Message.Should().ContainAll("tinh", "xa");
    }

    [Fact]
    public void DeactivateField_NotDependedOn_Succeeds()
    {
        var t = DraftTemplate();
        var standalone = AddRef(t, "standalone", 1);
        AddRef(t, "other", 2);

        var act = () => t.DeactivateField(standalone.Id, ActorId);

        act.Should().NotThrow();
    }

    // --- H2 Phase 05: authed-external DS is forbidden on PUBLIC forms (C1×H2 intersection) ---
    // A public form is reachable anonymously; an authed-external DS would read a Vault key and
    // issue an authenticated outbound fetch on behalf of an unauthenticated caller. Reject at publish.

    [Fact]
    public void Publish_PublicForm_WithAuthedExternalDataSource_ThrowsGovFrm044()
    {
        var t = DraftTemplate();
        t.SetPublicAccess(isPublic: true, ActorId);
        AddRef(t, "authed_ref", 1, DsAuthedExternal("Bearer"));

        var act = () => t.Publish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>()
            .Which.Message.Should().ContainAll("GOV_FRM_044", "authed_ref");
    }

    [Fact]
    public void Publish_PrivateForm_WithAuthedExternalDataSource_Succeeds()
    {
        var t = DraftTemplate();
        // IsPublic defaults false — private form may use an authed-external DS.
        AddRef(t, "authed_ref", 1, DsAuthedExternal("Bearer"));

        var act = () => t.Publish(ActorId);

        act.Should().NotThrow("authed-external DS is allowed on private (authenticated-only) forms");
    }

    [Fact]
    public void Publish_PublicForm_WithStaticExternalDataSource_Succeeds()
    {
        var t = DraftTemplate();
        t.SetPublicAccess(isPublic: true, ActorId);
        AddRef(t, "static_ref", 1, DsStaticExternal());

        var act = () => t.Publish(ActorId);

        act.Should().NotThrow("a static (non-authed) external DS is safe on a public form (backward)");
    }

    [Fact]
    public void Republish_PublicForm_WithAuthedExternalDataSource_ThrowsGovFrm044()
    {
        var t = DraftTemplate();
        t.SetPublicAccess(isPublic: true, ActorId);
        AddRef(t, "static_ref", 1, DsStaticExternal());
        t.Publish(ActorId); // Active, no authed-external yet

        // An admin later adds an authed-external field then republishes → must be rejected too.
        AddRef(t, "authed_ref", 2, DsAuthedExternal("ApiKeyHeader"));

        var act = () => t.Republish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>()
            .Which.Message.Should().ContainAll("GOV_FRM_044", "authed_ref");
    }

    // --- Parse-parity: the guard must resolve AuthMode the SAME way the resolver does (STJ last-wins),
    //     else a duplicate-key craft slips past the guard yet authenticates at runtime (key-leak). ---

    [Fact]
    public void Publish_PublicForm_WithDuplicateAuthModeKeys_ThrowsGovFrm044()
    {
        var t = DraftTemplate();
        t.SetPublicAccess(isPublic: true, ActorId);
        AddRef(t, "dup_ref", 1, DsDuplicateAuthModeKeys());

        var act = () => t.Publish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>("last-wins AuthMode='Bearer' is authed — guard must match resolver")
            .Which.Message.Should().ContainAll("GOV_FRM_044", "dup_ref");
    }

    // --- Fail-closed: an unparseable / non-string AuthMode on a public form is rejected at publish
    //     (else it published, then threw an unhandled JsonException out of the anonymous endpoint = 500). ---

    [Fact]
    public void Publish_PublicForm_WithNumberTypedAuthMode_ThrowsGovFrm044()
    {
        var t = DraftTemplate();
        t.SetPublicAccess(isPublic: true, ActorId);
        AddRef(t, "num_ref", 1, DsNumberTypedAuthMode());

        var act = () => t.Publish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>("unparseable AuthMode on a public form fails closed")
            .Which.Message.Should().ContainAll("GOV_FRM_044", "num_ref");
    }

    // --- Scope: the guard covers CHILD (in-row) fields too — the anonymous endpoint has no top-level
    //     filter, so an authed-external DS on a TableField column is anonymously reachable and must reject. ---

    [Fact]
    public void Publish_PublicForm_WithAuthedExternalOnChildField_ThrowsGovFrm044()
    {
        var t = DraftTemplate();
        t.SetPublicAccess(isPublic: true, ActorId);
        var table = t.AddField("tbl", FormFieldType.TableField, "tbl", "tbl", 1, required: false,
            validationRulesJson: null, updatedBy: ActorId);
        // Child column carrying an authed-external DS (ParentFieldId set).
        t.AddField("child_ref", FormFieldType.ExternalRef, "child_ref", "child_ref", 2, required: false,
            validationRulesJson: null, updatedBy: ActorId,
            dataSourceJson: DsAuthedExternal("Bearer"), parentFieldId: table.Id);

        var act = () => t.Publish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>("an authed-external DS on an in-row child field is anonymously reachable")
            .Which.Message.Should().ContainAll("GOV_FRM_044", "child_ref");
    }

    // ============================================================================
    // M1 Phase-06 slice 6a: in-row TableField-column cascade graph validation.
    // Each TableField is its OWN sub-graph (parent-column depends on other column
    // in the SAME TableField). Cross-TableField refs are treated as dangling.
    // Backward: child fields with no `dependencies` = skip (old shape preserved).
    // ============================================================================

    /// <summary>Add a CHILD column inside a TableField (parentFieldId set).</summary>
    private static FormField AddChildRef(FormTemplate t, string key, int order, long parentFieldId, string? dataSourceJson = null) =>
        t.AddField(key, FormFieldType.ExternalRef, key, key, order, required: false,
            validationRulesJson: null, updatedBy: ActorId,
            dataSourceJson: dataSourceJson, parentFieldId: parentFieldId);

    /// <summary>Simulate DB-assigned Id on a FormField (Entity.Id has `protected set` — Id stays 0 in-memory).
    /// Domain tests with multiple fields need distinct Ids so <c>AddField</c>'s parent lookup
    /// (<c>_fields.FirstOrDefault(f =&gt; f.Id == parentFieldId)</c>) resolves to the intended parent
    /// and does not silently pick the first-added field with Id=0 (which flips a TableField parent
    /// scenario into a bad-parent-type reject).</summary>
    private static long _nextFakeId = 1000;
    private static FormField AssignId(FormField field)
    {
        typeof(SharedKernel.Domain.Entity)
            .GetProperty(nameof(SharedKernel.Domain.Entity.Id))!
            .SetValue(field, _nextFakeId++);
        return field;
    }

    [Fact]
    public void Publish_InRowChildCascade_ValidChain_Succeeds()
    {
        // Same TableField: province -> ward cascade INSIDE the row. Sub-graph is acyclic.
        var t = DraftTemplate();
        var tbl = t.AddField("tbl", FormFieldType.TableField, "tbl", "tbl", 1, required: false,
            validationRulesJson: null, updatedBy: ActorId);
        AddChildRef(t, "row_tinh", 2, tbl.Id);
        AddChildRef(t, "row_xa",   3, tbl.Id, DsWithDeps("row_tinh"));

        var act = () => t.Publish(ActorId);

        act.Should().NotThrow("an in-row acyclic parent→child cascade is valid inside a TableField");
    }

    [Fact]
    public void Publish_InRowChildCascade_CycleWithinSameTable_ThrowsGovFrm043()
    {
        // Cycle a↔b entirely inside the SAME TableField sub-graph → reject.
        var t = DraftTemplate();
        var tbl = t.AddField("tbl", FormFieldType.TableField, "tbl", "tbl", 1, required: false,
            validationRulesJson: null, updatedBy: ActorId);
        AddChildRef(t, "a", 2, tbl.Id, DsWithDeps("b"));
        AddChildRef(t, "b", 3, tbl.Id, DsWithDeps("a"));

        var act = () => t.Publish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>("in-row child cascade cycle must be rejected at publish")
            .Which.Message.Should().ContainAny("GOV_FRM_043", "a", "b");
    }

    [Fact]
    public void Publish_InRowChildCascade_SelfDependency_ThrowsGovFrm040()
    {
        var t = DraftTemplate();
        var tbl = t.AddField("tbl", FormFieldType.TableField, "tbl", "tbl", 1, required: false,
            validationRulesJson: null, updatedBy: ActorId);
        AddChildRef(t, "self_child", 2, tbl.Id, DsWithDeps("self_child"));

        var act = () => t.Publish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>()
            .Which.Message.Should().ContainAll("GOV_FRM_040", "self_child");
    }

    [Fact]
    public void Publish_InRowChildCascade_CrossTableFieldRef_ThrowsDangling()
    {
        // Child in TF1 references a child in TF2 → dangling within TF1's sub-graph.
        // Rationale: at submit time, TF1 rows do not see TF2 row-values → a cross-TF cascade
        // has no way to compute a filter value. Only intra-TF is valid.
        var t = DraftTemplate();
        var tf1 = AssignId(t.AddField("tf1", FormFieldType.TableField, "tf1", "tf1", 1, required: false,
            validationRulesJson: null, updatedBy: ActorId));
        var tf2 = AssignId(t.AddField("tf2", FormFieldType.TableField, "tf2", "tf2", 2, required: false,
            validationRulesJson: null, updatedBy: ActorId));
        AddChildRef(t, "tf2_col", 3, tf2.Id);
        AddChildRef(t, "tf1_col", 4, tf1.Id, DsWithDeps("tf2_col")); // cross-TF → dangling

        var act = () => t.Publish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>("in-row cascade must be intra-TableField only")
            .Which.Message.Should().ContainAll("GOV_FRM_041", "tf1_col", "tf2_col");
    }

    [Fact]
    public void Publish_InRowChildCascade_RefsTopLevelField_ThrowsDangling()
    {
        // Child dep on a TOP-LEVEL field (outside any TableField). Reject as dangling — the
        // row-scoped resolver at submit time sees the same-row parent value only.
        var t = DraftTemplate();
        AssignId(AddRef(t, "top_province", 1));
        var tbl = AssignId(t.AddField("tbl", FormFieldType.TableField, "tbl", "tbl", 2, required: false,
            validationRulesJson: null, updatedBy: ActorId));
        AddChildRef(t, "row_child", 3, tbl.Id, DsWithDeps("top_province"));

        var act = () => t.Publish(ActorId);

        act.Should().Throw<InvalidFormSchemaException>("in-row child cannot depend on a top-level field")
            .Which.Message.Should().ContainAll("GOV_FRM_041", "row_child", "top_province");
    }

    [Fact]
    public void Publish_InRowChildCascade_NoDependencies_Succeeds_Backward()
    {
        // Backward guard: TableField with plain child columns (no `dependencies`) still publishes.
        var t = DraftTemplate();
        var tbl = t.AddField("tbl", FormFieldType.TableField, "tbl", "tbl", 1, required: false,
            validationRulesJson: null, updatedBy: ActorId);
        AddChildRef(t, "row_a", 2, tbl.Id); // no ds json
        AddChildRef(t, "row_b", 3, tbl.Id); // no ds json

        var act = () => t.Publish(ActorId);

        act.Should().NotThrow("in-row plain columns without dependencies keep the old backward path");
    }

    [Fact]
    public void Publish_TopLevelAndInRowCascade_Independent_BothValid_Succeeds()
    {
        // Top-level tỉnh→xã AND an in-row cascade in one TableField. Two independent sub-graphs.
        var t = DraftTemplate();
        AssignId(AddRef(t, "tinh", 1));
        AssignId(AddRef(t, "xa", 2, DsWithDeps("tinh")));
        var tbl = AssignId(t.AddField("tbl", FormFieldType.TableField, "tbl", "tbl", 3, required: false,
            validationRulesJson: null, updatedBy: ActorId));
        AddChildRef(t, "row_tinh", 4, tbl.Id);
        AddChildRef(t, "row_xa",   5, tbl.Id, DsWithDeps("row_tinh"));

        var act = () => t.Publish(ActorId);

        act.Should().NotThrow("top-level graph and in-row sub-graph are independent");
    }

    [Fact]
    public void DeactivateChildField_StillDependedOnBySibling_Throws()
    {
        // In-row: deleting `row_tinh` while `row_xa` still depends on it → reject.
        var t = DraftTemplate();
        var tbl = AssignId(t.AddField("tbl", FormFieldType.TableField, "tbl", "tbl", 1, required: false,
            validationRulesJson: null, updatedBy: ActorId));
        var rowTinh = AssignId(AddChildRef(t, "row_tinh", 2, tbl.Id));
        AssignId(AddChildRef(t, "row_xa", 3, tbl.Id, DsWithDeps("row_tinh")));

        var act = () => t.DeactivateField(rowTinh.Id, ActorId);

        act.Should().Throw<InvalidFormSchemaException>("in-row cascade parents cannot be deleted while a sibling still depends on them")
            .Which.Message.Should().ContainAll("row_tinh", "row_xa");
    }
}
