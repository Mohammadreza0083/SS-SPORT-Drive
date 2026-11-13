using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SsSport.Drive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIngestImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "card_key",
                table: "players",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "photo_cutout_key",
                table: "players",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "poster_key",
                table: "assets",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sprite_key",
                table: "assets",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_key",
                table: "assets",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "card_key",
                table: "players");

            migrationBuilder.DropColumn(
                name: "photo_cutout_key",
                table: "players");

            migrationBuilder.DropColumn(
                name: "poster_key",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "sprite_key",
                table: "assets");

            migrationBuilder.DropColumn(
                name: "thumbnail_key",
                table: "assets");
        }
    }
}
