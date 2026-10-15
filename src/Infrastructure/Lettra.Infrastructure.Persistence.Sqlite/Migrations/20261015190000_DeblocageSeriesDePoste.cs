using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class DeblocageSeriesDePoste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncSeriesReleases",
                columns: table => new
                {
                    Number = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SeriesCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleasedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleasedAtMs = table.Column<long>(type: "INTEGER", nullable: false),
                    ConsumedAtMs = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncSeriesReleases", x => x.Number);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncSeriesReleases_DeviceId",
                table: "SyncSeriesReleases",
                column: "DeviceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncSeriesReleases");
        }
    }
}
