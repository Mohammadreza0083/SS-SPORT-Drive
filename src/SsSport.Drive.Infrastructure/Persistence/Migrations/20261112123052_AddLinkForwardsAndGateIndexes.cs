using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SsSport.Drive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkForwardsAndGateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "link_forwards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    first_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_link_forwards", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_viewer_sessions_share_link_id_email",
                table: "viewer_sessions",
                columns: new[] { "share_link_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_trusted_devices_expires_at",
                table: "trusted_devices",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_trusted_devices_share_link_id_email",
                table: "trusted_devices",
                columns: new[] { "share_link_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_email_challenges_requested_at",
                table: "email_challenges",
                column: "requested_at");

            migrationBuilder.CreateIndex(
                name: "ix_link_forwards_share_link_id_email",
                table: "link_forwards",
                columns: new[] { "share_link_id", "email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "link_forwards");

            migrationBuilder.DropIndex(
                name: "ix_viewer_sessions_share_link_id_email",
                table: "viewer_sessions");

            migrationBuilder.DropIndex(
                name: "ix_trusted_devices_expires_at",
                table: "trusted_devices");

            migrationBuilder.DropIndex(
                name: "ix_trusted_devices_share_link_id_email",
                table: "trusted_devices");

            migrationBuilder.DropIndex(
                name: "ix_email_challenges_requested_at",
                table: "email_challenges");
        }
    }
}
