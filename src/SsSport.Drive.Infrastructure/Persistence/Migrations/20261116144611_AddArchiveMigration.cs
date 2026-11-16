using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SsSport.Drive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "players",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Team");

            migrationBuilder.CreateTable(
                name: "archive_scans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_by = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    bytes = table.Column<long>(type: "bigint", nullable: false),
                    files = table.Column<int>(type: "integer", nullable: false),
                    folders = table.Column<int>(type: "integer", nullable: false),
                    in_backup = table.Column<int>(type: "integer", nullable: false),
                    missing_from_backup = table.Column<int>(type: "integer", nullable: false),
                    skipped = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_scans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "archive_folders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    drive_folder_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    path = table.Column<string>(type: "character varying(1100)", maxLength: 1100, nullable: false),
                    folder_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    category = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    found_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    suggested_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    suggested_positions = table.Column<string[]>(type: "text[]", nullable: true),
                    state = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    bytes = table.Column<long>(type: "bigint", nullable: false),
                    documents = table.Column<int>(type: "integer", nullable: false),
                    in_backup = table.Column<int>(type: "integer", nullable: false),
                    is_truncated = table.Column<bool>(type: "boolean", nullable: false),
                    missing_from_backup = table.Column<int>(type: "integer", nullable: false),
                    scouts = table.Column<int>(type: "integer", nullable: false),
                    skipped = table.Column<int>(type: "integer", nullable: false),
                    videos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archive_folders", x => x.id);
                    table.ForeignKey(
                        name: "fk_archive_folders_archive_scans_scan_id",
                        column: x => x.scan_id,
                        principalTable: "archive_scans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_archive_folders_scan_id_drive_folder_id",
                table: "archive_folders",
                columns: new[] { "scan_id", "drive_folder_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_archive_folders_scan_id_state_path",
                table: "archive_folders",
                columns: new[] { "scan_id", "state", "path" });

            migrationBuilder.CreateIndex(
                name: "ix_archive_scans_started_at",
                table: "archive_scans",
                column: "started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archive_folders");

            migrationBuilder.DropTable(
                name: "archive_scans");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "players");
        }
    }
}
