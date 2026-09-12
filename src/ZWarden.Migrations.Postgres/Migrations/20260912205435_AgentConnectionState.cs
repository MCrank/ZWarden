using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZWarden.Migrations.Postgres.Migrations
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
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Disconnected");

            migrationBuilder.AddColumn<int>(
                name: "LastProtocolVersion",
                table: "Agents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAt",
                table: "Agents",
                type: "timestamp with time zone",
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
