using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClearOldProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"Products\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
