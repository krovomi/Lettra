using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TreasuryHorizonWeeks = table.Column<int>(type: "integer", nullable: false),
                    CashBalance = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CashBalanceOn = table.Column<DateOnly>(type: "date", nullable: true),
                    PaymentDelayRiseDays = table.Column<int>(type: "integer", nullable: false),
                    PeerGapPoints = table.Column<int>(type: "integer", nullable: false),
                    TopCustomerSharePercent = table.Column<int>(type: "integer", nullable: false),
                    BilledCollectedGapPercent = table.Column<int>(type: "integer", nullable: false),
                    CallSignalCount = table.Column<int>(type: "integer", nullable: false),
                    DisabledSignals = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
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
