using FluentResults;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using NSubstitute;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Behaviors;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Regression.Tests.Core;

/// <summary>
/// Regression tests for SharedKernel pipeline behaviors — TC-REG-CORE-001, TC-REG-CORE-002.
/// Ensures validation and tenant enforcement pipelines are never bypassed.
/// </summary>
public sealed class SharedKernelRegressionTests
{
    // --- Validation Behavior (TC-REG-CORE-001) ---

    // Must be public for NSubstitute/Castle.DynamicProxy to create IValidator<T> proxies
    public sealed record TestCommand(string Name) : ICommand<Guid>;

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Module", "Core")]
    public async Task ValidationBehavior_WithInvalidCommand_ReturnsFailResult()
    {
        var validator = Substitute.For<IValidator<TestCommand>>();
        validator.ValidateAsync(Arg.Any<ValidationContext<TestCommand>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(
                [new ValidationFailure("Name", "Name is required")]));

        var behavior = new ValidationBehavior<TestCommand, Result<Guid>>([validator]);
        var next = Substitute.For<RequestHandlerDelegate<Result<Guid>>>();

        var result = await behavior.Handle(
            new TestCommand(""), next, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await next.DidNotReceive().Invoke();
    }

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Module", "Core")]
    public async Task ValidationBehavior_WithValidCommand_ProceedsToHandler()
    {
        var validator = Substitute.For<IValidator<TestCommand>>();
        validator.ValidateAsync(Arg.Any<ValidationContext<TestCommand>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var behavior = new ValidationBehavior<TestCommand, Result<Guid>>([validator]);
        var next = Substitute.For<RequestHandlerDelegate<Result<Guid>>>();
        next.Invoke().Returns(Result.Ok(Guid.NewGuid()));

        var result = await behavior.Handle(
            new TestCommand("Valid"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await next.Received(1).Invoke();
    }

    // --- Tenant Aware Behavior (TC-REG-CORE-002) ---

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Module", "Core")]
    public async Task TenantAwareBehavior_CommandWithoutTenant_ReturnsFailResult()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns((Guid?)null);
        tenantCtx.IsSystemAdmin.Returns(false);

        var behavior = new TenantAwareBehavior<TestCommand, Result<Guid>>(tenantCtx);
        var next = Substitute.For<RequestHandlerDelegate<Result<Guid>>>();

        var result = await behavior.Handle(
            new TestCommand("test"), next, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("Tenant context is required");
        await next.DidNotReceive().Invoke();
    }

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Module", "Core")]
    public async Task TenantAwareBehavior_SystemAdmin_BypassesTenantCheck()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns((Guid?)null);
        tenantCtx.IsSystemAdmin.Returns(true);

        var behavior = new TenantAwareBehavior<TestCommand, Result<Guid>>(tenantCtx);
        var next = Substitute.For<RequestHandlerDelegate<Result<Guid>>>();
        next.Invoke().Returns(Result.Ok(Guid.NewGuid()));

        var result = await behavior.Handle(
            new TestCommand("admin-op"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await next.Received(1).Invoke();
    }

    [Fact]
    [Trait("Category", "Regression")]
    [Trait("Module", "Core")]
    public async Task TenantAwareBehavior_WithValidTenant_ProceedsToHandler()
    {
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.TenantId.Returns(Guid.NewGuid());
        tenantCtx.IsSystemAdmin.Returns(false);

        var behavior = new TenantAwareBehavior<TestCommand, Result<Guid>>(tenantCtx);
        var next = Substitute.For<RequestHandlerDelegate<Result<Guid>>>();
        next.Invoke().Returns(Result.Ok(Guid.NewGuid()));

        var result = await behavior.Handle(
            new TestCommand("tenant-op"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await next.Received(1).Invoke();
    }
}
