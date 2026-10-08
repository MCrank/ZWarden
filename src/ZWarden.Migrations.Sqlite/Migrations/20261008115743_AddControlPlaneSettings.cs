using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZWarden.Migrations.Sqlite.Migrations
{
    /// <summary>
    /// #345 (ADR 0048): the operator-edited <c>ControlPlaneSettings</c> row (one per tenant, null columns fall back to
    /// config), and a one-time grant of the new <c>Tenant.Settings.Manage</c> permission to an existing install's built-in
    /// <b>Tenant Owner</b> and <b>Administrator</b> roles (a fresh install gets it from the seeder), as #271 did for
    /// <c>Server.Delete</c>. Portable SQL (quoted identifiers) — identical on SQLite and PostgreSQL.
    /// </summary>
    public partial class AddControlPlaneSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ControlPlaneSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Version = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlPlaneSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ControlPlaneSettings_TenantId",
                table: "ControlPlaneSettings",
                column: "TenantId",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO "RolePermissionGrants" ("RoleId", "PermissionName")
                SELECT r."Id", 'Tenant.Settings.Manage'
                FROM "Roles" r
                WHERE r."BuiltIn" IN ('TenantOwner', 'Administrator')
                  AND NOT EXISTS (
                      SELECT 1 FROM "RolePermissionGrants" g
                      WHERE g."RoleId" = r."Id" AND g."PermissionName" = 'Tenant.Settings.Manage');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "RolePermissionGrants"
                WHERE "PermissionName" = 'Tenant.Settings.Manage'
                  AND "RoleId" IN (SELECT "Id" FROM "Roles" WHERE "BuiltIn" IN ('TenantOwner', 'Administrator'));
                """);

            migrationBuilder.DropTable(
                name: "ControlPlaneSettings");
        }
    }
}
