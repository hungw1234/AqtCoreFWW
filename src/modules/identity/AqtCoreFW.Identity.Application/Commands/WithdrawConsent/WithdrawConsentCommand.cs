using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.WithdrawConsent;

/// <summary>Withdraw a previously granted consent record (RTBF / PDPL withdrawal right).</summary>
public sealed record WithdrawConsentCommand(
    Guid ConsentId,
    Guid UserId,
    string Reason) : IRequest<Result>;
