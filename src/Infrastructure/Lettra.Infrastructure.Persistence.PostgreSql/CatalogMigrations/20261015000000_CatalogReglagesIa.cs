using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.CatalogMigrations
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
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Engine = table.Column<int>(type: "integer", nullable: false),
                    Features = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExcludedDossiers = table.Column<string>(type: "text", nullable: false),
                    OfficialWebLookup = table.Column<bool>(type: "boolean", nullable: false),
                    MaxToolRounds = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: false)
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
