using AqtCoreFW.Integration.Application.Commands.CreateMessageLog;
using AqtCoreFW.Integration.Domain.Entities;
using AqtCoreFW.Integration.Domain.Enums;
using AqtCoreFW.Integration.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Integration.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateMessageLogCommandHandler.
/// Phase-07 PR-B: validates PartnerPublicId + ContractPublicId (Guid T2 AK) resolution,
/// null-contract skip, not-found failure, and cross-tenant guard at repo level.
/// </summary>
public sealed class CreateMessageLogCommandHandlerTests
{
    private readonly IMessageLogRepository _repository;
    private readonly IPartnerRepository _partnerRepository;
    private readonly IContractRepository _contractRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IGuidGenerator _guidGenerator;
    private readonly CreateMessageLogCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid PartnerPublicId = Guid.NewGuid();
    private static readonly Guid ContractPublicId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    public CreateMessageLogCommandHandlerTests()
    {
        _repository = Substitute.For<IMessageLogRepository>();
        _partnerRepository = Substitute.For<IPartnerRepository>();
        _contractRepository = Substitute.For<IContractRepository>();
        _currentUser = Substitute.For<ICurrentUser>();
        _guidGenerator = Substitute.For<IGuidGenerator>();

        _currentUser.UserId.Returns(UserId);
        _guidGenerator.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        _handler = new CreateMessageLogCommandHandler(
            _repository, _partnerRepository, _contractRepository, _currentUser, _guidGenerator);

        // Default: Partner resolves successfully in the request tenant.
        // NOTE: MakePartner() must be cached BEFORE the .Returns() chain — calling it inline
        // creates a nested substitute whose last-recorded call collides with NSubstitute's
        // pending-call state, causing CheckResultIsCompatibleWithCall to fail.
        var defaultPartner = MakePartner();
        _partnerRepository
            .GetByPublicIdAsync(PartnerPublicId, TenantId, Arg.Any<CancellationToken>())
            .Returns(defaultPartner);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CreateMessageLogCommand BuildCommand(
        Guid? contractPublicId = null,
        Guid? tenantId = null,
        Guid? partnerPublicId = null) =>
        new(
            TenantId: tenantId ?? TenantId,
            PartnerPublicId: partnerPublicId ?? PartnerPublicId,
            ContractPublicId: contractPublicId,
            Direction: MessageDirection.Outbound,
            MessageType: "GOV_NOTIFY",
            Payload: null,
            CorrelationId: "corr-001");

    private static Partner MakePartner(Guid? tenantId = null)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(PartnerPublicId);
        gen.NewIdRandom().Returns(_ => Guid.NewGuid());
        return Partner.Create(
            gen,
            tenantId: tenantId ?? TenantId,
            name: "Test Partner",
            code: "TP-001",
            createdBy: UserId);
    }

    /// <summary>
    /// Creates a Contract in same tenant as TenantId by default.
    /// Contract.Create still takes long partnerId (internal Partner.Id post-resolve).
    /// </summary>
    private static Contract MakeContract(Guid? tenantId = null)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(ContractPublicId);
        return Contract.Create(
            gen,
            tenantId: tenantId ?? TenantId,
            partnerId: 100L,
            title: "Test Contract",
            description: null,
            effectiveDate: DateTime.UtcNow.Date,
            expiryDate: null,
            dataScopeJson: null,
            createdBy: UserId);
    }

    // -------------------------------------------------------------------------
    // Success: null contract (no contract lookup)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_NullContractPublicId_SucceedsWithoutLookup()
    {
        var command = BuildCommand(contractPublicId: null);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Contract repo lookup must NOT be called when ContractPublicId is null
        await _contractRepository.DidNotReceive()
            .GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        // MessageLog persisted with null ContractId
        await _repository.Received(1).AddAsync(
            Arg.Is<MessageLog>(m => m.ContractId == null),
            Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Success: contract resolved
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithContractPublicId_ResolvesAndPersistsContractInternalId()
    {
        // Cache MakeContract() before .Returns() to avoid NSubstitute pending-call collision.
        var contract = MakeContract();
        _contractRepository
            .GetByPublicIdAsync(ContractPublicId, TenantId, Arg.Any<CancellationToken>())
            .Returns(contract);

        var command = BuildCommand(contractPublicId: ContractPublicId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // MessageLog persisted with contract.Id (0L pre-SaveChanges — DB-assigned IDENTITY)
        await _repository.Received(1).AddAsync(
            Arg.Is<MessageLog>(m => m.ContractId == contract.Id),
            Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Failure: partner not found (tenant-scoped repo returns null)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_PartnerNotFound_ReturnsFail()
    {
        var unknownPartnerPublicId = Guid.NewGuid();
        _partnerRepository
            .GetByPublicIdAsync(unknownPartnerPublicId, TenantId, Arg.Any<CancellationToken>())
            .Returns((Partner?)null);

        var command = BuildCommand(partnerPublicId: unknownPartnerPublicId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("Partner not found"));
        await _repository.DidNotReceive().AddAsync(Arg.Any<MessageLog>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Failure: contract not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ContractNotFound_ReturnsFail()
    {
        _contractRepository
            .GetByPublicIdAsync(ContractPublicId, TenantId, Arg.Any<CancellationToken>())
            .Returns((Contract?)null);

        var command = BuildCommand(contractPublicId: ContractPublicId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("Contract not found"));
        await _repository.DidNotReceive().AddAsync(Arg.Any<MessageLog>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // Failure: cross-tenant guard at repo level (Contract belongs to another tenant)
    // Phase-07 PR-B: tenant guard moved into repo signature — cross-tenant call returns null.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ContractInDifferentTenant_ReturnsNotFound()
    {
        // Tenant-scoped repo returns null when contract.TenantId != requested TenantId.
        _contractRepository
            .GetByPublicIdAsync(ContractPublicId, TenantId, Arg.Any<CancellationToken>())
            .Returns((Contract?)null);

        var command = BuildCommand(contractPublicId: ContractPublicId, tenantId: TenantId);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Message.Contains("Contract not found"));
        await _repository.DidNotReceive().AddAsync(Arg.Any<MessageLog>(), Arg.Any<CancellationToken>());
    }
}
