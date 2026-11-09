using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.MasterData.Application.Commands.CreateDistrict;
using AqtCoreFW.MasterData.Application.Commands.CreateProvince;
using AqtCoreFW.MasterData.Application.Commands.CreateWard;
using AqtCoreFW.MasterData.Application.Commands.DeleteDistrict;
using AqtCoreFW.MasterData.Application.Commands.DeleteProvince;
using AqtCoreFW.MasterData.Application.Commands.DeleteWard;
using AqtCoreFW.MasterData.Application.Commands.UpdateDistrict;
using AqtCoreFW.MasterData.Application.Commands.UpdateProvince;
using AqtCoreFW.MasterData.Application.Commands.UpdateWard;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.MasterData.Presentation.Controllers;

/// <summary>
/// Admin CUD endpoints for master data (provinces, districts, wards).
/// Handlers invalidate IMemoryCache after every write so the read controller sees fresh data immediately.
/// </summary>
[ApiController]
[Route("api/v1/admin/master-data")]
[Authorize(Policy = PermCodes.Master.Data.Manage)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public class MasterDataAdminController(ISender mediator) : ApiControllerBase
{
    // â”€â”€ Province â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [HttpPost("provinces")]
    public async Task<IActionResult> CreateProvince(
        [FromBody] UpsertProvinceRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new CreateProvinceCommand(req.Code, req.NameVi, req.NameEn, req.SortOrder), ct);
        return ToApiResponse(result);
    }

    [HttpPut("provinces/{code}")]
    public async Task<IActionResult> UpdateProvince(
        string code,
        [FromBody] UpsertProvinceRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpdateProvinceCommand(code, req.NameVi, req.NameEn, req.SortOrder), ct);
        return ToApiResponse(result);
    }

    [HttpDelete("provinces/{code}")]
    public async Task<IActionResult> DeleteProvince(string code, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteProvinceCommand(code), ct);
        return ToApiResponse(result);
    }

    // â”€â”€ District â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [HttpPost("districts")]
    public async Task<IActionResult> CreateDistrict(
        [FromBody] UpsertDistrictRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new CreateDistrictCommand(req.Code, req.ProvinceCode, req.NameVi, req.NameEn, req.SortOrder), ct);
        return ToApiResponse(result);
    }

    [HttpPut("districts/{code}")]
    public async Task<IActionResult> UpdateDistrict(
        string code,
        [FromBody] UpsertDistrictRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpdateDistrictCommand(code, req.ProvinceCode, req.NameVi, req.NameEn, req.SortOrder), ct);
        return ToApiResponse(result);
    }

    [HttpDelete("districts/{code}")]
    public async Task<IActionResult> DeleteDistrict(string code, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteDistrictCommand(code), ct);
        return ToApiResponse(result);
    }

    // â”€â”€ Ward â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [HttpPost("wards")]
    public async Task<IActionResult> CreateWard(
        [FromBody] UpsertWardRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new CreateWardCommand(req.Code, req.DistrictCode, req.NameVi, req.NameEn, req.SortOrder), ct);
        return ToApiResponse(result);
    }

    [HttpPut("wards/{code}")]
    public async Task<IActionResult> UpdateWard(
        string code,
        [FromBody] UpsertWardRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpdateWardCommand(code, req.DistrictCode, req.NameVi, req.NameEn, req.SortOrder), ct);
        return ToApiResponse(result);
    }

    [HttpDelete("wards/{code}")]
    public async Task<IActionResult> DeleteWard(string code, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteWardCommand(code), ct);
        return ToApiResponse(result);
    }
}

// â”€â”€ Request DTOs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed record UpsertProvinceRequest(
    string Code, string NameVi, string NameEn, int SortOrder = 0);

public sealed record UpsertDistrictRequest(
    string Code, string ProvinceCode, string NameVi, string NameEn, int SortOrder = 0);

public sealed record UpsertWardRequest(
    string Code, string DistrictCode, string NameVi, string NameEn, int SortOrder = 0);
