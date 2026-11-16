using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentSync.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Automations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Task = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RunKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Items = table.Column<int>(type: "integer", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationRuns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendanceNudge = table.Column<bool>(type: "boolean", nullable: false),
                    AttendanceNudgeAfterMinutes = table.Column<int>(type: "integer", nullable: false),
                    AbsenceSms = table.Column<bool>(type: "boolean", nullable: false),
                    AbsenceFlagAt = table.Column<int>(type: "integer", nullable: false),
                    EveningTimetable = table.Column<bool>(type: "boolean", nullable: false),
                    EveningTimetableAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ClassDaySms = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduleRunningOut = table.Column<bool>(type: "boolean", nullable: false),
                    LowHoursAlert = table.Column<bool>(type: "boolean", nullable: false),
                    LowHoursAt = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    LowHoursSms = table.Column<bool>(type: "boolean", nullable: false),
                    FeeReminders = table.Column<bool>(type: "boolean", nullable: false),
                    FeeReminderDaysBefore = table.Column<int>(type: "integer", nullable: false),
                    FeeReminderDaysAfter = table.Column<int>(type: "integer", nullable: false),
                    FeeReminderSms = table.Column<bool>(type: "boolean", nullable: false),
                    PaymentLinks = table.Column<bool>(type: "boolean", nullable: false),
                    MonthEndSalaries = table.Column<bool>(type: "boolean", nullable: false),
                    MorningSummary = table.Column<bool>(type: "boolean", nullable: false),
                    MorningSummaryAt = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    MonthEndReport = table.Column<bool>(type: "boolean", nullable: false),
                    ProgressReports = table.Column<bool>(type: "boolean", nullable: false),
                    ProgressReportSms = table.Column<bool>(type: "boolean", nullable: false),
                    AutoAiNotes = table.Column<bool>(type: "boolean", nullable: false),
                    JobFailureAlerts = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationSettings", x => x.Id);
                    table.CheckConstraint("CK_AutomationSettings_Ranges", "\"AttendanceNudgeAfterMinutes\" BETWEEN 0 AND 720 AND \"AbsenceFlagAt\" BETWEEN 0 AND 31 AND \"LowHoursAt\" >= 0 AND \"FeeReminderDaysBefore\" BETWEEN 0 AND 30 AND \"FeeReminderDaysAfter\" BETWEEN 0 AND 60");
                    table.ForeignKey(
                        name: "FK_AutomationSettings_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboundMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Recipient = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PhoneMasked = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Text = table.Column<string>(type: "character varying(480)", maxLength: 480, nullable: false),
                    DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotBeforeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutboundMessages_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutboundMessages_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderLinkId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProviderPaymentId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FeePaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastCheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentLinks", x => x.Id);
                    table.CheckConstraint("CK_PaymentLinks_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_PaymentLinks_StudentPackages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "StudentPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentLinks_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentLinks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProgressReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    ClassesAttended = table.Column<int>(type: "integer", nullable: false),
                    ClassesMissed = table.Column<int>(type: "integer", nullable: false),
                    HoursAttended = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    HoursLeft = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    AssignmentsGraded = table.Column<int>(type: "integer", nullable: false),
                    AverageMarkPercent = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    Remark = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SentByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgressReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgressReports_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressReports_Teachers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProgressReports_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_TenantId",
                table: "AutomationRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_TenantId_Task_RunKey",
                table: "AutomationRuns",
                columns: new[] { "TenantId", "Task", "RunKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutomationSettings_TenantId",
                table: "AutomationSettings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_Status_NotBeforeUtc",
                table: "OutboundMessages",
                columns: new[] { "Status", "NotBeforeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_StudentId",
                table: "OutboundMessages",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_TenantId",
                table: "OutboundMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_TenantId_CreatedAtUtc",
                table: "OutboundMessages",
                columns: new[] { "TenantId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundMessages_TenantId_DedupeKey",
                table: "OutboundMessages",
                columns: new[] { "TenantId", "DedupeKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLinks_PackageId_Open",
                table: "PaymentLinks",
                column: "PackageId",
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLinks_Provider_ProviderPaymentId",
                table: "PaymentLinks",
                columns: new[] { "Provider", "ProviderPaymentId" },
                unique: true,
                filter: "\"ProviderPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLinks_StudentId",
                table: "PaymentLinks",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLinks_TenantId",
                table: "PaymentLinks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLinks_TenantId_Provider_ProviderLinkId",
                table: "PaymentLinks",
                columns: new[] { "TenantId", "Provider", "ProviderLinkId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgressReports_StudentId",
                table: "ProgressReports",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressReports_TeacherId_Status",
                table: "ProgressReports",
                columns: new[] { "TeacherId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProgressReports_TenantId",
                table: "ProgressReports",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgressReports_TenantId_StudentId_Month",
                table: "ProgressReports",
                columns: new[] { "TenantId", "StudentId", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationRuns");

            migrationBuilder.DropTable(
                name: "AutomationSettings");

            migrationBuilder.DropTable(
                name: "OutboundMessages");

            migrationBuilder.DropTable(
                name: "PaymentLinks");

            migrationBuilder.DropTable(
                name: "ProgressReports");
        }
    }
}
