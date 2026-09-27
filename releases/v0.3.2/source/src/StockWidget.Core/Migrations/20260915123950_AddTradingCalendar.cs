using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockWidget.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddTradingCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trading_calendar",
                columns: table => new
                {
                    Date = table.Column<string>(type: "TEXT", nullable: false),
                    IsTradingDay = table.Column<bool>(type: "INTEGER", nullable: false),
                    Remark = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trading_calendar", x => x.Date);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trading_calendar");
        }
    }
}
