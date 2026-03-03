using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MigrateAllImagesToMinio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Migrate all image URLs from static /images/ paths to MinIO /minio/digivault-images/ paths
            // Transformation: /images/{path} → /minio/digivault-images/{path}
            // Only affects rows with /images/ prefix (not already /minio/ or null)

            migrationBuilder.Sql(@"
                UPDATE ""GameProducts""
                SET ""ImageUrl"" = '/minio/digivault-images' || SUBSTRING(""ImageUrl"" FROM 8)
                WHERE ""ImageUrl"" LIKE '/images/%';

                UPDATE ""GiftCards""
                SET ""ImageUrl"" = '/minio/digivault-images' || SUBSTRING(""ImageUrl"" FROM 8)
                WHERE ""ImageUrl"" LIKE '/images/%';

                UPDATE ""Games""
                SET ""ImageUrl"" = '/minio/digivault-images' || SUBSTRING(""ImageUrl"" FROM 8)
                WHERE ""ImageUrl"" LIKE '/images/%';

                UPDATE ""Games""
                SET ""IconUrl"" = '/minio/digivault-images' || SUBSTRING(""IconUrl"" FROM 8)
                WHERE ""IconUrl"" LIKE '/images/%';

                UPDATE ""HeroBanners""
                SET ""ImageUrl"" = '/minio/digivault-images' || SUBSTRING(""ImageUrl"" FROM 8)
                WHERE ""ImageUrl"" LIKE '/images/%';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert: /minio/digivault-images/{path} → /images/{path}
            migrationBuilder.Sql(@"
                UPDATE ""GameProducts""
                SET ""ImageUrl"" = '/images' || SUBSTRING(""ImageUrl"" FROM 23)
                WHERE ""ImageUrl"" LIKE '/minio/digivault-images/%';

                UPDATE ""GiftCards""
                SET ""ImageUrl"" = '/images' || SUBSTRING(""ImageUrl"" FROM 23)
                WHERE ""ImageUrl"" LIKE '/minio/digivault-images/%';

                UPDATE ""Games""
                SET ""ImageUrl"" = '/images' || SUBSTRING(""ImageUrl"" FROM 23)
                WHERE ""ImageUrl"" LIKE '/images/%';

                UPDATE ""Games""
                SET ""IconUrl"" = '/images' || SUBSTRING(""IconUrl"" FROM 23)
                WHERE ""IconUrl"" LIKE '/images/%';

                UPDATE ""HeroBanners""
                SET ""ImageUrl"" = '/images' || SUBSTRING(""ImageUrl"" FROM 23)
                WHERE ""ImageUrl"" LIKE '/minio/digivault-images/%';
            ");
        }
    }
}
