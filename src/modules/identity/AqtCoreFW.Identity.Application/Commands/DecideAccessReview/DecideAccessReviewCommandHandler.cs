using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.DecideAccessReview;

public sealed class DecideAccessReviewCommandHandler
    : IRequestHandler<DecideAccessReviewCommand, Result>
{
    private readonly IAccessReviewRepository _repo;

    public DecideAccessReviewCommandHandler(IAccessReviewRepository repo) => _repo = repo;

    public async Task<Result> Handle(DecideAccessReviewCommand request, CancellationToken cancellationToken)
    {
        // T2: look up by PublicId (external addressing key)
        var review = await _repo.GetByPublicIdAsync(request.ReviewId, cancellationToken);
        if (review is null)
            return Result.Fail(new NotFoundError($"AccessReview {request.ReviewId} not found."));

        if (review.Decision is not null)
            return Result.Fail(new ConflictError("Access review already decided."));

        // Use domain method — encapsulates Decision, ReviewedBy, ReviewedAt, audit trail
        review.RecordDecision(request.Decision, request.ActorId);

        await _repo.UpdateAsync(review, cancellationToken);
        return Result.Ok();
    }
}
