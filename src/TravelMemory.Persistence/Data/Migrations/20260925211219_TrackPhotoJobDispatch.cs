using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelMemory.Persistence.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrackPhotoJobDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastDispatchedAtUtc",
                table: "PhotoProcessingJobs",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastDispatchedAtUtc",
                table: "PhotoProcessingJobs");
        }
    }
}
