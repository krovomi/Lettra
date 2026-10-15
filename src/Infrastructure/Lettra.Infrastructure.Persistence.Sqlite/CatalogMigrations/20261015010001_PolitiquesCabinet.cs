using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.CatalogMigrations
{
    /// <inheritdoc />
    public partial class PolitiquesCabinet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganisationPolicies",
                columns: table => new
                {
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SegregationOfDutiesEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreditNoteApprovalThresholdInclTax = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationPolicies", x => x.OrganisationId);
                    table.ForeignKey(
                        name: "FK_OrganisationPolicies_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganisationPolicies");
        }
    }
}
