using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// Unit tests for DelegationRule entity.
/// T1 entity: long Id (DB-assigned — 0 in unit tests without DB).
/// DelegatorUserId/DelegateUserId: Guid (identity FK exceptions).
/// Covers: factory guard clauses, IsEffectiveAt, Deactivate/Activate, UpdateWindow.
/// </summary>
public sealed class DelegationRuleTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid DelegatorId = Guid.NewGuid();
    private static readonly Guid DelegateId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateTimeOffset ValidFrom = Now.AddDays(-1);
    private static readonly DateTimeOffset ValidUntil = Now.AddDays(7);

    // --- Factory: happy path ---

    [Fact]
    public void Create_ValidArgs_ReturnsRule()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);

        rule.Should().NotBeNull();
        rule.DelegatorUserId.Should().Be(DelegatorId);
        rule.DelegateUserId.Should().Be(DelegateId);
        rule.ValidFrom.Should().Be(ValidFrom);
        rule.ValidUntil.Should().Be(ValidUntil);
        rule.TenantId.Should().Be(TenantId);
        rule.IsActive.Should().BeTrue();
        // T1 entity: Id is long (DB-assigned — 0 in unit tests)
        rule.Id.Should().BeGreaterThanOrEqualTo(0L);
    }

    [Fact]
    public void Create_SetsAuditCreateBy()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);

        rule.CreatedBy.Should().Be(CreatedBy);
    }

    // --- Factory: guard clauses ---

    [Fact]
    public void Create_EmptyDelegatorId_Throws()
    {
        var act = () => DelegationRule.Create(Guid.Empty, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);

        act.Should().Throw<ArgumentException>().WithMessage("*DelegatorUserId*");
    }

    [Fact]
    public void Create_EmptyDelegateId_Throws()
    {
        var act = () => DelegationRule.Create(DelegatorId, Guid.Empty, ValidFrom, ValidUntil, TenantId, CreatedBy);

        act.Should().Throw<ArgumentException>().WithMessage("*DelegateUserId*");
    }

    [Fact]
    public void Create_EmptyTenantId_Throws()
    {
        var act = () => DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, Guid.Empty, CreatedBy);

        act.Should().Throw<ArgumentException>().WithMessage("*TenantId*");
    }

    [Fact]
    public void Create_SameUserAsDelegatorAndDelegate_Throws()
    {
        var act = () => DelegationRule.Create(DelegatorId, DelegatorId, ValidFrom, ValidUntil, TenantId, CreatedBy);

        act.Should().Throw<ArgumentException>().WithMessage("*different users*");
    }

    [Fact]
    public void Create_ValidFromAfterValidUntil_Throws()
    {
        var from = Now.AddDays(5);
        var until = Now.AddDays(1);

        var act = () => DelegationRule.Create(DelegatorId, DelegateId, from, until, TenantId, CreatedBy);

        act.Should().Throw<ArgumentException>().WithMessage("*ValidFrom*");
    }

    [Fact]
    public void Create_ValidFromEqualValidUntil_Throws()
    {
        var act = () => DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidFrom, TenantId, CreatedBy);

        act.Should().Throw<ArgumentException>().WithMessage("*ValidFrom*");
    }

    // --- IsEffectiveAt ---

    [Fact]
    public void IsEffectiveAt_WithinWindow_ReturnsTrue()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);

        rule.IsEffectiveAt(Now).Should().BeTrue();
    }

    [Fact]
    public void IsEffectiveAt_BeforeWindow_ReturnsFalse()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);

        rule.IsEffectiveAt(ValidFrom.AddSeconds(-1)).Should().BeFalse();
    }

    [Fact]
    public void IsEffectiveAt_AfterWindow_ReturnsFalse()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);

        rule.IsEffectiveAt(ValidUntil.AddSeconds(1)).Should().BeFalse();
    }

    [Fact]
    public void IsEffectiveAt_WhenInactive_ReturnsFalse()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);
        rule.Deactivate(CreatedBy);

        rule.IsEffectiveAt(Now).Should().BeFalse();
    }

    [Fact]
    public void IsEffectiveAt_AtBoundary_ReturnsTrue()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);

        rule.IsEffectiveAt(ValidFrom).Should().BeTrue();
        rule.IsEffectiveAt(ValidUntil).Should().BeTrue();
    }

    // --- Deactivate / Activate ---

    [Fact]
    public void Deactivate_ActiveRule_SetsIsActiveFalse()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);

        rule.Deactivate(CreatedBy);

        rule.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Activate_InactiveRule_SetsIsActiveTrue()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);
        rule.Deactivate(CreatedBy);

        rule.Activate(CreatedBy);

        rule.IsActive.Should().BeTrue();
    }

    // --- UpdateWindow ---

    [Fact]
    public void UpdateWindow_ValidArgs_UpdatesFields()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);
        var newDelegate = Guid.NewGuid();
        var newFrom = Now.AddDays(1);
        var newUntil = Now.AddDays(14);

        rule.UpdateWindow(newDelegate, newFrom, newUntil, CreatedBy);

        rule.DelegateUserId.Should().Be(newDelegate);
        rule.ValidFrom.Should().Be(newFrom);
        rule.ValidUntil.Should().Be(newUntil);
    }

    [Fact]
    public void UpdateWindow_SameUserAsDelegator_Throws()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);

        var act = () => rule.UpdateWindow(DelegatorId, ValidFrom, ValidUntil, CreatedBy);

        act.Should().Throw<ArgumentException>().WithMessage("*different users*");
    }

    [Fact]
    public void UpdateWindow_InvalidWindow_Throws()
    {
        var rule = DelegationRule.Create(DelegatorId, DelegateId, ValidFrom, ValidUntil, TenantId, CreatedBy);

        var act = () => rule.UpdateWindow(DelegateId, ValidUntil, ValidFrom, CreatedBy);

        act.Should().Throw<ArgumentException>().WithMessage("*ValidFrom*");
    }
}
