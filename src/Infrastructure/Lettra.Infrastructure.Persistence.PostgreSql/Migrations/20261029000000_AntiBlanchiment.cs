using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AntiBlanchiment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientDueDiligence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Evidence = table.Column<int>(type: "integer", nullable: false),
                    IdentityVerifiedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Factors = table.Column<int>(type: "integer", nullable: false),
                    Justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationshipEndedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientDueDiligence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BeneficialOwners",
                columns: table => new
                {
                    Position = table.Column<int>(type: "integer", nullable: false),
                    ClientDueDiligenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SharePercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BeneficialOwners", x => new { x.ClientDueDiligenceId, x.Position });
                    table.ForeignKey(
                        name: "FK_BeneficialOwners_ClientDueDiligence_ClientDueDiligenceId",
                        column: x => x.ClientDueDiligenceId,
                        principalTable: "ClientDueDiligence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BeneficialOwners");

            migrationBuilder.DropTable(
                name: "ClientDueDiligence");
        }
    }
}
