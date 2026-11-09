using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.MasterData.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.MasterData.Tests.Entities;

/// <summary>
/// TC-MD-D005: WorkflowStateTaxonomy / WorkflowActionTaxonomy entities — Option B soft-FK masterdata for workflow vocabulary.
/// Covers: Create factory, Deprecate/Restore/Update mutation, ClassificationLevel=Public (V3), RowVersion default.
/// </summary>
public sealed class WorkflowTaxonomyTests
{
    // Phase-4b: T2 entities require IGuidGenerator. V7GuidGenerator is internal — use NSubstitute.
    private static readonly IGuidGenerator Gen;

    static WorkflowTaxonomyTests()
    {
        Gen = Substitute.For<IGuidGenerator>();
        Gen.NewIdRandom().Returns(_ => Guid.NewGuid());
    }
    // --- WorkflowStateCategory enum ---

    [Fact]
    public void WorkflowStateCategory_HasFourValues()
    {
        Enum.GetValues<WorkflowStateCategory>().Should().BeEquivalentTo(new[]
        {
            WorkflowStateCategory.Initial,
            WorkflowStateCategory.Intermediate,
            WorkflowStateCategory.Terminal,
            WorkflowStateCategory.Rejected,
        });
    }

    // Guards M3: EF Category column is nvarchar(30); enum name > 30 chars silently truncates on save.
    [Fact]
    public void WorkflowStateCategory_AllEnumNamesFitWithinDbColumnLength()
    {
        const int CategoryColumnMaxLength = 30;

        Enum.GetNames<WorkflowStateCategory>()
            .Should().OnlyContain(n => n.Length <= CategoryColumnMaxLength);
    }

