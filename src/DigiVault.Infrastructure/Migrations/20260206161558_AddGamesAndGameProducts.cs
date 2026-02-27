using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGamesAndGameProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GameProductId",
                table: "ProductKeys",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Currency = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CurrencyShort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IconUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Gradient = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Bonus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TotalDisplay = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OldPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Discount = table.Column<int>(type: "integer", nullable: false),
                    ProductType = table.Column<int>(type: "integer", nullable: false),
                    Multiplier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Region = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    StockQuantity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameProducts_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductKeys_GameProductId",
                table: "ProductKeys",
                column: "GameProductId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProducts_GameId",
                table: "GameProducts",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameProducts_IsActive",
                table: "GameProducts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_GameProducts_ProductType",
                table: "GameProducts",
                column: "ProductType");

            migrationBuilder.CreateIndex(
                name: "IX_GameProducts_SortOrder",
                table: "GameProducts",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_Games_IsActive",
                table: "Games",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Games_Slug",
                table: "Games",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Games_SortOrder",
                table: "Games",
                column: "SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductKeys_GameProducts_GameProductId",
                table: "ProductKeys",
                column: "GameProductId",
                principalTable: "GameProducts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductKeys_GameProducts_GameProductId",
                table: "ProductKeys");

            migrationBuilder.DropTable(
                name: "GameProducts");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropIndex(
                name: "IX_ProductKeys_GameProductId",
                table: "ProductKeys");

            migrationBuilder.DropColumn(
                name: "GameProductId",
                table: "ProductKeys");
        }
    }
}
