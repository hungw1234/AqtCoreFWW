using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Compliance;
using Dapper;
using FluentResults;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Audit.Application.Queries.GetClassificationRules;

public sealed class GetClassificationRulesQueryHandler(
    [FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection db)
    : IRequestHandler<GetClassificationRulesQuery, Result<IReadOnlyList<ClassificationRuleDto>>>
{
    public async Task<Result<IReadOnlyList<ClassificationRuleDto>>> Handle(
        GetClassificationRulesQuery request,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT Level, MustEncryptAtRest, AllowExport, MaxRetentionDays,
                   UpdatedBy, UpdatedAt, RowVersion
            FROM [audit].[ClassificationRules]
            ORDER BY Level
            """;

        var rows = await db.QueryAsync<ClassificationRuleRow>(sql, null, cancellationToken);

        var dtos = rows
            .Select(r => new ClassificationRuleDto(
                Level: (DataClassificationLevel)r.Level,
                MustEncryptAtRest: r.MustEncryptAtRest,
                AllowExport: r.AllowExport,
                MaxRetentionDays: r.MaxRetentionDays,
                UpdatedBy: r.UpdatedBy,
                UpdatedAt: r.UpdatedAt,
                RowVersion: r.RowVersion))
            .ToList();

        return Result.Ok<IReadOnlyList<ClassificationRuleDto>>(dtos);
    }

    /// <summary>Dapper read model — class (not positional record) to avoid nullable param issues.</summary>
    private sealed class ClassificationRuleRow
    {
        public int Level { get; set; }
        public bool MustEncryptAtRest { get; set; }
        public bool AllowExport { get; set; }
        public int? MaxRetentionDays { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        public byte[] RowVersion { get; set; } = [];
    }
}
