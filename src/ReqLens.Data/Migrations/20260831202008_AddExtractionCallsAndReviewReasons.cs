using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReqLens.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractionCallsAndReviewReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractionFailure",
                table: "Orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "ReviewReasons",
                table: "Orders",
                type: "text[]",
                nullable: false);

            migrationBuilder.CreateTable(
                name: "ExtractionCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    GuardrailIntervened = table.Column<bool>(type: "boolean", nullable: false),
                    SchemaValid = table.Column<bool>(type: "boolean", nullable: false),
                    MinFieldConfidence = table.Column<double>(type: "double precision", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtractionCalls_Orders_TenantId_OrderId",
                        columns: x => new { x.TenantId, x.OrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionCalls_TenantId_At",
                table: "ExtractionCalls",
                columns: new[] { "TenantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionCalls_TenantId_OrderId",
                table: "ExtractionCalls",
                columns: new[] { "TenantId", "OrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtractionCalls");

            migrationBuilder.DropColumn(
                name: "ExtractionFailure",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReviewReasons",
                table: "Orders");
        }
    }
}
