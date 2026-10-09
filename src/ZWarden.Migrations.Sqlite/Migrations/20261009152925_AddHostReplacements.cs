using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZWarden.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddHostReplacements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HostReplacements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SuccessorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PredecessorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReplacedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReplacedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostReplacements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HostReplacements_PredecessorId",
                table: "HostReplacements",
                column: "PredecessorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostReplacements_SuccessorId",
                table: "HostReplacements",
                column: "SuccessorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostReplacements");
        }
    }
}
