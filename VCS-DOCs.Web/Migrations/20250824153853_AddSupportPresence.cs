using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCS_DOCs.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportPresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportUserSessions",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    JwtId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsOnline = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportUserSessions", x => x.UserId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportUserSessions");
        }
    }
}
