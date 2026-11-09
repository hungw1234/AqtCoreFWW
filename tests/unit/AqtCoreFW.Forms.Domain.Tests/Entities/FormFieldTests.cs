using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using FluentAssertions;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for FormField entity.
/// Covers factory creation, validation rules, options, and deactivation.
/// FormField is T1: long Id (DB-assigned PK). FormTemplateId=long (FK to T1 FormTemplate).
/// </summary>
public sealed class FormFieldTests
{
    // T1 entity — Id is DB-assigned long; use a constant representing the FK
    private const long TemplateId = 1L;

    // --- Factory ---

    [Fact]
    public void Create_WithValidInputs_ReturnsActiveField()
    {
        var field = FormField.Create(
            TemplateId,
            "email",
            FormFieldType.Text,
            "Địa Chỉ Email",
            "Email Address",
            1,
            true,
            1,
            null);

        field.Should().NotBeNull();
        field.FormTemplateId.Should().Be(TemplateId);
        field.FieldKey.Should().Be("email");
        field.Type.Should().Be(FormFieldType.Text);
        field.LabelVi.Should().Be("Địa Chỉ Email");
        field.LabelEn.Should().Be("Email Address");
        field.DisplayOrder.Should().Be(1);
        field.Required.Should().BeTrue();
        field.IsActive.Should().BeTrue();
        field.AddedInVersion.Should().Be(1);
    }

    [Fact]
    public void Create_ReturnsTwoDistinctObjects()
    {
        // T1 Ids are DB-assigned; before save both are 0, but objects are distinct references
        var field1 = FormField.Create(TemplateId, "field1", FormFieldType.Text, "F1", "F1", 1, false, 1, null);
        var field2 = FormField.Create(TemplateId, "field2", FormFieldType.Text, "F2", "F2", 2, false, 1, null);

        field1.Should().NotBeSameAs(field2);
        field1.FieldKey.Should().NotBe(field2.FieldKey);
    }

    // --- Field Types ---

    [Theory]
    [InlineData(FormFieldType.Text)]
    [InlineData(FormFieldType.Number)]
    [InlineData(FormFieldType.Date)]
    [InlineData(FormFieldType.Boolean)]
    [InlineData(FormFieldType.Textarea)]
    [InlineData(FormFieldType.File)]
    [InlineData(FormFieldType.Signature)]
    public void Create_SupportAllPhase1FieldTypes(FormFieldType type)
    {
        var field = FormField.Create(
            TemplateId,
            "test_field",
            type,
            "Test",
            "Test",
            1,
            false,
            1,
            null);

        field.Type.Should().Be(type);
    }

    // --- Validation Rules ---

    [Fact]
    public void Create_WithJsonValidationRules_StoresJson()
    {
        var validationJson = """{"minLength": 5, "maxLength": 100, "pattern": "^[A-Za-z0-9]+$"}""";

        var field = FormField.Create(
            TemplateId,
            "code_field",
            FormFieldType.Text,
            "Code",
            "Code",
            1,
            true,
            1,
            validationJson);

        field.ValidationRulesJson.Should().Be(validationJson);
    }

    [Fact]
    public void Create_WithoutValidationRules_StoresNull()
    {
        var field = FormField.Create(
            TemplateId,
            "simple_field",
            FormFieldType.Text,
            "Simple",
            "Simple",
            1,
            false,
            1,
            null);

        field.ValidationRulesJson.Should().BeNull();
    }

    // --- Required Flag ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_PreservesRequiredFlag(bool required)
    {
        var field = FormField.Create(
            TemplateId,
            "test",
            FormFieldType.Text,
            "Test",
            "Test",
            1,
            required,
            1,
            null);

        field.Required.Should().Be(required);
    }

    // --- Display Order ---

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void Create_SupportsCustomDisplayOrder(int displayOrder)
    {
        var field = FormField.Create(
            TemplateId,
            "test",
            FormFieldType.Text,
            "Test",
            "Test",
            displayOrder,
            false,
            1,
            null);

        field.DisplayOrder.Should().Be(displayOrder);
    }

    // --- Version Tracking ---

    [Fact]
    public void Create_TracksAddedInVersion()
    {
        var field1 = FormField.Create(TemplateId, "field1", FormFieldType.Text, "F1", "F1", 1, false, 1, null);
        var field2 = FormField.Create(TemplateId, "field2", FormFieldType.Text, "F2", "F2", 2, false, 3, null);

        field1.AddedInVersion.Should().Be(1);
        field2.AddedInVersion.Should().Be(3);
    }

    // --- Deactivation ---

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var field = FormField.Create(TemplateId, "test", FormFieldType.Text, "Test", "Test", 1, false, 1, null);

        field.Deactivate();

