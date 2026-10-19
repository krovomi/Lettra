using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class ReglagesFiscaux : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FiscalFilings",
                columns: table => new
                {
                    Obligation = table.Column<int>(type: "int", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    FiledAtMs = table.Column<long>(type: "bigint", nullable: false),
                    FiledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalFilings", x => new { x.Obligation, x.PeriodStart });
                });

            migrationBuilder.CreateTable(
                name: "FiscalSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FiscalYearEndMonth = table.Column<int>(type: "int", nullable: false),
                    FiscalYearEndDay = table.Column<int>(type: "int", nullable: false),
                    VatPeriodicity = table.Column<int>(type: "int", nullable: false),
                    VatDeadlineDay = table.Column<int>(type: "int", nullable: false),
                    CorporateIncomeTax = table.Column<bool>(type: "bit", nullable: false),
                    Cfe = table.Column<bool>(type: "bit", nullable: false),
                    TrackedFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiscalFilings");

            migrationBuilder.DropTable(
                name: "FiscalSettings");
        }
    }
}
