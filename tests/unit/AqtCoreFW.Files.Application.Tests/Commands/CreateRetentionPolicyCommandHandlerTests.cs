using AqtCoreFW.Files.Application.Commands.CreateRetentionPolicy;
using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Files.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateRetentionPolicyCommandHandler.
/// Validates: success creation, persistence, DTO mapping, optional archive/destroy dates.
/// </summary>
public sealed class CreateRetentionPolicyCommandHandlerTests
{
    private readonly IRepository<RetentionPolicy, long> _repository;
    private readonly CreateRetentionPolicyCommandHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    public CreateRetentionPolicyCommandHandlerTests()
    {
        _repository = Substitute.For<IRepository<RetentionPolicy, long>>();
        _handler = new CreateRetentionPolicyCommandHandler(_repository);
    }

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithDto()
    {
        var command = new CreateRetentionPolicyCommand(
            Name: "Standard Retention",
            DocumentType: "Invoice",
            RetainDays: 365,
            ArchiveAfterDays: 90,
            DestroyAfterDays: 2555,
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Name.Should().Be("Standard Retention");
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsViaRepository()
    {
        var command = new CreateRetentionPolicyCommand(
            Name: "Compliance Policy",
            DocumentType: "Contract",
            RetainDays: 2555,
            ArchiveAfterDays: 365,
            DestroyAfterDays: 3650,
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<RetentionPolicy>(p => p.Name == "Compliance Policy"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MapsAllProperties()
    {
        var command = new CreateRetentionPolicyCommand(
            Name: "Email Retention",
            DocumentType: "Email",
            RetainDays: 1825,
            ArchiveAfterDays: 365,
            DestroyAfterDays: 2555,
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Name.Should().Be("Email Retention");
        result.Value.DocumentType.Should().Be("Email");
        result.Value.RetainDays.Should().Be(1825);
        result.Value.ArchiveAfterDays.Should().Be(365);
        result.Value.DestroyAfterDays.Should().Be(2555);
        result.Value.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public async Task Handle_PolicyCreatedAsActive()
    {
        var command = new CreateRetentionPolicyCommand(
            Name: "Policy",
            DocumentType: "Document",
            RetainDays: 365,
            ArchiveAfterDays: null,
            DestroyAfterDays: null,
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithoutOptionalArchiveAndDestroyDates()
    {
        var command = new CreateRetentionPolicyCommand(
            Name: "Simple Policy",
            DocumentType: "Report",
            RetainDays: 365,
            ArchiveAfterDays: null,
            DestroyAfterDays: null,
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ArchiveAfterDays.Should().BeNull();
        result.Value.DestroyAfterDays.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithOnlyArchiveDate()
    {
        var command = new CreateRetentionPolicyCommand(
            Name: "Archive Policy",
            DocumentType: "Document",
            RetainDays: 365,
            ArchiveAfterDays: 90,
            DestroyAfterDays: null,
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ArchiveAfterDays.Should().Be(90);
        result.Value.DestroyAfterDays.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WithBothArchiveAndDestroyDates()
    {
        var command = new CreateRetentionPolicyCommand(
            Name: "Full Lifecycle Policy",
            DocumentType: "Temp",
            RetainDays: 180,
            ArchiveAfterDays: 60,
            DestroyAfterDays: 365,
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ArchiveAfterDays.Should().Be(60);
        result.Value.DestroyAfterDays.Should().Be(365);
    }

    [Fact]
    public async Task Handle_VariousDocumentTypes()
    {
        var types = new[] { "Invoice", "Contract", "Email", "Report", "Form" };

        foreach (var type in types)
        {
            var command = new CreateRetentionPolicyCommand(
                Name: $"Policy-{type}",
                DocumentType: type,
                RetainDays: 365,
                ArchiveAfterDays: null,
                DestroyAfterDays: null,
                TenantId: TenantId,
                CreatedBy: CreatedBy);

            var result = await _handler.Handle(command, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.DocumentType.Should().Be(type);
        }
    }

    // --- Failure scenarios ---

    [Fact]
    public async Task Handle_RepositoryException_Propagates()
    {
        _repository
            .AddAsync(Arg.Any<RetentionPolicy>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB error")));

        var command = new CreateRetentionPolicyCommand(
            Name: "Policy",
            DocumentType: "Document",
            RetainDays: 365,
            ArchiveAfterDays: null,
            DestroyAfterDays: null,
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var act = async () => await _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB error*");
    }

    [Fact]
    public async Task Handle_CancellationTokenRespected()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _repository
            .AddAsync(Arg.Any<RetentionPolicy>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromCanceled(cts.Token));

        var command = new CreateRetentionPolicyCommand(
            Name: "Policy",
            DocumentType: "Document",
            RetainDays: 365,
            ArchiveAfterDays: null,
            DestroyAfterDays: null,
            TenantId: TenantId,
            CreatedBy: CreatedBy);

        var act = async () => await _handler.Handle(command, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
