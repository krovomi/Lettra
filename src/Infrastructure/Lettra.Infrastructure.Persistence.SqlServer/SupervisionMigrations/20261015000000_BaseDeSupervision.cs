using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.SqlServer.SupervisionMigrations
{
    /// <inheritdoc />
    public partial class BaseDeSupervision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "supervision");

            migrationBuilder.CreateTable(
                name: "Alerts",
                schema: "supervision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RuleId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    ScopeLevel = table.Column<int>(type: "int", nullable: false),
                    CabinetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DossierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastSeen = table.Column<long>(type: "bigint", nullable: false),
                    Snapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_CabinetId_State",
                schema: "supervision",
                table: "Alerts",
                columns: new[] { "CabinetId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_DossierId_State",
                schema: "supervision",
                table: "Alerts",
                columns: new[] { "DossierId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_Fingerprint",
                schema: "supervision",
                table: "Alerts",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_GroupId_State",
                schema: "supervision",
                table: "Alerts",
                columns: new[] { "GroupId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_State",
                schema: "supervision",
                table: "Alerts",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts",
                schema: "supervision");
        }
    }
}
