using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeepArquitectura.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatosProceso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AtrasoInformadoEn",
                table: "TareasObra",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CausaAtraso",
                table: "TareasObra",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetalleAtraso",
                table: "TareasObra",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PrecioUnitario",
                table: "RecepcionItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NumeroFactura",
                table: "RecepcionesMaterial",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecididoEn",
                table: "Presupuestos",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetallePerdida",
                table: "Presupuestos",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoPerdida",
                table: "Presupuestos",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorHora",
                table: "JornadasAlbanil",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Canal",
                table: "Consultas",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                // Las consultas que ya existían llegaron por el sitio (el enum se guarda como texto: "" no se podría leer).
                defaultValue: "Web");

            migrationBuilder.AddColumn<DateTime>(
                name: "ContactadaEn",
                table: "Consultas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotivoDescarte",
                table: "Consultas",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaginaEntrada",
                table: "Consultas",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublicacionId",
                table: "Consultas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Referente",
                table: "Consultas",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmCampania",
                table: "Consultas",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmFuente",
                table: "Consultas",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmMedio",
                table: "Consultas",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorHora",
                table: "Albaniles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActasEntrega",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FechaRecorrida = table.Column<DateOnly>(type: "date", nullable: true),
                    FirmadaEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirmadaPor = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    FirmadaDesdeIp = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ConReservas = table.Column<bool>(type: "bit", nullable: false),
                    ComentarioCliente = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    MesesGarantia = table.Column<int>(type: "int", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActasEntrega", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActasEntrega_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EncuestasCliente",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Momento = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EtapaObraId = table.Column<int>(type: "int", nullable: true),
                    Titulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreadaEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RespondidaEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Puntaje = table.Column<int>(type: "int", nullable: true),
                    Comentario = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Vista = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncuestasCliente", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EncuestasCliente_Etapas_EtapaObraId",
                        column: x => x.EtapaObraId,
                        principalTable: "Etapas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EncuestasCliente_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GastosObra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    EtapaObraId = table.Column<int>(type: "int", nullable: true),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Categoria = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Proveedor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    NumeroComprobante = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CapatazId = table.Column<int>(type: "int", nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GastosObra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GastosObra_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GastosObra_Etapas_EtapaObraId",
                        column: x => x.EtapaObraId,
                        principalTable: "Etapas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GastosObra_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaterialesSobrantes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    EtapaObraId = table.Column<int>(type: "int", nullable: true),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Material = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Cantidad = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Unidad = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Destino = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ObraDestinoId = table.Column<int>(type: "int", nullable: true),
                    ValorRecuperado = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CapatazId = table.Column<int>(type: "int", nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialesSobrantes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialesSobrantes_Capataces_CapatazId",
                        column: x => x.CapatazId,
                        principalTable: "Capataces",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MaterialesSobrantes_Etapas_EtapaObraId",
                        column: x => x.EtapaObraId,
                        principalTable: "Etapas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MaterialesSobrantes_Obras_ObraDestinoId",
                        column: x => x.ObraDestinoId,
                        principalTable: "Obras",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MaterialesSobrantes_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PagosInformados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Medio = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CertificadoId = table.Column<int>(type: "int", nullable: true),
                    Nota = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InformadoPor = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    InformadoEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MotivoRechazo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RevisadoEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PagoId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagosInformados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PagosInformados_Certificados_CertificadoId",
                        column: x => x.CertificadoId,
                        principalTable: "Certificados",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PagosInformados_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PagosInformados_Pagos_PagoId",
                        column: x => x.PagoId,
                        principalTable: "Pagos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PermisosDifusion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    UsarFotos = table.Column<bool>(type: "bit", nullable: false),
                    MostrarNombre = table.Column<bool>(type: "bit", nullable: false),
                    Testimonio = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    DesdeIp = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    ActualizadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermisosDifusion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermisosDifusion_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReclamosGarantia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ref = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Rubro = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Ubicacion = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Descripcion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Urgente = table.Column<bool>(type: "bit", nullable: false),
                    ReportadoPor = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Respuesta = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    FechaVisita = table.Column<DateOnly>(type: "date", nullable: true),
                    ResueltoEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Visto = table.Column<bool>(type: "bit", nullable: false),
                    Causa = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    EtapaObraId = table.Column<int>(type: "int", nullable: true),
                    Responsable = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    CostoReparacion = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NotaInterna = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReclamosGarantia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReclamosGarantia_Etapas_EtapaObraId",
                        column: x => x.EtapaObraId,
                        principalTable: "Etapas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReclamosGarantia_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Relevamientos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClienteId = table.Column<int>(type: "int", nullable: true),
                    ConsultaId = table.Column<int>(type: "int", nullable: true),
                    ObraId = table.Column<int>(type: "int", nullable: true),
                    Contacto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Direccion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Localidad = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Latitud = table.Column<double>(type: "float", nullable: true),
                    Longitud = table.Column<double>(type: "float", nullable: true),
                    FrenteM = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    FondoM = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    SuperficieTerrenoM2 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    SuperficieCubiertaM2 = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Plantas = table.Column<int>(type: "int", nullable: true),
                    AntiguedadAnios = table.Column<int>(type: "int", nullable: true),
                    EstadoGeneral = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Agua = table.Column<bool>(type: "bit", nullable: true),
                    Luz = table.Column<bool>(type: "bit", nullable: true),
                    Gas = table.Column<bool>(type: "bit", nullable: true),
                    Cloaca = table.Column<bool>(type: "bit", nullable: true),
                    Hallazgos = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Pedido = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PresupuestoCliente = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PlazoDeseado = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Notas = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relevamientos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Relevamientos_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Relevamientos_Consultas_ConsultaId",
                        column: x => x.ConsultaId,
                        principalTable: "Consultas",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Relevamientos_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TramitesObra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: false),
                    Orden = table.Column<int>(type: "int", nullable: false),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Organismo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Expediente = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FechaPresentado = table.Column<DateOnly>(type: "date", nullable: true),
                    FechaAprobado = table.Column<DateOnly>(type: "date", nullable: true),
                    Vence = table.Column<DateOnly>(type: "date", nullable: true),
                    VisibleCliente = table.Column<bool>(type: "bit", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TramitesObra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TramitesObra_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ObservacionesEntrega",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActaEntregaId = table.Column<int>(type: "int", nullable: false),
                    Descripcion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Ubicacion = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Rubro = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Responsable = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Resuelta = table.Column<bool>(type: "bit", nullable: false),
                    ResueltaEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NotaResolucion = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservacionesEntrega", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObservacionesEntrega_ActasEntrega_ActaEntregaId",
                        column: x => x.ActaEntregaId,
                        principalTable: "ActasEntrega",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovimientosTramite",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TramiteObraId = table.Column<int>(type: "int", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Nota = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DocumentoObraId = table.Column<int>(type: "int", nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimientosTramite", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimientosTramite_DocumentosObra_DocumentoObraId",
                        column: x => x.DocumentoObraId,
                        principalTable: "DocumentosObra",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MovimientosTramite_TramitesObra_TramiteObraId",
                        column: x => x.TramiteObraId,
                        principalTable: "TramitesObra",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Adjuntos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObraId = table.Column<int>(type: "int", nullable: true),
                    RelevamientoId = table.Column<int>(type: "int", nullable: true),
                    GastoObraId = table.Column<int>(type: "int", nullable: true),
                    RecepcionMaterialId = table.Column<int>(type: "int", nullable: true),
                    ObservacionEntregaId = table.Column<int>(type: "int", nullable: true),
                    ReclamoGarantiaId = table.Column<int>(type: "int", nullable: true),
                    PagoInformadoId = table.Column<int>(type: "int", nullable: true),
                    EsResolucion = table.Column<bool>(type: "bit", nullable: false),
                    Ruta = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tamano = table.Column<long>(type: "bigint", nullable: false),
                    NombreOriginal = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adjuntos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Adjuntos_GastosObra_GastoObraId",
                        column: x => x.GastoObraId,
                        principalTable: "GastosObra",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Adjuntos_Obras_ObraId",
                        column: x => x.ObraId,
                        principalTable: "Obras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Adjuntos_ObservacionesEntrega_ObservacionEntregaId",
                        column: x => x.ObservacionEntregaId,
                        principalTable: "ObservacionesEntrega",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Adjuntos_PagosInformados_PagoInformadoId",
                        column: x => x.PagoInformadoId,
                        principalTable: "PagosInformados",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Adjuntos_RecepcionesMaterial_RecepcionMaterialId",
                        column: x => x.RecepcionMaterialId,
                        principalTable: "RecepcionesMaterial",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Adjuntos_ReclamosGarantia_ReclamoGarantiaId",
                        column: x => x.ReclamoGarantiaId,
                        principalTable: "ReclamosGarantia",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Adjuntos_Relevamientos_RelevamientoId",
                        column: x => x.RelevamientoId,
                        principalTable: "Relevamientos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Consultas_Canal_CreadoEn",
                table: "Consultas",
                columns: new[] { "Canal", "CreadoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_Consultas_PublicacionId",
                table: "Consultas",
                column: "PublicacionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActasEntrega_ObraId",
                table: "ActasEntrega",
                column: "ObraId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Adjuntos_GastoObraId",
                table: "Adjuntos",
                column: "GastoObraId");

            migrationBuilder.CreateIndex(
                name: "IX_Adjuntos_ObraId",
                table: "Adjuntos",
                column: "ObraId");

            migrationBuilder.CreateIndex(
                name: "IX_Adjuntos_ObservacionEntregaId",
                table: "Adjuntos",
                column: "ObservacionEntregaId");

            migrationBuilder.CreateIndex(
                name: "IX_Adjuntos_PagoInformadoId",
                table: "Adjuntos",
                column: "PagoInformadoId");

            migrationBuilder.CreateIndex(
                name: "IX_Adjuntos_RecepcionMaterialId",
                table: "Adjuntos",
                column: "RecepcionMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_Adjuntos_ReclamoGarantiaId",
                table: "Adjuntos",
                column: "ReclamoGarantiaId");

            migrationBuilder.CreateIndex(
                name: "IX_Adjuntos_RelevamientoId",
                table: "Adjuntos",
                column: "RelevamientoId");

            migrationBuilder.CreateIndex(
                name: "IX_EncuestasCliente_EtapaObraId",
                table: "EncuestasCliente",
                column: "EtapaObraId");

            migrationBuilder.CreateIndex(
                name: "IX_EncuestasCliente_ObraId_Momento_EtapaObraId",
                table: "EncuestasCliente",
                columns: new[] { "ObraId", "Momento", "EtapaObraId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GastosObra_CapatazId",
                table: "GastosObra",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_GastosObra_EtapaObraId",
                table: "GastosObra",
                column: "EtapaObraId");

            migrationBuilder.CreateIndex(
                name: "IX_GastosObra_ObraId_Fecha",
                table: "GastosObra",
                columns: new[] { "ObraId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialesSobrantes_CapatazId",
                table: "MaterialesSobrantes",
                column: "CapatazId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialesSobrantes_EtapaObraId",
                table: "MaterialesSobrantes",
                column: "EtapaObraId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialesSobrantes_ObraDestinoId",
                table: "MaterialesSobrantes",
                column: "ObraDestinoId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialesSobrantes_ObraId_Fecha",
                table: "MaterialesSobrantes",
                columns: new[] { "ObraId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosTramite_DocumentoObraId",
                table: "MovimientosTramite",
                column: "DocumentoObraId");

            migrationBuilder.CreateIndex(
                name: "IX_MovimientosTramite_TramiteObraId",
                table: "MovimientosTramite",
                column: "TramiteObraId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservacionesEntrega_ActaEntregaId",
                table: "ObservacionesEntrega",
                column: "ActaEntregaId");

            migrationBuilder.CreateIndex(
                name: "IX_PagosInformados_CertificadoId",
                table: "PagosInformados",
                column: "CertificadoId");

            migrationBuilder.CreateIndex(
                name: "IX_PagosInformados_ObraId_Estado",
                table: "PagosInformados",
                columns: new[] { "ObraId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_PagosInformados_PagoId",
                table: "PagosInformados",
                column: "PagoId");

            migrationBuilder.CreateIndex(
                name: "IX_PagosInformados_Ref",
                table: "PagosInformados",
                column: "Ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PermisosDifusion_ObraId",
                table: "PermisosDifusion",
                column: "ObraId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReclamosGarantia_Estado_Fecha",
                table: "ReclamosGarantia",
                columns: new[] { "Estado", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_ReclamosGarantia_EtapaObraId",
                table: "ReclamosGarantia",
                column: "EtapaObraId");

            migrationBuilder.CreateIndex(
                name: "IX_ReclamosGarantia_ObraId",
                table: "ReclamosGarantia",
                column: "ObraId");

            migrationBuilder.CreateIndex(
                name: "IX_ReclamosGarantia_Ref",
                table: "ReclamosGarantia",
                column: "Ref",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Relevamientos_ClienteId",
                table: "Relevamientos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Relevamientos_ConsultaId",
                table: "Relevamientos",
                column: "ConsultaId");

            migrationBuilder.CreateIndex(
                name: "IX_Relevamientos_Fecha",
                table: "Relevamientos",
                column: "Fecha");

            migrationBuilder.CreateIndex(
                name: "IX_Relevamientos_ObraId",
                table: "Relevamientos",
                column: "ObraId");

            migrationBuilder.CreateIndex(
                name: "IX_TramitesObra_ObraId_Orden",
                table: "TramitesObra",
                columns: new[] { "ObraId", "Orden" });

            migrationBuilder.AddForeignKey(
                name: "FK_Consultas_Publicaciones_PublicacionId",
                table: "Consultas",
                column: "PublicacionId",
                principalTable: "Publicaciones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Consultas_Publicaciones_PublicacionId",
                table: "Consultas");

            migrationBuilder.DropTable(
                name: "Adjuntos");

            migrationBuilder.DropTable(
                name: "EncuestasCliente");

            migrationBuilder.DropTable(
                name: "MaterialesSobrantes");

            migrationBuilder.DropTable(
                name: "MovimientosTramite");

            migrationBuilder.DropTable(
                name: "PermisosDifusion");

            migrationBuilder.DropTable(
                name: "GastosObra");

            migrationBuilder.DropTable(
                name: "ObservacionesEntrega");

            migrationBuilder.DropTable(
                name: "PagosInformados");

            migrationBuilder.DropTable(
                name: "ReclamosGarantia");

            migrationBuilder.DropTable(
                name: "Relevamientos");

            migrationBuilder.DropTable(
                name: "TramitesObra");

            migrationBuilder.DropTable(
                name: "ActasEntrega");

            migrationBuilder.DropIndex(
                name: "IX_Consultas_Canal_CreadoEn",
                table: "Consultas");

            migrationBuilder.DropIndex(
                name: "IX_Consultas_PublicacionId",
                table: "Consultas");

            migrationBuilder.DropColumn(
                name: "AtrasoInformadoEn",
                table: "TareasObra");

            migrationBuilder.DropColumn(
                name: "CausaAtraso",
                table: "TareasObra");

            migrationBuilder.DropColumn(
                name: "DetalleAtraso",
                table: "TareasObra");

            migrationBuilder.DropColumn(
                name: "PrecioUnitario",
                table: "RecepcionItems");

            migrationBuilder.DropColumn(
                name: "NumeroFactura",
                table: "RecepcionesMaterial");

            migrationBuilder.DropColumn(
                name: "DecididoEn",
                table: "Presupuestos");

            migrationBuilder.DropColumn(
                name: "DetallePerdida",
                table: "Presupuestos");

            migrationBuilder.DropColumn(
                name: "MotivoPerdida",
                table: "Presupuestos");

            migrationBuilder.DropColumn(
                name: "ValorHora",
                table: "JornadasAlbanil");

            migrationBuilder.DropColumn(
                name: "Canal",
                table: "Consultas");

            migrationBuilder.DropColumn(
                name: "ContactadaEn",
                table: "Consultas");

            migrationBuilder.DropColumn(
                name: "MotivoDescarte",
                table: "Consultas");

            migrationBuilder.DropColumn(
                name: "PaginaEntrada",
                table: "Consultas");

            migrationBuilder.DropColumn(
                name: "PublicacionId",
                table: "Consultas");

            migrationBuilder.DropColumn(
                name: "Referente",
                table: "Consultas");

            migrationBuilder.DropColumn(
                name: "UtmCampania",
                table: "Consultas");

            migrationBuilder.DropColumn(
                name: "UtmFuente",
                table: "Consultas");

            migrationBuilder.DropColumn(
                name: "UtmMedio",
                table: "Consultas");

            migrationBuilder.DropColumn(
                name: "ValorHora",
                table: "Albaniles");
        }
    }
}
