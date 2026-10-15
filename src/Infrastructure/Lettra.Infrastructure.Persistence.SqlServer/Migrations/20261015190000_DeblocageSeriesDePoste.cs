using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.Migrations
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
                    Number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SeriesCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleasedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleasedAtMs = table.Column<long>(type: "bigint", nullable: false),
                    ConsumedAtMs = table.Column<long>(type: "bigint", nullable: true)
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
