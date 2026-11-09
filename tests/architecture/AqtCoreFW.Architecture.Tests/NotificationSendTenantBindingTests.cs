using AqtCoreFW.Notifications.Application.Commands.SendNotification;
using AqtCoreFW.Notifications.Presentation.Controllers;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// POST /api/v1/notifications must address the recipient within the caller's own tenant.
///
/// The send handler resolves the recipient's real mailbox from the command's TenantId, so a
/// client-supplied tenant would let any holder of the send permission mail a user in another
/// tenant — and learn whether that user exists. The tenant therefore comes from the caller's
/// claims, with the SystemAdmin X-Tenant-Id override that every other action on this controller
/// already honours.
/// </summary>
public sealed class NotificationSendTenantBindingTests
{
    private static readonly Guid CallerTenant = Guid.NewGuid();
    private static readonly Guid ForeignTenant = Guid.NewGuid();
    private static readonly Guid RecipientId = Guid.NewGuid();

    private static (NotificationsController Controller, ISender Mediator) Build(
        Guid? claimTenant, bool isSystemAdmin, string? tenantHeader = null)
    {
        var mediator = Substitute.For<ISender>();
        mediator.Send(Arg.Any<SendNotificationCommand>(), Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok(Guid.NewGuid()));

        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(claimTenant);
        tenantContext.IsSystemAdmin.Returns(isSystemAdmin);

        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (tenantHeader is not null)
            httpContext.Request.Headers["X-Tenant-Id"] = tenantHeader;

        var controller = new NotificationsController(mediator)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        return (controller, mediator);
    }

    private static SendNotificationCommand Body(Guid tenantId) =>
        new(tenantId, RecipientId, "Subject", "Body", "email");

    [Fact]
    public async Task Send_IgnoresTheTenantInTheBody_AndUsesTheCallersClaim()
    {
        var (controller, mediator) = Build(CallerTenant, isSystemAdmin: false);

        await controller.Send(Body(ForeignTenant), CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<SendNotificationCommand>(c => c.TenantId == CallerTenant),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_PreservesEveryOtherFieldFromTheBody()
    {
        var (controller, mediator) = Build(CallerTenant, isSystemAdmin: false);

        await controller.Send(Body(ForeignTenant), CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<SendNotificationCommand>(c =>
                c.RecipientUserId == RecipientId
                && c.Subject == "Subject"
                && c.Body == "Body"
                && c.Channel == "email"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_SystemAdmin_MayStillTargetAnotherTenantViaHeader()
    {
        var (controller, mediator) = Build(
            CallerTenant, isSystemAdmin: true, tenantHeader: ForeignTenant.ToString());

        await controller.Send(Body(CallerTenant), CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<SendNotificationCommand>(c => c.TenantId == ForeignTenant),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_NonAdmin_CannotUseTheHeaderToCrossTenants()
    {
        var (controller, mediator) = Build(
            CallerTenant, isSystemAdmin: false, tenantHeader: ForeignTenant.ToString());

        await controller.Send(Body(ForeignTenant), CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<SendNotificationCommand>(c => c.TenantId == CallerTenant),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_WithoutATenantClaim_IsRejected()
    {
        var (controller, mediator) = Build(claimTenant: null, isSystemAdmin: false);

        var act = async () => await controller.Send(Body(ForeignTenant), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await mediator.DidNotReceiveWithAnyArgs().Send(default(SendNotificationCommand)!, default);
    }
}
