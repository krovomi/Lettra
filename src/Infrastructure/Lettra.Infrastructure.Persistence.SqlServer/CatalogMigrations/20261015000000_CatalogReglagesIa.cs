using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.CatalogMigrations
{
    /// <inheritdoc />
    public partial class CatalogReglagesIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiSettings",
                schema: "catalog",
                columns: table => new
                {
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Engine = table.Column<int>(type: "int", nullable: false),
                    Features = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExcludedDossiers = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OfficialWebLookup = table.Column<bool>(type: "bit", nullable: false),
                    MaxToolRounds = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSettings", x => x.OrganisationId);
                    table.ForeignKey(
                        name: "FK_AiSettings_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalSchema: "catalog",
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiSettings",
                schema: "catalog");
        }
    }
}
