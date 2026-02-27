using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGiftCardProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GiftCardId",
                table: "GameProducts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GameProducts_GiftCardId",
                table: "GameProducts",
                column: "GiftCardId");

            migrationBuilder.AddForeignKey(
                name: "FK_GameProducts_GiftCards_GiftCardId",
                table: "GameProducts",
                column: "GiftCardId",
                principalTable: "GiftCards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GameProducts_GiftCards_GiftCardId",
                table: "GameProducts");

            migrationBuilder.DropIndex(
                name: "IX_GameProducts_GiftCardId",
                table: "GameProducts");

            migrationBuilder.DropColumn(
                name: "GiftCardId",
                table: "GameProducts");
        }
    }
}
