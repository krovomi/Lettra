using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.SupervisionMigrations
{
    /// <inheritdoc />
    public partial class BaseDeSupervision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    RuleId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ScopeLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    CabinetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DossierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastSeen = table.Column<long>(type: "INTEGER", nullable: false),
                    Snapshot = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_CabinetId_State",
                table: "Alerts",
                columns: new[] { "CabinetId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_DossierId_State",
                table: "Alerts",
                columns: new[] { "DossierId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Fingerprint",
                table: "Alerts",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_GroupId_State",
                table: "Alerts",
                columns: new[] { "GroupId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_State",
                table: "Alerts",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");
        }
    }
}
