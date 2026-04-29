using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "OrderItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryPayloadJson",
                table: "OrderItems",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryStatus",
                table: "OrderItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_DeliveryStatus",
                table: "OrderItems",
                column: "DeliveryStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderItems_DeliveryStatus",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "DeliveryPayloadJson",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                table: "OrderItems");
        }
    }
}
