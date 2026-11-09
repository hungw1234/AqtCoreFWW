using AqtCoreFW.Identity.Domain.Services;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing;

/// <summary>Returns all registered IDigitalSignatureProvider instances with their capabilities.</summary>
public sealed record GetAvailableProvidersQuery : IRequest<Result<IReadOnlyList<AvailableProviderDto>>>;

public sealed class GetAvailableProvidersQueryHandler(ISignatureProviderResolver resolver)
    : IRequestHandler<GetAvailableProvidersQuery, Result<IReadOnlyList<AvailableProviderDto>>>
{
    public Task<Result<IReadOnlyList<AvailableProviderDto>>> Handle(
        GetAvailableProvidersQuery request, CancellationToken ct)
    {
        var providers = resolver.GetAvailableProviders();
        IReadOnlyList<AvailableProviderDto> dtos = providers
            .Select(p => new AvailableProviderDto(p.ProviderKey, p.DisplayName, p.Capabilities))
            .ToList();
        return Task.FromResult(Result.Ok(dtos));
    }
}
