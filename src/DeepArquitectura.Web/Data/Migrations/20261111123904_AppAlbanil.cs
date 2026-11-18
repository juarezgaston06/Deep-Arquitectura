using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepArquitectura.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppAlbanil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AlbanilId",
                table: "Registros",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Cantidad",
                table: "Registros",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PorcentajeTarea",
                table: "Registros",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TareaObraId",
                table: "Registros",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Albaniles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Usuario = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Telefono = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Oficio = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PinHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UltimoAcceso = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CapatazId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Albaniles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Albaniles_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AlbanilObras",
                columns: table => new
                {
                    AlbanilId = table.Column<int>(type: "int", nullable: false),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    AsignadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlbanilObras", x => new { x.AlbanilId, x.ObraId });
                    table.ForeignKey(
                        name: "FK_AlbanilObras_Albaniles_AlbanilId",
                        column: x => x.AlbanilId,
                        principalTable: "Albaniles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AlbanilObras_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JornadasAlbanil",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AlbanilId = table.Column<int>(type: "int", nullable: false),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    CapatazId = table.Column<int>(type: "int", nullable: true),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Entrada = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EntradaLatitud = table.Column<double>(type: "float", nullable: true),
                    EntradaLongitud = table.Column<double>(type: "float", nullable: true),
                    EntradaPrecision = table.Column<double>(type: "float", nullable: true),
                    Salida = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SalidaLatitud = table.Column<double>(type: "float", nullable: true),
                    SalidaLongitud = table.Column<double>(type: "float", nullable: true),
                    SalidaPrecision = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JornadasAlbanil", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JornadasAlbanil_Albaniles_AlbanilId",
                        column: x => x.AlbanilId,
                        principalTable: "Albaniles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JornadasAlbanil_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_JornadasAlbanil_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartesDiarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AlbanilId = table.Column<int>(type: "int", nullable: false),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    CapatazId = table.Column<int>(type: "int", nullable: true),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Resumen = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    HorasTotales = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartesDiarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartesDiarios_Albaniles_AlbanilId",
                        column: x => x.AlbanilId,
                        principalTable: "Albaniles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartesDiarios_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PartesDiarios_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SesionesAlbanil",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AlbanilId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Dispositivo = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    CreadaEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UltimoUso = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiraEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SesionesAlbanil", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SesionesAlbanil_Albaniles_AlbanilId",
                        column: x => x.AlbanilId,
                        principalTable: "Albaniles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TareasObra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    EtapaObraId = table.Column<int>(type: "int", nullable: true),
                    AlbanilId = table.Column<int>(type: "int", nullable: true),
                    Titulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Unidad = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CantidadObjetivo = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    CantidadHecha = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Porcentaje = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FechaLimite = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletadaEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TareasObra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TareasObra_Albaniles_AlbanilId",
                        column: x => x.AlbanilId,
                        principalTable: "Albaniles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TareasObra_Etapas_EtapaObraId",
                        column: x => x.EtapaObraId,
                        principalTable: "Etapas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TareasObra_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParteDiarioItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParteDiarioId = table.Column<int>(type: "int", nullable: false),
                    TareaObraId = table.Column<int>(type: "int", nullable: true),
                    Horas = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    Detalle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParteDiarioItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParteDiarioItems_PartesDiarios_ParteDiarioId",
                        column: x => x.ParteDiarioId,
                        principalTable: "PartesDiarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParteDiarioItems_TareasObra_TareaObraId",
                        column: x => x.TareaObraId,
                        principalTable: "TareasObra",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PedidosMaterial",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    AlbanilId = table.Column<int>(type: "int", nullable: false),
                    CapatazId = table.Column<int>(type: "int", nullable: true),
                    TareaObraId = table.Column<int>(type: "int", nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Urgente = table.Column<bool>(type: "bit", nullable: false),
                    Nota = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Respuesta = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ActualizadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidosMaterial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PedidosMaterial_Albaniles_AlbanilId",
                        column: x => x.AlbanilId,
                        principalTable: "Albaniles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PedidosMaterial_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PedidosMaterial_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PedidosMaterial_TareasObra_TareaObraId",
                        column: x => x.TareaObraId,
                        principalTable: "TareasObra",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProblemasObra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    AlbanilId = table.Column<int>(type: "int", nullable: false),
                    CapatazId = table.Column<int>(type: "int", nullable: true),
                    TareaObraId = table.Column<int>(type: "int", nullable: true),
                    RegistroAvanceId = table.Column<int>(type: "int", nullable: true),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Urgencia = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Respuesta = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResueltoEn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemasObra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProblemasObra_Albaniles_AlbanilId",
                        column: x => x.AlbanilId,
                        principalTable: "Albaniles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProblemasObra_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProblemasObra_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProblemasObra_Registros_RegistroAvanceId",
                        column: x => x.RegistroAvanceId,
                        principalTable: "Registros",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProblemasObra_TareasObra_TareaObraId",
                        column: x => x.TareaObraId,
                        principalTable: "TareasObra",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PedidoMaterialItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PedidoMaterialId = table.Column<int>(type: "int", nullable: false),
                    Material = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Unidad = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidoMaterialItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PedidoMaterialItems_PedidosMaterial_PedidoMaterialId",
                        column: x => x.PedidoMaterialId,
                        principalTable: "PedidosMaterial",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Registros_AlbanilId",
                table: "Registros",
                column: "AlbanilId");

            migrationBuilder.CreateIndex(
                name: "IX_Registros_TareaObraId",
                table: "Registros",
                column: "TareaObraId");

            migrationBuilder.CreateIndex(
                name: "IX_Albaniles_CapatazId",
                table: "Albaniles",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_Albaniles_Usuario",
                table: "Albaniles",
                column: "Usuario",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlbanilObras_ObraId",
                table: "AlbanilObras",
                column: "ObraId");

            migrationBuilder.CreateIndex(
                name: "IX_JornadasAlbanil_AlbanilId_Entrada",
                table: "JornadasAlbanil",
                columns: new[] { "AlbanilId", "Entrada" });

            migrationBuilder.CreateIndex(
                name: "IX_JornadasAlbanil_CapatazId",
                table: "JornadasAlbanil",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_JornadasAlbanil_ObraId_Entrada",
                table: "JornadasAlbanil",
                columns: new[] { "ObraId", "Entrada" });

            migrationBuilder.CreateIndex(
                name: "IX_JornadasAlbanil_Ref",
                table: "JornadasAlbanil",
                column: "Ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParteDiarioItems_ParteDiarioId",
                table: "ParteDiarioItems",
                column: "ParteDiarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ParteDiarioItems_TareaObraId",
                table: "ParteDiarioItems",
                column: "TareaObraId");

            migrationBuilder.CreateIndex(
                name: "IX_PartesDiarios_AlbanilId_ObraId_Fecha",
                table: "PartesDiarios",
                columns: new[] { "AlbanilId", "ObraId", "Fecha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartesDiarios_CapatazId",
                table: "PartesDiarios",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_PartesDiarios_ObraId_Fecha",
                table: "PartesDiarios",
                columns: new[] { "ObraId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_PedidoMaterialItems_PedidoMaterialId",
                table: "PedidoMaterialItems",
                column: "PedidoMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidosMaterial_AlbanilId",
                table: "PedidosMaterial",
                column: "AlbanilId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidosMaterial_CapatazId",
                table: "PedidosMaterial",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidosMaterial_Estado_Fecha",
                table: "PedidosMaterial",
                columns: new[] { "Estado", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_PedidosMaterial_ObraId",
                table: "PedidosMaterial",
                column: "ObraId");

            migrationBuilder.CreateIndex(
                name: "IX_PedidosMaterial_Ref",
                table: "PedidosMaterial",
                column: "Ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PedidosMaterial_TareaObraId",
                table: "PedidosMaterial",
                column: "TareaObraId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemasObra_AlbanilId",
                table: "ProblemasObra",
                column: "AlbanilId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemasObra_CapatazId",
                table: "ProblemasObra",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemasObra_Estado_Fecha",
                table: "ProblemasObra",
                columns: new[] { "Estado", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_ProblemasObra_ObraId",
                table: "ProblemasObra",
                column: "ObraId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemasObra_Ref",
                table: "ProblemasObra",
                column: "Ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProblemasObra_RegistroAvanceId",
                table: "ProblemasObra",
                column: "RegistroAvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemasObra_TareaObraId",
                table: "ProblemasObra",
                column: "TareaObraId");

            migrationBuilder.CreateIndex(
                name: "IX_SesionesAlbanil_AlbanilId",
                table: "SesionesAlbanil",
                column: "AlbanilId");

            migrationBuilder.CreateIndex(
                name: "IX_SesionesAlbanil_TokenHash",
                table: "SesionesAlbanil",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TareasObra_AlbanilId",
                table: "TareasObra",
                column: "AlbanilId");

            migrationBuilder.CreateIndex(
                name: "IX_TareasObra_EtapaObraId",
                table: "TareasObra",
                column: "EtapaObraId");

            migrationBuilder.CreateIndex(
                name: "IX_TareasObra_ObraId_Estado",
                table: "TareasObra",
                columns: new[] { "ObraId", "Estado" });

            migrationBuilder.AddForeignKey(
                name: "FK_Registros_Albaniles_AlbanilId",
                table: "Registros",
                column: "AlbanilId",
                principalTable: "Albaniles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Registros_TareasObra_TareaObraId",
                table: "Registros",
                column: "TareaObraId",
                principalTable: "TareasObra",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Registros_Albaniles_AlbanilId",
                table: "Registros");

            migrationBuilder.DropForeignKey(
                name: "FK_Registros_TareasObra_TareaObraId",
                table: "Registros");

            migrationBuilder.DropTable(
                name: "AlbanilObras");

            migrationBuilder.DropTable(
                name: "JornadasAlbanil");

            migrationBuilder.DropTable(
                name: "ParteDiarioItems");

            migrationBuilder.DropTable(
                name: "PedidoMaterialItems");

            migrationBuilder.DropTable(
                name: "ProblemasObra");

            migrationBuilder.DropTable(
                name: "SesionesAlbanil");

            migrationBuilder.DropTable(
                name: "PartesDiarios");

            migrationBuilder.DropTable(
                name: "PedidosMaterial");

            migrationBuilder.DropTable(
                name: "TareasObra");

            migrationBuilder.DropTable(
                name: "Albaniles");

            migrationBuilder.DropIndex(
                name: "IX_Registros_AlbanilId",
                table: "Registros");

            migrationBuilder.DropIndex(
                name: "IX_Registros_TareaObraId",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "AlbanilId",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "Cantidad",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "PorcentajeTarea",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "TareaObraId",
                table: "Registros");
        }
    }
}
