using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepArquitectura.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CircuitoDatosObra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CantidadAprobada",
                table: "TareasObra",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FechaInicioPlan",
                table: "TareasObra",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Oficio",
                table: "TareasObra",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Peso",
                table: "TareasObra",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PorcentajeAprobado",
                table: "TareasObra",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Rubro",
                table: "TareasObra",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CantidadInformada",
                table: "Registros",
                type: "decimal(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoValidacion",
                table: "Registros",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PorcentajeInformado",
                table: "Registros",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PuntoFotoId",
                table: "Registros",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Validacion",
                table: "Registros",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidadoEn",
                table: "Registros",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ValidadoPorCapatazId",
                table: "Registros",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VisibleCliente",
                table: "Registros",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "Latitud",
                table: "Obras",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitud",
                table: "Obras",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RadioMetros",
                table: "Obras",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TokenPortal",
                table: "Obras",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EntradaDistanciaMetros",
                table: "JornadasAlbanil",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EppFaltante",
                table: "JornadasAlbanil",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalidaDistanciaMetros",
                table: "JornadasAlbanil",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrabajoEnAltura",
                table: "JornadasAlbanil",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Empresa",
                table: "Albaniles",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Certificados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    PorcentajeAnterior = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    PorcentajeAcumulado = table.Column<decimal>(type: "decimal(7,3)", precision: 7, scale: 3, nullable: false),
                    MontoObra = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Detalle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notas = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PagoId = table.Column<int>(type: "int", nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Certificados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Certificados_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Certificados_Pagos_PagoId",
                        column: x => x.PagoId,
                        principalTable: "Pagos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClimaDiario",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    TempMax = table.Column<double>(type: "float", nullable: true),
                    TempMin = table.Column<double>(type: "float", nullable: true),
                    LluviaMm = table.Column<double>(type: "float", nullable: true),
                    VientoMaxKmh = table.Column<double>(type: "float", nullable: true),
                    CodigoTiempo = table.Column<int>(type: "int", nullable: true),
                    ActualizadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClimaDiario", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClimaDiario_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ControlesEtapa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EtapaObraId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RequiereFoto = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlesEtapa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ControlesEtapa_Etapas_EtapaObraId",
                        column: x => x.EtapaObraId,
                        principalTable: "Etapas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibroObra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Numero = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Texto = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CapatazId = table.Column<int>(type: "int", nullable: true),
                    AlbanilId = table.Column<int>(type: "int", nullable: true),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RegistroAvanceId = table.Column<int>(type: "int", nullable: true),
                    RespondeAId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibroObra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibroObra_Albaniles_AlbanilId",
                        column: x => x.AlbanilId,
                        principalTable: "Albaniles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LibroObra_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LibroObra_LibroObra_RespondeAId",
                        column: x => x.RespondeAId,
                        principalTable: "LibroObra",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LibroObra_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibroObra_Registros_RegistroAvanceId",
                        column: x => x.RegistroAvanceId,
                        principalTable: "Registros",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PuntosFoto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Indicacion = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FrecuenciaDias = table.Column<int>(type: "int", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    VisibleCliente = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PuntosFoto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PuntosFoto_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecepcionesMaterial",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    PedidoMaterialId = table.Column<int>(type: "int", nullable: true),
                    AlbanilId = table.Column<int>(type: "int", nullable: true),
                    CapatazId = table.Column<int>(type: "int", nullable: true),
                    RegistroAvanceId = table.Column<int>(type: "int", nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Proveedor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    NumeroRemito = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecepcionesMaterial", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecepcionesMaterial_Albaniles_AlbanilId",
                        column: x => x.AlbanilId,
                        principalTable: "Albaniles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecepcionesMaterial_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecepcionesMaterial_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecepcionesMaterial_PedidosMaterial_PedidoMaterialId",
                        column: x => x.PedidoMaterialId,
                        principalTable: "PedidosMaterial",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecepcionesMaterial_Registros_RegistroAvanceId",
                        column: x => x.RegistroAvanceId,
                        principalTable: "Registros",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RespuestasControl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ControlEtapaId = table.Column<int>(type: "int", nullable: false),
                    AlbanilId = table.Column<int>(type: "int", nullable: true),
                    CapatazId = table.Column<int>(type: "int", nullable: true),
                    RegistroAvanceId = table.Column<int>(type: "int", nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Resultado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Nota = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RespuestasControl", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RespuestasControl_Albaniles_AlbanilId",
                        column: x => x.AlbanilId,
                        principalTable: "Albaniles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RespuestasControl_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RespuestasControl_ControlesEtapa_ControlEtapaId",
                        column: x => x.ControlEtapaId,
                        principalTable: "ControlesEtapa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RespuestasControl_Registros_RegistroAvanceId",
                        column: x => x.RegistroAvanceId,
                        principalTable: "Registros",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LecturasLibro",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AsientoLibroId = table.Column<int>(type: "int", nullable: false),
                    CapatazId = table.Column<int>(type: "int", nullable: true),
                    AlbanilId = table.Column<int>(type: "int", nullable: true),
                    LeidoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LecturasLibro", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LecturasLibro_Albaniles_AlbanilId",
                        column: x => x.AlbanilId,
                        principalTable: "Albaniles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LecturasLibro_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LecturasLibro_LibroObra_AsientoLibroId",
                        column: x => x.AsientoLibroId,
                        principalTable: "LibroObra",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecepcionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecepcionMaterialId = table.Column<int>(type: "int", nullable: false),
                    PedidoMaterialItemId = table.Column<int>(type: "int", nullable: true),
                    Material = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Unidad = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecepcionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecepcionItems_PedidoMaterialItems_PedidoMaterialItemId",
                        column: x => x.PedidoMaterialItemId,
                        principalTable: "PedidoMaterialItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecepcionItems_RecepcionesMaterial_RecepcionMaterialId",
                        column: x => x.RecepcionMaterialId,
                        principalTable: "RecepcionesMaterial",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Registros_PuntoFotoId",
                table: "Registros",
                column: "PuntoFotoId");

            migrationBuilder.CreateIndex(
                name: "IX_Registros_Validacion_ObraId",
                table: "Registros",
                columns: new[] { "Validacion", "ObraId" });

            migrationBuilder.CreateIndex(
                name: "IX_Registros_ValidadoPorCapatazId",
                table: "Registros",
                column: "ValidadoPorCapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_Obras_TokenPortal",
                table: "Obras",
                column: "TokenPortal",
                unique: true,
                filter: "[TokenPortal] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Certificados_ObraId_Numero",
                table: "Certificados",
                columns: new[] { "ObraId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Certificados_PagoId",
                table: "Certificados",
                column: "PagoId");

            migrationBuilder.CreateIndex(
                name: "IX_ClimaDiario_ObraId_Fecha",
                table: "ClimaDiario",
                columns: new[] { "ObraId", "Fecha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ControlesEtapa_EtapaObraId_Orden",
                table: "ControlesEtapa",
                columns: new[] { "EtapaObraId", "Orden" });

            migrationBuilder.CreateIndex(
                name: "IX_LecturasLibro_AlbanilId",
                table: "LecturasLibro",
                column: "AlbanilId");

            migrationBuilder.CreateIndex(
                name: "IX_LecturasLibro_AsientoLibroId_AlbanilId",
                table: "LecturasLibro",
                columns: new[] { "AsientoLibroId", "AlbanilId" },
                unique: true,
                filter: "[AlbanilId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LecturasLibro_AsientoLibroId_CapatazId",
                table: "LecturasLibro",
                columns: new[] { "AsientoLibroId", "CapatazId" },
                unique: true,
                filter: "[CapatazId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LecturasLibro_CapatazId",
                table: "LecturasLibro",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_LibroObra_AlbanilId",
                table: "LibroObra",
                column: "AlbanilId");

            migrationBuilder.CreateIndex(
                name: "IX_LibroObra_CapatazId",
                table: "LibroObra",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_LibroObra_ObraId_Tipo_Numero",
                table: "LibroObra",
                columns: new[] { "ObraId", "Tipo", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibroObra_Ref",
                table: "LibroObra",
                column: "Ref",
                unique: true,
                filter: "[Ref] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LibroObra_RegistroAvanceId",
                table: "LibroObra",
                column: "RegistroAvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_LibroObra_RespondeAId",
                table: "LibroObra",
                column: "RespondeAId");

            migrationBuilder.CreateIndex(
                name: "IX_PuntosFoto_ObraId",
                table: "PuntosFoto",
                column: "ObraId");

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesMaterial_AlbanilId",
                table: "RecepcionesMaterial",
                column: "AlbanilId");

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesMaterial_CapatazId",
                table: "RecepcionesMaterial",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesMaterial_ObraId_Fecha",
                table: "RecepcionesMaterial",
                columns: new[] { "ObraId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesMaterial_PedidoMaterialId",
                table: "RecepcionesMaterial",
                column: "PedidoMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesMaterial_Ref",
                table: "RecepcionesMaterial",
                column: "Ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesMaterial_RegistroAvanceId",
                table: "RecepcionesMaterial",
                column: "RegistroAvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionItems_PedidoMaterialItemId",
                table: "RecepcionItems",
                column: "PedidoMaterialItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionItems_RecepcionMaterialId",
                table: "RecepcionItems",
                column: "RecepcionMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_RespuestasControl_AlbanilId",
                table: "RespuestasControl",
                column: "AlbanilId");

            migrationBuilder.CreateIndex(
                name: "IX_RespuestasControl_CapatazId",
                table: "RespuestasControl",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_RespuestasControl_ControlEtapaId",
                table: "RespuestasControl",
                column: "ControlEtapaId");

            migrationBuilder.CreateIndex(
                name: "IX_RespuestasControl_Ref",
                table: "RespuestasControl",
                column: "Ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RespuestasControl_RegistroAvanceId",
                table: "RespuestasControl",
                column: "RegistroAvanceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Registros_Capataces_ValidadoPorCapatazId",
                table: "Registros",
                column: "ValidadoPorCapatazId",
                principalTable: "Capataces",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Registros_PuntosFoto_PuntoFotoId",
                table: "Registros",
                column: "PuntoFotoId",
                principalTable: "PuntosFoto",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Registros_Capataces_ValidadoPorCapatazId",
                table: "Registros");

            migrationBuilder.DropForeignKey(
                name: "FK_Registros_PuntosFoto_PuntoFotoId",
                table: "Registros");

            migrationBuilder.DropTable(
                name: "Certificados");

            migrationBuilder.DropTable(
                name: "ClimaDiario");

            migrationBuilder.DropTable(
                name: "LecturasLibro");

            migrationBuilder.DropTable(
                name: "PuntosFoto");

            migrationBuilder.DropTable(
                name: "RecepcionItems");

            migrationBuilder.DropTable(
                name: "RespuestasControl");

            migrationBuilder.DropTable(
                name: "LibroObra");

            migrationBuilder.DropTable(
                name: "RecepcionesMaterial");

            migrationBuilder.DropTable(
                name: "ControlesEtapa");

            migrationBuilder.DropIndex(
                name: "IX_Registros_PuntoFotoId",
                table: "Registros");

            migrationBuilder.DropIndex(
                name: "IX_Registros_Validacion_ObraId",
                table: "Registros");

            migrationBuilder.DropIndex(
                name: "IX_Registros_ValidadoPorCapatazId",
                table: "Registros");

            migrationBuilder.DropIndex(
                name: "IX_Obras_TokenPortal",
                table: "Obras");

            migrationBuilder.DropColumn(
                name: "CantidadAprobada",
                table: "TareasObra");

            migrationBuilder.DropColumn(
                name: "FechaInicioPlan",
                table: "TareasObra");

            migrationBuilder.DropColumn(
                name: "Oficio",
                table: "TareasObra");

            migrationBuilder.DropColumn(
                name: "Peso",
                table: "TareasObra");

            migrationBuilder.DropColumn(
                name: "PorcentajeAprobado",
                table: "TareasObra");

            migrationBuilder.DropColumn(
                name: "Rubro",
                table: "TareasObra");

            migrationBuilder.DropColumn(
                name: "CantidadInformada",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "MotivoValidacion",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "PorcentajeInformado",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "PuntoFotoId",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "Validacion",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "ValidadoEn",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "ValidadoPorCapatazId",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "VisibleCliente",
                table: "Registros");

            migrationBuilder.DropColumn(
                name: "Latitud",
                table: "Obras");

            migrationBuilder.DropColumn(
                name: "Longitud",
                table: "Obras");

            migrationBuilder.DropColumn(
                name: "RadioMetros",
                table: "Obras");

            migrationBuilder.DropColumn(
                name: "TokenPortal",
                table: "Obras");

            migrationBuilder.DropColumn(
                name: "EntradaDistanciaMetros",
                table: "JornadasAlbanil");

            migrationBuilder.DropColumn(
                name: "EppFaltante",
                table: "JornadasAlbanil");

            migrationBuilder.DropColumn(
                name: "SalidaDistanciaMetros",
                table: "JornadasAlbanil");

            migrationBuilder.DropColumn(
                name: "TrabajoEnAltura",
                table: "JornadasAlbanil");

            migrationBuilder.DropColumn(
                name: "Empresa",
                table: "Albaniles");
        }
    }
}
