using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WalletApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchedAtUtc",
                table: "WithdrawalEvents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WithdrawalEvents_Pending",
                table: "WithdrawalEvents",
                column: "OccurredAtUtc",
                filter: "[DispatchedAtUtc] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WithdrawalEvents_Pending",
                table: "WithdrawalEvents");

            migrationBuilder.DropColumn(
                name: "DispatchedAtUtc",
                table: "WithdrawalEvents");
        }
    }
}
