using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SsSport.Drive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    folder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    drive_file_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    original_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    md5 = table.Column<string>(type: "character(32)", fixedLength: true, maxLength: 32, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title_is_custom = table.Column<bool>(type: "boolean", nullable: false),
                    tags = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    visibility = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    removed_from_source_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    original_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: true),
                    page_count = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    target_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    data = table.Column<string>(type: "json", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "drive_folders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    drive_folder_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    access = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    access_lost_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drive_folders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "players",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    nationality = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    positions = table.Column<string[]>(type: "text[]", nullable: true),
                    volleybox_player_id = table.Column<int>(type: "integer", nullable: true),
                    volleybox_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    photo_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    analyst_id = table.Column<Guid>(type: "uuid", nullable: true),
                    qr_code_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    hero_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_players", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_id = table.Column<long>(type: "bigint", nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "renditions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quality = table.Column<int>(type: "integer", nullable: false),
                    playlist_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renditions", x => x.id);
                    table.ForeignKey(
                        name: "fk_renditions_assets_asset_id",
                        column: x => x.asset_id,
                        principalTable: "assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assets_drive_file_id",
                table: "assets",
                column: "drive_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_assets_folder_id",
                table: "assets",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_assets_md5",
                table: "assets",
                column: "md5");

            migrationBuilder.CreateIndex(
                name: "ix_assets_player_id_sort_order",
                table: "assets",
                columns: new[] { "player_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_assets_state",
                table: "assets",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_actor_user_id",
                table: "audit_entries",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_at",
                table: "audit_entries",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_target_type_target_id",
                table: "audit_entries",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_drive_folders_drive_folder_id",
                table: "drive_folders",
                column: "drive_folder_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_drive_folders_player_id",
                table: "drive_folders",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_players_analyst_id",
                table: "players",
                column: "analyst_id");

            migrationBuilder.CreateIndex(
                name: "ix_players_slug",
                table: "players",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_players_status",
                table: "players",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_players_volleybox_player_id",
                table: "players",
                column: "volleybox_player_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_renditions_asset_id_quality",
                table: "renditions",
                columns: new[] { "asset_id", "quality" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_status",
                table: "users",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_users_telegram_id",
                table: "users",
                column: "telegram_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "drive_folders");

            migrationBuilder.DropTable(
                name: "players");

            migrationBuilder.DropTable(
                name: "renditions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "assets");
        }
    }
}
