using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormTemplatePublicCodeUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fail-loud pre-flight (non-destructive): the Active->IsPublic=1 backfill may have left two
            // tenants sharing an Active public Code. Creating the filtered unique index on that data would
            // fail with an opaque duplicate-key error, so surface the offending Code(s) first and abort —
            // never silently demote a tenant's public form. Resolve by demoting all-but-one IsPublic=0 per
            // Code, then re-run. Runs in the migration transaction, so the THROW rolls the migration back.
            migrationBuilder.Sql("""
                DECLARE @dupes nvarchar(max);
                SELECT @dupes = STRING_AGG(CONVERT(nvarchar(max), Code), N', ')
                FROM (
                    SELECT Code
                    FROM forms.FormTemplates
                    WHERE IsPublic = 1 AND IsDeleted = 0
                    GROUP BY Code
                    HAVING COUNT(*) > 1
                ) d;
                IF @dupes IS NOT NULL
                BEGIN
                    DECLARE @msg nvarchar(2048) =
                        N'Cannot create UX_FormTemplate_PublicCode: public Code(s) duplicated across tenants (IsPublic=1): '
                        + @dupes
                        + N'. Demote all-but-one FormTemplate to IsPublic=0 per Code, then re-run this migration.';
                    THROW 50000, @msg, 1;
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_FormTemplate_PublicCode",
                schema: "forms",
                table: "FormTemplates",
                column: "Code",
                unique: true,
                filter: "[IsPublic] = 1 AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_FormTemplate_PublicCode",
                schema: "forms",
                table: "FormTemplates");
        }
    }
}
