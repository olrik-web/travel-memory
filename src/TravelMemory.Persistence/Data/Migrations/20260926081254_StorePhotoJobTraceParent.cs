using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelMemory.Persistence.Data.Migrations
{
    /// <inheritdoc />
    public partial class StorePhotoJobTraceParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "PhotoProcessingJobs",
                type: "varchar(55)",
                unicode: false,
                maxLength: 55,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "PhotoProcessingJobs");
        }
    }
}
