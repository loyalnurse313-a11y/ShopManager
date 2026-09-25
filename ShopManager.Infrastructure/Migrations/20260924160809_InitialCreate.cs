using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashLedgers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DateShamsi = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DateGregorian = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Counterparty = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AmountIn = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    AmountOut = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashLedgers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    OpeningWarehouseQty = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    OpeningWarehouseUnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    OpeningShopQty = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SalePrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    LowStockCriticalPct = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    LowStockWarningPct = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    LowStockCriticalFixed = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    LowStockWarningFixed = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InitialCapital = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CriticalPct = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false),
                    WarningPct = table.Column<decimal>(type: "TEXT", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Purchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateShamsi = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DateGregorian = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Qty = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TotalCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SupplierNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PaymentStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    EntryType = table.Column<int>(type: "INTEGER", nullable: false),
                    ReversalOfId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Purchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Purchases_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Purchases_Purchases_ReversalOfId",
                        column: x => x.ReversalOfId,
                        principalTable: "Purchases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateShamsi = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DateGregorian = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Qty = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    SaleUnitPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    LockedUnitCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Revenue = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Cost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Profit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PaymentStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    EntryType = table.Column<int>(type: "INTEGER", nullable: false),
                    ReversalOfId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sales_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Sales_ReversalOfId",
                        column: x => x.ReversalOfId,
                        principalTable: "Sales",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Transfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateShamsi = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    DateGregorian = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Qty = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    EntryType = table.Column<int>(type: "INTEGER", nullable: false),
                    ReversalOfId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transfers_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transfers_Transfers_ReversalOfId",
                        column: x => x.ReversalOfId,
                        principalTable: "Transfers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashLedgers_DateGregorian",
                table: "CashLedgers",
                column: "DateGregorian");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ItemCode",
                table: "Items",
                column: "ItemCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_ItemId_DateGregorian",
                table: "Purchases",
                columns: new[] { "ItemId", "DateGregorian" });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_ReversalOfId",
                table: "Purchases",
                column: "ReversalOfId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_DateShamsi",
                table: "Sales",
                column: "DateShamsi");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ItemId_DateGregorian",
                table: "Sales",
                columns: new[] { "ItemId", "DateGregorian" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ReversalOfId",
                table: "Sales",
                column: "ReversalOfId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_ItemId_DateGregorian",
                table: "Transfers",
                columns: new[] { "ItemId", "DateGregorian" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_ReversalOfId",
                table: "Transfers",
                column: "ReversalOfId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashLedgers");

            migrationBuilder.DropTable(
                name: "Purchases");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Transfers");

            migrationBuilder.DropTable(
                name: "Items");
        }
    }
}
