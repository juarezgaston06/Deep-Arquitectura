using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepArquitectura.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppCapataz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CapatazId",
                table: "Registros",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Ref",
                table: "Registros",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Capataces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PinHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UltimoAcceso = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Capataces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediosRegistro",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegistroAvanceId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Ruta = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tamano = table.Column<long>(type: "bigint", nullable: false),
                    NombreOriginal = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediosRegistro", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediosRegistro_Registros_RegistroAvanceId",
                        column: x => x.RegistroAvanceId,
                        principalTable: "Registros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CapatazObras",
                columns: table => new
                {
                    CapatazId = table.Column<int>(type: "int", nullable: false),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    AsignadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapatazObras", x => new { x.CapatazId, x.ObraId });
                    table.ForeignKey(
                        name: "FK_CapatazObras_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CapatazObras_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SesionesCapataz",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CapatazId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Dispositivo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CreadaEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UltimoUso = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiraEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SesionesCapataz", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SesionesCapataz_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Registros_CapatazId",
                table: "Registros",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_Registros_Ref",
                table: "Registros",
                column: "Ref",
                unique: true,
                filter: "[Ref] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Capataces_Usuario",
                table: "Capataces",
                column: "Usuario",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapatazObras_ObraId",
                table: "CapatazObras",
                column: "ObraId");

            migrationBuilder.CreateIndex(
                name: "IX_MediosRegistro_Ref",
                table: "MediosRegistro",
                column: "Ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediosRegistro_RegistroAvanceId",
                table: "MediosRegistro",
                column: "RegistroAvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SesionesCapataz_CapatazId",
                table: "SesionesCapataz",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_SesionesCapataz_TokenHash",
                table: "SesionesCapataz",
                column: "TokenHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Registros_Capataces_CapatazId",
                table: "Registros",
                column: "CapatazId",
                principalTable: "Capataces",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Registros_Capataces_CapatazId",
                table: "Registros");

            migrationBuilder.DropTable(
                name: "CapatazObras");

            migrationBuilder.DropTable(
                name: "MediosRegistro");

            migrationBuilder.DropTable(
                name: "SesionesCapataz");

            migrationBuilder.DropTable(
                name: "Capataces");

            migrationBuilder.DropIndex(
                name: "IX_Registros_CapatazId",
                table: "Registros");

            migrationBuilder.DropIndex(
                name: "IX_Registros_Ref",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "CapatazId",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "Ref",
                table: "Registros");
        }
    }
}
