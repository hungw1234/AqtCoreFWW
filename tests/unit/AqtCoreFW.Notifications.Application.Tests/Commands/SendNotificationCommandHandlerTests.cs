using AqtCoreFW.Notifications.Application.Commands.SendNotification;
using AqtCoreFW.Notifications.Application.Providers;
using AqtCoreFW.Notifications.Domain.Repositories;
using AqtCoreFW.Notifications.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AqtCoreFW.Notifications.Application.Tests.Commands;

/// <summary>
/// Unit tests for SendNotificationCommandHandler.
/// Tests channel routing (InApp, Email, SMS), dedup guard, and failure path.
/// All external dependencies are NSubstitute mocks — no database, no HTTP.
/// Phase-07: IGuidGenerator injected; TemplateId is Guid? (T2 PublicId AK) — resolved to long FK in handler.
/// </summary>
public sealed class SendNotificationCommandHandlerTests
{
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly INotificationRepository _repo = Substitute.For<INotificationRepository>();
    private readonly INotificationLogRepository _logRepo = Substitute.For<INotificationLogRepository>();
    private readonly INotificationTemplateRepository _templateRepo = Substitute.For<INotificationTemplateRepository>();
    private readonly IEmailSender _emailSender = Substitute.For<IEmailSender>();
    private readonly ISmsProvider _smsProvider = Substitute.For<ISmsProvider>();
    private readonly IIdentityModuleClient _identityClient = Substitute.For<IIdentityModuleClient>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly SendNotificationCommandHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RecipientId = Guid.NewGuid();
    private const string RecipientEmail = "an.nguyen@gov.vn";
    private const string RecipientPhone = "+84901234567";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid FakePublicId = Guid.NewGuid();
    private static readonly Guid TemplatePublicId = Guid.NewGuid();
    private const long TemplateInternalId = 42L;

    public SendNotificationCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        _currentUser.TenantId.Returns(TenantId);
        // IGuidGenerator returns deterministic value for assertions
        _guidGen.NewIdMonotonic().Returns(FakePublicId);
        // Default: template resolves successfully
        _templateRepo.GetInternalIdByPublicIdAsync(TemplatePublicId, TenantId, Arg.Any<CancellationToken>())
            .Returns((long?)TemplateInternalId);
        // Default: no duplicate
        _logRepo.ExistsAsync(Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Default: the recipient resolves to a real mailbox
        _identityClient.GetUserInfoByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserInfoDto>
            {
                [RecipientId] = new() { UserId = RecipientId, Email = RecipientEmail, TenantId = TenantId },
            });

