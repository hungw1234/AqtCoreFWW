using FluentAssertions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.SharedKernel.Tests.Domain;

/// <summary>
/// Tests Entity (long Id PK) and AuditableEntity (Guid CreatedBy/ModifiedBy + ClassificationLevel).
/// Phase-2 Foundation Refactor (2026-05-03): Id is long (was Guid generic). CreatedAt is DateTime (was DateTimeOffset).
/// TC-SK-A009, TC-SK-A010
/// </summary>
public sealed class EntityAndAuditableEntityTests
{
    // Test-only concrete Entity. Id is protected-set in base; expose via test ctor.
    private sealed class SampleEntity : Entity
    {
        public SampleEntity() { }
        public SampleEntity(long id) { Id = id; }
    }

    private sealed class SampleAuditableEntity : AuditableEntity
    {
        public SampleAuditableEntity() { }
        public SampleAuditableEntity(long id) { Id = id; }
    }

    [Fact]
    public void Entity_DefaultId_IsZero()
    {
        var entity = new SampleEntity();
        entity.Id.Should().Be(0L);
    }

    [Fact]
    public void Entity_AssignedId_IsAccessible()
    {
        var a = new SampleEntity(42);
        var b = new SampleEntity(42);

        a.Id.Should().Be(b.Id);
    }

    [Fact]
    public void Entity_DifferentId_HaveDifferentIds()
    {
        var a = new SampleEntity(1);
        var b = new SampleEntity(2);

        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void Entity_MarkDeleted_SetsIsDeletedTrue()
    {
        var entity = new SampleEntity(1);

        entity.MarkDeleted();

        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void Entity_Delete_SetsIsDeletedTrue()
    {
        var entity = new SampleEntity(1);

        entity.Delete();

        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void Entity_CreatedAt_IsSetOnCreation_AsUtcDateTime()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var entity = new SampleEntity();

        entity.CreatedAt.Should().BeAfter(before);
        entity.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void AuditableEntity_SetAuditUpdate_SetsModifiedByAndUpdatedAt()
    {
        var entity = new SampleAuditableEntity(1);
        var userId = Guid.NewGuid();
        var before = DateTime.UtcNow.AddSeconds(-1);

        entity.SetAuditUpdate(userId);

        entity.ModifiedBy.Should().Be(userId);
        entity.UpdatedAt.Should().NotBeNull();
        entity.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public void AuditableEntity_SetAuditCreate_SetsCreatedBy()
    {
        var entity = new SampleAuditableEntity(1);
        var userId = Guid.NewGuid();

        entity.SetAuditCreate(userId);

        entity.CreatedBy.Should().Be(userId);
    }

    [Fact]
    public void AuditableEntity_DefaultClassificationLevel_IsInternal()
    {
        var entity = new SampleAuditableEntity(1);

        entity.ClassificationLevel.Should().Be(ClassificationLevel.Internal);
    }
}
