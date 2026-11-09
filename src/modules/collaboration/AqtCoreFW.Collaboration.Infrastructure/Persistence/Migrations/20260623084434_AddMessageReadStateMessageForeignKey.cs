using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqtCoreFW.Collaboration.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageReadStateMessageForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_MessageReadStates_Messages_MessageId",
                schema: "collab",
                table: "MessageReadStates",
                column: "MessageId",
                principalSchema: "collab",
                principalTable: "Messages",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MessageReadStates_Messages_MessageId",
                schema: "collab",
                table: "MessageReadStates");
        }
    }
}