    // Guard null-inputs contract for Deprecate (M1) + Create/Update required params (M2).
    [Fact]
    public void State_Deprecate_ThrowsOnNullOrWhitespaceReason()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);

        ((Action)(() => entity.Deprecate(""))).Should().Throw<ArgumentException>();
        ((Action)(() => entity.Deprecate("   "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void State_Create_ThrowsOnNullOrWhitespaceRequiredFields()
    {
        ((Action)(() => WorkflowStateTaxonomy.Create(Gen, "", "Nháp", "Draft", WorkflowStateCategory.Initial)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => WorkflowStateTaxonomy.Create(Gen, "Draft", " ", "Draft", WorkflowStateCategory.Initial)))
            .Should().Throw<ArgumentException>();
        ((Action)(() => WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "", WorkflowStateCategory.Initial)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Action_Deprecate_ThrowsOnNullOrWhitespaceReason()
    {
        var entity = WorkflowActionTaxonomy.Create(Gen, "Submit", "Gửi", "Submit");

        ((Action)(() => entity.Deprecate(""))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Action_Create_ThrowsOnNullOrWhitespaceRequiredFields()
    {
        ((Action)(() => WorkflowActionTaxonomy.Create(Gen, "", "Gửi", "Submit")))
            .Should().Throw<ArgumentException>();
    }

    // --- WorkflowStateTaxonomy.Create ---

    [Fact]
    public void State_Create_SetsAllRequiredFields()
    {
        var entity = WorkflowStateTaxonomy.Create(
            gen: Gen,
            code: "Draft",
            nameVi: "Nháp",
            nameEn: "Draft",
            category: WorkflowStateCategory.Initial,
            sortOrder: 10);

        entity.Code.Should().Be("Draft");
        entity.NameVi.Should().Be("Nháp");
        entity.NameEn.Should().Be("Draft");
        entity.Category.Should().Be(WorkflowStateCategory.Initial);
        entity.SortOrder.Should().Be(10);
    }

    [Fact]
    public void State_Create_GeneratesUniqueId()
    {
        var a = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);
        var b = WorkflowStateTaxonomy.Create(Gen, "Submitted", "Đã nộp", "Submitted", WorkflowStateCategory.Intermediate);

        a.PublicId.Should().NotBe(b.PublicId);
        a.PublicId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void State_Create_DefaultsIsActiveTrue()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);

        entity.IsActive.Should().BeTrue();
        entity.DeprecatedReason.Should().BeNull();
    }

    [Fact]
    public void State_Create_DefaultsOptionalVisualFieldsNull()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);

        entity.I18nKey.Should().BeNull();
        entity.Icon.Should().BeNull();
        entity.Color.Should().BeNull();
    }

    [Fact]
    public void State_Create_SetsClassificationLevelPublic()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);

        entity.ClassificationLevel.Should().Be(ClassificationLevel.Public);
    }

    [Fact]
    public void State_Create_RowVersionInitializedNotNull()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);

        entity.RowVersion.Should().NotBeNull();
    }

    [Fact]
    public void State_Create_DefaultSortOrderIsZero()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);

        entity.SortOrder.Should().Be(0);
    }

    // --- WorkflowStateTaxonomy.Deprecate ---

    [Fact]
    public void State_Deprecate_SetsInactiveAndReason()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);

        entity.Deprecate("Replaced by Initial in 2026");

        entity.IsActive.Should().BeFalse();
        entity.DeprecatedReason.Should().Be("Replaced by Initial in 2026");
    }

    [Fact]
    public void State_Deprecate_UpdatesUpdatedAt()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);

        entity.Deprecate("obsolete vocabulary");

        entity.UpdatedAt.Should().NotBeNull();
    }

    // --- WorkflowStateTaxonomy.Restore ---

    [Fact]
    public void State_Restore_SetsActiveAndClearsReason()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);
        entity.Deprecate("temp");

        entity.Restore();

        entity.IsActive.Should().BeTrue();
        entity.DeprecatedReason.Should().BeNull();
    }

    [Fact]
    public void State_Restore_UpdatesUpdatedAt()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);
        entity.Deprecate("temp");

        entity.Restore();

        entity.UpdatedAt.Should().NotBeNull();
    }

    // --- WorkflowStateTaxonomy.Update ---

    [Fact]
    public void State_Update_ChangesLabelsCategoryAndVisual()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);

        entity.Update(
            nameVi: "Bản nháp",
            nameEn: "Initial Draft",
            i18nKey: "wf.state.draft",
            icon: "EditFilled",
            color: "#AAAAAA",
            category: WorkflowStateCategory.Intermediate,
            sortOrder: 5);

        entity.NameVi.Should().Be("Bản nháp");
        entity.NameEn.Should().Be("Initial Draft");
        entity.I18nKey.Should().Be("wf.state.draft");
        entity.Icon.Should().Be("EditFilled");
        entity.Color.Should().Be("#AAAAAA");
        entity.Category.Should().Be(WorkflowStateCategory.Intermediate);
        entity.SortOrder.Should().Be(5);
    }

    [Fact]
    public void State_Update_UpdatesUpdatedAt()
    {
        var entity = WorkflowStateTaxonomy.Create(Gen, "Draft", "Nháp", "Draft", WorkflowStateCategory.Initial);

        entity.Update("a", "b", null, null, null, WorkflowStateCategory.Initial, 0);

        entity.UpdatedAt.Should().NotBeNull();
    }

    // --- WorkflowActionTaxonomy.Create (no Category, no Color per plan line 165) ---

    [Fact]
    public void Action_Create_SetsFields()
    {
        var entity = WorkflowActionTaxonomy.Create(
            gen: Gen,
            code: "Submit",
            nameVi: "Gửi",
            nameEn: "Submit",
            sortOrder: 10);

        entity.Code.Should().Be("Submit");
        entity.NameVi.Should().Be("Gửi");
        entity.NameEn.Should().Be("Submit");
        entity.SortOrder.Should().Be(10);
    }

    [Fact]
    public void Action_Create_DefaultsIsActiveTrueAndOptionalsNull()
    {
        var entity = WorkflowActionTaxonomy.Create(Gen, "Submit", "Gửi", "Submit");

        entity.IsActive.Should().BeTrue();
        entity.DeprecatedReason.Should().BeNull();
        entity.I18nKey.Should().BeNull();
        entity.Icon.Should().BeNull();
    }

    [Fact]
    public void Action_Create_SetsClassificationLevelPublic()
    {
        var entity = WorkflowActionTaxonomy.Create(Gen, "Submit", "Gửi", "Submit");

        entity.ClassificationLevel.Should().Be(ClassificationLevel.Public);
    }

    [Fact]
    public void Action_Create_RowVersionInitializedNotNull()
    {
        var entity = WorkflowActionTaxonomy.Create(Gen, "Submit", "Gửi", "Submit");

        entity.RowVersion.Should().NotBeNull();
    }

    [Fact]
    public void Action_Deprecate_SetsInactiveAndReason()
    {
        var entity = WorkflowActionTaxonomy.Create(Gen, "Submit", "Gửi", "Submit");

        entity.Deprecate("Merged with Send action in 2026");

        entity.IsActive.Should().BeFalse();
        entity.DeprecatedReason.Should().Be("Merged with Send action in 2026");
    }

    [Fact]
    public void Action_Restore_ClearsReasonAndSetsActive()
    {
        var entity = WorkflowActionTaxonomy.Create(Gen, "Submit", "Gửi", "Submit");
        entity.Deprecate("temp");

        entity.Restore();

        entity.IsActive.Should().BeTrue();
        entity.DeprecatedReason.Should().BeNull();
    }

    [Fact]
    public void Action_Update_ChangesLabelsAndIcon()
    {
        var entity = WorkflowActionTaxonomy.Create(Gen, "Submit", "Gửi", "Submit");

        entity.Update(
            nameVi: "Nộp",
            nameEn: "Send",
            i18nKey: "wf.action.submit",
            icon: "SendOutlined",
            sortOrder: 1);

        entity.NameVi.Should().Be("Nộp");
        entity.NameEn.Should().Be("Send");
        entity.I18nKey.Should().Be("wf.action.submit");
        entity.Icon.Should().Be("SendOutlined");
        entity.SortOrder.Should().Be(1);
    }
}
