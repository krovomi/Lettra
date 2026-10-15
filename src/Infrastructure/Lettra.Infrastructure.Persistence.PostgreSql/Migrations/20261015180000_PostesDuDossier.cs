using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class PostesDuDossier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncDevices",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstSeenAtMs = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenAtMs = table.Column<long>(type: "bigint", nullable: false),
                    LastUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAtMs = table.Column<long>(type: "bigint", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    WipedAtMs = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncDevices", x => x.DeviceId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncDevices");
        }
    }
}
