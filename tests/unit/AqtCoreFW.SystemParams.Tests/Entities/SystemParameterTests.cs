using AqtCoreFW.SystemParams.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.SystemParams.Tests.Entities;

/// <summary>
/// Unit tests for SystemParameter entity.
/// Key business logic: key is lowercased on Create, UpdateValue throws on read-only params (GOV_CFG_001).
/// </summary>
public sealed class SystemParameterTests
{
    // --- Create ---

    [Fact]
    public void Create_NormalizesKeyToLowercase()
    {
        var param = SystemParameter.Create("Feature:Reports", "true", SystemParamDataType.Bool, "desc", isEditable: true);

        param.Key.Should().Be("feature:reports");
    }

    [Fact]
    public void Create_SetsValueAndDataType()
    {
        var param = SystemParameter.Create("app:timeout", "30", SystemParamDataType.Int, "timeout seconds", isEditable: true);

        param.Value.Should().Be("30");
        param.DataType.Should().Be(SystemParamDataType.Int);
    }

    [Fact]
    public void Create_SetsDescription()
    {
        var param = SystemParameter.Create("app:mode", "prod", SystemParamDataType.String, "Runtime mode", isEditable: false);

        param.Description.Should().Be("Runtime mode");
    }

    [Fact]
    public void Create_SetsIsEditable()
    {
        var editable = SystemParameter.Create("k", "v", SystemParamDataType.String, "d", isEditable: true);
        var readOnly = SystemParameter.Create("k2", "v", SystemParamDataType.String, "d", isEditable: false);

        editable.IsEditable.Should().BeTrue();
        readOnly.IsEditable.Should().BeFalse();
    }

    [Fact]
    public void Create_WithTenantId_SetsTenantId()
    {
        var param = SystemParameter.Create("feature:x", "true", SystemParamDataType.Bool, "d", isEditable: true, tenantId: "t1");

        param.TenantId.Should().Be("t1");
    }

    [Fact]
    public void Create_WithoutTenantId_TenantIdIsNull()
    {
        var param = SystemParameter.Create("feature:x", "true", SystemParamDataType.Bool, "d", isEditable: true);

        param.TenantId.Should().BeNull();
    }

    [Fact]
    public void Create_IdIsDefaultPreSave()
    {
        // Id is long (T1 bigint PK, DB-assigned). Pre-save value is 0 — uniqueness guaranteed by DB IDENTITY.
        var a = SystemParameter.Create("k1", "v", SystemParamDataType.String, "d", isEditable: true);
        var b = SystemParameter.Create("k2", "v", SystemParamDataType.String, "d", isEditable: true);

        a.Id.Should().Be(0);
        b.Id.Should().Be(0);
    }

    // --- UpdateValue ---

    [Fact]
    public void UpdateValue_EditableParam_ChangesValue()
    {
        var param = SystemParameter.Create("app:timeout", "30", SystemParamDataType.Int, "d", isEditable: true);

        param.UpdateValue("60");

        param.Value.Should().Be("60");
    }

    [Fact]
    public void UpdateValue_EditableParam_SetsUpdatedAt()
    {
        var param = SystemParameter.Create("app:timeout", "30", SystemParamDataType.Int, "d", isEditable: true);
        // UpdatedAt is DateTime (UTC, datetime2(3)) per Phase-4a Entity refactor.
        var before = DateTime.UtcNow.AddSeconds(-1);

        param.UpdateValue("60");

        param.UpdatedAt.Should().NotBeNull();
        param.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public void UpdateValue_ReadOnlyParam_ThrowsInvalidOperationException()
    {
        var param = SystemParameter.Create("system:version", "1.0", SystemParamDataType.String, "d", isEditable: false);

        var act = () => param.UpdateValue("2.0");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GOV_CFG_001*");
    }

    [Fact]
    public void UpdateValue_ReadOnlyParam_DoesNotChangeValue()
    {
        var param = SystemParameter.Create("system:version", "1.0", SystemParamDataType.String, "d", isEditable: false);

        try { param.UpdateValue("2.0"); } catch (InvalidOperationException) { /* expected */ }

        param.Value.Should().Be("1.0");
    }
}
