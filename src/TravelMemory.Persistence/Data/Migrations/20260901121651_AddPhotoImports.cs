using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelMemory.Persistence.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedFileCount = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TimeAdjustmentMinutes = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoImportBatches", x => x.Id);
                    table.CheckConstraint("CK_PhotoImportBatches_ExpectedFileCount", "[ExpectedFileCount] BETWEEN 1 AND 500");
                    table.ForeignKey(
                        name: "FK_PhotoImportBatches_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhotoImportItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientFileId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ExpectedSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    TemporaryBlobName = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CapturedAtOriginalLocal = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    ExifOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    ErrorCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DerivativesVerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    OriginalDeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    OriginalRetainedUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoImportItems", x => x.Id);
                    table.CheckConstraint("CK_PhotoImportItems_ExpectedSizeBytes", "[ExpectedSizeBytes] > 0");
                    table.ForeignKey(
                        name: "FK_PhotoImportItems_PhotoImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "PhotoImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoProcessingJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    AvailableAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    LastEnqueuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoProcessingJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoProcessingJobs_PhotoImportItems_ImportItemId",
                        column: x => x.ImportItemId,
                        principalTable: "PhotoImportItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Photos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CapturedAtOriginalLocal = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    TimeAdjustmentMinutes = table.Column<int>(type: "int", nullable: false),
                    CapturedAtTimelineLocal = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    ExifOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    WebBlobName = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    ThumbnailBlobName = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    ThumbnailWidth = table.Column<int>(type: "int", nullable: false),
                    ThumbnailHeight = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Photos_PhotoImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "PhotoImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Photos_PhotoImportItems_ImportItemId",
                        column: x => x.ImportItemId,
                        principalTable: "PhotoImportItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Photos_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoImportBatches_OwnerId_TripId_ClientBatchId",
                table: "PhotoImportBatches",
                columns: new[] { "OwnerId", "TripId", "ClientBatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoImportBatches_OwnerId_TripId_CreatedAtUtc",
                table: "PhotoImportBatches",
                columns: new[] { "OwnerId", "TripId", "CreatedAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoImportBatches_TripId",
                table: "PhotoImportBatches",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoImportItems_ImportBatchId_ClientFileId",
                table: "PhotoImportItems",
                columns: new[] { "ImportBatchId", "ClientFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoImportItems_OwnerId_TripId_ContentHash",
                table: "PhotoImportItems",
                columns: new[] { "OwnerId", "TripId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoProcessingJobs_ImportItemId_Kind",
                table: "PhotoProcessingJobs",
                columns: new[] { "ImportItemId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoProcessingJobs_State_AvailableAtUtc",
                table: "PhotoProcessingJobs",
                columns: new[] { "State", "AvailableAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Photos_ImportBatchId",
                table: "Photos",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_ImportItemId",
                table: "Photos",
                column: "ImportItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_OwnerId_TripId_CapturedAtTimelineLocal_Id",
                table: "Photos",
                columns: new[] { "OwnerId", "TripId", "CapturedAtTimelineLocal", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Photos_OwnerId_TripId_ContentHash",
                table: "Photos",
                columns: new[] { "OwnerId", "TripId", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_TripId",
                table: "Photos",
                column: "TripId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoProcessingJobs");

            migrationBuilder.DropTable(
                name: "Photos");

            migrationBuilder.DropTable(
                name: "PhotoImportItems");

            migrationBuilder.DropTable(
                name: "PhotoImportBatches");
        }
    }
}
