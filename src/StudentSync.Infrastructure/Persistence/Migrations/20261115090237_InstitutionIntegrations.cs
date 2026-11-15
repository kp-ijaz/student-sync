using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstitutionIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstitutionIntegrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SettingsJson = table.Column<string>(type: "text", maxLength: 500, nullable: false),
                    EncryptedSecrets = table.Column<string>(type: "text", maxLength: 500, nullable: true),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCheckOk = table.Column<bool>(type: "boolean", nullable: true),
                    LastCheckMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionIntegrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionIntegrations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionIntegrations_TenantId",
                table: "InstitutionIntegrations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionIntegrations_TenantId_Kind",
                table: "InstitutionIntegrations",
                columns: new[] { "TenantId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstitutionIntegrations");
        }
    }
}
