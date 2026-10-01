using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WalletApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "WithdrawalEvents",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_WithdrawalEvents_WalletId_IdempotencyKey",
                table: "WithdrawalEvents",
                columns: new[] { "WalletId", "IdempotencyKey" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_WithdrawalEvents_WalletId_IdempotencyKey",
                table: "WithdrawalEvents");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "WithdrawalEvents");
        }
    }
}
