using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.CatalogMigrations
{
    /// <inheritdoc />
    public partial class CatalogInitial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clusters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CellId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDatabases = table.Column<int>(type: "INTEGER", nullable: false),
                    AcceptsNewDossiers = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clusters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organisations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CellId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organisations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dossiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    ConnectionSecretRef = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ClusterId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DatabaseName = table.Column<string>(type: "TEXT", maxLength: 63, nullable: true),
                    Schema = table.Column<string>(type: "TEXT", maxLength: 63, nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    WrappedDek = table.Column<byte[]>(type: "BLOB", maxLength: 60, nullable: false),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dossiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dossiers_Clusters_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "Clusters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Dossiers_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_ClusterId_DatabaseName",
                table: "Dossiers",
                columns: new[] { "ClusterId", "DatabaseName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_OrganisationId",
                table: "Dossiers",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Dossiers_State_SchemaVersion",
                table: "Dossiers",
                columns: new[] { "State", "SchemaVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Dossiers");

            migrationBuilder.DropTable(
                name: "Clusters");

            migrationBuilder.DropTable(
                name: "Organisations");
        }
    }
}
