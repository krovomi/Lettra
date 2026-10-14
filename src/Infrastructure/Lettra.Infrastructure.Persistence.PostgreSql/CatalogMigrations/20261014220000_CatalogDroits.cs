using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lettra.Infrastructure.Persistence.PostgreSql.CatalogMigrations
{
    /// <inheritdoc />
    public partial class CatalogDroits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomRoles",
                schema: "catalog",
                columns: table => new
                {
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WidestScope = table.Column<int>(type: "integer", nullable: false),
                    RequiresExpiry = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomRoles", x => new { x.OrganisationId, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "Groups",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoleAssignments",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectKind = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScopeKind = table.Column<int>(type: "integer", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomRolePermissions",
                schema: "catalog",
                columns: table => new
                {
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Permission = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomRolePermissions", x => new { x.OrganisationId, x.RoleKey, x.Permission });
                    table.ForeignKey(
                        name: "FK_CustomRolePermissions_CustomRoles_OrganisationId_RoleKey",
                        columns: x => new { x.OrganisationId, x.RoleKey },
                        principalSchema: "catalog",
                        principalTable: "CustomRoles",
                        principalColumns: new[] { "OrganisationId", "Key" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupDossiers",
                schema: "catalog",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    DossierId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupDossiers", x => new { x.GroupId, x.DossierId });
                    table.ForeignKey(
                        name: "FK_GroupDossiers_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "catalog",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupMembers",
                schema: "catalog",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembers", x => new { x.GroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_GroupMembers_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "catalog",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupDossiers_DossierId",
                schema: "catalog",
                table: "GroupDossiers",
                column: "DossierId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_UserId",
                schema: "catalog",
                table: "GroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_OrganisationId",
                schema: "catalog",
                table: "Groups",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleAssignments_SubjectKind_SubjectId",
                schema: "catalog",
                table: "RoleAssignments",
                columns: new[] { "SubjectKind", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_OrganisationId",
                schema: "catalog",
                table: "UserAccounts",
                column: "OrganisationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomRolePermissions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "GroupDossiers",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "GroupMembers",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "RoleAssignments",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "UserAccounts",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "CustomRoles",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Groups",
                schema: "catalog");
        }
    }
}
