using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Commands.RevokeToken;

/// <summary>Revoke a specific OpenIddict token or all tokens for a user.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record RevokeTokenCommand(
    string? TokenId,
    Guid? UserId,
    Guid ActorId) : ICommand;
