using AqtCoreFW.AssetManagement.Application.Commands.CreateAssetManagement;
using AqtCoreFW.AssetManagement.Application.Commands.DeleteAssetManagement;
using AqtCoreFW.AssetManagement.Application.Commands.UpdateAssetManagement;
using AqtCoreFW.AssetManagement.Application.DTOs;
using AqtCoreFW.AssetManagement.Application.Queries.GetAssetManagement;
using AqtCoreFW.AssetManagement.Application.Queries.ListAssetManagement;
using AqtCoreFW.AssetManagement.Presentation.Controllers;
using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace AqtCoreFW.AssetManagement.Application.Tests.Controllers;

public sealed class AssetManagementEndpointsTests
{
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private AssetManagementController Sut() =>
        new(_sender, _tenantContext, _currentUser);

    [Fact]
    public async Task List_WithTenant_SendsTenantScopedQueryAndReturns200()
    {
        _tenantContext.TenantId.Returns(_tenantId);
        _sender.Send(
                Arg.Any<ListAssetManagementQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Ok<IReadOnlyList<AssetManagementDto>>(
                Array.Empty<AssetManagementDto>()));

        var result = await Sut().List(CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await _sender.Received(1).Send(
            Arg.Is<ListAssetManagementQuery>(query => query.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_WithoutTenant_ReturnsUnauthorizedWithoutSending()
    {
        _tenantContext.TenantId.Returns((Guid?)null);

        var result = await Sut().List(CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
        await _sender.DidNotReceive().Send(
            Arg.Any<ListAssetManagementQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetById_WithTenant_SendsIdAndTenantScopedQuery()
    {
        const long assetId = 42;
        _tenantContext.TenantId.Returns(_tenantId);
        _sender.Send(
                Arg.Any<GetAssetManagementQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new AssetManagementDto(
                assetId, _tenantId, "Laptop", "Office device", DateTimeOffset.UtcNow)));

        var result = await Sut().GetById(assetId, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await _sender.Received(1).Send(
            Arg.Is<GetAssetManagementQuery>(query =>
                query.Id == assetId && query.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Authenticated_SendsCommandWithTenantAndUserFromContext()
    {
        _tenantContext.TenantId.Returns(_tenantId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_userId);
        _sender.Send(
                Arg.Any<CreateAssetManagementCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Ok(123L));
        var request = new CreateAssetManagementRequest("Laptop", "Office device");

        var result = await Sut().Create(request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await _sender.Received(1).Send(
            Arg.Is<CreateAssetManagementCommand>(command =>
                command.TenantId == _tenantId &&
                command.CreatedBy == _userId &&
                command.Title == request.Title &&
                command.Description == request.Description),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_UnauthenticatedUser_ReturnsUnauthorizedWithoutSending()
    {
        _tenantContext.TenantId.Returns(_tenantId);
        _currentUser.IsAuthenticated.Returns(false);

        var result = await Sut().Create(
            new CreateAssetManagementRequest("Laptop", null),
            CancellationToken.None);

        result.Should().BeOfType<UnauthorizedResult>();
        await _sender.DidNotReceive().Send(
            Arg.Any<CreateAssetManagementCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_Authenticated_SendsCommandWithRouteIdAndContextValues()
    {
        const long assetId = 42;
        _tenantContext.TenantId.Returns(_tenantId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_userId);
        _sender.Send(
                Arg.Any<UpdateAssetManagementCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Ok(assetId));
        var request = new UpdateAssetManagementRequest("Desktop", "Updated description");

        var result = await Sut().Update(assetId, request, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await _sender.Received(1).Send(
            Arg.Is<UpdateAssetManagementCommand>(command =>
                command.Id == assetId &&
                command.TenantId == _tenantId &&
                command.UpdatedBy == _userId &&
                command.Title == request.Title &&
                command.Description == request.Description),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_WithTenant_SendsTenantScopedCommandAndReturns204()
    {
        const long assetId = 42;
        _tenantContext.TenantId.Returns(_tenantId);
        _sender.Send(
                Arg.Any<DeleteAssetManagementCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Ok());

        var result = await Sut().Delete(assetId, CancellationToken.None);

        result.Should().BeOfType<NoContentResult>();
        await _sender.Received(1).Send(
            Arg.Is<DeleteAssetManagementCommand>(command =>
                command.Id == assetId && command.TenantId == _tenantId),
            Arg.Any<CancellationToken>());
    }
}
