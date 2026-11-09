using AqtCoreFW.Identity.Application.Queries.GetRoles;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.GetRoleById;

public sealed class GetRoleByIdQueryHandler(IReadDbConnection db)
    : IRequestHandler<GetRoleByIdQuery, Result<RoleDefinitionDto>>
{
    public async Task<Result<RoleDefinitionDto>> Handle(
        GetRoleByIdQuery request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Name, Code, RoleType, Description, IsActive, TenantId
            FROM [identity].AspNetRoles
            WHERE Id = @RoleId AND (TenantId = @TenantId OR TenantId IS NULL)
            """;

        var row = (await db.QueryAsync<RoleRow>(
            sql,
            new { request.RoleId, request.TenantId },
            cancellationToken: cancellationToken)).FirstOrDefault();

        if (row is null)
            return Result.Fail<RoleDefinitionDto>(new NotFoundError("Vai trò không tồn tại."));

        return Result.Ok(new RoleDefinitionDto
        {
            Id = row.Id,
            Name = row.Name,
            Code = row.Code,
            RoleType = row.RoleType,
            Description = row.Description,
            IsActive = row.IsActive,
            TenantId = row.TenantId
        });
    }

    internal sealed class RoleRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public int RoleType { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public Guid? TenantId { get; set; }
    }
}
