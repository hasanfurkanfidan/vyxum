using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vyxum.Modules.Products.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddSaleUnitAndUnitCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SaleUnit",
                table: "prd_Products",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Kg");

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "prd_Products",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SaleUnit",
                table: "prd_Products");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "prd_Products");
        }
    }
}
