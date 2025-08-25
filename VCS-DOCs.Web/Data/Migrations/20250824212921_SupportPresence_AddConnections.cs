using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCS_DOCs.Data.Migrations
{
    /// <inheritdoc />
    public partial class SupportPresence_AddConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportUserConnections",
                columns: table => new
                {
                    ConnectionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportUserConnections", x => x.ConnectionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportUserConnections_UserId",
                table: "SupportUserConnections",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportUserConnections");
        }
    }
}
