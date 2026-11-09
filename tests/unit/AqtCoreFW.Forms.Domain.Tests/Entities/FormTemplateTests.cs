using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for FormTemplate aggregate root.
/// Covers factory creation, field management, status transitions, and validation.
/// Phase-07: FormTemplate retiered T1→T2 — factory requires IGuidGenerator gen.
/// </summary>
public sealed class FormTemplateTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId  = Guid.NewGuid();

    // IGuidGenerator stub — each NewIdMonotonic() call returns a fresh Guid (T2 PublicId)
    private static readonly IGuidGenerator Gen = CreateGen();
    private static IGuidGenerator CreateGen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return g;
    }

    // --- Factory ---

    [Fact]
    public void Create_WithValidInputs_ReturnsTemplateInDraftStatus()
    {
        var result = FormTemplate.Create(
            Gen,
            TenantId,
            "Test Form",
            "Biểu Mẫu Kiểm Tra",
            "test_form_01",
            StorageMode.Json,
            ActorId);

        result.Should().NotBeNull();
        result.Status.Should().Be(FormTemplateStatus.Draft);
        result.Version.Should().Be(1);
        result.Name.Should().Be("Test Form");
        result.NameVi.Should().Be("Biểu Mẫu Kiểm Tra");
        result.Code.Should().Be("test_form_01");
        result.StorageMode.Should().Be(StorageMode.Json);
        result.TenantId.Should().Be(TenantId);
        result.CreatedBy.Should().Be(ActorId);
    }

    [Fact]
    public void Create_ReturnsTwoDistinctObjects()
    {
        // T2: PublicId (Guid) is set by IGuidGenerator in factory; objects are distinct references
        var template1 = FormTemplate.Create(Gen, TenantId, "Form 1", "Form 1 VI", "form_1", StorageMode.Json, ActorId);
        var template2 = FormTemplate.Create(Gen, TenantId, "Form 2", "Form 2 VI", "form_2", StorageMode.Json, ActorId);

        template1.Should().NotBeSameAs(template2);
        template1.Code.Should().NotBe(template2.Code);
    }

    // --- Code Validation ---

    [Theory]
    [InlineData("valid_code")]
    [InlineData("_leading_underscore")]
    [InlineData("code-with-hyphens")]
    [InlineData("UPPERCASE_CODE")]
    [InlineData("mixed_Case_123")]
    public void Create_WithValidCode_Succeeds(string code)
    {
        var action = () => FormTemplate.Create(Gen, TenantId, "Test", "Test VI", code, StorageMode.Json, ActorId);
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123_starts_with_number")]
    [InlineData("-invalid-leading-hyphen")]
    [InlineData("code with spaces")]
    [InlineData("code@with#special$chars")]
    public void Create_WithInvalidCode_ThrowsException(string code)
    {
        var action = () => FormTemplate.Create(Gen, TenantId, "Test", "Test VI", code, StorageMode.Json, ActorId);
        action.Should().Throw<InvalidFormSchemaException>();
    }

    [Fact]
    public void Create_WithCodeExceedingMaxLength_ThrowsException()
    {
        var longCode = new string('a', 101);
        var action = () => FormTemplate.Create(Gen, TenantId, "Test", "Test VI", longCode, StorageMode.Json, ActorId);
        action.Should().Throw<InvalidFormSchemaException>();
    }

    // --- Field Management ---

    [Fact]
    public void AddField_InDraftStatus_SucceedsAndDoesNotIncrementVersion()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);

        var field = template.AddField(
            "email",
            FormFieldType.Text,
            "Email",
            "Email",
            1,
            true,
            null,
            ActorId);

        field.Should().NotBeNull();
        field.FieldKey.Should().Be("email");
        template.Version.Should().Be(1); // No increment in Draft
        template.Fields.Should().ContainSingle();
    }

    [Fact]
    public void AddField_InActiveStatus_IncrementVersionAndSucceeds()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);
        var field1 = template.AddField("field1", FormFieldType.Text, "Field 1", "Field 1", 1, false, null, ActorId);
        template.Publish(ActorId);

        var initialVersion = template.Version;
        var field2 = template.AddField("field2", FormFieldType.Text, "Field 2", "Field 2", 2, false, null, ActorId);

        field2.Should().NotBeNull();
        template.Version.Should().Be(initialVersion + 1);
        template.Fields.Should().HaveCount(2);
    }

    [Fact]
    public void AddField_WithDuplicateFieldKey_ThrowsException()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);
        template.AddField("email", FormFieldType.Text, "Email", "Email", 1, true, null, ActorId);

        var action = () => template.AddField("email", FormFieldType.Text, "Email 2", "Email 2", 2, false, null, ActorId);
        action.Should().Throw<InvalidFormSchemaException>();
    }

    [Fact]
    public void AddField_InInactiveStatus_ThrowsException()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);
        var field = template.AddField("field1", FormFieldType.Text, "Field 1", "Field 1", 1, false, null, ActorId);
        template.Publish(ActorId);
        template.Deactivate(ActorId);

        var action = () => template.AddField("field2", FormFieldType.Text, "Field 2", "Field 2", 2, false, null, ActorId);
        action.Should().Throw<InvalidFormSchemaException>();
    }

    [Fact]
    public void AddField_WithDeactivatedFieldKey_Succeeds()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);
        var field1 = template.AddField("email", FormFieldType.Text, "Email", "Email", 1, true, null, ActorId);
        template.Publish(ActorId);
        template.DeactivateField(field1.Id, ActorId);

        // Same key should now be available again
        var action = () => template.AddField("email", FormFieldType.Text, "Email New", "Email New", 2, false, null, ActorId);
        action.Should().NotThrow();
    }

    [Fact]
    public void DeactivateField_WithValidFieldId_Succeeds()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);
        var field = template.AddField("email", FormFieldType.Text, "Email", "Email", 1, true, null, ActorId);

        template.DeactivateField(field.Id, ActorId);

        field.IsActive.Should().BeFalse();
    }

    [Fact]
    public void DeactivateField_WithInvalidFieldId_ThrowsException()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);

        // Use a non-zero long that won't match any field (all unsaved fields have Id=0)
        var action = () => template.DeactivateField(9999L, ActorId);
        action.Should().Throw<InvalidFormSchemaException>();
    }

    // --- Status Transitions ---

    [Fact]
    public void Publish_DraftFormWithFields_TransitionsToActiveForJsonMode()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);
        template.AddField("field1", FormFieldType.Text, "Field 1", "Field 1", 1, false, null, ActorId);

        template.Publish(ActorId);

        template.Status.Should().Be(FormTemplateStatus.Active);
    }

    [Fact]
    public void Publish_DraftFormWithFields_TransitionsToMaterializingForTableMode()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Table, ActorId);
        template.AddField("field1", FormFieldType.Text, "Field 1", "Field 1", 1, false, null, ActorId);

        template.Publish(ActorId);

        template.Status.Should().Be(FormTemplateStatus.Materializing);
    }

    [Fact]
    public void Publish_DraftFormWithoutFields_ThrowsException()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);

        var action = () => template.Publish(ActorId);
        action.Should().Throw<InvalidFormSchemaException>();
    }

    [Fact]
    public void Publish_NonDraftForm_ThrowsException()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);
        template.AddField("field1", FormFieldType.Text, "Field 1", "Field 1", 1, false, null, ActorId);
        template.Publish(ActorId);

        var action = () => template.Publish(ActorId);
        action.Should().Throw<InvalidFormSchemaException>();
    }

    [Fact]
    public void MarkActive_FromMaterializingStatus_TransitionsToActive()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Table, ActorId);
        template.AddField("field1", FormFieldType.Text, "Field 1", "Field 1", 1, false, null, ActorId);
        template.Publish(ActorId); // -> Materializing

        template.MarkActive(ActorId);

        template.Status.Should().Be(FormTemplateStatus.Active);
    }

    [Fact]
    public void MarkActive_FromNonMaterializingStatus_ThrowsException()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);
        template.AddField("field1", FormFieldType.Text, "Field 1", "Field 1", 1, false, null, ActorId);
        template.Publish(ActorId); // -> Active (not Materializing)

        var action = () => template.MarkActive(ActorId);
        action.Should().Throw<InvalidFormSchemaException>();
    }

    [Fact]
    public void Deactivate_FromAnyStatus_TransitionsToInactive()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);
        template.AddField("field1", FormFieldType.Text, "Field 1", "Field 1", 1, false, null, ActorId);
        template.Publish(ActorId);

        template.Deactivate(ActorId);

        template.Status.Should().Be(FormTemplateStatus.Inactive);
    }

    // --- Code Updates ---

    [Fact]
    public void SetCode_InDraftStatus_Succeeds()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "original_code", StorageMode.Json, ActorId);

        template.SetCode("new_code", ActorId);

        template.Code.Should().Be("new_code");
    }

    [Fact]
    public void SetCode_InNonDraftStatus_ThrowsException()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);
        template.AddField("field1", FormFieldType.Text, "Field 1", "Field 1", 1, false, null, ActorId);
        template.Publish(ActorId);

        var action = () => template.SetCode("new_code", ActorId);
        action.Should().Throw<InvalidFormSchemaException>();
    }

    [Fact]
    public void SetCode_WithInvalidCode_ThrowsException()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);

        var action = () => template.SetCode("123_invalid", ActorId);
        action.Should().Throw<InvalidFormSchemaException>();
    }

    // --- Domain Events ---

    [Fact]
    public void Create_PopulatesdDomainEventsCollection()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);

        template.DomainEvents.Should().NotBeNull();
        template.DomainEvents.Should().BeAssignableTo<IReadOnlyCollection<object>>();
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var template = FormTemplate.Create(Gen, TenantId, "Test", "Test VI", "test", StorageMode.Json, ActorId);
        template.ClearDomainEvents();

        template.DomainEvents.Should().BeEmpty();
    }
}
