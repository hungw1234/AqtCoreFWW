using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Drop the redundant store default (<c>DEFAULT 0</c>) on TenantSignatureConfig.IsEnabled.
    /// The application always sets IsEnabled explicitly (Create + Update both take isEnabled), so
    /// the DB default was never relied upon; it only equalled the CLR default (false) and produced
    /// a benign EF model-build warning that no sentinel could silence safely (a config is created
    /// both enabled and disabled, so any bool sentinel would flip one legitimate value). Behaviour
    /// is unchanged — this is pure cleanup of an unused column default.
    ///
    /// The [DbContext]/[Migration] attributes + BuildTargetModel live in the companion
    /// .Designer.cs (EF convention); the model snapshot is updated alongside so the migration-
    /// consistency guard and future scaffolding diff against the correct model.
    /// </summary>
    public partial class DropTenantSignatureConfigIsEnabledDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                schema: "identity",
                table: "TenantSignatureConfig",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValueSql: "0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsEnabled",
                schema: "identity",
                table: "TenantSignatureConfig",
                type: "bit",
                nullable: false,
                defaultValueSql: "0",
                oldClrType: typeof(bool),
                oldType: "bit");
        }
    }
}
