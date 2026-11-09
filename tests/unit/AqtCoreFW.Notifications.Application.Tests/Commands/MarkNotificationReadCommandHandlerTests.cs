using AqtCoreFW.Notifications.Application.Commands.MarkNotificationRead;
using AqtCoreFW.Notifications.Domain.Entities;
using AqtCoreFW.Notifications.Domain.Repositories;
using AqtCoreFW.Notifications.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Notifications.Application.Tests.Commands;

/// <summary>
/// Unit tests for MarkNotificationReadCommandHandler.
/// Covers success, not-found, and ownership-violation paths.
/// Phase-4b/B5: GetByIdAsync → GetByPublicIdAsync (T2); IGuidGenerator in factory.
/// </summary>
public sealed class MarkNotificationReadCommandHandlerTests
{
    private readonly INotificationRepository _repo = Substitute.For<INotificationRepository>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly MarkNotificationReadCommandHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OwnerUserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    public MarkNotificationReadCommandHandlerTests()
    {
        _sut = new MarkNotificationReadCommandHandler(_repo);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidOwner_ReturnsOk()
    {
        var notification = CreateNotification(OwnerUserId);
        _repo.GetByPublicIdAsync(notification.PublicId, Arg.Any<CancellationToken>())
            .Returns(notification);

        var command = new MarkNotificationReadCommand(notification.PublicId, TenantId, OwnerUserId);
        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidOwner_CallsUpdateAsync()
    {
        var notification = CreateNotification(OwnerUserId);
        _repo.GetByPublicIdAsync(notification.PublicId, Arg.Any<CancellationToken>())
            .Returns(notification);

        var command = new MarkNotificationReadCommand(notification.PublicId, TenantId, OwnerUserId);
        await _sut.Handle(command, CancellationToken.None);

        await _repo.Received(1).UpdateAsync(notification, Arg.Any<CancellationToken>());
    }

    // --- Not found ---

    [Fact]
    public async Task Handle_NotificationNotFound_ReturnsFail()
    {
        var missingPublicId = Guid.NewGuid();
        _repo.GetByPublicIdAsync(missingPublicId, Arg.Any<CancellationToken>())
            .Returns((Notification?)null);

        var command = new MarkNotificationReadCommand(missingPublicId, TenantId, OwnerUserId);
        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NotificationNotFound_DoesNotCallUpdate()
    {
        var missingPublicId = Guid.NewGuid();
        _repo.GetByPublicIdAsync(missingPublicId, Arg.Any<CancellationToken>())
            .Returns((Notification?)null);

        var command = new MarkNotificationReadCommand(missingPublicId, TenantId, OwnerUserId);
        await _sut.Handle(command, CancellationToken.None);

        await _repo.DidNotReceive().UpdateAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    // --- Ownership violation ---

    [Fact]
    public async Task Handle_WrongUser_ReturnsForbiddenError()
    {
        var notification = CreateNotification(OwnerUserId);
        _repo.GetByPublicIdAsync(notification.PublicId, Arg.Any<CancellationToken>())
            .Returns(notification);

        // OtherUserId tries to mark OwnerUserId's notification as read
        var command = new MarkNotificationReadCommand(notification.PublicId, TenantId, OtherUserId);
        var result = await _sut.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Message.Contains("Cannot mark another user"));
    }

    [Fact]
    public async Task Handle_WrongUser_DoesNotCallUpdate()
    {
        var notification = CreateNotification(OwnerUserId);
        _repo.GetByPublicIdAsync(notification.PublicId, Arg.Any<CancellationToken>())
            .Returns(notification);

        var command = new MarkNotificationReadCommand(notification.PublicId, TenantId, OtherUserId);
        await _sut.Handle(command, CancellationToken.None);

        await _repo.DidNotReceive().UpdateAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    // --- Helpers ---

    /// <summary>
    /// Creates a valid Notification aggregate via domain factory.
    /// IGuidGenerator substitute provides a deterministic PublicId for assertions.
    /// </summary>
    private Notification CreateNotification(Guid recipientUserId)
    {
        var publicId = Guid.NewGuid();
        _guidGen.NewIdMonotonic().Returns(publicId);

        return Notification.Create(
            _guidGen,
            TenantId,
            recipientUserId,
            subject: "Test Subject",
            body: "Test Body",
            channel: NotificationChannel.InApp,
            createdBy: Guid.NewGuid());
    }
}
