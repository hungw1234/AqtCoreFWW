using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Queries.GetFormTemplateSignerRoles;

public sealed class GetFormTemplateSignerRolesQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetFormTemplateSignerRolesQuery, Result<IReadOnlyList<FormTemplateSignerRoleDto>>>
{
    public async Task<Result<IReadOnlyList<FormTemplateSignerRoleDto>>> Handle(
        GetFormTemplateSignerRolesQuery request,
        CancellationToken cancellationToken)
    {
        // Verify template exists and belongs to tenant before returning child rows
        const string templateExistsSql = """
            SELECT Id
            FROM forms.FormTemplates
            WHERE PublicId = @TemplatePublicId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var internalId = await db.QueryFirstOrDefaultAsync<long?>(
            templateExistsSql,
            new { request.TemplatePublicId, request.TenantId },
            cancellationToken);

        if (internalId is null)
            return Result.Fail(new NotFoundError($"FormTemplate {request.TemplatePublicId} không tìm thấy."));

        const string rolesSql = """
            SELECT Id, RoleKey, RoleLabelVi, DisplayOrder, IsRequired, PlacementJson
            FROM forms.FormTemplateSignerRoles
            WHERE FormTemplateId = @InternalId AND IsDeleted = 0
            ORDER BY DisplayOrder
            """;

        var rows = await db.QueryAsync<SignerRoleRow>(
            rolesSql, new { InternalId = internalId.Value }, cancellationToken);

        IReadOnlyList<FormTemplateSignerRoleDto> result = rows
            .Select(r => new FormTemplateSignerRoleDto(
                r.Id, r.RoleKey, r.RoleLabelVi, r.DisplayOrder, r.IsRequired, r.PlacementJson))
            .ToList();

        return Result.Ok(result);
    }

    // Dapper requires parameterless ctor — positional records with nullable params fail materialization
    private sealed class SignerRoleRow
    {
        public long Id { get; set; }
        public string RoleKey { get; set; } = string.Empty;
        public string RoleLabelVi { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool IsRequired { get; set; }
        public string? PlacementJson { get; set; }
    }
}
