using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZWarden.Migrations.Postgres.Migrations
{
    /// <summary>
    /// A data-only migration (#229): grant the new <c>Server.Recreate</c> permission to an existing install's built-in
    /// <b>Tenant Owner</b> and <b>Administrator</b> roles. Built-in roles are seeded once and their grants persisted (and
    /// editable), so a catalogue addition never reaches roles seeded before it; a fresh install gets the grant from the
    /// seeder instead. It runs exactly once, so a later deliberate revoke by an administrator is respected. Portable SQL
    /// (quoted identifiers) — identical on SQLite and PostgreSQL.
    /// </summary>
    public partial class GrantServerRecreateToBuiltInRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO "RolePermissionGrants" ("RoleId", "PermissionName")
                SELECT r."Id", 'Server.Recreate'
                FROM "Roles" r
                WHERE r."BuiltIn" IN ('TenantOwner', 'Administrator')
                  AND NOT EXISTS (
                      SELECT 1 FROM "RolePermissionGrants" g
                      WHERE g."RoleId" = r."Id" AND g."PermissionName" = 'Server.Recreate');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "RolePermissionGrants"
                WHERE "PermissionName" = 'Server.Recreate'
                  AND "RoleId" IN (SELECT "Id" FROM "Roles" WHERE "BuiltIn" IN ('TenantOwner', 'Administrator'));
                """);
        }
    }
}
