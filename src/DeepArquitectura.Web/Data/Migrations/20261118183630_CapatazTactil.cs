using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepArquitectura.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CapatazTactil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Ref",
                table: "MaterialesSobrantes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Ref",
                table: "GastosObra",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialesSobrantes_Ref",
                table: "MaterialesSobrantes",
                column: "Ref",
                unique: true,
                filter: "[Ref] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GastosObra_Ref",
                table: "GastosObra",
                column: "Ref",
                unique: true,
                filter: "[Ref] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MaterialesSobrantes_Ref",
                table: "MaterialesSobrantes");

            migrationBuilder.DropIndex(
                name: "IX_GastosObra_Ref",
                table: "GastosObra");

            migrationBuilder.DropColumn(
                name: "Ref",
                table: "MaterialesSobrantes");

            migrationBuilder.DropColumn(
                name: "Ref",
                table: "GastosObra");
        }
    }
}
