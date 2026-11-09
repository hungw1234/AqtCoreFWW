using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Application.Commands.BulkImportUsers;

public sealed class BulkImportUsersCommandHandler
    : IRequestHandler<BulkImportUsersCommand, Result<BulkImportResultDto>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDomainEventPublisher _events;
    private readonly IGuidGenerator _guidGen;
    private readonly ILogger<BulkImportUsersCommandHandler> _logger;

    public BulkImportUsersCommandHandler(
        UserManager<ApplicationUser> userManager,
        IDomainEventPublisher events,
        IGuidGenerator guidGen,
        ILogger<BulkImportUsersCommandHandler> logger)
    {
        _userManager = userManager;
        _events = events;
        _guidGen = guidGen;
        _logger = logger;
    }

    public async Task<Result<BulkImportResultDto>> Handle(
        BulkImportUsersCommand cmd, CancellationToken ct)
    {
        var errors = new List<BulkImportRowError>();
        int successCount = 0;

        // Generate temp password — user must reset on first login
        const string tempPasswordTemplate = "Temp@{0}!Gov";

        foreach (var row in cmd.Rows)
        {
            try
            {
                var existing = await _userManager.FindByEmailAsync(row.Email);
                if (existing is not null)
                {
                    errors.Add(new BulkImportRowError(row.RowNumber, row.Email, "Email already exists"));
                    continue;
                }

                var user = new ApplicationUser
                {
                    Id = _guidGen.NewIdRandom(),
                    FullName = row.FullName,
                    Email = row.Email,
                    UserName = row.Email,
                    DepartmentCode = row.DepartmentCode,
                    TenantId = cmd.TenantId,
                    PasswordExpiresAt = DateTime.UtcNow, // Force immediate password change
                    // QĐ 742 1.3.a: admin-imported users must change their temp password on first login
                    MustChangePassword = true
                };

                var tempPassword = string.Format(tempPasswordTemplate, _guidGen.NewIdRandom().ToString("N")[..8]);
                var result = await _userManager.CreateAsync(user, tempPassword);

                if (!result.Succeeded)
                {
                    var msg = string.Join("; ", result.Errors.Select(e => e.Description));
                    errors.Add(new BulkImportRowError(row.RowNumber, row.Email, msg));
                    continue;
                }

                // The user is persisted at this point; a failed role bind must surface as a row error
                // rather than silently leaving the user without the intended role (privilege gap).
                var roleFailed = false;
                if (!string.IsNullOrEmpty(row.InitialRole))
                {
                    var roleResult = await _userManager.AddToRoleAsync(user, row.InitialRole);
                    if (!roleResult.Succeeded)
                    {
                        var roleMsg = string.Join("; ", roleResult.Errors.Select(e => e.Description));
                        _logger.LogWarning(
                            "BulkImport row {Row} ({Email}): user created but role '{Role}' not assigned — {Reason}",
                            row.RowNumber, row.Email, row.InitialRole, roleMsg);
                        errors.Add(new BulkImportRowError(
                            row.RowNumber, row.Email, $"User created but role '{row.InitialRole}' not assigned: {roleMsg}"));
                        roleFailed = true;
                    }
                }

                // User exists → emit the creation event regardless. But a role-failed row is reported as
                // an error, so it must NOT also count as a success (else successCount + errorCount > rows).
                await _events.PublishAsync(
                    [new UserCreatedEvent(user.Id, user.FullName, user.Email!, user.TenantId, _guidGen.NewIdRandom())], ct);

                if (!roleFailed)
                    successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "BulkImport row {Row} ({Email}) failed.", row.RowNumber, row.Email);
                errors.Add(new BulkImportRowError(row.RowNumber, row.Email, $"Import failed for this row: {ex.Message}"));
            }
        }

        return Result.Ok(new BulkImportResultDto(successCount, errors.Count, errors));
    }
}
