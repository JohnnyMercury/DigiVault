using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AiServiceId",
                table: "GameProducts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiServices",
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
                    table.PrimaryKey("PK_AiServices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameProducts_AiServiceId",
                table: "GameProducts",
                column: "AiServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_AiServices_IsActive",
                table: "AiServices",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AiServices_Slug",
                table: "AiServices",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiServices_SortOrder",
                table: "AiServices",
                column: "SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_GameProducts_AiServices_AiServiceId",
                table: "GameProducts",
                column: "AiServiceId",
                principalTable: "AiServices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GameProducts_AiServices_AiServiceId",
                table: "GameProducts");

            migrationBuilder.DropTable(
                name: "AiServices");

            migrationBuilder.DropIndex(
                name: "IX_GameProducts_AiServiceId",
                table: "GameProducts");

            migrationBuilder.DropColumn(
                name: "AiServiceId",
                table: "GameProducts");
        }
    }
}
