using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AvantLeRetard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BeforeDueDays",
                table: "ReminderSettings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "BeforeDueEnabled",
                table: "ReminderSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PaymentPlanMaxInstallments",
                table: "ReminderSettings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<decimal>(
                name: "PaymentPlanMinimumAmount",
                table: "ReminderSettings",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 300m);

            migrationBuilder.AddColumn<bool>(
                name: "PaymentPlansEnabled",
                table: "ReminderSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Installment",
                table: "PaymentReminders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PortalLinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTargetingObjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTargetingObjections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentPlanInstallments",
                columns: table => new
                {
                    Number = table.Column<int>(type: "integer", nullable: false),
                    PaymentPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentPlanInstallments", x => new { x.PaymentPlanId, x.Number });
                    table.ForeignKey(
                        name: "FK_PaymentPlanInstallments_PaymentPlans_PaymentPlanId",
                        column: x => x.PaymentPlanId,
                        principalTable: "PaymentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlans_InvoiceId_Status",
                table: "PaymentPlans",
                columns: new[] { "InvoiceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlans_RequestedAt",
                table: "PaymentPlans",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTargetingObjections_CustomerId",
                table: "PaymentTargetingObjections",
                column: "CustomerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentPlanInstallments");

            migrationBuilder.DropTable(
                name: "PaymentTargetingObjections");

            migrationBuilder.DropTable(
                name: "PaymentPlans");

            migrationBuilder.DropColumn(
                name: "BeforeDueDays",
                table: "ReminderSettings");

            migrationBuilder.DropColumn(
                name: "BeforeDueEnabled",
                table: "ReminderSettings");

            migrationBuilder.DropColumn(
                name: "PaymentPlanMaxInstallments",
                table: "ReminderSettings");

            migrationBuilder.DropColumn(
                name: "PaymentPlanMinimumAmount",
                table: "ReminderSettings");

            migrationBuilder.DropColumn(
                name: "PaymentPlansEnabled",
                table: "ReminderSettings");

            migrationBuilder.DropColumn(
                name: "Installment",
                table: "PaymentReminders");
        }
    }
}
