using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SsSport.Drive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminConsole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "completion",
                table: "watch_sessions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    digest_at = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    quiet_hours_start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    quiet_hours_end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    default_link_expiry_days = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "viewers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_viewers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_watch_sessions_last_seen_at_id",
                table: "watch_sessions",
                columns: new[] { "last_seen_at", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_watch_sessions_player_id_email",
                table: "watch_sessions",
                columns: new[] { "player_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_watch_sessions_started_at",
                table: "watch_sessions",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_viewers_email",
                table: "viewers",
                column: "email",
                unique: true);

            // Backfill (A-2, A-7): the stored completion from each watch's ranges (their summed length over the video's
            // length, capped at 1), and a viewer row for every email that already has a recipient or a watch.
            migrationBuilder.Sql(
                """
                UPDATE watch_sessions w
                SET completion = LEAST(1, COALESCE((SELECT sum((r->>'to')::float8 - (r->>'from')::float8) FROM jsonb_array_elements(w.ranges) r), 0) / NULLIF(w.duration_seconds, 0))
                WHERE w.duration_seconds > 0
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO viewers (id, email, first_seen_at)
                SELECT gen_random_uuid(), e.email, min(e.seen_at)
                FROM (
                    SELECT unnest(emails) AS email, created_at AS seen_at FROM recipients
                    UNION ALL SELECT email, started_at FROM watch_sessions
                    UNION ALL SELECT email, created_at FROM viewer_sessions
                ) e
                GROUP BY e.email
                ON CONFLICT (email) DO NOTHING
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "viewers");

            migrationBuilder.DropIndex(
                name: "ix_watch_sessions_last_seen_at_id",
                table: "watch_sessions");

            migrationBuilder.DropIndex(
                name: "ix_watch_sessions_player_id_email",
                table: "watch_sessions");

            migrationBuilder.DropIndex(
                name: "ix_watch_sessions_started_at",
                table: "watch_sessions");

            migrationBuilder.DropColumn(
                name: "completion",
                table: "watch_sessions");

        }
    }
}
