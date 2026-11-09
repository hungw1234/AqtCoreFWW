using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SystemParams.Application.Services;
using AqtCoreFW.SystemParams.Presentation.Controllers;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// The virus-scan flag is deployment-wide security posture, so its toggle must write the GLOBAL
/// (NULL-tenant) row and must be refused to non-SystemAdmin callers.
///
/// Both properties are load-bearing rather than stylistic:
/// <list type="bullet">
///   <item>A tenant-scoped write would be invisible in production. The flag cache is filled by
///     ReloadAllAsync running in a background service with no ambient tenant, so SESSION_CONTEXT is
///     unset and the RLS filter on config.SystemParameters admits only NULL-tenant rows. The toggle
///     would return 200 while every upload stayed unscanned.</item>
///   <item>A tenant-level opt-out of malware scanning is a compliance defect (NĐ68) — the enforcement
///     gates read the global row precisely so one tenant cannot disable scanning for itself.</item>
/// </list>
/// Ordinary flags keep their per-tenant behaviour; that path is asserted here too so a future edit
/// cannot quietly make every flag global.
/// </summary>
public sealed class FeatureFlagsControllerGlobalOnlyToggleTests
{
    private const string VirusScanFlag = "feature:files.virus-scan";

    private static (FeatureFlagsController Controller, ISystemParamService Params) Build(bool isSystemAdmin)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsSystemAdmin.Returns(isSystemAdmin);
        tenantContext.TenantId.Returns(Guid.NewGuid());

        var paramService = Substitute.For<ISystemParamService>();

        // ResolveTenantId() (the per-tenant path) pulls ITenantContext out of HttpContext.RequestServices
        // rather than the constructor, so the provider must be populated or that path throws before it
        // ever reaches SetAsync.
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        var controller = new FeatureFlagsController(
            Substitute.For<ISender>(),
            paramService,
            Substitute.For<IFeatureFlagService>(),
            tenantContext)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return (controller, paramService);
    }

    [Fact]
    public async Task VirusScanToggle_AsSystemAdmin_WritesTheGlobalRow()
    {
        var (controller, paramService) = Build(isSystemAdmin: true);

        var result = await controller.Toggle(
            "files.virus-scan", new ToggleFeatureFlagRequest(true), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();

        // The 3-arg overload is the NULL-tenant one. Asserting the tenant-scoped overload was NOT called
        // is what convicts a regression: a call to the 4-arg overload also returns 200, so a status-only
        // assertion would pass while the write went to an invisible tenant row.
        await paramService.Received(1).SetAsync(VirusScanFlag, true, Arg.Any<CancellationToken>());
        await paramService.DidNotReceive().SetAsync(
            VirusScanFlag, Arg.Any<object>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VirusScanToggle_AsTenantAdmin_IsForbidden_AndWritesNothing()
    {
        var (controller, paramService) = Build(isSystemAdmin: false);

        var result = await controller.Toggle(
            "files.virus-scan", new ToggleFeatureFlagRequest(false), CancellationToken.None);

        result.Should().BeOfType<ForbidResult>(
            "a tenant admin must not be able to disable malware scanning for its tenant");
        await paramService.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, default(CancellationToken));
        await paramService.DidNotReceiveWithAnyArgs().SetAsync(default!, default!, default(string)!, default);
    }

    [Fact]
    public async Task OrdinaryFlagToggle_StaysTenantScoped()
    {
        // Not every flag is deployment posture — the per-tenant path must survive this change.
        var (controller, paramService) = Build(isSystemAdmin: false);

        var result = await controller.Toggle(
            "ai_assistant", new ToggleFeatureFlagRequest(true), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await paramService.Received(1).SetAsync(
            "feature:ai_assistant", true, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
