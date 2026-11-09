using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using OpenIddict.Abstractions;

namespace AqtCoreFW.Identity.Application.Commands.RevokeToken;

public sealed class RevokeTokenCommandHandler : IRequestHandler<RevokeTokenCommand, Result>
{
    private readonly IOpenIddictTokenManager _tokenManager;

    public RevokeTokenCommandHandler(IOpenIddictTokenManager tokenManager)
        => _tokenManager = tokenManager;

    public async Task<Result> Handle(RevokeTokenCommand cmd, CancellationToken ct)
    {
        if (cmd.TokenId is not null)
        {
            var token = await _tokenManager.FindByIdAsync(cmd.TokenId, ct);
            if (token is null)
                return Result.Fail(new NotFoundError($"Token {cmd.TokenId} not found"));
            await _tokenManager.TryRevokeAsync(token, ct);
            return Result.Ok();
        }

        if (cmd.UserId.HasValue)
        {
            // Revoke all tokens for user — forces re-login
            await foreach (var token in _tokenManager.FindBySubjectAsync(
                cmd.UserId.Value.ToString(), ct))
            {
                await _tokenManager.TryRevokeAsync(token, ct);
            }
            return Result.Ok();
        }

        return Result.Fail(new ValidationError("Either TokenId or UserId must be provided"));
    }
}
