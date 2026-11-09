using AqtCoreFW.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Ai.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RestorePhase06RlsPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Note: VectorDocuments entity maps to table "vector_documents" (snake_case) per
            // VectorDocumentConfiguration.ToTable("vector_documents", "ai").
            foreach (var sql in RlsMigrationHelper.GenerateRlsPolicyStatements(
                "ai", "AiSessions", "vector_documents"))
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in RlsMigrationHelper.DropRlsPolicyStatements(
                "ai", "AiSessions", "vector_documents"))
                migrationBuilder.Sql(sql);
        }
    }
}
