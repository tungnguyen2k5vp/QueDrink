using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueDrink.Migrations
{
    /// <inheritdoc />
    public partial class ExternalPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "gateway_response_code",
                table: "payment_transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_transaction_id",
                table: "payment_transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gateway_transaction_status",
                table: "payment_transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "needs_review",
                table: "payment_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_provider_gateway_transaction_id",
                table: "payment_transactions",
                columns: new[] { "provider", "gateway_transaction_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            throw new NotSupportedException("Payment evidence must be preserved. Restore a verified backup to roll back this migration.");
    }
}
