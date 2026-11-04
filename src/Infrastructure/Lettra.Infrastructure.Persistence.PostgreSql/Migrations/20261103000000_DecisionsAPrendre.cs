using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.Migrations
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    RefreshedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionPolicy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Subject = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Audience = table.Column<int>(type: "integer", nullable: false),
                    Urgency = table.Column<int>(type: "integer", nullable: false),
                    Impact = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Facts = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Link = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ActionKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActionPayload = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RuleVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SnoozedUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    ProposedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<long>(type: "bigint", nullable: true),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
