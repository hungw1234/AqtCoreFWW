using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for FormTemplate.AddSignerRole — multi-signer support for forms.
/// Covers creation, role validation, duplicate key detection, version bumping on Active templates,
/// and ordering semantics. Mirrors AddField pattern.
/// </summary>
public sealed class FormTemplateSignerRoleTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static readonly IGuidGenerator Gen = CreateGen();
    private static IGuidGenerator CreateGen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return g;
    }

    private static FormTemplate DraftTemplate()
    {
        var t = FormTemplate.Create(Gen, TenantId, "T", "T", "tpl_code", StorageMode.Json, ActorId);
        t.AddField("f1", FormFieldType.Text, "Tên", "Name", 1, true, null, ActorId);
        return t;
    }

    private static FormTemplate ActiveTemplate()
    {
        var t = DraftTemplate();
        t.Publish(ActorId);
        return t;
    }

    // --- Factory and happy path ---

    [Fact]
    public void AddSignerRole_InDraftStatus_AddsRoleAndDoesNotBumpVersion()
    {
        var template = DraftTemplate();
        var versionBefore = template.Version;

        var role = template.AddSignerRole(
            "approver",
            "Người duyệt",
            displayOrder: 1,
            isRequired: true,
            placementJson: null,
            ActorId);

        role.Should().NotBeNull();
        role.RoleKey.Should().Be("approver");
        role.RoleLabelVi.Should().Be("Người duyệt");
        role.DisplayOrder.Should().Be(1);
        role.IsRequired.Should().BeTrue();
        template.Version.Should().Be(versionBefore); // No version bump in Draft
        template.SignerRoles.Should().ContainSingle();
    }

    [Fact]
    public void AddSignerRole_InActiveStatus_AddRoleAndBumpVersion()
    {
        var template = ActiveTemplate();
        var versionBefore = template.Version;

        var role = template.AddSignerRole(
            "approver",
            "Người duyệt",
            displayOrder: 1,
            isRequired: true,
            placementJson: null,
            ActorId);

        role.Should().NotBeNull();
        template.Version.Should().Be(versionBefore + 1); // Version bump mirrors AddField on Active
        template.SignerRoles.Should().ContainSingle();
    }

    [Fact]
    public void AddSignerRole_MultipleRoles_MaintainsDisplayOrder()
    {
        var template = DraftTemplate();

        var role1 = template.AddSignerRole("reviewer", "Phản biện", displayOrder: 2, isRequired: true, placementJson: null, ActorId);
        var role2 = template.AddSignerRole("approver", "Duyệt", displayOrder: 1, isRequired: true, placementJson: null, ActorId);
        var role3 = template.AddSignerRole("signatory", "Ký", displayOrder: 3, isRequired: true, placementJson: null, ActorId);

        // Order is insertion order in the collection, not sorted by DisplayOrder (that's UI concern)
        template.SignerRoles.Should().HaveCount(3);
        template.SignerRoles.Should().ContainInOrder(role1, role2, role3);
    }

    [Fact]
    public void AddSignerRole_WithPlacementJson_StoredAsProvided()
    {
        var template = DraftTemplate();
        var placement = "{\"x\": 100, \"y\": 50, \"width\": 150, \"height\": 75}";

        var role = template.AddSignerRole(
            "signer",
            "Người ký",
            displayOrder: 1,
            isRequired: true,
            placementJson: placement,
            ActorId);

        role.PlacementJson.Should().Be(placement);
    }

    [Fact]
    public void AddSignerRole_NotRequired_Allowed()
    {
        var template = DraftTemplate();

        var role = template.AddSignerRole(
            "witness",
            "Nhân chứng",
            displayOrder: 1,
            isRequired: false,
            placementJson: null,
            ActorId);

        role.IsRequired.Should().BeFalse();
    }

    // --- Validation: RoleKey ---

    [Fact]
    public void AddSignerRole_WithEmptyRoleKey_Throws()
    {
        var template = DraftTemplate();

        var action = () => template.AddSignerRole("", "Label", 1, true, null, ActorId);

        action.Should().Throw<InvalidFormSchemaException>()
            .WithMessage("*GOV_FRM_030*RoleKey*trống*");
    }

    [Fact]
    public void AddSignerRole_WithWhitespaceOnlyRoleKey_Throws()
    {
        var template = DraftTemplate();

        var action = () => template.AddSignerRole("   ", "Label", 1, true, null, ActorId);

        action.Should().Throw<InvalidFormSchemaException>()
            .WithMessage("*GOV_FRM_030*RoleKey*trống*");
    }

    [Fact]
    public void AddSignerRole_WithDuplicateRoleKey_Throws()
    {
        var template = DraftTemplate();
        template.AddSignerRole("approver", "Duyệt", 1, true, null, ActorId);

        var action = () => template.AddSignerRole("approver", "Duyệt - khác", 2, true, null, ActorId);

        action.Should().Throw<InvalidFormSchemaException>()
            .WithMessage("*GOV_FRM_031*approver*đã tồn tại*");
    }

    [Fact]
    public void AddSignerRole_CaseInsensitiveDuplicateDetection()
    {
        // RoleKey matching is case-insensitive (normalized on trim)
        var template = DraftTemplate();
        template.AddSignerRole("Approver", "Duyệt", 1, true, null, ActorId);

        var action = () => template.AddSignerRole("APPROVER", "Duyệt 2", 2, true, null, ActorId);

        action.Should().Throw<InvalidFormSchemaException>()
            .WithMessage("*GOV_FRM_031*");
    }

    [Fact]
    public void AddSignerRole_WithLeadingTrailingWhitespace_TrimmedAndCheckedForDuplicate()
    {
        var template = DraftTemplate();
        template.AddSignerRole("  approver  ", "Duyệt", 1, true, null, ActorId);

        // Same key with different whitespace should be detected as duplicate
        var action = () => template.AddSignerRole("approver", "Duyệt 2", 2, true, null, ActorId);

        action.Should().Throw<InvalidFormSchemaException>()
            .WithMessage("*GOV_FRM_031*");
    }

    // --- State guards ---

    [Fact]
    public void AddSignerRole_InMaterializingStatus_Throws()
    {
        var template = FormTemplate.Create(Gen, TenantId, "T", "T", "tpl", StorageMode.Table, ActorId);
        template.AddField("f1", FormFieldType.Text, "F", "F", 1, true, null, ActorId);
        template.Publish(ActorId); // Table mode → Materializing

        var action = () => template.AddSignerRole("approver", "Duyệt", 1, true, null, ActorId);

        action.Should().Throw<InvalidFormSchemaException>()
            .WithMessage("*GOV_FRM_010*");
    }

    [Fact]
    public void AddSignerRole_InInactiveStatus_Throws()
    {
        var template = ActiveTemplate();
        template.Deactivate(ActorId);

        var action = () => template.AddSignerRole("approver", "Duyệt", 1, true, null, ActorId);

        action.Should().Throw<InvalidFormSchemaException>()
            .WithMessage("*GOV_FRM_010*");
    }

    // --- Version bump semantics ---

    [Fact]
    public void AddSignerRole_TwoTimesOnActive_EachBumpsVersion()
    {
        var template = ActiveTemplate();
        var v0 = template.Version;

        template.AddSignerRole("role1", "Role 1", 1, true, null, ActorId);
        var v1 = template.Version;

        template.AddSignerRole("role2", "Role 2", 2, true, null, ActorId);
        var v2 = template.Version;

        v1.Should().Be(v0 + 1);
        v2.Should().Be(v1 + 1);
    }

    [Fact]
    public void AddSignerRole_AfterAddField_BothBumpVersionOnActive()
    {
        var template = ActiveTemplate();
        var v0 = template.Version;

        template.AddField("newfield", FormFieldType.Text, "New", "New", 2, false, null, ActorId);
        var afterField = template.Version;

        template.AddSignerRole("approver", "Duyệt", 1, true, null, ActorId);
        var afterRole = template.Version;

        afterField.Should().Be(v0 + 1);
        afterRole.Should().Be(afterField + 1);
    }

    // --- Coexistence with fields ---

    [Fact]
    public void Template_CanHaveBothFieldsAndSignerRoles()
    {
        var template = DraftTemplate(); // Already has 1 field

        template.AddSignerRole("approver", "Duyệt", 1, true, null, ActorId);
        template.AddSignerRole("witness", "Nhân chứng", 2, false, null, ActorId);

        template.Fields.Should().HaveCount(1);
        template.SignerRoles.Should().HaveCount(2);
    }
}
