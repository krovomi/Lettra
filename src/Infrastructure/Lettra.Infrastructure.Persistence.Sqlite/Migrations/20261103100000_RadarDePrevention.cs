using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RadarDePrevention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PreventionSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TreasuryHorizonWeeks = table.Column<int>(type: "INTEGER", nullable: false),
                    CashBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    CashBalanceOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PaymentDelayRiseDays = table.Column<int>(type: "INTEGER", nullable: false),
                    PeerGapPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    TopCustomerSharePercent = table.Column<int>(type: "INTEGER", nullable: false),
                    BilledCollectedGapPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    CallSignalCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DisabledSignals = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreventionSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PreventionSettings");
        }
    }
}