        field.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_CanBeCalledMultipleTimes()
    {
        var field = FormField.Create(TemplateId, "test", FormFieldType.Text, "Test", "Test", 1, false, 1, null);

        field.Deactivate();
        field.Deactivate();

        field.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_AfterDeactivation_CannotBeReactivated()
    {
        var field = FormField.Create(TemplateId, "test", FormFieldType.Text, "Test", "Test", 1, false, 1, null);

        field.Deactivate();
        var isStillInactive = !field.IsActive;

        // No public Reactivate method, so once deactivated, stays deactivated
        isStillInactive.Should().BeTrue();
    }

    // --- Options (for Select/Checkbox types) ---

    [Fact]
    public void Create_WithoutOptions_ReturnsEmptyOptionsCollection()
    {
        var field = FormField.Create(
            TemplateId,
            "status",
            FormFieldType.EnumRef,
            "Status",
            "Status",
            1,
            false,
            1,
            null);

        field.Options.Should().NotBeNull();
        field.Options.Should().BeEmpty();
    }

    [Fact]
    public void AddOption_CreatesNewOption()
    {
        var field = FormField.Create(
            TemplateId,
            "status",
            FormFieldType.EnumRef,
            "Status",
            "Status",
            1,
            false,
            1,
            null);

        // field.Id is 0 before DB save (T1 DB-assigned); pass 0 as formFieldId
        var option = FormFieldOption.Create(field.Id, "active", "Hoạt Động", "Active", 1);
        field.AddOption(option);

        option.Should().NotBeNull();
        option.Value.Should().Be("active");
        option.LabelEn.Should().Be("Active");
        option.LabelVi.Should().Be("Hoạt Động");
        option.DisplayOrder.Should().Be(1);
        field.Options.Should().ContainSingle();
    }

    [Fact]
    public void AddOption_AllowsMultipleOptionsWithUniqueValues()
    {
        var field = FormField.Create(
            TemplateId,
            "status",
            FormFieldType.EnumRef,
            "Status",
            "Status",
            1,
            false,
            1,
            null);

        var option1 = FormFieldOption.Create(field.Id, "active", "Hoạt Động", "Active", 1);
        var option2 = FormFieldOption.Create(field.Id, "inactive", "Không Hoạt Động", "Inactive", 2);
        var option3 = FormFieldOption.Create(field.Id, "pending", "Chờ Xử Lý", "Pending", 3);
        field.AddOption(option1);
        field.AddOption(option2);
        field.AddOption(option3);

        field.Options.Should().HaveCount(3);
    }

    [Fact]
    public void AddOption_PreservesDisplayOrder()
    {
        var field = FormField.Create(
            TemplateId,
            "priority",
            FormFieldType.EnumRef,
            "Priority",
            "Priority",
            1,
            false,
            1,
            null);

        var lowOption = FormFieldOption.Create(field.Id, "low", "Thấp", "Low", 3);
        var mediumOption = FormFieldOption.Create(field.Id, "medium", "Trung Bình", "Medium", 1);
        var highOption = FormFieldOption.Create(field.Id, "high", "Cao", "High", 2);
        field.AddOption(lowOption);
        field.AddOption(mediumOption);
        field.AddOption(highOption);

        lowOption.DisplayOrder.Should().Be(3);
        mediumOption.DisplayOrder.Should().Be(1);
        highOption.DisplayOrder.Should().Be(2);
    }

    // --- Language Support ---

    [Fact]
    public void Create_SupportsBilingualLabels()
    {
        var field = FormField.Create(
            TemplateId,
            "name",
            FormFieldType.Text,
            "Họ và Tên",
            "Full Name",
            1,
            true,
            1,
            null);

        field.LabelVi.Should().Be("Họ và Tên");
        field.LabelEn.Should().Be("Full Name");
    }

    [Fact]
    public void AddOption_SupportsBilingualLabels()
    {
        var field = FormField.Create(
            TemplateId,
            "gender",
            FormFieldType.EnumRef,
            "Giới Tính",
            "Gender",
            1,
            false,
            1,
            null);

        var option = FormFieldOption.Create(field.Id, "M", "Nam", "Male", 1);
        field.AddOption(option);

        option.LabelVi.Should().Be("Nam");
        option.LabelEn.Should().Be("Male");
    }

    // --- Template Reference ---

    [Fact]
    public void Create_MaintainsTemplateReference()
    {
        const long template1Id = 10L;
        const long template2Id = 20L;

        var field1 = FormField.Create(template1Id, "field1", FormFieldType.Text, "F1", "F1", 1, false, 1, null);
        var field2 = FormField.Create(template2Id, "field2", FormFieldType.Text, "F2", "F2", 1, false, 1, null);

        field1.FormTemplateId.Should().Be(template1Id);
        field2.FormTemplateId.Should().Be(template2Id);
    }

    // --- FieldKey Validation ---

    [Theory]
    [InlineData("email")]
    [InlineData("phone_number")]
    [InlineData("firstName")]
    [InlineData("_private")]
    public void Create_AcceptsValidFieldKeys(string fieldKey)
    {
        var action = () => FormField.Create(
            TemplateId,
            fieldKey,
            FormFieldType.Text,
            "Test",
            "Test",
            1,
            false,
            1,
            null);

        action.Should().NotThrow();
    }
}
