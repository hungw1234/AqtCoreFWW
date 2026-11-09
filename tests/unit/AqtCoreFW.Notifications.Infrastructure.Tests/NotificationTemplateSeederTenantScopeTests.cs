using AqtCoreFW.Infrastructure.Security;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Notifications.Infrastructure.Tests;

/// <summary>
/// Pins the RLS-blindness fix in <see cref="NotificationTemplateSeeder"/>: the seeder pushes
/// tenant-zero into <see cref="AmbientTenantScope"/> BEFORE creating its DI scope, so a scope
/// resolved inside the push sees <see cref="ScopedTenantContext"/> with <c>Guid.Empty</c>
/// (→ SESSION_CONTEXT pinned → RLS FILTER matches the global template rows). Without the push,
/// the same scope resolves the cross-tenant background context (TenantId=null → SESSION_CONTEXT
/// unset → FILTER hides tenant-zero rows → the check-then-insert goes blind — the 2026-07-23
/// NonAudit CrashLoop). The factory below is mirrored VERBATIM from
/// InfrastructureRegistration.cs (branch-selection logic only); the REAL registration path
/// (push → factory → interceptor → SESSION_CONTEXT) is covered end-to-end by
/// ConsumeScopeTenantContextTests through WebAppFixture — if the real factory changes, that
/// integration test is the drift guard, this one pins the branch semantics.
/// </summary>
public sealed class NotificationTemplateSeederTenantScopeTests
{
    private static ServiceProvider BuildProviderWithRealTenantContextFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // The exact registrations the seeder's resolution path depends on, mirrored from
        // InfrastructureRegistration (no HttpContext in a hosted-service flow).
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddSingleton<AmbientTenantScope>();
        services.AddScoped<ITenantContext>(sp =>
        {
            var httpAccessor = sp.GetRequiredService<IHttpContextAccessor>();
            if (httpAccessor.HttpContext is not null)
                return new AqtCoreFW.Infrastructure.Security.HttpContextTenantContext(httpAccessor);

            var ambient = sp.GetRequiredService<AmbientTenantScope>();
            return ambient.Current is { } state
                ? new ScopedTenantContext(state.TenantId)
                : new AqtCoreFW.Infrastructure.BackgroundJobs.BackgroundJobTenantContext();
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ScopeCreatedInsideAmbientPush_ResolvesTenantZeroContext()
    {
        using var provider = BuildProviderWithRealTenantContextFactory();
        var ambient = provider.GetRequiredService<AmbientTenantScope>();

        using (ambient.Push(Guid.Empty))
        using (var scope = provider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            ctx.TenantId.Should().Be(Guid.Empty,
                "the seeder's scope must act as tenant-zero so the RLS FILTER matches global rows");
        }
    }

    [Fact]
    public void ScopeCreatedWithoutPush_ResolvesCrossTenantBackgroundContext()
    {
        using var provider = BuildProviderWithRealTenantContextFactory();

        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        ctx.TenantId.Should().BeNull(
            "without the ambient push the scope has no tenant — SESSION_CONTEXT stays unset " +
            "and the RLS FILTER hides tenant-zero rows (the pre-fix blind state)");
    }

    [Fact]
    public void AmbientPush_IsRestoredAfterDispose()
    {
        using var provider = BuildProviderWithRealTenantContextFactory();
        var ambient = provider.GetRequiredService<AmbientTenantScope>();

        using (ambient.Push(Guid.Empty))
        {
            ambient.Current.Should().NotBeNull();
        }

        ambient.Current.Should().BeNull(
            "the seeder's push must not leak tenant-zero into later resolutions on the same flow");
    }
}
