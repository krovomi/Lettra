using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class FactureElectroniqueReception : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Size",
                table: "CompanyProfile",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ReceivedInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Format = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SupplierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupplierSiren = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    SupplierVatNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuyerSiren = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    TotalExclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalVat = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalInclTax = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BusinessProcess = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ReceivedAtMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivedInvoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReceivedInvoiceStatusEvents",
                columns: table => new
                {
                    ReceivedInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AtMs = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivedInvoiceStatusEvents", x => new { x.ReceivedInvoiceId, x.Sequence });
                    table.ForeignKey(
                        name: "FK_ReceivedInvoiceStatusEvents_ReceivedInvoices_ReceivedInvoic~",
                        column: x => x.ReceivedInvoiceId,
                        principalTable: "ReceivedInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedInvoices_ReceivedAtMs",
                table: "ReceivedInvoices",
                column: "ReceivedAtMs");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedInvoices_Reference",
                table: "ReceivedInvoices",
                column: "Reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReceivedInvoiceStatusEvents");

            migrationBuilder.DropTable(
                name: "ReceivedInvoices");

            migrationBuilder.DropColumn(
                name: "Size",
                table: "CompanyProfile");
        }
    }
}
