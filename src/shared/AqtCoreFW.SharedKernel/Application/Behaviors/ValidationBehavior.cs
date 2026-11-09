using FluentResults;
using FluentValidation;
using MediatR;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.SharedKernel.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that runs FluentValidation validators.
/// Returns Result.Fail with validation errors instead of throwing exceptions.
/// Registered AFTER TenantAwareBehavior in pipeline.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(
            validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count == 0)
            return await next();

        var errors = failures.Select(f =>
            (IError)new ValidationError(f.ErrorMessage, f.PropertyName))
            .ToList();

        return ResultBehaviorHelper.CreateFail<TResponse>(errors);
    }
}
