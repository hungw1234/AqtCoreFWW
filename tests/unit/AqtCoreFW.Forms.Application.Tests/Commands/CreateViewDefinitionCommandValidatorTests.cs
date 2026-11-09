using AqtCoreFW.Forms.Application.Commands.CreateViewDefinition;
using AqtCoreFW.Forms.Domain.Enums;
using FluentAssertions;
using FluentValidation.TestHelper;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateViewDefinitionCommandValidator.
/// Validates all field-level rules without invoking the handler.
/// </summary>
public sealed class CreateViewDefinitionCommandValidatorTests
{
    private readonly CreateViewDefinitionCommandValidator _validator = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    // --- Valid command passes ---

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        var cmd = BuildValid();
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // --- TenantId ---

    [Fact]
    public void Validate_EmptyTenantId_HasError()
    {
        var cmd = BuildValid() with { TenantId = Guid.Empty };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.TenantId);
    }

    // --- CreatedBy ---

    [Fact]
    public void Validate_EmptyCreatedBy_HasError()
    {
        var cmd = BuildValid() with { CreatedBy = Guid.Empty };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.CreatedBy);
    }

    // --- Name ---

    [Fact]
    public void Validate_EmptyName_HasError()
    {
        var cmd = BuildValid() with { Name = "" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Validate_NameTooLong_HasError()
    {
        var cmd = BuildValid() with { Name = new string('x', 201) };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Name);
    }

    // --- EntityType ---

    [Fact]
    public void Validate_EmptyEntityType_HasError()
    {
        var cmd = BuildValid() with { EntityType = "" };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.EntityType);
    }

    // --- PageSize ---

    [Fact]
    public void Validate_ZeroPageSize_HasError()
    {
        var cmd = BuildValid() with { PageSize = 0 };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void Validate_PageSizeOver500_HasError()
    {
        var cmd = BuildValid() with { PageSize = 501 };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    // --- DefaultSortDir ---

    [Theory]
    [InlineData("up")]
    [InlineData("DESC")]
    [InlineData("ascending")]
    public void Validate_InvalidSortDir_HasError(string dir)
    {
        var cmd = BuildValid() with { DefaultSortDir = dir };
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.DefaultSortDir);
    }

    [Theory]
    [InlineData("asc")]
    [InlineData("desc")]
    public void Validate_ValidSortDir_NoError(string dir)
    {
        var cmd = BuildValid() with { DefaultSortDir = dir };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.DefaultSortDir);
    }

    [Fact]
    public void Validate_NullSortDir_NoError()
    {
        var cmd = BuildValid() with { DefaultSortDir = null };
        _validator.TestValidate(cmd).ShouldNotHaveValidationErrorFor(x => x.DefaultSortDir);
    }

    // --- Column child rules ---

    [Fact]
    public void Validate_ColumnWithEmptyFieldName_HasError()
    {
        var cmd = BuildValid() with
        {
            Columns = [new CreateViewColumnDto("", "Label", 1)]
        };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveAnyValidationError();
    }

    // --- Helpers ---

    private static CreateViewDefinitionCommand BuildValid() =>
        new(TenantId, "Test View", "Case", ViewType.List, UserId, PageSize: 20);
}
