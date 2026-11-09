using AqtCoreFW.Forms.Application.Commands.SignSubmissionDocument;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for GenerateAndInitiateSubmissionSigningCommandValidator.
/// Covers validation of SignType, DocNumber, and all required fields.
/// </summary>
public sealed class GenerateAndInitiateSubmissionSigningCommandValidatorTests
{
    private readonly GenerateAndInitiateSubmissionSigningCommandValidator _validator;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();
    private static readonly Guid TestSubmissionId = Guid.NewGuid();

    public GenerateAndInitiateSubmissionSigningCommandValidatorTests()
    {
        _validator = new GenerateAndInitiateSubmissionSigningCommandValidator();
    }

    // ─── SignType validation ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("Issued")]
    [InlineData("Approved")]
    [InlineData("Income")]
    [InlineData("Comment")]
    [InlineData("Appendix")]
    [InlineData("Copy")]
    [InlineData("Files")]
    public void Validate_ValidSignType_PassesValidation(string signType)
    {
        var command = BuildCommand(signType: signType);
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.SignType);
    }

    [Fact]
    public void Validate_EmptySignType_FailsValidation()
    {
        var command = BuildCommand(signType: "");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.SignType);
    }

    [Fact]
    public void Validate_InvalidSignType_FailsValidation()
    {
        var command = BuildCommand(signType: "InvalidType");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.SignType)
            .WithErrorMessage("SignType không hợp lệ. Các giá trị được phép: Issued, Approved, Income, Comment, Appendix, Copy, Files.");
    }

    [Fact]
    public void Validate_CaseSensitiveSignType_FailsValidation()
    {
        var command = BuildCommand(signType: "issued"); // lowercase
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.SignType);
    }

    // ─── DocNumber validation ─────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidDocNumber_PassesValidation()
    {
        var command = BuildCommand(docNumber: "QĐ/2026/001");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.DocNumber);
    }

    [Fact]
    public void Validate_EmptyDocNumber_FailsValidation()
    {
        var command = BuildCommand(docNumber: "");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.DocNumber);
    }

    [Fact]
    public void Validate_DocNumberExceedsMaxLength_FailsValidation()
    {
        var docNumber = new string('A', 201);
        var command = BuildCommand(docNumber: docNumber);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.DocNumber);
    }

    [Fact]
    public void Validate_DocNumberAtMaxLength_PassesValidation()
    {
        var docNumber = new string('A', 200);
        var command = BuildCommand(docNumber: docNumber);
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.DocNumber);
    }

    // ─── IssuedDate validation ────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidIssuedDate_PassesValidation()
    {
        var command = BuildCommand(issuedDate: new DateTime(2026, 6, 17));
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.IssuedDate);
    }

    [Fact]
    public void Validate_MinValueIssuedDate_FailsValidation()
    {
        var command = BuildCommand(issuedDate: DateTime.MinValue);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.IssuedDate);
    }

    // ─── ProviderKey validation ───────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidProviderKey_PassesValidation()
    {
        var command = BuildCommand(providerKey: "usb-vgca");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.ProviderKey);
    }

    [Fact]
    public void Validate_EmptyProviderKey_FailsValidation()
    {
        var command = BuildCommand(providerKey: "");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.ProviderKey);
    }

    [Fact]
    public void Validate_ProviderKeyExceedsMaxLength_FailsValidation()
    {
        var providerKey = new string('A', 101);
        var command = BuildCommand(providerKey: providerKey);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.ProviderKey);
    }

    // ─── BaseUrl validation ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidBaseUrl_PassesValidation()
    {
        var command = BuildCommand(baseUrl: "https://api.example.com");
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.BaseUrl);
    }

    [Fact]
    public void Validate_EmptyBaseUrl_FailsValidation()
    {
        var command = BuildCommand(baseUrl: "");
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.BaseUrl);
    }

    [Fact]
    public void Validate_BaseUrlExceedsMaxLength_FailsValidation()
    {
        var baseUrl = "https://" + new string('a', 250) + ".com";
        var command = BuildCommand(baseUrl: baseUrl);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.BaseUrl);
    }

    // ─── SubmissionPublicId validation ────────────────────────────────────────────

    [Fact]
    public void Validate_EmptySubmissionPublicId_FailsValidation()
    {
        var command = BuildCommand(submissionPublicId: Guid.Empty);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.SubmissionPublicId);
    }

    [Fact]
    public void Validate_ValidSubmissionPublicId_PassesValidation()
    {
        var command = BuildCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.SubmissionPublicId);
    }

    // ─── TenantId validation ──────────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyTenantId_FailsValidation()
    {
        var command = BuildCommand(tenantId: Guid.Empty);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.TenantId);
    }

    [Fact]
    public void Validate_ValidTenantId_PassesValidation()
    {
        var command = BuildCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.TenantId);
    }

    // ─── InitiatedBy validation ───────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyInitiatedBy_FailsValidation()
    {
        var command = BuildCommand(initiatedBy: Guid.Empty);
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.InitiatedBy);
    }

    [Fact]
    public void Validate_ValidInitiatedBy_PassesValidation()
    {
        var command = BuildCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(c => c.InitiatedBy);
    }

    // ─── Complete valid command ───────────────────────────────────────────────────

    [Fact]
    public void Validate_CompleteValidCommand_PassesAllValidations()
    {
        var command = BuildCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ─── Helper ───────────────────────────────────────────────────────────────────

    private GenerateAndInitiateSubmissionSigningCommand BuildCommand(
        Guid? submissionPublicId = null,
        Guid? tenantId = null,
        Guid? initiatedBy = null,
        string? signType = null,
        string? docNumber = null,
        DateTime? issuedDate = null,
        string? providerKey = null,
        string? baseUrl = null) =>
        new(
            submissionPublicId ?? TestSubmissionId,
            tenantId ?? TestTenantId,
            initiatedBy ?? TestUserId,
            signType ?? "Issued",
            docNumber ?? "QĐ/2026/001",
            issuedDate ?? new DateTime(2026, 6, 17),
            providerKey ?? "usb-vgca",
            baseUrl ?? "https://api.example.com");
}
