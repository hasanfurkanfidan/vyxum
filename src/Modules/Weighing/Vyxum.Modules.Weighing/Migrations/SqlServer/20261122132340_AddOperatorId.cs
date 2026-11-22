using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Vyxum.Modules.Weighing.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddOperatorId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OperatorId",
                table: "wgh_Records",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OperatorId",
                table: "wgh_Records");
        }
    }
}
