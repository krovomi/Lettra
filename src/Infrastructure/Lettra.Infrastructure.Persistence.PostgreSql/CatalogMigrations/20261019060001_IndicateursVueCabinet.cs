using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.CatalogMigrations
{
    /// <inheritdoc />
    public partial class IndicateursVueCabinet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DossierIndicators",
                schema: "catalog",
                columns: table => new
                {
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DossierIndicators", x => new { x.DossierId, x.Key });
                    table.ForeignKey(
                        name: "FK_DossierIndicators_Dossiers_DossierId",
                        column: x => x.DossierId,
                        principalSchema: "catalog",
                        principalTable: "Dossiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DossierIndicators",
                schema: "catalog");
        }
    }
}
