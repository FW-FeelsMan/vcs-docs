using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCS_DOCs.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportTicketAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "SupportTickets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedByUserId",
                table: "SupportTickets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedUserId",
                table: "SupportTickets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignmentMode",
                table: "SupportTickets",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "AssignedByUserId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "AssignedUserId",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "AssignmentMode",
                table: "SupportTickets");
        }
    }
}
