using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SsSport.Drive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "progress_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    message_id = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_progress_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_progress_messages_player_id",
                table: "progress_messages",
                column: "player_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "progress_messages");
        }
    }
}
