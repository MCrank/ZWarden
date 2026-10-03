using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZWarden.Migrations.Postgres.Migrations
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
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfiguredWorkshopIds = table.Column<string[]>(type: "text[]", nullable: false),
                    ConfiguredModIds = table.Column<string[]>(type: "text[]", nullable: false),
                    ConfigObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BootedWorkshopIds = table.Column<string[]>(type: "text[]", nullable: false),
                    BootedModIds = table.Column<string[]>(type: "text[]", nullable: false),
                    BootedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BootSnapshotAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BootSnapshotPending = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerModStates", x => x.ServerId);
                });

            migrationBuilder.CreateTable(
                name: "ServerWorkshopItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkshopId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PreviewUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    SteamUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    MetadataRefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GuessedModIds = table.Column<string[]>(type: "text[]", nullable: false),
                    ObservedModIds = table.Column<string[]>(type: "text[]", nullable: false),
                    OnDisk = table.Column<bool>(type: "boolean", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
