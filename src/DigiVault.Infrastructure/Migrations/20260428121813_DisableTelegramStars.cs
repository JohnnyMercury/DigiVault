using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DisableTelegramStars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Soft-deactivate the Telegram Stars gift card and its sub-products,
            // and purge all reviews tied to it. Telegram Premium remains active.
            migrationBuilder.Sql(@"
                DELETE FROM ""ProductReviews""
                WHERE ""GiftCardId"" IN (SELECT ""Id"" FROM ""GiftCards"" WHERE ""Slug"" = 'telegram-stars');

                UPDATE ""GameProducts""
                SET ""IsActive"" = false
                WHERE ""GiftCardId"" IN (SELECT ""Id"" FROM ""GiftCards"" WHERE ""Slug"" = 'telegram-stars');

                UPDATE ""GiftCards""
                SET ""IsActive"" = false
                WHERE ""Slug"" = 'telegram-stars';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-enable the GiftCard + its products. Reviews are not restored (they were deleted).
            migrationBuilder.Sql(@"
                UPDATE ""GiftCards""
                SET ""IsActive"" = true
                WHERE ""Slug"" = 'telegram-stars';

                UPDATE ""GameProducts""
                SET ""IsActive"" = true
                WHERE ""GiftCardId"" IN (SELECT ""Id"" FROM ""GiftCards"" WHERE ""Slug"" = 'telegram-stars');
            ");
        }
    }
}
