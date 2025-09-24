using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCS_DOCs.Data.Migrations
{
    /// <inheritdoc />
    public partial class TicketEmailReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailNotifyEnabled",
                table: "SupportTickets",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderEmailSentAt",
                table: "SupportTicketMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicketMessages_TicketId_CreatedAt",
                table: "SupportTicketMessages",
                columns: new[] { "TicketId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SupportTicketMessages_TicketId_CreatedAt",
                table: "SupportTicketMessages");

            migrationBuilder.DropColumn(
                name: "EmailNotifyEnabled",
                table: "SupportTickets");

            migrationBuilder.DropColumn(
                name: "ReminderEmailSentAt",
                table: "SupportTicketMessages");
        }
    }
}
