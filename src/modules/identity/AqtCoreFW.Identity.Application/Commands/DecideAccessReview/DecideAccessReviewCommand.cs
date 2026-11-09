using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Commands.DecideAccessReview;

/// <summary>Approve or reject a pending access review record (QĐ742).</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record DecideAccessReviewCommand(
    Guid ReviewId,
    AccessReviewDecision Decision,
    Guid ActorId) : ICommand;
