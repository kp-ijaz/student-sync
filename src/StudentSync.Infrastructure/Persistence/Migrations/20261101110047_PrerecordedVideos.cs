using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrerecordedVideos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrerecordedVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DurationSeconds = table.Column<int>(type: "int", nullable: true),
                    UnlockRule = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UnlockOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    AccessOverride = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UnlockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrerecordedVideos", x => x.Id);
                    table.CheckConstraint("CK_PrerecordedVideos_Offset", "[UnlockOffsetMinutes] >= 0 AND [UnlockOffsetMinutes] <= 20160");
                    table.ForeignKey(
                        name: "FK_PrerecordedVideos_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrerecordedVideos_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrerecordedVideos_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrerecordedVideos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrerecordedVideos_FileId",
                table: "PrerecordedVideos",
                column: "FileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrerecordedVideos_SessionId_DeletedAtUtc",
                table: "PrerecordedVideos",
                columns: new[] { "SessionId", "DeletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PrerecordedVideos_TenantId",
                table: "PrerecordedVideos",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PrerecordedVideos_UploadedByUserId",
                table: "PrerecordedVideos",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrerecordedVideos");
        }
    }
}
