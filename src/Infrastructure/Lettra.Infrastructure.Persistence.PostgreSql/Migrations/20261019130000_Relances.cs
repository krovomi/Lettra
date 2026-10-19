using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class Relances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Outstanding = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DaysOverdue = table.Column<int>(type: "integer", nullable: false),
                    Trigger = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReminders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReminderSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AutoSend = table.Column<bool>(type: "boolean", nullable: false),
                    FriendlyDays = table.Column<int>(type: "integer", nullable: false),
                    ReminderDays = table.Column<int>(type: "integer", nullable: false),
                    FinalDays = table.Column<int>(type: "integer", nullable: false),
                    FormalNoticeDays = table.Column<int>(type: "integer", nullable: false),
                    MinimumGapDays = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReminders_CustomerId",
                table: "PaymentReminders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReminders_InvoiceId_OccurredOn",
                table: "PaymentReminders",
                columns: new[] { "InvoiceId", "OccurredOn" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReminders_OccurredOn",
                table: "PaymentReminders",
                column: "OccurredOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentReminders");

            migrationBuilder.DropTable(
                name: "ReminderSettings");
        }
    }
}
