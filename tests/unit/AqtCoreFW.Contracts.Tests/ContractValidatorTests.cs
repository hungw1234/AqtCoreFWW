using AqtCoreFW.Contracts.Application.Commands.CreateContract;
using AqtCoreFW.Contracts.Application.Commands.DeleteContract;
using AqtCoreFW.Contracts.Application.Commands.UpdateContract;
using AqtCoreFW.Contracts.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.Contracts.Tests;

public sealed class ContractValidatorTests
{
    private readonly CreateContractCommandValidator _createValidator = new();
    private readonly UpdateContractCommandValidator _updateValidator = new();
    private readonly DeleteContractCommandValidator _deleteValidator = new();

    [Fact]
    public void Create_ValidCommand_Passes()
    {
        _createValidator.Validate(ValidCreate()).IsValid.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidCreateCommands))]
    public void Create_InvalidCommand_FailsOnExpectedProperty(
        CreateContractCommand command,
        string property) =>
        _createValidator.Validate(command).Errors
            .Should().Contain(error => error.PropertyName == property);

    [Fact]
    public void Update_ValidCommand_Passes()
    {
        _updateValidator.Validate(ValidUpdate()).IsValid.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidUpdateCommands))]
    public void Update_InvalidCommand_FailsOnExpectedProperty(
        UpdateContractCommand command,
        string property) =>
        _updateValidator.Validate(command).Errors
            .Should().Contain(error => error.PropertyName == property);

    [Theory]
    [InlineData(0, "Mã hợp đồng không hợp lệ.")]
    [InlineData(-1, "Mã hợp đồng không hợp lệ.")]
    public void Delete_InvalidId_FailsValidation(long id, string expectedMessage)
    {
        var result = _deleteValidator.Validate(new DeleteContractCommand(id, Guid.NewGuid()));

        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(DeleteContractCommand.Id) &&
            error.ErrorMessage == expectedMessage);
    }

    [Fact]
    public void Delete_EmptyTenant_FailsValidation()
    {
        var result = _deleteValidator.Validate(new DeleteContractCommand(1, Guid.Empty));

        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(DeleteContractCommand.TenantId));
    }

    public static TheoryData<CreateContractCommand, string> InvalidCreateCommands() => new()
    {
        { ValidCreate() with { TenantId = Guid.Empty }, nameof(CreateContractCommand.TenantId) },
        { ValidCreate() with { ContractNumber = "" }, nameof(CreateContractCommand.ContractNumber) },
        { ValidCreate() with { ContractNumber = new string('x', 51) }, nameof(CreateContractCommand.ContractNumber) },
        { ValidCreate() with { Title = "" }, nameof(CreateContractCommand.Title) },
        { ValidCreate() with { Title = new string('x', 201) }, nameof(CreateContractCommand.Title) },
        { ValidCreate() with { CounterpartyName = "" }, nameof(CreateContractCommand.CounterpartyName) },
        { ValidCreate() with { CounterpartyName = new string('x', 201) }, nameof(CreateContractCommand.CounterpartyName) },
        { ValidCreate() with { EndDate = new DateTime(2025, 12, 31) }, nameof(CreateContractCommand.EndDate) },
        { ValidCreate() with { Description = new string('x', 2001) }, nameof(CreateContractCommand.Description) },
        { ValidCreate() with { CreatedBy = Guid.Empty }, nameof(CreateContractCommand.CreatedBy) },
    };

    public static TheoryData<UpdateContractCommand, string> InvalidUpdateCommands() => new()
    {
        { ValidUpdate() with { Id = 0 }, nameof(UpdateContractCommand.Id) },
        { ValidUpdate() with { TenantId = Guid.Empty }, nameof(UpdateContractCommand.TenantId) },
        { ValidUpdate() with { ContractNumber = "" }, nameof(UpdateContractCommand.ContractNumber) },
        { ValidUpdate() with { ContractNumber = new string('x', 51) }, nameof(UpdateContractCommand.ContractNumber) },
        { ValidUpdate() with { Title = "" }, nameof(UpdateContractCommand.Title) },
        { ValidUpdate() with { Title = new string('x', 201) }, nameof(UpdateContractCommand.Title) },
        { ValidUpdate() with { CounterpartyName = "" }, nameof(UpdateContractCommand.CounterpartyName) },
        { ValidUpdate() with { CounterpartyName = new string('x', 201) }, nameof(UpdateContractCommand.CounterpartyName) },
        { ValidUpdate() with { EndDate = new DateTime(2025, 12, 31) }, nameof(UpdateContractCommand.EndDate) },
        { ValidUpdate() with { Status = (ContractStatus)99 }, nameof(UpdateContractCommand.Status) },
        { ValidUpdate() with { Description = new string('x', 2001) }, nameof(UpdateContractCommand.Description) },
        { ValidUpdate() with { UpdatedBy = Guid.Empty }, nameof(UpdateContractCommand.UpdatedBy) },
    };

    private static CreateContractCommand ValidCreate() =>
        new(
            Guid.NewGuid(),
            "HD-001",
            "Service agreement",
            "Acme",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            "Contract description",
            Guid.NewGuid());

    private static UpdateContractCommand ValidUpdate() =>
        new(
            1,
            Guid.NewGuid(),
            "HD-001",
            "Service agreement",
            "Acme",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            ContractStatus.Draft,
            "Contract description",
            Guid.NewGuid());
}
