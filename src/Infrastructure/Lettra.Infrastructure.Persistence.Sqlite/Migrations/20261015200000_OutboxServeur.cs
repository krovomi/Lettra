using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class OutboxServeur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    EnqueuedAtMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtMs = table.Column<long>(type: "INTEGER", nullable: false),
                    LeaseUntilMs = table.Column<long>(type: "INTEGER", nullable: true),
                    LeaseOwner = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeliveredAtMs = table.Column<long>(type: "INTEGER", nullable: true),
                    ParkedAtMs = table.Column<long>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_DeliveredAtMs_ParkedAtMs_NextAttemptAtMs",
                table: "OutboxMessages",
                columns: new[] { "DeliveredAtMs", "ParkedAtMs", "NextAttemptAtMs" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_IdempotencyKey",
                table: "OutboxMessages",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");
        }
    }
}
