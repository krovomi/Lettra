using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class FacturesRecurrentes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BillingPeriodEnd",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BillingPeriodStart",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecurringInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VatRegime = table.Column<int>(type: "INTEGER", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    PaymentTerms = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Frequency = table.Column<int>(type: "INTEGER", nullable: false),
                    Timing = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ValidateAutomatically = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    RevisionPercent = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    SyntecReferenceIndex = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    SyntecLatestIndex = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    NextRevisionDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    GeneratedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextIssueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TotalExclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringInvoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecurringInvoiceLines",
                columns: table => new
                {
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    RecurringInvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Designation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Nature = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitPriceExclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    VatRateCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    VatRatePercent = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    VatCategory = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    VatExemptionCode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    TotalExclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringInvoiceLines", x => new { x.RecurringInvoiceId, x.Position });
                    table.ForeignKey(
                        name: "FK_RecurringInvoiceLines_RecurringInvoices_RecurringInvoiceId",
                        column: x => x.RecurringInvoiceId,
                        principalTable: "RecurringInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringInvoiceOccurrences",
                columns: table => new
                {
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RecurringInvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionCoefficient = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringInvoiceOccurrences", x => new { x.RecurringInvoiceId, x.PeriodStart });
                    table.ForeignKey(
                        name: "FK_RecurringInvoiceOccurrences_RecurringInvoices_RecurringInvoiceId",
                        column: x => x.RecurringInvoiceId,
                        principalTable: "RecurringInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoiceOccurrences_InvoiceId",
                table: "RecurringInvoiceOccurrences",
                column: "InvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoices_CustomerId",
                table: "RecurringInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringInvoices_IsActive_NextIssueDate",
                table: "RecurringInvoices",
                columns: new[] { "IsActive", "NextIssueDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringInvoiceLines");

            migrationBuilder.DropTable(
                name: "RecurringInvoiceOccurrences");

            migrationBuilder.DropTable(
                name: "RecurringInvoices");

            migrationBuilder.DropColumn(
                name: "BillingPeriodEnd",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BillingPeriodStart",
                table: "Invoices");
        }
    }
}
