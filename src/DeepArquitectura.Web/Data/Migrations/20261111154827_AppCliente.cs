using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepArquitectura.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccesosCliente",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PinHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Sello = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    IntentosFallidos = table.Column<int>(type: "int", nullable: false),
                    BloqueadoHasta = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UltimoIngreso = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccesosCliente", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccesosCliente_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentosObra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Categoria = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Ruta = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tamano = table.Column<long>(type: "bigint", nullable: false),
                    NombreOriginal = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    VisibleCliente = table.Column<bool>(type: "bit", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentosObra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentosObra_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MensajesObra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    DelCliente = table.Column<bool>(type: "bit", nullable: false),
                    Autor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Texto = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeidoEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MensajesObra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MensajesObra_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AprobacionesCliente",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Opciones = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FechaLimite = table.Column<DateOnly>(type: "date", nullable: true),
                    DocumentoObraId = table.Column<int>(type: "int", nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OpcionElegida = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Comentario = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RespondidoPor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RespondidoEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RespondidoDesdeIp = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RespuestaVista = table.Column<bool>(type: "bit", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AprobacionesCliente", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AprobacionesCliente_DocumentosObra_DocumentoObraId",
                        column: x => x.DocumentoObraId,
                        principalTable: "DocumentosObra",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AprobacionesCliente_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccesosCliente_Codigo",
                table: "AccesosCliente",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccesosCliente_ObraId",
                table: "AccesosCliente",
                column: "ObraId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AprobacionesCliente_DocumentoObraId",
                table: "AprobacionesCliente",
                column: "DocumentoObraId");

            migrationBuilder.CreateIndex(
                name: "IX_AprobacionesCliente_ObraId_Estado",
                table: "AprobacionesCliente",
                columns: new[] { "ObraId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentosObra_ObraId_Categoria",
                table: "DocumentosObra",
                columns: new[] { "ObraId", "Categoria" });

            migrationBuilder.CreateIndex(
                name: "IX_MensajesObra_ObraId_Fecha",
                table: "MensajesObra",
                columns: new[] { "ObraId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_MensajesObra_Ref",
                table: "MensajesObra",
                column: "Ref",
                unique: true,
                filter: "[Ref] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccesosCliente");

            migrationBuilder.DropTable(
                name: "AprobacionesCliente");

            migrationBuilder.DropTable(
                name: "MensajesObra");

            migrationBuilder.DropTable(
                name: "DocumentosObra");
        }
    }
}
