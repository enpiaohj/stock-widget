using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockWidget.Core.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_rules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    StockName = table.Column<string>(type: "TEXT", nullable: false),
                    ThresholdPct = table.Column<decimal>(type: "TEXT", nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "daily_amount_history",
                columns: table => new
                {
                    Date = table.Column<string>(type: "TEXT", nullable: false),
                    ShAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    SzAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_amount_history", x => x.Date);
                });

            migrationBuilder.CreateTable(
                name: "quote_snapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: true),
                    ChangePct = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "watchlist",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Group = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watchlist", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_rules_Code",
                table: "alert_rules",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_quote_snapshots_Code_Time",
                table: "quote_snapshots",
                columns: new[] { "Code", "Time" });

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_SortOrder",
                table: "watchlist",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_rules");

            migrationBuilder.DropTable(
                name: "daily_amount_history");

            migrationBuilder.DropTable(
                name: "quote_snapshots");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "watchlist");
        }
    }
}
