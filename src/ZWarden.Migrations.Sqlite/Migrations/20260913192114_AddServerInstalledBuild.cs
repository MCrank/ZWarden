using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZWarden.Migrations.Sqlite.Migrations
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
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InstalledBuildReportedAt",
                table: "Servers",
                type: "TEXT",
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
