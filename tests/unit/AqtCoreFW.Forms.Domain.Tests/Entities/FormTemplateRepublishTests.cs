using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for FormTemplate.Republish (BP MVP P1-FU).
/// Covers state guards (Active-only), Version bump semantics, and sequential republish
/// produces monotonically distinct VersionNumbers.
/// </summary>
public sealed class FormTemplateRepublishTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId  = Guid.NewGuid();

    private static readonly IGuidGenerator Gen = CreateGen();
    private static IGuidGenerator CreateGen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return g;
    }

    private static FormTemplate ActiveTemplate()
    {
        var t = FormTemplate.Create(Gen, TenantId, "T", "T", "tpl_code", StorageMode.Json, ActorId);
        t.AddField("f1", FormFieldType.Text, "Tên", "Name", 1, true, null, ActorId);
        t.Publish(ActorId);
        return t;
    }

    // --- Happy path ---

    [Fact]
    public void Republish_ActiveTemplateWithField_BumpsVersion()
    {
        var template = ActiveTemplate();
        var versionBefore = template.Version;

        template.Republish(ActorId);

        template.Version.Should().Be(versionBefore + 1);
        template.Status.Should().Be(FormTemplateStatus.Active);
    }

    [Fact]
    public void Republish_TwoSequentialCalls_ProduceMonotonicVersions()
    {
        // Guarantees the (FormTemplateId, VersionNumber) unique index cannot be violated by
        // rapid back-to-back republish with zero schema changes between calls.
        var template = ActiveTemplate();
        var v0 = template.Version;

        template.Republish(ActorId);
        var v1 = template.Version;

        template.Republish(ActorId);
        var v2 = template.Version;

        v1.Should().Be(v0 + 1);
        v2.Should().Be(v1 + 1);
        new[] { v0, v1, v2 }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Republish_AfterAddField_VersionReflectsBothBumps()
    {
        var template = ActiveTemplate();        // Version = 1
        template.AddField("f2", FormFieldType.Text, "F2", "F2", 2, false, null, ActorId); // AddField on Active bumps → 2
        var afterAdd = template.Version;

        template.Republish(ActorId);            // bumps → 3

        afterAdd.Should().Be(2);
        template.Version.Should().Be(3);
    }

    // --- State guards ---

    [Fact]
    public void Republish_DraftTemplate_Throws()
    {
        var template = FormTemplate.Create(Gen, TenantId, "T", "T", "tpl_code", StorageMode.Json, ActorId);
        template.AddField("f1", FormFieldType.Text, "Tên", "Name", 1, true, null, ActorId);
        // Skip Publish — stays in Draft

        var action = () => template.Republish(ActorId);

        action.Should().Throw<InvalidFormSchemaException>()
            .WithMessage("*GOV_FRM_010*Active*");
    }

    [Fact]
    public void Republish_MaterializingTemplate_Throws()
    {
        var template = FormTemplate.Create(Gen, TenantId, "T", "T", "tpl_code", StorageMode.Table, ActorId);
        template.AddField("f1", FormFieldType.Text, "Tên", "Name", 1, true, null, ActorId);
        template.Publish(ActorId); // Table mode → Materializing

        template.Status.Should().Be(FormTemplateStatus.Materializing);

        var action = () => template.Republish(ActorId);

        action.Should().Throw<InvalidFormSchemaException>()
            .WithMessage("*GOV_FRM_010*Active*");
    }

    [Fact]
    public void Republish_InactiveTemplate_Throws()
    {
        var template = ActiveTemplate();
        template.Deactivate(ActorId);

        var action = () => template.Republish(ActorId);

        action.Should().Throw<InvalidFormSchemaException>()
            .WithMessage("*GOV_FRM_010*Active*");
    }

    [Fact]
    public void Republish_ActiveButAllFieldsDeactivated_Throws()
    {
        var template = ActiveTemplate();
        var fieldId = template.Fields.Single().Id;
        template.DeactivateField(fieldId, ActorId);

        var action = () => template.Republish(ActorId);

        action.Should().Throw<InvalidFormSchemaException>()
            .WithMessage("*GOV_FRM_003*ít nhất một field*");
    }
}
