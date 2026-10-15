using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class SeriesDePoste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceSeries",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BaseSeries = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAtMs = table.Column<long>(type: "bigint", nullable: false),
                    ClosedAtMs = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSeries", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSeries_DeviceId_BaseSeries",
                table: "DeviceSeries",
                columns: new[] { "DeviceId", "BaseSeries" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceSeries");
        }
    }
}
