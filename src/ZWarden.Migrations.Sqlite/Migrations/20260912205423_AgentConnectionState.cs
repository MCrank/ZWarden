using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZWarden.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AgentConnectionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConnectionState",
                table: "Agents",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Disconnected");

            migrationBuilder.AddColumn<int>(
                name: "LastProtocolVersion",
                table: "Agents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAt",
                table: "Agents",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectionState",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LastProtocolVersion",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "Agents");
        }
    }
}
