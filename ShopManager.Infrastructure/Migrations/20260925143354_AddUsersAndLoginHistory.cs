using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndLoginHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoginHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LoginAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LogoutAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanDashboard = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanItems = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanPurchase = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanTransfer = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanSale = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanCustomers = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanStats = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanCashbox = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanSettings = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanUserManagement = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanPrint = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanExportExcel = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastLogoutAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoginHistories_LoginAt",
                table: "LoginHistories",
                column: "LoginAt");

            migrationBuilder.CreateIndex(
                name: "IX_LoginHistories_UserId",
                table: "LoginHistories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoginHistories");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
