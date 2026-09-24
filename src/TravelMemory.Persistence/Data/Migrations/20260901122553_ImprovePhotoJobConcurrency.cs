using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelMemory.Persistence.Data.Migrations
{
    /// <inheritdoc />
    public partial class ImprovePhotoJobConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastEnqueuedAtUtc",
                table: "PhotoProcessingJobs");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PhotoImportBatches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastEnqueuedAtUtc",
                table: "PhotoProcessingJobs",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "PhotoImportBatches",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }
    }
}
