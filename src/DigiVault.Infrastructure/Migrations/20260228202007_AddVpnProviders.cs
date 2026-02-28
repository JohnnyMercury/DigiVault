using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVpnProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VpnProviderId",
                table: "GameProducts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VpnProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Tagline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Features = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Gradient = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VpnProviders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameProducts_VpnProviderId",
                table: "GameProducts",
                column: "VpnProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_VpnProviders_IsActive",
                table: "VpnProviders",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_VpnProviders_Slug",
                table: "VpnProviders",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VpnProviders_SortOrder",
                table: "VpnProviders",
                column: "SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_GameProducts_VpnProviders_VpnProviderId",
                table: "GameProducts",
                column: "VpnProviderId",
                principalTable: "VpnProviders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GameProducts_VpnProviders_VpnProviderId",
                table: "GameProducts");

            migrationBuilder.DropTable(
                name: "VpnProviders");

            migrationBuilder.DropIndex(
                name: "IX_GameProducts_VpnProviderId",
                table: "GameProducts");

            migrationBuilder.DropColumn(
                name: "VpnProviderId",
                table: "GameProducts");
        }
    }
}
