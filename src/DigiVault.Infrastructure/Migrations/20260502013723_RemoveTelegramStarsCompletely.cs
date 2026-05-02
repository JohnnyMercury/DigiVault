using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTelegramStarsCompletely : Migration
    {
        /// <summary>
        /// SB request: страница, slug и любые публичные отзывы по
        /// «telegram-stars» должны исчезнуть с сайта, в т.ч. из индекса.
        /// Раньше DisableTelegramStars только перевела IsActive=false,
        /// но запись осталась и /Reviews?product=telegram-stars
        /// продолжал отдавать страницу с историческими отзывами.
        ///
        /// Здесь мы:
        ///   1. удаляем все отзывы, привязанные к этой GiftCard;
        ///   2. удаляем GameProducts, у которых нет ссылок из OrderItems
        ///      (исторические заказы остаются цельными - продукт остаётся
        ///      IsActive=false из прошлой миграции);
        ///   3. удаляем саму GiftCard, если на неё уже не ссылается ни
        ///      один GameProduct.
        ///
        /// Условные DELETE с NOT IN гарантируют, что мы не сломаем
        /// foreign keys у исторических заказов.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- 1. wipe all reviews tied to telegram-stars
                DELETE FROM ""ProductReviews""
                WHERE ""GiftCardId"" IN (
                    SELECT ""Id"" FROM ""GiftCards"" WHERE ""Slug"" = 'telegram-stars'
                );

                -- 2. delete sub-products that no historical OrderItem points to
                DELETE FROM ""GameProducts""
                WHERE ""GiftCardId"" IN (
                    SELECT ""Id"" FROM ""GiftCards"" WHERE ""Slug"" = 'telegram-stars'
                )
                AND ""Id"" NOT IN (
                    SELECT ""GameProductId"" FROM ""OrderItems"" WHERE ""GameProductId"" IS NOT NULL
                );

                -- 3. delete the GiftCard row itself if no GameProduct still
                -- references it (i.e. there were no historical orders)
                DELETE FROM ""GiftCards""
                WHERE ""Slug"" = 'telegram-stars'
                AND ""Id"" NOT IN (
                    SELECT ""GiftCardId"" FROM ""GameProducts"" WHERE ""GiftCardId"" IS NOT NULL
                );

                -- 4. drop legacy AppSettings keys that were used to configure
                -- the Stars slider (no longer rendered anywhere).
                DELETE FROM ""AppSettings""
                WHERE ""Key"" IN ('telegram:star_rate', 'telegram:min_stars', 'telegram:max_stars');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op. Restoring deleted reviews / products from the live DB is
            // not feasible; rerunning DbSeeder on a fresh environment will
            // recreate the (Premium-only) catalog.
        }
    }
}
