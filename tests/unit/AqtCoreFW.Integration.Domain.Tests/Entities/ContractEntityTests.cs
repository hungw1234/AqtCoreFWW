using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Domain.Enums;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Integration.Domain.Tests.Entities;

/// <summary>
/// Unit tests for Contract entity.
/// Verifies factory, update, and state transitions (Draft → Active → Expired | Terminated).
/// Pure in-process tests — no DB, no HTTP.
/// </summary>
public sealed class ContractEntityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const long PartnerId = 100L;   // T1 Partner internal PK — long, not Guid
    private static readonly Guid CreatedBy = Guid.NewGuid();
    private static readonly DateTime EffectiveDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiryDate = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static IGuidGenerator MakeGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(Guid.NewGuid());
        return gen;
    }

    private static Contract CreateContract()
    {
        var gen = MakeGen();
        return Contract.Create(gen, TenantId, PartnerId, "Test Contract", "Description",
            EffectiveDate, ExpiryDate, """{"scope":"read"}""", CreatedBy);
    }

    // --- Factory ---

    [Fact]
    public void Create_ValidParams_ReturnsDraftContract()
    {
        var contract = CreateContract();
        contract.Status.Should().Be(ContractStatus.Draft);
    }

    [Fact]
    public void Create_ValidParams_SetsAllProperties()
    {
        var contract = CreateContract();
        contract.TenantId.Should().Be(TenantId);
        contract.PartnerId.Should().Be(PartnerId);
        contract.Title.Should().Be("Test Contract");
        contract.Description.Should().Be("Description");
        contract.EffectiveDate.Should().Be(EffectiveDate);
        contract.ExpiryDate.Should().Be(ExpiryDate);
        contract.DataScopeJson.Should().Be("""{"scope":"read"}""");
    }

    [Fact]
    public void Create_ValidParams_SetsPublicId()
    {
        var contract = CreateContract();
        contract.PublicId.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_ValidParams_SetsAuditFields()
    {
        var contract = CreateContract();
        contract.CreatedBy.Should().Be(CreatedBy);
    }

    // --- Update ---

    [Fact]
    public void Update_ChangesAllMutableFields()
    {
        var contract = CreateContract();
        var newExpiry = new DateTime(2028, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var modifiedBy = Guid.NewGuid();
        contract.Update("Updated Title", "New desc",
            EffectiveDate, newExpiry, """{"scope":"write"}""", modifiedBy);

        contract.Title.Should().Be("Updated Title");
        contract.Description.Should().Be("New desc");
        contract.ExpiryDate.Should().Be(newExpiry);
        contract.DataScopeJson.Should().Be("""{"scope":"write"}""");
    }

    [Fact]
    public void Update_SetsModifiedBy()
    {
        var contract = CreateContract();
        var modifiedBy = Guid.NewGuid();
        contract.Update("Title", null, EffectiveDate, null, null, modifiedBy);
        contract.ModifiedBy.Should().Be(modifiedBy);
    }

    // --- Activate ---

    [Fact]
    public void Activate_WhenDraft_SetsStatusActive()
    {
        var contract = CreateContract();
        contract.Activate();
        contract.Status.Should().Be(ContractStatus.Active);
    }

    [Fact]
    public void Activate_WhenActive_ThrowsInvalidOperation()
    {
        var contract = CreateContract();
        contract.Activate();
        var act = () => contract.Activate();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Activate_WhenExpired_ThrowsInvalidOperation()
    {
        var contract = CreateContract();
        contract.Activate();
        contract.MarkExpired();
        var act = () => contract.Activate();
        act.Should().Throw<InvalidOperationException>();
    }

    // --- Terminate ---

    [Fact]
    public void Terminate_WhenDraft_SetsStatusTerminated()
    {
        var contract = CreateContract();
        contract.Terminate();
        contract.Status.Should().Be(ContractStatus.Terminated);
    }

    [Fact]
    public void Terminate_WhenActive_SetsStatusTerminated()
    {
        var contract = CreateContract();
        contract.Activate();
        contract.Terminate();
        contract.Status.Should().Be(ContractStatus.Terminated);
    }

    [Fact]
    public void Terminate_WhenExpired_ThrowsInvalidOperation()
    {
        var contract = CreateContract();
        contract.Activate();
        contract.MarkExpired();
        var act = () => contract.Terminate();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Terminate_WhenAlreadyTerminated_ThrowsInvalidOperation()
    {
        var contract = CreateContract();
        contract.Terminate();
        var act = () => contract.Terminate();
        act.Should().Throw<InvalidOperationException>();
    }

    // --- MarkExpired ---

    [Fact]
    public void MarkExpired_WhenActive_SetsStatusExpired()
    {
        var contract = CreateContract();
        contract.Activate();
        contract.MarkExpired();
        contract.Status.Should().Be(ContractStatus.Expired);
    }

    [Fact]
    public void MarkExpired_WhenDraft_ThrowsInvalidOperation()
    {
        var contract = CreateContract();
        var act = () => contract.MarkExpired();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkExpired_WhenTerminated_ThrowsInvalidOperation()
    {
        var contract = CreateContract();
        contract.Terminate();
        var act = () => contract.MarkExpired();
        act.Should().Throw<InvalidOperationException>();
    }
}
