using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseSystemRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductKeys_GameProducts_GameProductId",
                table: "ProductKeys");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductKeys_Products_ProductId",
                table: "ProductKeys");

            migrationBuilder.DropIndex(
                name: "IX_ProductKeys_ProductId",
                table: "ProductKeys");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "ProductKeys");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "OrderItems",
                newName: "GameProductId");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                newName: "IX_OrderItems_GameProductId");

            migrationBuilder.AlterColumn<int>(
                name: "GameProductId",
                table: "ProductKeys",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryInfo",
                table: "Orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_GameProducts_GameProductId",
                table: "OrderItems",
                column: "GameProductId",
                principalTable: "GameProducts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductKeys_GameProducts_GameProductId",
                table: "ProductKeys",
                column: "GameProductId",
                principalTable: "GameProducts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_GameProducts_GameProductId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductKeys_GameProducts_GameProductId",
                table: "ProductKeys");

            migrationBuilder.DropColumn(
                name: "DeliveryInfo",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "GameProductId",
                table: "OrderItems",
                newName: "ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_GameProductId",
                table: "OrderItems",
                newName: "IX_OrderItems_ProductId");

            migrationBuilder.AlterColumn<int>(
                name: "GameProductId",
                table: "ProductKeys",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "ProductKeys",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ProductKeys_ProductId",
                table: "ProductKeys",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Products_ProductId",
                table: "OrderItems",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductKeys_GameProducts_GameProductId",
                table: "ProductKeys",
                column: "GameProductId",
                principalTable: "GameProducts",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductKeys_Products_ProductId",
                table: "ProductKeys",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
