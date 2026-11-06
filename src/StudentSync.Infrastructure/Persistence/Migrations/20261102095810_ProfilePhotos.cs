using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfilePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PhotoFileId",
                table: "AspNetUsers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_PhotoFileId",
                table: "AspNetUsers",
                column: "PhotoFileId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Files_PhotoFileId",
                table: "AspNetUsers",
                column: "PhotoFileId",
                principalTable: "Files",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Files_PhotoFileId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_PhotoFileId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PhotoFileId",
                table: "AspNetUsers");
        }
    }
}
