using AqtCoreFW.AssetManagement.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.AssetManagement.Domain.Tests.Entities;

public sealed class AssetManagementTests
{
    [Fact]
    public void Create_SetsFields()
    {
        var tenantId = Guid.NewGuid();
        var createdBy = Guid.NewGuid();

        var asset = AssetManagementEntity.Create(tenantId, "Laptop", "Office device", createdBy);

        asset.TenantId.Should().Be(tenantId);
        asset.Title.Should().Be("Laptop");
        asset.Description.Should().Be("Office device");
        asset.CreatedBy.Should().Be(createdBy);
        asset.Id.Should().Be(0);
    }

    [Fact]
    public void Update_ChangesAssetAndUpdateAuditFields()
    {
        var asset = AssetManagementEntity.Create(Guid.NewGuid(), "Laptop", null, Guid.NewGuid());
        var updatedBy = Guid.NewGuid();

        asset.Update("Desktop", "Updated", updatedBy);

        asset.Title.Should().Be("Desktop");
        asset.Description.Should().Be("Updated");
        asset.ModifiedBy.Should().Be(updatedBy);
    }
}
