using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AttestationsVigilance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubcontractorAttestations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContractAmountExclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SecurityCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    VerifiedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubcontractorAttestations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubcontractorAttestations_Suppliers_Id",
                        column: x => x.Id,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubcontractorAttestations");
        }
    }
}
