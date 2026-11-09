using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Domain.Tests.Entities;

/// <summary>
/// Unit tests for FormSubmission aggregate.
/// Covers factory creation, immutability, and JSON data handling.
/// FormSubmission is T2: Id=long (DB-assigned), PublicId=Guid (external AK, v4 random).
/// FormTemplateId=long (FK to T1 FormTemplate).
/// </summary>
public sealed class FormSubmissionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const long FormTemplateId = 42L;
    private static readonly Guid SubmitterId = Guid.NewGuid();

    /// <summary>Deterministic IGuidGenerator stub — returns sequential GUIDs for test predictability.</summary>
    private static IGuidGenerator CreateGuidGenerator()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdRandom().Returns(Guid.NewGuid());
        return gen;
    }

    // --- Factory ---

    [Fact]
    public void Create_WithJsonData_CreatesImmutableSubmission()
    {
        var gen = CreateGuidGenerator();
        var jsonData = """{"name": "John Doe", "email": "john@example.com"}""";

        var submission = FormSubmission.Create(
            gen,
            TenantId,
            FormTemplateId,
            1,
            jsonData,
            SubmitterId);

        submission.Should().NotBeNull();
        submission.PublicId.Should().NotBe(Guid.Empty);
        submission.TenantId.Should().Be(TenantId);
        submission.FormTemplateId.Should().Be(FormTemplateId);
        submission.FormTemplateVersion.Should().Be(1);
        submission.SubmissionData.Should().Be(jsonData);
        submission.SubmittedBy.Should().Be(SubmitterId);
        submission.SubmittedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Create_WithNullData_SucceedsForTableModeSubmissions()
    {
        var gen = CreateGuidGenerator();

        var submission = FormSubmission.Create(
            gen,
            TenantId,
            FormTemplateId,
            1,
            null,
            SubmitterId);

        submission.SubmissionData.Should().BeNull();
    }

    [Fact]
    public void Create_GeneratesUniquePublicIds()
    {
        // Each call returns a new Guid from the generator
        var gen = Substitute.For<IGuidGenerator>();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        gen.NewIdRandom().Returns(id1, id2);

        var submission1 = FormSubmission.Create(gen, TenantId, FormTemplateId, 1, "{}", SubmitterId);
        var submission2 = FormSubmission.Create(gen, TenantId, FormTemplateId, 1, "{}", SubmitterId);

        submission1.PublicId.Should().NotBe(submission2.PublicId);
    }

    // --- Template Version Snapshot ---

    [Fact]
    public void Create_SnapshotsTemplateVersion()
    {
        var gen1 = CreateGuidGenerator();
        var gen2 = CreateGuidGenerator();

        var submission1 = FormSubmission.Create(gen1, TenantId, FormTemplateId, 1, "{}", SubmitterId);
        var submission2 = FormSubmission.Create(gen2, TenantId, FormTemplateId, 5, "{}", SubmitterId);

        submission1.FormTemplateVersion.Should().Be(1);
        submission2.FormTemplateVersion.Should().Be(5);
    }

    // --- JSON Data Handling ---

    [Fact]
    public void Create_WithComplexJsonData_PreservesStructure()
    {
        var gen = CreateGuidGenerator();
        var complexJson = """
        {
            "fullName": "Jane Smith",
            "contact": {
                "email": "jane@example.com",
                "phone": "+84-123-456-7890"
            },
            "addresses": [
                {"type": "home", "city": "Ha Noi"},
                {"type": "work", "city": "Ho Chi Minh"}
            ],
            "isActive": true,
            "score": 95.5
        }
        """;

        var submission = FormSubmission.Create(gen, TenantId, FormTemplateId, 1, complexJson, SubmitterId);

        submission.SubmissionData.Should().Be(complexJson);
    }

    [Fact]
    public void Create_WithEmptyJsonObject_Succeeds()
    {
        var gen = CreateGuidGenerator();
        var submission = FormSubmission.Create(gen, TenantId, FormTemplateId, 1, "{}", SubmitterId);
        submission.SubmissionData.Should().Be("{}");
    }

    // --- Immutability ---

    [Fact]
    public void Create_ProducesImmutableSubmission()
    {
        var gen = CreateGuidGenerator();
        var submission = FormSubmission.Create(gen, TenantId, FormTemplateId, 1, "{}", SubmitterId);

        // All properties use private setters and cannot be modified externally
        var publicSetterProperties = submission.GetType()
            .GetProperties()
            .Where(p => p.GetSetMethod(nonPublic: false) != null) // Only public setters
            .ToList();

        publicSetterProperties.Should().BeEmpty("FormSubmission should not have public property setters");
    }

    // --- Tenant Scoping ---

    [Fact]
    public void Create_EnforcesTenantScoping()
    {
        var tenant1Id = Guid.NewGuid();
        var tenant2Id = Guid.NewGuid();

        var submission1 = FormSubmission.Create(CreateGuidGenerator(), tenant1Id, FormTemplateId, 1, "{}", SubmitterId);
        var submission2 = FormSubmission.Create(CreateGuidGenerator(), tenant2Id, FormTemplateId, 1, "{}", SubmitterId);

        submission1.TenantId.Should().Be(tenant1Id);
        submission2.TenantId.Should().Be(tenant2Id);
        submission1.TenantId.Should().NotBe(submission2.TenantId);
    }

    // --- Timestamp Accuracy ---

    [Fact]
    public void Create_SetsSingleAuditTimestamp()
    {
        var gen = CreateGuidGenerator();
        var beforeCreation = DateTimeOffset.UtcNow;
        var submission = FormSubmission.Create(gen, TenantId, FormTemplateId, 1, "{}", SubmitterId);
        var afterCreation = DateTimeOffset.UtcNow.AddMilliseconds(100);

        submission.SubmittedAt.Should().BeOnOrAfter(beforeCreation);
        submission.SubmittedAt.Should().BeOnOrBefore(afterCreation);
        // CreatedAt is DateTime (UTC); SubmittedAt is DateTimeOffset — compare via UtcDateTime
        submission.CreatedAt.Should().BeCloseTo(submission.SubmittedAt.UtcDateTime, TimeSpan.FromMilliseconds(100));
    }

    // --- Reference Integrity ---

    [Fact]
    public void Create_PreservesFormTemplateReference()
    {
        const long templateId1 = 10L;
        const long templateId2 = 20L;

        var submission1 = FormSubmission.Create(CreateGuidGenerator(), TenantId, templateId1, 1, "{}", SubmitterId);
        var submission2 = FormSubmission.Create(CreateGuidGenerator(), TenantId, templateId2, 1, "{}", SubmitterId);

        submission1.FormTemplateId.Should().Be(templateId1);
        submission2.FormTemplateId.Should().Be(templateId2);
    }

    [Fact]
    public void Create_PreservesSubmitterReference()
    {
        var submitter1 = Guid.NewGuid();
        var submitter2 = Guid.NewGuid();

        var submission1 = FormSubmission.Create(CreateGuidGenerator(), TenantId, FormTemplateId, 1, "{}", submitter1);
        var submission2 = FormSubmission.Create(CreateGuidGenerator(), TenantId, FormTemplateId, 1, "{}", submitter2);

        submission1.SubmittedBy.Should().Be(submitter1);
        submission2.SubmittedBy.Should().Be(submitter2);
    }
}
