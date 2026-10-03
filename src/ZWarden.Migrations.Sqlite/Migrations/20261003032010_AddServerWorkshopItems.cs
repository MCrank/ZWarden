using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZWarden.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddServerWorkshopItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerModStates",
                columns: table => new
                {
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConfiguredWorkshopIds = table.Column<string>(type: "TEXT", nullable: false),
                    ConfiguredModIds = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigObservedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BootedWorkshopIds = table.Column<string>(type: "TEXT", nullable: false),
                    BootedModIds = table.Column<string>(type: "TEXT", nullable: false),
                    BootedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BootSnapshotAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BootSnapshotPending = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerModStates", x => x.ServerId);
                });

            migrationBuilder.CreateTable(
                name: "ServerWorkshopItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkshopId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    PreviewUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    SteamUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    MetadataRefreshedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    GuessedModIds = table.Column<string>(type: "TEXT", nullable: false),
                    ObservedModIds = table.Column<string>(type: "TEXT", nullable: false),
                    OnDisk = table.Column<bool>(type: "INTEGER", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerWorkshopItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerModStates_TenantId",
                table: "ServerModStates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerWorkshopItems_TenantId_ServerId_WorkshopId",
                table: "ServerWorkshopItems",
                columns: new[] { "TenantId", "ServerId", "WorkshopId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerModStates");

            migrationBuilder.DropTable(
                name: "ServerWorkshopItems");
        }
    }
}
