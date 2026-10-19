using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RevueDeDossier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReviewMarks",
                columns: table => new
                {
                    Period = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CheckedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewMarks", x => new { x.Period, x.ItemKey });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewMarks");
        }
    }
}
