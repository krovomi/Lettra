using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
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
                    Obligation = table.Column<int>(type: "INTEGER", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FiledAtMs = table.Column<long>(type: "INTEGER", nullable: false),
                    FiledBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalFilings", x => new { x.Obligation, x.PeriodStart });
                });

            migrationBuilder.CreateTable(
                name: "FiscalSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FiscalYearEndMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    FiscalYearEndDay = table.Column<int>(type: "INTEGER", nullable: false),
                    VatPeriodicity = table.Column<int>(type: "INTEGER", nullable: false),
                    VatDeadlineDay = table.Column<int>(type: "INTEGER", nullable: false),
                    CorporateIncomeTax = table.Column<bool>(type: "INTEGER", nullable: false),
                    Cfe = table.Column<bool>(type: "INTEGER", nullable: false),
                    TrackedFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAtMs = table.Column<long>(type: "INTEGER", nullable: false)
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
