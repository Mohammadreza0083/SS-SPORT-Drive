using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SsSport.Drive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_code_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_code_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "access_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    about = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "viewer_account_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    signed_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_viewer_account_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_code_challenges_email_requested_at",
                table: "access_code_challenges",
                columns: new[] { "email", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_access_code_challenges_requested_at",
                table: "access_code_challenges",
                column: "requested_at");

            migrationBuilder.CreateIndex(
                name: "ix_access_requests_email_created_at",
                table: "access_requests",
                columns: new[] { "email", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_access_requests_email_player_id_status",
                table: "access_requests",
                columns: new[] { "email", "player_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_access_requests_player_id_created_at",
                table: "access_requests",
                columns: new[] { "player_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_viewer_account_sessions_expires_at",
                table: "viewer_account_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_viewer_account_sessions_token_hash",
                table: "viewer_account_sessions",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_code_challenges");

            migrationBuilder.DropTable(
                name: "access_requests");

            migrationBuilder.DropTable(
                name: "viewer_account_sessions");
        }
    }
}