        // Default: the recipient resolves to a real phone number
        _identityClient.GetUserPhoneNumbersByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string?> { [RecipientId] = RecipientPhone });

        _sut = new SendNotificationCommandHandler(
            _guidGen, _repo, _logRepo, _templateRepo, _emailSender, _smsProvider, _identityClient,
            NullLogger<SendNotificationCommandHandler>.Instance, _currentUser);
    }

    // --- InApp channel happy path ---

    [Fact]
    public async Task Handle_InAppChannel_ReturnsSuccessWithNotificationPublicId()
    {
        var command = BuildCommand("inapp");

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(FakePublicId);
    }

    [Fact]
    public async Task Handle_InAppChannel_PersistsNotificationToRepository()
    {
        var command = BuildCommand("inapp");

        await _sut.Handle(command, CancellationToken.None);

        await _repo.Received(1).AddAsync(Arg.Any<Domain.Entities.Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InAppChannel_DoesNotCallEmailOrSms()
    {
        var command = BuildCommand("inapp");

        await _sut.Handle(command, CancellationToken.None);

        await _emailSender.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _smsProvider.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Email channel routing ---

    [Fact]
    public async Task Handle_EmailChannel_SendsToTheResolvedAddress_NotTheUserId()
    {
        var command = BuildCommand("email", subject: "Welcome", body: "Hello there");

        await _sut.Handle(command, CancellationToken.None);

        await _emailSender.Received(1).SendAsync(
            RecipientEmail,
            "Welcome",
            "Hello there",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmailChannel_ResolvesTheRecipientWithinTheTenant()
    {
        var command = BuildCommand("email");

        await _sut.Handle(command, CancellationToken.None);

        await _identityClient.Received(1).GetUserInfoByIdsAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.Single() == RecipientId),
            TenantId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmailChannel_RecipientNotFound_FailsWithoutSending()
    {
        _identityClient.GetUserInfoByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserInfoDto>()); // lookup omits ids it cannot find

        var result = await _sut.Handle(BuildCommand("email"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Single().Message.Should().Contain("not found");
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Handle_EmailChannel_RecipientHasNoEmail_FailsWithoutSending()
    {
        _identityClient.GetUserInfoByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserInfoDto>
            {
                [RecipientId] = new() { UserId = RecipientId, Email = null },
            });

        var result = await _sut.Handle(BuildCommand("email"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Single().Message.Should().Contain("no email address");
        await _emailSender.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Handle_EmailChannel_UnresolvableRecipient_MarksTheNotificationFailed()
    {
        _identityClient.GetUserInfoByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserInfoDto>());

        await _sut.Handle(BuildCommand("email"), CancellationToken.None);

        // The row was already persisted before dispatch; it must not stay Pending forever.
        await _repo.Received(1).UpdateAsync(
            Arg.Is<Domain.Entities.Notification>(n => n.Status == NotificationStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InAppChannel_DoesNotResolveTheRecipient()
    {
        // InApp carries every Forms notification — it must not pay for an identity round-trip.
        await _sut.Handle(BuildCommand("inapp"), CancellationToken.None);

        await _identityClient.DidNotReceiveWithAnyArgs()
            .GetUserInfoByIdsAsync(default!, default, default);
        await _identityClient.DidNotReceiveWithAnyArgs()
            .GetUserPhoneNumbersByIdsAsync(default!, default, default);
    }

    // --- SMS channel routing ---

    [Fact]
    public async Task Handle_SmsChannel_SendsToTheResolvedNumber_NotTheUserId()
    {
        var command = BuildCommand("sms", body: "Your OTP is 123456");

        await _sut.Handle(command, CancellationToken.None);

        await _smsProvider.Received(1).SendAsync(
            RecipientPhone,
            "Your OTP is 123456",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SmsChannel_ResolvesTheRecipientWithinTheTenant()
    {
        await _sut.Handle(BuildCommand("sms"), CancellationToken.None);

        await _identityClient.Received(1).GetUserPhoneNumbersByIdsAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.Single() == RecipientId),
            TenantId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SmsChannel_RecipientNotFound_FailsWithoutSending()
    {
        _identityClient.GetUserPhoneNumbersByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string?>()); // lookup omits ids it cannot find

        var result = await _sut.Handle(BuildCommand("sms"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Single().Message.Should().Contain("not found");
        await _smsProvider.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_SmsChannel_RecipientHasNoPhoneNumber_FailsWithoutSending()
    {
        _identityClient.GetUserPhoneNumbersByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string?> { [RecipientId] = null });

        var result = await _sut.Handle(BuildCommand("sms"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Single().Message.Should().Contain("no phone number");
        await _smsProvider.DidNotReceiveWithAnyArgs().SendAsync(default!, default!, default);
    }

    [Fact]
    public async Task Handle_SmsChannel_UnresolvableRecipient_MarksTheNotificationFailed()
    {
        _identityClient.GetUserPhoneNumbersByIdsAsync(
                Arg.Any<IEnumerable<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string?>());

        await _sut.Handle(BuildCommand("sms"), CancellationToken.None);

        // The row was already persisted before dispatch; it must not stay Pending forever.
        await _repo.Received(1).UpdateAsync(
            Arg.Is<Domain.Entities.Notification>(n => n.Status == NotificationStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    // --- Template resolution ---

    [Fact]
    public async Task Handle_WithValidTemplatePublicId_ResolvesToInternalId()
    {
        var command = BuildCommand("inapp", templateId: TemplatePublicId, correlationId: "corr-resolve");

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _templateRepo.Received(1).GetInternalIdByPublicIdAsync(
            TemplatePublicId, TenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithUnknownTemplatePublicId_ReturnsFailResult()
    {
        var unknownId = Guid.NewGuid();
        _templateRepo.GetInternalIdByPublicIdAsync(unknownId, TenantId, Arg.Any<CancellationToken>())
            .Returns((long?)null);

        var command = BuildCommand("inapp", templateId: unknownId);

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("not found"));
    }

    // --- Dedup guard ---

    [Fact]
    public async Task Handle_DuplicateCorrelationId_ReturnsFailResult()
    {
        _logRepo.ExistsAsync(TemplateInternalId, RecipientId, "corr-001", Arg.Any<CancellationToken>())
            .Returns(true);

        var command = BuildCommand("inapp", templateId: TemplatePublicId, correlationId: "corr-001");

        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("already sent"));
    }

    [Fact]
    public async Task Handle_DuplicateCheck_SkippedWhenNoTemplateId()
    {
        // No templateId → dedup check must NOT be called
        var command = BuildCommand("inapp"); // templateId = null

        await _sut.Handle(command, CancellationToken.None);

        await _logRepo.DidNotReceive().ExistsAsync(
            Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Failure path: email sender throws ---

    [Fact]
    public async Task Handle_EmailSenderThrows_MarksNotificationFailedAndRethrows()
    {
        _emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        var command = BuildCommand("email");

        var act = async () => await _sut.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SMTP unavailable*");
        // UpdateAsync called for MarkAsFailed path
        await _repo.Received().UpdateAsync(
            Arg.Is<Domain.Entities.Notification>(n => n.Status == Domain.ValueObjects.NotificationStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    // --- Dedup log recorded after successful send ---

    [Fact]
    public async Task Handle_WithTemplateAndCorrelation_LogsDedupEntryAfterSend()
    {
        var command = BuildCommand("inapp", templateId: TemplatePublicId, correlationId: "evt-abc");

        await _sut.Handle(command, CancellationToken.None);

        await _logRepo.Received(1).AddAsync(
            Arg.Is<Domain.Entities.NotificationLog>(l =>
                l.TemplateId == TemplateInternalId && l.CorrelationId == "evt-abc"),
            Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    private static SendNotificationCommand BuildCommand(
        string channel,
        string subject = "Test Subject",
        string body = "Test Body",
        Guid? templateId = null,
        string? correlationId = null) =>
        new(TenantId, RecipientId, subject, body, channel, templateId, correlationId);
}
