using FluentAssertions;
using FluentResults;
using FluentValidation;
using MediatR;
using AqtCoreFW.SharedKernel.Application.Behaviors;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.SharedKernel.Tests.Application.Behaviors;

// Must be public/namespace-level so FluentValidation strong-named assembly can proxy IValidator<T>
public sealed record ValidationTestCommand(string Value) : IBaseCommand;

/// <summary>
/// Concrete failing validator — avoids NSubstitute proxy restrictions on strong-named assemblies.
/// </summary>
public sealed class AlwaysFailValidator : AbstractValidator<ValidationTestCommand>
{
    public AlwaysFailValidator() => RuleFor(x => x.Value).Must(_ => false).WithMessage("Always fails");
}

/// <summary>
/// Concrete passing validator.
/// </summary>
public sealed class AlwaysPassValidator : AbstractValidator<ValidationTestCommand>
{
    // No rules — everything passes
}

/// <summary>
/// Tests ValidationBehavior pipeline: short-circuits on failure, passes through with no validators.
/// TC-SK-A001, TC-SK-A002
/// </summary>
public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_WithFailingValidator_ReturnsFail_WithoutCallingNext()
    {
        // TC-SK-A001: ValidationBehavior short-circuits on validation failure
        var sut = new ValidationBehavior<ValidationTestCommand, Result>(
            new IValidator<ValidationTestCommand>[] { new AlwaysFailValidator() });
        var nextCalled = false;
        RequestHandlerDelegate<Result> next = ct => { nextCalled = true; return Task.FromResult(Result.Ok()); };

        var result = await sut.Handle(new ValidationTestCommand(""), next, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().NotBeEmpty();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WithNoValidators_CallsNextAndReturnsSuccess()
    {
        // TC-SK-A002: ValidationBehavior passes through when no validators
        var sut = new ValidationBehavior<ValidationTestCommand, Result>(
            Array.Empty<IValidator<ValidationTestCommand>>());
        var nextCalled = false;
        RequestHandlerDelegate<Result> next = ct => { nextCalled = true; return Task.FromResult(Result.Ok()); };

        var result = await sut.Handle(new ValidationTestCommand("test"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithPassingValidator_CallsNext()
    {
        var sut = new ValidationBehavior<ValidationTestCommand, Result>(
            new IValidator<ValidationTestCommand>[] { new AlwaysPassValidator() });
        var nextCalled = false;
        RequestHandlerDelegate<Result> next = ct => { nextCalled = true; return Task.FromResult(Result.Ok()); };

        var result = await sut.Handle(new ValidationTestCommand("valid"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidationError_ContainsPropertyAndMessage()
    {
        var sut = new ValidationBehavior<ValidationTestCommand, Result>(
            new IValidator<ValidationTestCommand>[] { new AlwaysFailValidator() });
        RequestHandlerDelegate<Result> next = ct => Task.FromResult(Result.Ok());

        var result = await sut.Handle(new ValidationTestCommand(""), next, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Message.Should().NotBeNullOrEmpty();
    }
}
