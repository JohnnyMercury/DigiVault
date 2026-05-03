using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderVisibilityAndMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default: all methods enabled, provider visible. Existing rows
            // (enot / paymentlink / overpay) keep their full surface area —
            // admin can narrow it later via the edit form.
            migrationBuilder.AddColumn<string>(
                name: "EnabledMethods",
                table: "PaymentProviderConfigs",
                type: "text",
                nullable: false,
                defaultValue: "card,sbp,qr,p2p");

            migrationBuilder.AddColumn<bool>(
                name: "IsVisible",
                table: "PaymentProviderConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnabledMethods",
                table: "PaymentProviderConfigs");

            migrationBuilder.DropColumn(
                name: "IsVisible",
                table: "PaymentProviderConfigs");
        }
    }
}
