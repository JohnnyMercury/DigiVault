using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SetMobileLegendProductImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Set ImageUrl for all Mobile Legends products
            // Images are in /images/products/mobilelegends/ and will be served via MinIO proxy
            migrationBuilder.Sql(@"
                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/minio/digivault-images/products/mobilelegends/35-diamonds.webp'
                WHERE ""Name"" = '35 Diamonds' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'mobilelegends');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/minio/digivault-images/products/mobilelegends/55-diamonds.webp'
                WHERE ""Name"" = '55 Diamonds' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'mobilelegends');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/minio/digivault-images/products/mobilelegends/165-diamonds.webp'
                WHERE ""Name"" = '165 Diamonds' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'mobilelegends');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/minio/digivault-images/products/mobilelegends/275-diamonds.webp'
                WHERE ""Name"" = '275 Diamonds' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'mobilelegends');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/minio/digivault-images/products/mobilelegends/565-diamonds.webp'
                WHERE ""Name"" = '565 Diamonds' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'mobilelegends');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/minio/digivault-images/products/mobilelegends/1155-diamonds.webp'
                WHERE ""Name"" = '1155 Diamonds' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'mobilelegends');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/minio/digivault-images/products/mobilelegends/1765-diamonds.webp'
                WHERE ""Name"" = '1765 Diamonds' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'mobilelegends');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/minio/digivault-images/products/mobilelegends/2975-diamonds.webp'
                WHERE ""Name"" = '2975 Diamonds' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'mobilelegends');

                UPDATE ""GameProducts"" SET ""ImageUrl"" = '/minio/digivault-images/products/mobilelegends/6000-diamonds.webp'
                WHERE ""Name"" = '6000 Diamonds' AND ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'mobilelegends');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""GameProducts"" SET ""ImageUrl"" = NULL
                WHERE ""GameId"" IN (SELECT ""Id"" FROM ""Games"" WHERE ""Slug"" = 'mobilelegends');
            ");
        }
    }
}
