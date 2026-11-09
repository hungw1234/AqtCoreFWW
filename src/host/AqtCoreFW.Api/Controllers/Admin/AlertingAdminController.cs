using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Infrastructure.Alerting;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Api.Controllers.Admin;

/// <summary>
/// Admin CRUD for AlertRules (M14 Observability).
/// POST /api/v1/admin/alerts/{id}/test evaluates that one rule immediately and returns the outcome,
/// including whether the notification was actually delivered.
/// Requires SystemAdmin role.
/// </summary>
[Route("api/v1/admin/alerts")]
[Authorize(Policy = PermCodes.System.Config.Manage)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class AlertingAdminController(
    AlertingDbContext db,
    AlertEvaluationJob evaluationJob) : ApiControllerBase
{
    // =========================================================================
    // Alert Rules
    // =========================================================================

    /// <summary>List all alert rules with last-trigger info, most-recently-modified first.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListRules(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (Math.Max(page, 1) - 1) * pageSize;

        var total = await db.AlertRules.CountAsync(ct);
        var items = await db.AlertRules
            .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
            .Skip(skip).Take(pageSize)
            .Select(r => new AlertRuleDto(
                r.Id, r.Name, r.MetricName, r.Condition, r.Threshold,
                r.WindowMinutes, r.NotifyChannel, r.NotifyTarget,
                r.Enabled, r.LastTriggeredAt, r.ConsecutiveBreaches))
            .ToListAsync(ct);

        return Ok(ApiResponse<object>.Ok(new { items, totalCount = total }));
    }

    /// <summary>Get a single alert rule by ID.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetRule(long id, CancellationToken ct)
    {
        var rule = await db.AlertRules.FindAsync([id], ct);
        if (rule is null) return NotFound();

        return Ok(ApiResponse<AlertRuleDto>.Ok(new AlertRuleDto(
            rule.Id, rule.Name, rule.MetricName, rule.Condition, rule.Threshold,
            rule.WindowMinutes, rule.NotifyChannel, rule.NotifyTarget,
            rule.Enabled, rule.LastTriggeredAt, rule.ConsecutiveBreaches)));
    }

    /// <summary>Create a new alert rule.</summary>
    [HttpPost]
    [ProducesResponseType(201)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> CreateRule(
        [FromBody] UpsertAlertRuleRequest req,
        CancellationToken ct)
    {
        var userId = GetUserId();
        var rule = AlertRule.Create(
            req.Name, req.MetricName, req.Condition, req.Threshold,
            req.WindowMinutes, req.NotifyChannel, req.NotifyTarget, userId);

        db.AlertRules.Add(rule);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetRule), new { id = rule.Id },
            ApiResponse<object>.Ok(new { id = rule.Id }));
    }

    /// <summary>Update an existing alert rule.</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateRule(
        long id,
        [FromBody] UpsertAlertRuleRequest req,
        CancellationToken ct)
    {
        var rule = await db.AlertRules.FindAsync([id], ct);
        if (rule is null) return NotFound();

        rule.Update(req.Name, req.MetricName, req.Condition, req.Threshold,
            req.WindowMinutes, req.NotifyChannel, req.NotifyTarget, GetUserId());

        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { id = rule.Id }));
    }

    /// <summary>Soft-delete an alert rule.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteRule(long id, CancellationToken ct)
    {
        var rule = await db.AlertRules.FindAsync([id], ct);
        if (rule is null) return NotFound();

        rule.Delete();
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Evaluate one alert rule now and report what happened.
    ///
    /// <para>
    /// The rule is evaluated whether or not it is enabled, and the notification is really dispatched
    /// when the condition breaches — verifying the delivery path is the point. No state is written:
    /// a test does not record a breach, move LastTriggeredAt, or write an audit entry.
    /// </para>
    /// <para>
    /// A failed dispatch still answers 200. The request succeeded; it is the alert delivery that
    /// failed, and the payload says so. A 5xx would be indistinguishable from a broken endpoint.
    /// </para>
    /// </summary>
    [HttpPost("{id:long}/test")]
    [ProducesResponseType(typeof(ApiResponse<AlertRuleTestOutcome>), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> TestRule(long id, CancellationToken ct)
    {
        var rule = await db.AlertRules.FindAsync([id], ct);
        if (rule is null) return NotFound();

        var outcome = await evaluationJob.TestRuleAsync(rule, ct);

        return Ok(ApiResponse<AlertRuleTestOutcome>.Ok(outcome));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private Guid GetUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

// ---- Request / Response DTOs ------------------------------------------------

public sealed record UpsertAlertRuleRequest(
    string Name,
    string MetricName,
    string Condition,
    double Threshold,
    int WindowMinutes,
    string NotifyChannel,
    string? NotifyTarget);

public sealed record AlertRuleDto(
    long Id,
    string Name,
    string MetricName,
    string Condition,
    double Threshold,
    int WindowMinutes,
    string NotifyChannel,
    string? NotifyTarget,
    bool Enabled,
    DateTime? LastTriggeredAt,
    int ConsecutiveBreaches);

