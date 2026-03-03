using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SetRemainingFortniteProductImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Set ImageUrl for remaining Fortnite V-Bucks products (17 products not covered by previous migration)
            migrationBuilder.Sql(@"
                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/300-v-bucks.webp'
                WHERE ""Amount"" = '300' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/800-v-bucks.webp'
                WHERE ""Amount"" = '800' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/1200-v-bucks.webp'
                WHERE ""Amount"" = '1200' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/1300-v-bucks.webp'
                WHERE ""Amount"" = '1300' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/1500-v-bucks.webp'
                WHERE ""Amount"" = '1500' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/1600-v-bucks.webp'
                WHERE ""Amount"" = '1600' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/1800-v-bucks.webp'
                WHERE ""Amount"" = '1800' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/1900-v-bucks.webp'
                WHERE ""Amount"" = '1900' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/2000-v-bucks.webp'
                WHERE ""Amount"" = '2000' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/2100-v-bucks.webp'
                WHERE ""Amount"" = '2100' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/2400-v-bucks.webp'
                WHERE ""Amount"" = '2400' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/2600-v-bucks.webp'
                WHERE ""Amount"" = '2600' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/3800-v-bucks.webp'
                WHERE ""Amount"" = '3800' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/5600-v-bucks.webp'
                WHERE ""Amount"" = '5600' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/6000-v-bucks.webp'
                WHERE ""Amount"" = '6000' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/7800-v-bucks.webp'
                WHERE ""Amount"" = '7800' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/10000-v-bucks.webp'
                WHERE ""Amount"" = '10000' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
