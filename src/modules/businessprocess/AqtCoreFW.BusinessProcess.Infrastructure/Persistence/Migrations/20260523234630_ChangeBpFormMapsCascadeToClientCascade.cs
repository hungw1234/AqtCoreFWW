using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.BusinessProcess.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeBpFormMapsCascadeToClientCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BusinessProcessStateForms_BusinessProcessDefinitions_BusinessProcessDefinitionId",
                schema: "businessprocess",
                table: "BusinessProcessStateForms");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessProcessTransitionForms_BusinessProcessDefinitions_BusinessProcessDefinitionId",
                schema: "businessprocess",
                table: "BusinessProcessTransitionForms");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessProcessStateForms_BusinessProcessDefinitions_BusinessProcessDefinitionId",
                schema: "businessprocess",
                table: "BusinessProcessStateForms",
                column: "BusinessProcessDefinitionId",
                principalSchema: "businessprocess",
                principalTable: "BusinessProcessDefinitions",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessProcessTransitionForms_BusinessProcessDefinitions_BusinessProcessDefinitionId",
                schema: "businessprocess",
                table: "BusinessProcessTransitionForms",
                column: "BusinessProcessDefinitionId",
                principalSchema: "businessprocess",
                principalTable: "BusinessProcessDefinitions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BusinessProcessStateForms_BusinessProcessDefinitions_BusinessProcessDefinitionId",
                schema: "businessprocess",
                table: "BusinessProcessStateForms");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessProcessTransitionForms_BusinessProcessDefinitions_BusinessProcessDefinitionId",
                schema: "businessprocess",
                table: "BusinessProcessTransitionForms");

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessProcessStateForms_BusinessProcessDefinitions_BusinessProcessDefinitionId",
                schema: "businessprocess",
                table: "BusinessProcessStateForms",
                column: "BusinessProcessDefinitionId",
                principalSchema: "businessprocess",
                principalTable: "BusinessProcessDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessProcessTransitionForms_BusinessProcessDefinitions_BusinessProcessDefinitionId",
                schema: "businessprocess",
                table: "BusinessProcessTransitionForms",
                column: "BusinessProcessDefinitionId",
                principalSchema: "businessprocess",
                principalTable: "BusinessProcessDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
