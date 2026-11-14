using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SsSport.Drive.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessRequestDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "access_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by_user_id",
                table: "access_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "share_link_id",
                table: "access_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_access_requests_status_created_at",
                table: "access_requests",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_access_requests_status_created_at",
                table: "access_requests");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "access_requests");

            migrationBuilder.DropColumn(
                name: "decided_by_user_id",
                table: "access_requests");

            migrationBuilder.DropColumn(
                name: "share_link_id",
                table: "access_requests");
        }
    }
}
