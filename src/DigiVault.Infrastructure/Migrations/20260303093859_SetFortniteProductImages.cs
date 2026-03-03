using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SetFortniteProductImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Set ImageUrl for Fortnite V-Bucks products by matching Amount field
            // GameProducts joined with Games where Games.Slug = 'fortnite'
            migrationBuilder.Sql(@"
                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/200-v-buck.webp'
                WHERE ""Amount"" = '200' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/500-v-bucks.webp'
                WHERE ""Amount"" = '500' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/1000.webp'
                WHERE ""Amount"" = '1000' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/2800.webp'
                WHERE ""Amount"" = '2800' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/5000.webp'
                WHERE ""Amount"" = '5000' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/images/products/fortnite/13500.webp'
                WHERE ""Amount"" = '13500' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'fortnite');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
