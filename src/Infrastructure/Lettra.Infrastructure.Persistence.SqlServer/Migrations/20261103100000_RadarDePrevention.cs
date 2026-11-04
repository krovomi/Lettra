using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TreasuryHorizonWeeks = table.Column<int>(type: "int", nullable: false),
                    CashBalance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CashBalanceOn = table.Column<DateOnly>(type: "date", nullable: true),
                    PaymentDelayRiseDays = table.Column<int>(type: "int", nullable: false),
                    PeerGapPoints = table.Column<int>(type: "int", nullable: false),
                    TopCustomerSharePercent = table.Column<int>(type: "int", nullable: false),
                    BilledCollectedGapPercent = table.Column<int>(type: "int", nullable: false),
                    CallSignalCount = table.Column<int>(type: "int", nullable: false),
                    DisabledSignals = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
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
