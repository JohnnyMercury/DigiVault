using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiVault.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHeroBannerLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Layout",
                table: "HeroBanners",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Layout",
                table: "HeroBanners");
        }
    }
}
