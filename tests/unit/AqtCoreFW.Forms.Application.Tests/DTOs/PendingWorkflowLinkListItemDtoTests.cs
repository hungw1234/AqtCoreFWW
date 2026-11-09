using AqtCoreFW.Forms.Application.DTOs;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.DTOs;

/// <summary>
/// DTO-symmetry guard (phase-05 tests spec): pins the exact field set of
/// <see cref="PendingWorkflowLinkListItemDto"/> so the FE contract (Phase 06) can be authored against
/// a known, stable shape. A field renamed/added/removed here without updating this test is a signal
/// the FE hand-written interface (no orval regen for this slice) is now out of sync.
/// </summary>
public sealed class PendingWorkflowLinkListItemDtoTests
{
    [Fact]
    public void Dto_Exposes_Exactly_The_Fields_The_Controller_JSON_Will_Serialize()
    {
        var properties = typeof(PendingWorkflowLinkListItemDto)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "ApprovalWorkflowDefinitionId",
            "AttemptCount",
            "CreatedAt",
            "FormTemplateId",
            "Id",
            "LastError",
            "LastFailureCode",
            "NextRetryAt",
            "ResolvedAt",
            "ResolvedBy",
            "Status",
            "TemplateName",
        }.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        properties.Should().BeEquivalentTo(expected,
            "the FE Phase-06 hand-written interface must mirror this exact field set — " +
            "Id is SubmissionId (Guid AK), never the row's internal long PK");
    }

    [Fact]
    public void Id_Property_Is_A_Guid_Not_A_Long_InternalKey()
    {
        // Anti-regression: the row's own PK (long) must never leak as "Id" — Id is always the
        // SubmissionId Guid AK, per the entity's external-addressing convention.
        var idProperty = typeof(PendingWorkflowLinkListItemDto).GetProperty("Id");

        idProperty.Should().NotBeNull();
        idProperty!.PropertyType.Should().Be(typeof(Guid));
    }

    [Fact]
    public void CreatedAt_Is_Plain_DateTime_Not_DateTimeOffset()
    {
        // Anti-regression for the Dapper materialization bug this DTO hit at runtime: Entity.CreatedAt
        // is a plain DateTime (datetime2(3) convention) — declaring it DateTimeOffset here, mixed with
        // this DTO's other DateTimeOffset? columns, made Dapper's positional-record constructor-matching
        // fail with an opaque InvalidOperationException at query time (see AuditLogDto.cs precedent).
        var createdAtProperty = typeof(PendingWorkflowLinkListItemDto).GetProperty("CreatedAt");

        createdAtProperty.Should().NotBeNull();
        createdAtProperty!.PropertyType.Should().Be(typeof(DateTime));
    }

    [Fact]
    public void Dto_Is_A_Class_With_A_Parameterless_Constructor_ForDapperCompatibility()
    {
        // Anti-regression: a positional record here previously failed Dapper materialization at
        // runtime (500 on GET) despite compiling and passing all other unit tests — only an
        // integration test against a real SQL connection caught it. Pin the shape so nobody
        // reverts to a positional record without re-discovering the same runtime failure.
        var type = typeof(PendingWorkflowLinkListItemDto);

        type.IsClass.Should().BeTrue();
        type.GetConstructor(Type.EmptyTypes).Should().NotBeNull(
            "Dapper requires a parameterless constructor + settable properties for this column shape");
    }
}
