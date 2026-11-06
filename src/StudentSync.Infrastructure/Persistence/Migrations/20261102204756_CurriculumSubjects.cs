using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CurriculumSubjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChoiceGroup",
                table: "Subjects",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Subjects",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                // Existing subjects keep their current behaviour: every student enrolled in the course takes them.
                defaultValue: "Core");

            migrationBuilder.CreateTable(
                name: "StudentSubjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentSubjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentSubjects_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentSubjects_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentSubjects_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subjects_TenantId_CourseId_Kind",
                table: "Subjects",
                columns: new[] { "TenantId", "CourseId", "Kind" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Subjects_ChoiceGroup",
                table: "Subjects",
                sql: "([Kind] <> 'LanguageOption' OR [ChoiceGroup] IS NOT NULL) AND ([Kind] <> 'Core' OR [ChoiceGroup] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjects_StudentId_SubjectId",
                table: "StudentSubjects",
                columns: new[] { "StudentId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjects_SubjectId",
                table: "StudentSubjects",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentSubjects_TenantId",
                table: "StudentSubjects",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentSubjects");

            migrationBuilder.DropIndex(
                name: "IX_Subjects_TenantId_CourseId_Kind",
                table: "Subjects");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Subjects_ChoiceGroup",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "ChoiceGroup",
                table: "Subjects");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Subjects");
        }
    }
}
