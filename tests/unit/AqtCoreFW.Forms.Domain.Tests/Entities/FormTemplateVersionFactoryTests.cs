using System.Text.Json;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for FormTemplateVersionFactory — snapshot builder for BP MVP P1.
/// Verifies that Snapshot() captures full schema (active fields + metadata) deterministically
/// and that the immutable FormTemplateVersion entity round-trips through JSON.
/// </summary>
public sealed class FormTemplateVersionFactoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId  = Guid.NewGuid();

    private static IGuidGenerator Gen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return g;
    }

    private static FormTemplate BuildTemplateWith(params (string key, FormFieldType type)[] fields)
    {
        var t = FormTemplate.Create(Gen(), TenantId, "T", "B", "tpl_code", StorageMode.Json, ActorId);
        var order = 1;
        foreach (var (key, type) in fields)
            t.AddField(key, type, $"vi_{key}", $"en_{key}", order++, false, null, ActorId);
        return t;
    }

    [Fact]
    public void Snapshot_TemplateWithFields_BuildsImmutableVersionEntity()
    {
        var template = BuildTemplateWith(("name", FormFieldType.Text), ("email", FormFieldType.Text));

        var snapshot = FormTemplateVersionFactory.Snapshot(template, ActorId);

        snapshot.TenantId.Should().Be(template.TenantId);
        snapshot.FormTemplateId.Should().Be(template.Id);
        snapshot.VersionNumber.Should().Be(template.Version);
        snapshot.CreatedBy.Should().Be(ActorId);
        snapshot.SchemaJson.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Snapshot_JsonRoundtrip_ContainsAllActiveFields()
    {
        var template = BuildTemplateWith(
            ("a", FormFieldType.Text),
            ("b", FormFieldType.Number),
            ("c", FormFieldType.Date));

        var snapshot = FormTemplateVersionFactory.Snapshot(template, ActorId);
        var doc = JsonDocument.Parse(snapshot.SchemaJson);

        doc.RootElement.GetProperty("schemaEnvelopeVersion").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("version").GetInt32().Should().Be(template.Version);
        doc.RootElement.GetProperty("storageMode").GetString().Should().Be("Json");
        var fields = doc.RootElement.GetProperty("fields").EnumerateArray().ToList();
        fields.Should().HaveCount(3);
        fields.Select(f => f.GetProperty("key").GetString()).Should().Contain(["a", "b", "c"]);
    }

    [Fact]
    public void Snapshot_DeactivatedField_NotIncluded()
    {
        var template = BuildTemplateWith(("active", FormFieldType.Text), ("dead", FormFieldType.Text));
        // FormField.Id is DB-assigned and is 0 for unsaved fields, so the by-id lookup in
        // FormTemplate.DeactivateField cannot disambiguate. Call FormField.Deactivate() directly —
        // same final mutation, scoped to verifying snapshot serialization filter only.
        template.Fields.First(f => f.FieldKey == "dead").Deactivate();

        var snapshot = FormTemplateVersionFactory.Snapshot(template, ActorId);
        var keys = JsonDocument.Parse(snapshot.SchemaJson).RootElement
            .GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("key").GetString())
            .ToList();

        keys.Should().ContainSingle().And.Contain("active");
    }

    [Fact]
    public void Snapshot_FieldOrder_FollowsDisplayOrder()
    {
        var template = FormTemplate.Create(Gen(), TenantId, "T", "B", "tpl_code", StorageMode.Json, ActorId);
        template.AddField("z", FormFieldType.Text, "z", "z", 3, false, null, ActorId);
        template.AddField("a", FormFieldType.Text, "a", "a", 1, false, null, ActorId);
        template.AddField("m", FormFieldType.Text, "m", "m", 2, false, null, ActorId);

        var snapshot = FormTemplateVersionFactory.Snapshot(template, ActorId);
        var keys = JsonDocument.Parse(snapshot.SchemaJson).RootElement
            .GetProperty("fields").EnumerateArray()
            .Select(f => f.GetProperty("key").GetString())
            .ToList();

        keys.Should().Equal("a", "m", "z");
    }

    [Fact]
    public void Snapshot_FieldWithOptions_SerializesOptionsInDisplayOrder()
    {
        var template = FormTemplate.Create(Gen(), TenantId, "T", "B", "tpl_code", StorageMode.Json, ActorId);
        template.AddField("prio", FormFieldType.EnumRef, "Mức độ", "Priority", 1, false, null, ActorId);
        var field = template.Fields.First(f => f.FieldKey == "prio");
        // Added out of order to prove the serializer sorts by DisplayOrder.
        field.AddOption(FormFieldOption.Create(0, "high", "Cao", "High", 3));
        field.AddOption(FormFieldOption.Create(0, "low", "Thấp", "Low", 1));
        field.AddOption(FormFieldOption.Create(0, "mid", "Trung bình", "Medium", 2));

        var snapshot = FormTemplateVersionFactory.Snapshot(template, ActorId);
        var fieldEl = JsonDocument.Parse(snapshot.SchemaJson).RootElement
            .GetProperty("fields").EnumerateArray().Single();
        var options = fieldEl.GetProperty("options").EnumerateArray().ToList();

        options.Should().HaveCount(3);
        options.Select(o => o.GetProperty("value").GetString()).Should().Equal("low", "mid", "high");
        options.Select(o => o.GetProperty("labelVi").GetString()).Should().Equal("Thấp", "Trung bình", "Cao");
        options.Select(o => o.GetProperty("labelEn").GetString()).Should().Equal("Low", "Medium", "High");
        options[0].GetProperty("displayOrder").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Snapshot_FieldWithoutOptions_SerializesNullOptions()
    {
        var template = BuildTemplateWith(("name", FormFieldType.Text));

        var snapshot = FormTemplateVersionFactory.Snapshot(template, ActorId);
        var fieldEl = JsonDocument.Parse(snapshot.SchemaJson).RootElement
            .GetProperty("fields").EnumerateArray().Single();

        fieldEl.GetProperty("options").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Snapshot_PreservesConsentAndApprovalWorkflow()
    {
        var workflowId = Guid.NewGuid();
        var template = BuildTemplateWith(("x", FormFieldType.Text));
        template.SetConsentConfig(true, "Đồng ý xử lý PII", ActorId);
        template.SetApprovalWorkflow(workflowId, ActorId);

        var snapshot = FormTemplateVersionFactory.Snapshot(template, ActorId);
        var root = JsonDocument.Parse(snapshot.SchemaJson).RootElement;

        root.GetProperty("requiresConsent").GetBoolean().Should().BeTrue();
        root.GetProperty("approvalWorkflowDefinitionId").GetGuid().Should().Be(workflowId);
    }

    [Fact]
    public void Snapshot_TableMode_CapturesStorageMode()
    {
        var template = FormTemplate.Create(Gen(), TenantId, "T", "B", "tpl_code", StorageMode.Table, ActorId);
        template.AddField("col1", FormFieldType.Text, "C", "C", 1, false, null, ActorId);

        var snapshot = FormTemplateVersionFactory.Snapshot(template, ActorId);

        JsonDocument.Parse(snapshot.SchemaJson).RootElement
            .GetProperty("storageMode").GetString().Should().Be("Table");
    }

    [Fact]
    public void Snapshot_NullTemplate_Throws()
    {
        var act = () => FormTemplateVersionFactory.Snapshot(null!, ActorId);
        act.Should().Throw<ArgumentNullException>();
    }
}
