using FluentAssertions;
using FluentResults;
using MediatR;
using NSubstitute;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Behaviors;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.SharedKernel.Tests.Application.Behaviors;

// Namespace-level so Castle DynamicProxy can resolve it through strong-named assemblies
public sealed record TenantTestCommand : IBaseCommand;

// A command that opts out of ambient-tenant enforcement (e.g. an anonymous system webhook whose
// tenant is derived from a persisted entity). Used to prove the ITenantAgnosticCommand bypass.
public sealed record TenantAgnosticTestCommand : ITenantAgnosticCommand;

/// <summary>
/// Tests TenantAwareBehavior: injects tenant context for commands, rejects missing tenant.
/// TC-SK-A003, TC-SK-A004
/// </summary>
public sealed class TenantAwareBehaviorTests
{

    private static ITenantContext MakeTenantContext(Guid? tenantId, bool isSystemAdmin = false)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsSystemAdmin.Returns(isSystemAdmin);
        return ctx;
    }

    [Fact]
    public async Task Handle_CommandWithTenantId_CallsNext()
    {
        // TC-SK-A003: TenantAwareBehavior passes through when tenant is set
        var ctx = MakeTenantContext(Guid.NewGuid());
        var sut = new TenantAwareBehavior<TenantTestCommand, Result>(ctx);

        var nextCalled = false;
        RequestHandlerDelegate<Result> next = ct => { nextCalled = true; return Task.FromResult(Result.Ok()); };

        var result = await sut.Handle(new TenantTestCommand(), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CommandWithMissingTenant_ReturnsFail()
    {
        // TC-SK-A004: TenantAwareBehavior rejects missing tenant
        var ctx = MakeTenantContext(null, isSystemAdmin: false);
        var sut = new TenantAwareBehavior<TenantTestCommand, Result>(ctx);

        var nextCalled = false;
        RequestHandlerDelegate<Result> next = ct => { nextCalled = true; return Task.FromResult(Result.Ok()); };

        var result = await sut.Handle(new TenantTestCommand(), next, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_SystemAdminWithNoTenant_CallsNext()
    {
        // SystemAdmin bypasses tenant check
        var ctx = MakeTenantContext(null, isSystemAdmin: true);
        var sut = new TenantAwareBehavior<TenantTestCommand, Result>(ctx);

        var nextCalled = false;
        RequestHandlerDelegate<Result> next = ct => { nextCalled = true; return Task.FromResult(Result.Ok()); };

        var result = await sut.Handle(new TenantTestCommand(), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_TenantAgnosticCommandWithNoTenant_CallsNext()
    {
        // The opt-in marker bypasses ambient-tenant enforcement — a system command (e.g. anonymous
        // webhook) whose tenant comes from a persisted entity must reach its handler with no tenant set.
        var ctx = MakeTenantContext(null, isSystemAdmin: false);
        var sut = new TenantAwareBehavior<TenantAgnosticTestCommand, Result>(ctx);

        var nextCalled = false;
        RequestHandlerDelegate<Result> next = ct => { nextCalled = true; return Task.FromResult(Result.Ok()); };

        var result = await sut.Handle(new TenantAgnosticTestCommand(), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue("a command marked ITenantAgnosticCommand opts out of tenant enforcement");
    }

    [Fact]
    public async Task Handle_FailedResult_ContainsTenantErrorMessage()
    {
        var ctx = MakeTenantContext(null, isSystemAdmin: false);
        var sut = new TenantAwareBehavior<TenantTestCommand, Result>(ctx);

        RequestHandlerDelegate<Result> next = ct => Task.FromResult(Result.Ok());
        var result = await sut.Handle(new TenantTestCommand(), next, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().NotBeEmpty();
        result.Errors[0].Message.Should().Contain("Tenant");
    }
}
