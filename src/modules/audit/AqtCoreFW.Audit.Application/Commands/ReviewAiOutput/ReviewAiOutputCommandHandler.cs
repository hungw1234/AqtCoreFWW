using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.ReviewAiOutput;

/// <summary>
/// Submits a review decision for an AI output.
/// PromptTraceId in the command is the AiPromptTrace MessageId (Guid AK).
/// We resolve the InternalId from the trace to set AiOutputReview.PromptTraceId (long FK).
/// </summary>
public sealed class ReviewAiOutputCommandHandler(
    IAiOutputReviewRepository reviewRepository,
    IAiPromptTraceRepository traceRepository,
    IGuidGenerator guidGenerator)
    : IRequestHandler<ReviewAiOutputCommand, Result>
{
    public async Task<Result> Handle(
        ReviewAiOutputCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve the prompt trace by its public MessageId to get the InternalId (long FK)
        var trace = await traceRepository.GetByIdAsync(request.PromptTraceId, cancellationToken);
        if (trace is null)
            return Result.Fail(new NotFoundError($"AiPromptTrace {request.PromptTraceId} not found."));

        var review = await reviewRepository.GetByPromptTraceInternalIdAsync(trace.InternalId, cancellationToken);

        if (review is null)
        {
            // Create a new pending review record linked by InternalId (long FK)
            review = AiOutputReview.Create(guidGenerator, trace.InternalId);
            await reviewRepository.AddAsync(review, cancellationToken);
        }

        review.Submit(request.ReviewerId, request.Decision, request.Reason);
        await reviewRepository.SaveChangesAsync(cancellationToken);

        return Result.Ok();
    }
}
