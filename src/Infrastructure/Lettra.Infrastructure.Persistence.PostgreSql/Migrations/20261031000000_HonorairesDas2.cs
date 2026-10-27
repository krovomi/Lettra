using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nature = table.Column<int>(type: "integer", nullable: false),
                    Profession = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                    FiscalYearStart = table.Column<DateOnly>(type: "date", nullable: false),
                    FeeBeneficiaryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
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
