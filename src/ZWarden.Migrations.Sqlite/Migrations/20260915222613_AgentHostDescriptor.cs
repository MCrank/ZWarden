using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZWarden.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AgentHostDescriptor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentVersion",
                table: "Agents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hostname",
                table: "Agents",
                type: "TEXT",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsPlatform",
                table: "Agents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentVersion",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "Hostname",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "OsPlatform",
                table: "Agents");
        }
    }
}
