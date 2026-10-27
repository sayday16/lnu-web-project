using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LNU_Web.Migrations
{
    public partial class AddJobNodeId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NodeId",
                table: "Jobs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NodeId",
                table: "Jobs");
        }
    }
}
