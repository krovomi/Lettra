using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class HonorairesDas2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeeBeneficiaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Nature = table.Column<int>(type: "INTEGER", nullable: false),
                    Profession = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeeBeneficiaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeeBeneficiaries_Suppliers_Id",
                        column: x => x.Id,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeePaidOutside",
                columns: table => new
                {
                    FiscalYearStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FeeBeneficiaryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeePaidOutside", x => new { x.FeeBeneficiaryId, x.FiscalYearStart });
                    table.ForeignKey(
                        name: "FK_FeePaidOutside_FeeBeneficiaries_FeeBeneficiaryId",
                        column: x => x.FeeBeneficiaryId,
                        principalTable: "FeeBeneficiaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeePaidOutside");

            migrationBuilder.DropTable(
                name: "FeeBeneficiaries");
        }
    }
}
