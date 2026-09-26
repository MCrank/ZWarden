using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZWarden.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddServerHeapSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "HeapSizeBytes",
                table: "Servers",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeapSizeBytes",
                table: "Servers");
        }
    }
}
