using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSentContactsToPaymentTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SentEmail",
                table: "PaymentTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SentPhone",
                table: "PaymentTransactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SentName",
                table: "PaymentTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SentUserId",
                table: "PaymentTransactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SentIp",
                table: "PaymentTransactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasAnonymized",
                table: "PaymentTransactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SentEmail", table: "PaymentTransactions");
            migrationBuilder.DropColumn(name: "SentPhone", table: "PaymentTransactions");
            migrationBuilder.DropColumn(name: "SentName", table: "PaymentTransactions");
            migrationBuilder.DropColumn(name: "SentUserId", table: "PaymentTransactions");
            migrationBuilder.DropColumn(name: "SentIp", table: "PaymentTransactions");
            migrationBuilder.DropColumn(name: "WasAnonymized", table: "PaymentTransactions");
        }
    }
}
