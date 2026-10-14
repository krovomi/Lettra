using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.CatalogMigrations
{
    /// <inheritdoc />
    public partial class CatalogInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "Clusters",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CellId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    MaxDatabases = table.Column<int>(type: "integer", nullable: false),
                    AcceptsNewDossiers = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clusters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organisations",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CellId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organisations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dossiers",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ConnectionSecretRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ClusterId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DatabaseName = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    Schema = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    WrappedDek = table.Column<byte[]>(type: "bytea", maxLength: 60, nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dossiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dossiers_Clusters_ClusterId",
                        column: x => x.ClusterId,
                        principalSchema: "catalog",
                        principalTable: "Clusters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Dossiers_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalSchema: "catalog",
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_ClusterId_DatabaseName",
                schema: "catalog",
                table: "Dossiers",
                columns: new[] { "ClusterId", "DatabaseName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_OrganisationId",
                schema: "catalog",
                table: "Dossiers",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_State_SchemaVersion",
                schema: "catalog",
                table: "Dossiers",
                columns: new[] { "State", "SchemaVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Dossiers",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Clusters",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Organisations",
                schema: "catalog");
        }
    }
}
