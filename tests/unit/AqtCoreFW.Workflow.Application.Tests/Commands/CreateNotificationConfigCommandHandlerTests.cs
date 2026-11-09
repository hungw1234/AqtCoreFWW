using System.Reflection;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Workflow.Application.Commands.CreateNotificationConfig;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using FluentResults;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateNotificationConfigCommandHandler.
/// Validates: entity creation, persistence, DTO mapping, active-by-default.
/// Phase-4b B6a: DefinitionId changed long→Guid; handler resolves T2 PublicId→internal long FK.
/// </summary>
public sealed class CreateNotificationConfigCommandHandlerTests
{
    // WorkflowNotificationConfig is T1: IRepository<T, long>
    private readonly IRepository<WorkflowNotificationConfig, long> _repository;
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly CreateNotificationConfigCommandHandler _handler;

    // Phase-4b B6a: DefinitionId is now Guid (T2 PublicId); handler resolves → DefinitionInternalId (long)
    private static readonly Guid DefinitionPublicId = Guid.NewGuid();
    private const long DefinitionInternalId = 1L;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    public CreateNotificationConfigCommandHandlerTests()
    {
        _repository = Substitute.For<IRepository<WorkflowNotificationConfig, long>>();
        _definitionRepository = Substitute.For<IWorkflowDefinitionRepository>();
        _handler = new CreateNotificationConfigCommandHandler(_repository, _definitionRepository);

        // Default: definition lookup succeeds, stub with InternalId = DefinitionInternalId
        _definitionRepository
            .GetByIdAsync(DefinitionPublicId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok(CreateStubDefinition(DefinitionInternalId)));
    }

    private static CreateNotificationConfigCommand BuildCommand(
        string actionName = "Approve",
        string channel = "email",
        string recipientType = "user",
        string recipientValue = "00000000-0000-0000-0000-000000000001",
        string subject = "Test Subject",
        string bodyTemplate = "Hello {{user}}") =>
        new(DefinitionPublicId, actionName, channel, recipientType, recipientValue, subject, bodyTemplate, TenantId, CreatedBy);

    // --- Success path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccess()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsViaRepository()
    {
        await _handler.Handle(BuildCommand(), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Any<WorkflowNotificationConfig>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReturnsDto_WithCorrectFields()
    {
        var command = BuildCommand(
            actionName: "Submit",
            channel: "inapp",
            recipientType: "creator",
            recipientValue: "creator",
            subject: "Submitted",
            bodyTemplate: "Your form was submitted.");

        var result = await _handler.Handle(command, CancellationToken.None);

        // DefinitionId in DTO is the resolved internal long FK, not the Guid PublicId
        result.Value.DefinitionId.Should().Be(DefinitionInternalId);
        result.Value.ActionName.Should().Be("Submit");
        result.Value.Channel.Should().Be("inapp");
        result.Value.RecipientType.Should().Be("creator");
        result.Value.RecipientValue.Should().Be("creator");
        result.Value.Subject.Should().Be("Submitted");
        result.Value.BodyTemplate.Should().Be("Your form was submitted.");
    }

    [Fact]
    public async Task Handle_NewConfig_IsActiveByDefault()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnedDto_ReturnsDto()
    {
        var result = await _handler.Handle(BuildCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_PersistedEntity_MatchesCommand()
    {
        WorkflowNotificationConfig? captured = null;
        await _repository.AddAsync(
            Arg.Do<WorkflowNotificationConfig>(e => captured = e),
            Arg.Any<CancellationToken>());

        var command = BuildCommand(actionName: "Reject", channel: "sms");
        await _handler.Handle(command, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ActionName.Should().Be("Reject");
        captured.Channel.Should().Be("sms");
        captured.TenantId.Should().Be(TenantId);
    }

    // --- Definition not found ---

    [Fact]
    public async Task Handle_DefinitionNotFound_ReturnsFailure()
    {
        var unknownId = Guid.NewGuid();
        _definitionRepository
            .GetByIdAsync(unknownId, Arg.Any<CancellationToken>())
            .Returns(Result.Fail<WorkflowDefinition>("not found"));

        var command = new CreateNotificationConfigCommand(
            unknownId, "Approve", "email", "user", "user@test.com", "Subj", "Body", TenantId, CreatedBy);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _repository.DidNotReceive().AddAsync(
            Arg.Any<WorkflowNotificationConfig>(), Arg.Any<CancellationToken>());
    }

    // --- Edge cases ---

    [Fact]
    public async Task Handle_RepositoryException_Propagates()
    {
        _repository
            .AddAsync(Arg.Any<WorkflowNotificationConfig>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(new InvalidOperationException("DB unavailable")));

        var act = async () => await _handler.Handle(BuildCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DB unavailable*");
    }

    [Fact]
    public async Task Handle_DifferentChannels_AllSupported()
    {
        foreach (var channel in new[] { "email", "sms", "inapp" })
        {
            var result = await _handler.Handle(BuildCommand(channel: channel), CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Value.Channel.Should().Be(channel);
        }
    }

    // ── Helper: create a WorkflowDefinition stub with a specific internal Id ──

    private static WorkflowDefinition CreateStubDefinition(long internalId)
    {
        var def = (WorkflowDefinition)Activator.CreateInstance(
            typeof(WorkflowDefinition), nonPublic: true)!;

        typeof(Entity)
            .GetProperty(nameof(Entity.Id), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(def, internalId);

        return def;
    }
}
