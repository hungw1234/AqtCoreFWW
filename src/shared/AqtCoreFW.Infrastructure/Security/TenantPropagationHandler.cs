using AqtCoreFW.SharedKernel.Contracts;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Infrastructure.Security;

/// <summary>
/// DelegatingHandler for outbound HttpClient calls to other modules/services.
/// Propagates X-Tenant-Id + X-Correlation-Id + service Bearer token.
/// Monolith: InProcessXxxModuleClient bypasses this (DI scope propagates tenant natively).
/// Microservice extraction: register this handler on the typed HttpClient for the target service.
/// </summary>
public sealed class TenantPropagationHandler(
    ITenantContext tenantContext,
    IServiceIdentityProvider serviceIdentityProvider,
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId.HasValue)
            request.Headers.TryAddWithoutValidation(
                "X-Tenant-Id", tenantContext.TenantId.Value.ToString());

        var correlationId = httpContextAccessor.HttpContext?.Items["X-Correlation-Id"]?.ToString()
            ?? System.Diagnostics.Activity.Current?.TraceId.ToString();
        if (correlationId is not null)
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);

        var serviceToken = await serviceIdentityProvider.GetServiceTokenAsync(cancellationToken);
        if (serviceToken is not null)
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {serviceToken}");

        return await base.SendAsync(request, cancellationToken);
    }
}
