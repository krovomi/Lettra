using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class DecisionsAPrendre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DecisionPolicy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    RefreshedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionPolicy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Audience = table.Column<int>(type: "INTEGER", nullable: false),
                    Urgency = table.Column<int>(type: "INTEGER", nullable: false),
                    Impact = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    DueOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Facts = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Link = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ActionKind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ActionPayload = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RuleVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SnoozedUntil = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ProposedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DecidedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Decisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_Kind_Subject",
                table: "Decisions",
                columns: new[] { "Kind", "Subject" });

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_Status",
                table: "Decisions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DecisionPolicy");

            migrationBuilder.DropTable(
                name: "Decisions");
        }
    }
}
