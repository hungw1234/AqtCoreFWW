using AqtCoreFW.Contracts.Application.Commands.CreateContract;
using AqtCoreFW.Contracts.Application.Commands.DeleteContract;
using AqtCoreFW.Contracts.Application.Commands.UpdateContract;
using AqtCoreFW.Contracts.Application.Queries.GetContract;
using AqtCoreFW.Contracts.Application.Queries.ListContracts;
using AqtCoreFW.Contracts.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Contracts.Presentation.Controllers;

[Route("api/v1/contracts")]
[Authorize]
public sealed class ContractsController(
    ISender sender,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
    : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId)
            return Unauthorized();

        return ToApiResponse(await sender.Send(new ListContractsQuery(tenantId), ct));
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId)
            return Unauthorized();

        return ToApiResponse(await sender.Send(new GetContractQuery(id, tenantId), ct));
    }

    [HttpPost]
    [ProducesResponseType(200)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create([FromBody] CreateContractRequest request, CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId ||
            !currentUser.IsAuthenticated ||
            currentUser.UserId == Guid.Empty)
            return Unauthorized();

        var command = new CreateContractCommand(
            tenantId,
            request.ContractNumber,
            request.Title,
            request.CounterpartyName,
            request.StartDate,
            request.EndDate,
            request.Description,
            currentUser.UserId);

        return ToApiResponse(await sender.Send(command, ct));
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateContractRequest request,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId ||
            !currentUser.IsAuthenticated ||
            currentUser.UserId == Guid.Empty)
            return Unauthorized();

        var command = new UpdateContractCommand(
            id,
            tenantId,
            request.ContractNumber,
            request.Title,
            request.CounterpartyName,
            request.StartDate,
            request.EndDate,
            request.Status,
            request.Description,
            currentUser.UserId);

        return ToApiResponse(await sender.Send(command, ct));
    }

    [HttpDelete("{id:long}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId)
            return Unauthorized();

        return ToApiResponse(await sender.Send(new DeleteContractCommand(id, tenantId), ct));
    }
}

public sealed record CreateContractRequest(
    string ContractNumber,
    string Title,
    string CounterpartyName,
    DateTime StartDate,
    DateTime EndDate,
    string? Description);

public sealed record UpdateContractRequest(
    string ContractNumber,
    string Title,
    string CounterpartyName,
    DateTime StartDate,
    DateTime EndDate,
    ContractStatus Status,
    string? Description);
