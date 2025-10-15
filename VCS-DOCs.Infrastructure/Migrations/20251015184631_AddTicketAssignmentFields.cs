using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCS_DOCs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketAssignmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_AssignedUserId",
                table: "SupportTickets",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTickets_Status_AssignedUserId",
                table: "SupportTickets",
                columns: new[] { "Status", "AssignedUserId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SupportTickets_AspNetUsers_AssignedUserId",
                table: "SupportTickets",
                column: "AssignedUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupportTickets_AspNetUsers_AssignedUserId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_AssignedUserId",
                table: "SupportTickets");

            migrationBuilder.DropIndex(
                name: "IX_SupportTickets_Status_AssignedUserId",
                table: "SupportTickets");
        }
    }
}
