using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vyxum.Modules.Weighing.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wgh_InboxMessages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HandlerName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wgh_InboxMessages", x => new { x.MessageId, x.HandlerName });
                });

            migrationBuilder.CreateTable(
                name: "wgh_OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wgh_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "wgh_Products",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    PackageTare = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetWeight = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    LowerTolerance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    UpperTolerance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ShelfLifeDays = table.Column<int>(type: "INTEGER", nullable: true),
                    BarcodePrefix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SourceOccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wgh_Products", x => x.ProductId);
                });

            migrationBuilder.CreateTable(
                name: "wgh_Records",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ProductName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Lot = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Gross = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    GrossScale = table.Column<int>(type: "INTEGER", nullable: false),
                    Tare = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    TareScale = table.Column<int>(type: "INTEGER", nullable: false),
                    Net = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    NetUnits = table.Column<long>(type: "INTEGER", nullable: false),
                    NetMilligrams = table.Column<long>(type: "INTEGER", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetWeight = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    LowerTolerance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    UpperTolerance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: true),
                    AmountScale = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Operator = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wgh_Records", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wgh_OutboxMessages_ProcessedAt_NextAttemptAt",
                table: "wgh_OutboxMessages",
                columns: new[] { "ProcessedAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_wgh_Products_Code",
                table: "wgh_Products",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_wgh_Records_CreatedAtUtc",
                table: "wgh_Records",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_wgh_Records_DeviceId_Id",
                table: "wgh_Records",
                columns: new[] { "DeviceId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_wgh_Records_Lot",
                table: "wgh_Records",
                column: "Lot");

            migrationBuilder.CreateIndex(
                name: "IX_wgh_Records_ProductId",
                table: "wgh_Records",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_wgh_Records_RequestId",
                table: "wgh_Records",
                column: "RequestId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wgh_InboxMessages");

            migrationBuilder.DropTable(
                name: "wgh_OutboxMessages");

            migrationBuilder.DropTable(
                name: "wgh_Products");

            migrationBuilder.DropTable(
                name: "wgh_Records");
        }
    }
}
