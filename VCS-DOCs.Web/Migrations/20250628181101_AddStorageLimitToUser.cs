using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCS_DOCs.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageLimitToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileUploadChunks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_FileUploadSessions",
                table: "FileUploadSessions");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "FileUploadSessions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "FileUploadSessions");

            migrationBuilder.DropColumn(
                name: "IsLatest",
                table: "FileUploadSessions");

            migrationBuilder.DropColumn(
                name: "TotalChunks",
                table: "FileUploadSessions");

            migrationBuilder.AddColumn<long>(
                name: "StorageLimitBytes",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_FileUploadSessions",
                table: "FileUploadSessions",
                column: "FileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_FileUploadSessions",
                table: "FileUploadSessions");

            migrationBuilder.DropColumn(
                name: "StorageLimitBytes",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "FileUploadSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "FileUploadSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsLatest",
                table: "FileUploadSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TotalChunks",
                table: "FileUploadSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_FileUploadSessions",
                table: "FileUploadSessions",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "FileUploadChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Uploaded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileUploadChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileUploadChunks_FileUploadSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "FileUploadSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileUploadChunks_SessionId",
                table: "FileUploadChunks",
                column: "SessionId");
        }
    }
}
