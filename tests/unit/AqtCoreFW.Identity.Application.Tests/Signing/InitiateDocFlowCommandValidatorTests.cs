using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using FluentAssertions;

namespace AqtCoreFW.Identity.Application.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="InitiateDocFlowCommandValidator"/> focusing on the incremental
/// multi-signer chaining rules added for sequential PAdES signing.
///
/// Invariant: when SignerSequenceNumber > 1, PreviousSignedStorageKey is required and must reside
/// within the caller's tenant prefix. When sequence == 1, PreviousSignedStorageKey must be absent.
/// This prevents a caller from supplying a key belonging to a different tenant (cross-tenant read).
/// </summary>
public sealed class InitiateDocFlowCommandValidatorTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly InitiateDocFlowCommandValidator Validator = new();

    private static InitiateDocFlowCommand ValidCmd(
        int sequence = 1,
        string? previousSignedKey = null,
        string? originalKey = null)
        => new(
            TenantId:                TenantId,
            DocId:                   Guid.NewGuid(),
            SignType:                DocFlowSignType.Issued,
            OriginalStorageKey:      originalKey ?? $"{TenantId}/doc/original.pdf",
            Metadata:                new DocFlowMetadata(null, "VB-001", DateTime.UtcNow),
            UserId:                  Guid.NewGuid(),
            BaseUrl:                 "https://api.example.com",
            SignerSequenceNumber:    sequence,
            PreviousSignedStorageKey: previousSignedKey);

    private static bool HasErrorOn(InitiateDocFlowCommand cmd, string propertyName)
    {
        var result = Validator.Validate(cmd);
        return result.Errors.Any(e => e.PropertyName == propertyName);
    }

    // ── Sequence 1 (first signer) ──────────────────────────────────────────────

    [Fact]
    public void Validate_Sequence1_NullPreviousKey_PassesValidation()
    {
        var result = Validator.Validate(ValidCmd(sequence: 1, previousSignedKey: null));

        result.IsValid.Should().BeTrue("first signer with no previous key is the normal single-sign path");
    }

    [Fact]
    public void Validate_Sequence1_WithPreviousKey_FailsValidation()
    {
        // First signer must not supply a previous-signed key — the server derives whether this is
        // the first signature from the sequence number, never trusts the caller.
        var cmd = ValidCmd(sequence: 1, previousSignedKey: $"{TenantId}/doc/round1.signed.pdf");

        HasErrorOn(cmd, nameof(cmd.PreviousSignedStorageKey)).Should().BeTrue(
            "supplying a previous-signed key for sequence 1 violates the first-signer invariant");
    }

    // ── Sequence > 1 (subsequent signers) ─────────────────────────────────────

    [Fact]
    public void Validate_Sequence2_WithValidTenantPrefixedPreviousKey_PassesValidation()
    {
        var cmd = ValidCmd(
            sequence:          2,
            previousSignedKey: $"{TenantId}/doc/round1.signed.pdf");

        var result = Validator.Validate(cmd);

        result.Errors.Should().NotContain(
            e => e.PropertyName == nameof(cmd.PreviousSignedStorageKey)
              || e.PropertyName == nameof(cmd.SignerSequenceNumber),
            "sequence 2 with a valid tenant-prefixed previous key must pass all chaining rules");
    }

    [Fact]
    public void Validate_Sequence2_NullPreviousKey_FailsValidation()
    {
        // Sequence 2 without a previous-signed key: the validator must reject this because the
        // desktop tool would have no prior-signer artifact to append to.
        var cmd = ValidCmd(sequence: 2, previousSignedKey: null);

        HasErrorOn(cmd, nameof(cmd.PreviousSignedStorageKey)).Should().BeTrue(
            "sequence 2 without a previous key is an initiator error (422-class)");
    }

    [Fact]
    public void Validate_Sequence2_ForeignTenantPreviousKey_FailsValidation()
    {
        // A previous-signed key belonging to a different tenant must be rejected to prevent
        // cross-tenant storage reads during the [AllowAnonymous] download round-trip.
        var foreignTenant = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var cmd = ValidCmd(
            sequence:          2,
            previousSignedKey: $"{foreignTenant}/doc/round1.signed.pdf"); // foreign prefix

        var result = Validator.Validate(cmd);
        var prevKeyErrors = result.Errors
            .Where(e => e.PropertyName == nameof(cmd.PreviousSignedStorageKey))
            .ToList();

        prevKeyErrors.Should().NotBeEmpty("a foreign-tenant previous key must be rejected (cross-tenant guard)");
        prevKeyErrors.Should().Contain(
            e => e.ErrorMessage.Contains("tenant prefix", StringComparison.OrdinalIgnoreCase),
            "the error message must name the tenant-prefix constraint so operators can diagnose it");
    }

    [Fact]
    public void Validate_Sequence2_EmptyStringPreviousKey_FailsValidation()
    {
        var cmd = ValidCmd(sequence: 2, previousSignedKey: string.Empty);

        HasErrorOn(cmd, nameof(cmd.PreviousSignedStorageKey)).Should().BeTrue(
            "empty string is equivalent to null for sequence > 1 (key required)");
    }

    // ── Sequence number range ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidSequenceNumber_FailsValidation(int sequence)
    {
        var cmd = ValidCmd(sequence: sequence, previousSignedKey: null);

        HasErrorOn(cmd, nameof(cmd.SignerSequenceNumber)).Should().BeTrue(
            $"sequence {sequence} is below the minimum of 1");
    }

    // ── Original storage key still validated (existing behaviour unaffected) ───

    [Fact]
    public void Validate_OriginalKeyForeignTenant_FailsValidation()
    {
        // Existing guard: the original PDF key must be within the initiating tenant's prefix.
        // This test confirms the new sequence-number params did not disturb the existing rule.
        var foreignTenant = Guid.NewGuid();
        var cmd = ValidCmd(
            sequence:     1,
            originalKey:  $"{foreignTenant}/doc/original.pdf");

        var result = Validator.Validate(cmd);
        result.Errors.Should().Contain(
            e => e.PropertyName == nameof(cmd.OriginalStorageKey),
            "OriginalStorageKey with a foreign-tenant prefix must still be rejected");
    }
}
