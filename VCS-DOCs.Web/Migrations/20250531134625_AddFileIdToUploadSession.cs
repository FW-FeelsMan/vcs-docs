using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VCS_DOCs.Migrations
{
    /// <inheritdoc />
    public partial class AddFileIdToUploadSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FileName",
                table: "FileUploadSessions",
                newName: "OriginalFileName");

            migrationBuilder.AddColumn<Guid>(
                name: "FileId",
                table: "FileUploadSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileId",
                table: "FileUploadSessions");

            migrationBuilder.RenameColumn(
                name: "OriginalFileName",
                table: "FileUploadSessions",
                newName: "FileName");
        }
    }
}
