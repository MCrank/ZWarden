using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZWarden.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddServerInstalledBuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstalledBuildId",
                table: "Servers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InstalledBuildReportedAt",
                table: "Servers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstalledBuildId",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "InstalledBuildReportedAt",
                table: "Servers");
        }
    }
}
