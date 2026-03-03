using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SetRobloxProductImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Set ImageUrl for all Roblox products by matching Amount
            migrationBuilder.Sql(@"
                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/minio/digivault-images/products/roblox/' || ""Amount"" || '-robux.webp'
                WHERE ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'roblox')
                AND ""ImageUrl"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""GameProducts"" SET ""ImageUrl"" = NULL
                WHERE ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'roblox');
            ");
        }
    }
}
