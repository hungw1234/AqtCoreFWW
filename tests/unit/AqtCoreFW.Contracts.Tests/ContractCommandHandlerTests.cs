using AqtCoreFW.Contracts.Application.Commands.CreateContract;
using AqtCoreFW.Contracts.Application.Commands.DeleteContract;
using AqtCoreFW.Contracts.Application.Commands.UpdateContract;
using AqtCoreFW.Contracts.Domain.Entities;
using AqtCoreFW.Contracts.Domain.Repositories;
using FluentAssertions;
using FluentResults;
using NSubstitute;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.Contracts.Tests;

public sealed class ContractCommandHandlerTests
{
    [Fact]
    public async Task CreateHandler_PersistsContractAndReturnsSuccess()
    {
        var repository = Substitute.For<IContractRepository>();
        repository.AddAsync(Arg.Any<Contract>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var handler = new CreateContractCommandHandler(repository);
        var command = new CreateContractCommand(
            Guid.NewGuid(),
            "HD-001",
            "Service agreement",
            "Acme",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            null,
            Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await repository.Received(1).AddAsync(
            Arg.Is<Contract>(contract => contract.ContractNumber == "HD-001"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateHandler_ReturnsConflictWhenContractNumberAlreadyExists()
    {
        var repository = Substitute.For<IContractRepository>();
        repository.ContractNumberExistsAsync(
                "HD-001", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        var handler = new CreateContractCommandHandler(repository);
        var command = new CreateContractCommand(
            Guid.NewGuid(),
            "HD-001",
            "Service agreement",
            "Acme",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            null,
            Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().BeOfType<ConflictError>();
        await repository.DidNotReceive().AddAsync(
            Arg.Any<Contract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateHandler_ReturnsValidationFailureWhenEndDatePrecedesStartDate()
    {
        var repository = Substitute.For<IContractRepository>();
        var handler = new CreateContractCommandHandler(repository);
        var command = new CreateContractCommand(
            Guid.NewGuid(),
            "HD-001",
            "Service agreement",
            "Acme",
            new DateTime(2026, 12, 31),
            new DateTime(2026, 1, 1),
            null,
            Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().BeOfType<ValidationError>();
        await repository.DidNotReceive().ContractNumberExistsAsync(
            Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive().AddAsync(
            Arg.Any<Contract>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateHandler_ReturnsFailureWhenContractDoesNotExist()
    {
        var repository = Substitute.For<IContractRepository>();
        repository.GetByIdAsync(42, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<Contract>("missing"));
        var handler = new UpdateContractCommandHandler(repository);
        var command = new UpdateContractCommand(
            42,
            Guid.NewGuid(),
            "HD-001",
            "Service agreement",
            "Acme",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            ContractStatus.Active,
            null,
            Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateHandler_UpdatesContractAndPersistsChanges()
    {
        const long contractId = 42;
        var repository = Substitute.For<IContractRepository>();
        var contract = Contract.Create(
            Guid.NewGuid(),
            "HD-001",
            "Service agreement",
            "Acme",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            null,
            Guid.NewGuid());
        repository.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(contract));
        var handler = new UpdateContractCommandHandler(repository);
        var updatedBy = Guid.NewGuid();

        var result = await handler.Handle(
            new UpdateContractCommand(
                contractId,
                Guid.NewGuid(),
                "HD-002",
                "Updated agreement",
                "Contoso",
                new DateTime(2026, 2, 1),
                new DateTime(2027, 1, 31),
                ContractStatus.Active,
                "Updated",
                updatedBy),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(contract.Id);
        contract.ContractNumber.Should().Be("HD-002");
        contract.Title.Should().Be("Updated agreement");
        contract.Status.Should().Be(ContractStatus.Active);
        contract.ModifiedBy.Should().Be(updatedBy);
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateHandler_ReturnsConflictWhenNumberBelongsToAnotherContract()
    {
        const long contractId = 42;
        var repository = Substitute.For<IContractRepository>();
        var contract = Contract.Create(
            Guid.NewGuid(),
            "HD-001",
            "Service agreement",
            "Acme",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            null,
            Guid.NewGuid());
        repository.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(contract));
        repository.ContractNumberExistsAsync(
                "HD-002", contractId, Arg.Any<CancellationToken>())
            .Returns(true);
        var handler = new UpdateContractCommandHandler(repository);

        var result = await handler.Handle(
            new UpdateContractCommand(
                contractId,
                Guid.NewGuid(),
                "HD-002",
                "Updated agreement",
                "Contoso",
                new DateTime(2026, 2, 1),
                new DateTime(2027, 1, 31),
                ContractStatus.Active,
                null,
                Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().BeOfType<ConflictError>();
        contract.ContractNumber.Should().Be("HD-001");
        await repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteHandler_SoftDeletesContractAndSavesChanges()
    {
        const long contractId = 42;
        var repository = Substitute.For<IContractRepository>();
        var contract = Contract.Create(
            Guid.NewGuid(),
            "HD-001",
            "Service agreement",
            "Acme",
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            null,
            Guid.NewGuid());
        repository.GetByIdAsync(contractId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(contract));
        var handler = new DeleteContractCommandHandler(repository);

        var result = await handler.Handle(
            new DeleteContractCommand(contractId, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        contract.IsDeleted.Should().BeTrue();
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteHandler_ReturnsFailureWhenContractDoesNotExist()
    {
        var repository = Substitute.For<IContractRepository>();
        repository.GetByIdAsync(42, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<Contract>("missing"));
        var handler = new DeleteContractCommandHandler(repository);

        var result = await handler.Handle(
            new DeleteContractCommand(42, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CreateValidator_ReportsVietnameseDateRangeError()
    {
        var validator = new CreateContractCommandValidator();
        var result = validator.Validate(new CreateContractCommand(
            Guid.NewGuid(),
            "HD-001",
            "Service agreement",
            "Acme",
            new DateTime(2026, 12, 31),
            new DateTime(2026, 1, 1),
            null,
            Guid.NewGuid()));

        result.Errors.Should().Contain(error =>
            error.PropertyName == nameof(CreateContractCommand.EndDate) &&
            error.ErrorMessage.Contains("Ngày kết thúc"));
    }
}
