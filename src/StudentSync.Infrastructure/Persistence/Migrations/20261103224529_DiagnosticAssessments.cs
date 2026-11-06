using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DiagnosticAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImageFileId",
                table: "Questions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CurriculumId",
                table: "Exams",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GradeLevelId",
                table: "Exams",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "Exams",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Regular");

            migrationBuilder.AddColumn<Guid>(
                name: "EnteredByUserId",
                table: "ExamAttempts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntryMode",
                table: "ExamAttempts",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Digital");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_ImageFileId",
                table: "Questions",
                column: "ImageFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_CurriculumId",
                table: "Exams",
                column: "CurriculumId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_GradeLevelId",
                table: "Exams",
                column: "GradeLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamAttempts_EnteredByUserId",
                table: "ExamAttempts",
                column: "EnteredByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExamAttempts_AspNetUsers_EnteredByUserId",
                table: "ExamAttempts",
                column: "EnteredByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_Curricula_CurriculumId",
                table: "Exams",
                column: "CurriculumId",
                principalTable: "Curricula",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Exams_GradeLevels_GradeLevelId",
                table: "Exams",
                column: "GradeLevelId",
                principalTable: "GradeLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_Files_ImageFileId",
                table: "Questions",
                column: "ImageFileId",
                principalTable: "Files",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExamAttempts_AspNetUsers_EnteredByUserId",
                table: "ExamAttempts");

            migrationBuilder.DropForeignKey(
                name: "FK_Exams_Curricula_CurriculumId",
                table: "Exams");

            migrationBuilder.DropForeignKey(
                name: "FK_Exams_GradeLevels_GradeLevelId",
                table: "Exams");

            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Files_ImageFileId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_ImageFileId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Exams_CurriculumId",
                table: "Exams");

            migrationBuilder.DropIndex(
                name: "IX_Exams_GradeLevelId",
                table: "Exams");

            migrationBuilder.DropIndex(
                name: "IX_ExamAttempts_EnteredByUserId",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "ImageFileId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "CurriculumId",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "GradeLevelId",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "EnteredByUserId",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "EntryMode",
                table: "ExamAttempts");
        }
    }
}
