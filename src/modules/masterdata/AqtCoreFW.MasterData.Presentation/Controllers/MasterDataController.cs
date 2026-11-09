using AqtCoreFW.MasterData.Application.Queries.GetCaseTypes;
using AqtCoreFW.MasterData.Application.Queries.GetJobTitles;
using AqtCoreFW.MasterData.Application.Services;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Memory;

namespace AqtCoreFW.MasterData.Presentation.Controllers;

/// <summary>
/// Read-only master data endpoints — Province/District/Ward from IMemoryCache,
/// CaseType/JobTitle via MediatR handlers with OutputCache 30min.
/// TenantId resolved from JWT (ITenantContext) — never from query params.
/// </summary>
[ApiController]
[Route("api/v1/masterdata")]
[Authorize]
public class MasterDataController(
    IMemoryCache memoryCache,
    ISender mediator,
    IAdministrativeUnitService adminUnitService,
    ITenantContext tenantContext) : ApiControllerBase
{
    [HttpGet("provinces")]
    [OutputCache(PolicyName = "MasterData")]
    [AllowAnonymous]
    public IActionResult GetProvinces()
    {
        var provinces = memoryCache.Get<List<Province>>("masterdata:provinces") ?? [];
        return Ok(ApiResponse<IReadOnlyList<object>>.Ok(
            provinces.Where(p => p.IsActive)
                     .Select(p => new { p.Code, p.NameVi, p.NameEn, p.SortOrder })
                     .ToList<object>()));
    }

    [HttpGet("provinces/{provinceCode}/districts")]
    [OutputCache(PolicyName = "MasterData")]
    [AllowAnonymous]
    public IActionResult GetDistricts(string provinceCode)
    {
        var districts = memoryCache.Get<List<District>>("masterdata:districts") ?? [];
        var result = districts
            .Where(d => d.ProvinceCode == provinceCode && d.IsActive)
            .Select(d => new { d.Code, d.NameVi, d.NameEn, d.SortOrder })
            .ToList();
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("provinces/{provinceCode}/districts/{districtCode}/wards")]
    [OutputCache(PolicyName = "MasterData")]
    [AllowAnonymous]
    public IActionResult GetWards(string provinceCode, string districtCode)
    {
        var wards = memoryCache.Get<List<Ward>>("masterdata:wards") ?? [];
        var result = wards
            .Where(w => w.DistrictCode == districtCode && w.IsActive)
            .Select(w => new { w.Code, w.NameVi, w.NameEn, w.SortOrder })
            .ToList();
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("districts/{districtCode}/wards")]
    [OutputCache(PolicyName = "MasterData")]
    [AllowAnonymous]
    public IActionResult GetWardsByDistrict(string districtCode)
    {
        var wards = memoryCache.Get<List<Ward>>("masterdata:wards") ?? [];
        var result = wards
            .Where(w => w.DistrictCode == districtCode && w.IsActive)
            .Select(w => new { w.Code, w.NameVi, w.NameEn, w.SortOrder })
            .ToList();
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("admin-units")]
    [OutputCache(PolicyName = "AdminUnits")]
    [AllowAnonymous]
    public IActionResult GetAdminUnits([FromQuery] int? level, [FromQuery] string? parentCode)
    {
        var units = adminUnitService.GetByLevel(level ?? 1, parentCode);
        return Ok(ApiResponse<object>.Ok(
            units.Select(u => new
            {
                u.Code, u.NameVi, u.NameEn, u.Level,
                u.ParentCode, u.IsActive, u.EffectiveTo
            }).ToList()));
    }

    [HttpGet("admin-units/{code}/successor")]
    [AllowAnonymous]
    public IActionResult GetSuccessor(string code)
    {
        var activeCode = adminUnitService.ResolveActive(code);
        return Ok(ApiResponse<object>.Ok(new { requestedCode = code, activeCode }));
    }

    [HttpGet("case-types")]
    [OutputCache(PolicyName = "MasterData")]
    public async Task<IActionResult> GetCaseTypes(CancellationToken ct)
    {
        // TenantId from JWT — never from query param (GOV_SEC: tenant isolation)
        var result = await mediator.Send(new GetCaseTypesQuery(tenantContext.TenantId), ct);
        return ToApiResponse(result);
    }

    [HttpGet("job-titles")]
    [OutputCache(PolicyName = "MasterData")]
    public async Task<IActionResult> GetJobTitles(CancellationToken ct)
    {
        var result = await mediator.Send(new GetJobTitlesQuery(tenantContext.TenantId), ct);
        return ToApiResponse(result);
    }
}
