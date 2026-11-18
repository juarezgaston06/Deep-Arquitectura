using DeepArquitectura.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeepArquitectura.Web.Data;

public partial class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Consulta> Consultas => Set<Consulta>();
    public DbSet<Obra> Obras => Set<Obra>();
    public DbSet<EtapaObra> Etapas => Set<EtapaObra>();
    public DbSet<RegistroAvance> Registros => Set<RegistroAvance>();
    public DbSet<Pago> Pagos => Set<Pago>();
    public DbSet<Publicacion> Publicaciones => Set<Publicacion>();
    public DbSet<ArancelVersion> Aranceles => Set<ArancelVersion>();
    public DbSet<Presupuesto> Presupuestos => Set<Presupuesto>();
    public DbSet<PresupuestoItem> PresupuestoItems => Set<PresupuestoItem>();
    public DbSet<CredencialAdmin> Credenciales => Set<CredencialAdmin>();
    public DbSet<Capataz> Capataces => Set<Capataz>();
    public DbSet<CapatazObra> CapatazObras => Set<CapatazObra>();
    public DbSet<SesionCapataz> SesionesCapataz => Set<SesionCapataz>();
    public DbSet<MedioRegistro> Medios => Set<MedioRegistro>();
    public DbSet<Albanil> Albaniles => Set<Albanil>();
    public DbSet<AlbanilObra> AlbanilObras => Set<AlbanilObra>();
    public DbSet<SesionAlbanil> SesionesAlbanil => Set<SesionAlbanil>();
    public DbSet<TareaObra> Tareas => Set<TareaObra>();
    public DbSet<JornadaAlbanil> Jornadas => Set<JornadaAlbanil>();
    public DbSet<PedidoMaterial> Pedidos => Set<PedidoMaterial>();
    public DbSet<PedidoMaterialItem> PedidoItems => Set<PedidoMaterialItem>();
    public DbSet<ProblemaObra> Problemas => Set<ProblemaObra>();
    public DbSet<ParteDiario> Partes => Set<ParteDiario>();
    public DbSet<ParteDiarioItem> ParteItems => Set<ParteDiarioItem>();
    public DbSet<RecepcionMaterial> Recepciones => Set<RecepcionMaterial>();
    public DbSet<RecepcionItem> RecepcionItems => Set<RecepcionItem>();
    public DbSet<ControlEtapa> Controles => Set<ControlEtapa>();
    public DbSet<RespuestaControl> RespuestasControl => Set<RespuestaControl>();
    public DbSet<AsientoLibro> Asientos => Set<AsientoLibro>();
    public DbSet<LecturaAsiento> Lecturas => Set<LecturaAsiento>();
    public DbSet<ClimaDiario> Climas => Set<ClimaDiario>();
    public DbSet<PuntoFoto> PuntosFoto => Set<PuntoFoto>();
    public DbSet<Certificado> Certificados => Set<Certificado>();
    public DbSet<AccesoCliente> AccesosCliente => Set<AccesoCliente>();
    public DbSet<DocumentoObra> Documentos => Set<DocumentoObra>();
    public DbSet<MensajeObra> Mensajes => Set<MensajeObra>();
    public DbSet<AprobacionCliente> Aprobaciones => Set<AprobacionCliente>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Enums como texto: la base queda legible ("EnCurso" en vez de 1) y agregar valores no rompe datos.
        b.Properties<Enum>().HaveConversion<string>().HaveMaxLength(40);
        b.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<CredencialAdmin>(e =>
        {
            e.ToTable("CredencialesAdmin");
            e.Property(x => x.Usuario).HasMaxLength(100).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.Property(x => x.Sello).HasMaxLength(64).IsRequired();
        });

        m.Entity<Cliente>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Telefono).HasMaxLength(50);
            e.Property(x => x.DniCuit).HasMaxLength(20);
            e.Property(x => x.Direccion).HasMaxLength(300);
            e.Property(x => x.Localidad).HasMaxLength(120);
            e.Property(x => x.Notas).HasMaxLength(4000);
            e.HasIndex(x => x.Email);
            e.HasIndex(x => x.Telefono);
            e.HasIndex(x => x.Nombre);
        });

        m.Entity<Consulta>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.Telefono).HasMaxLength(50);
            e.Property(x => x.Mensaje).HasMaxLength(4000).IsRequired();
            e.HasIndex(x => new { x.Estado, x.CreadoEn });
            e.HasOne(x => x.Cliente).WithMany(c => c.Consultas)
                .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.SetNull);
        });

        m.Entity<Obra>(e =>
        {
            e.Property(x => x.Codigo).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            e.Property(x => x.Direccion).HasMaxLength(300);
            e.Property(x => x.Localidad).HasMaxLength(120);
            e.Property(x => x.ExpedienteMunicipal).HasMaxLength(80);
            e.Property(x => x.Descripcion).HasMaxLength(4000);
            e.HasIndex(x => x.Estado);
            e.Property(x => x.TokenPortal).HasMaxLength(64);
            e.HasIndex(x => x.TokenPortal).IsUnique().HasFilter("[TokenPortal] IS NOT NULL");
            e.Ignore(x => x.PorcentajeAvance);
            // Un cliente con obras no se puede borrar por accidente.
            e.HasOne(x => x.Cliente).WithMany(c => c.Obras)
                .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<EtapaObra>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.Property(x => x.Notas).HasMaxLength(2000);
            e.HasIndex(x => new { x.ObraId, x.Orden });
            e.HasOne(x => x.Obra).WithMany(o => o.Etapas)
                .HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<RegistroAvance>(e =>
        {
            e.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(4000);
            e.HasIndex(x => new { x.ObraId, x.Fecha });
            e.HasIndex(x => new { x.UsableEnRedes, x.Fecha });
            e.HasOne(x => x.Obra).WithMany(o => o.Registros)
                .HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            // SQL Server no admite dos caminos de cascada (Obra→Etapa→Registro y Obra→Registro).
            e.HasOne(x => x.Etapa).WithMany()
                .HasForeignKey(x => x.EtapaObraId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Capataz).WithMany()
                .HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Albanil).WithMany()
                .HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Tarea).WithMany()
                .HasForeignKey(x => x.TareaObraId).OnDelete(DeleteBehavior.ClientSetNull);
            e.Property(x => x.Cantidad).HasPrecision(12, 2);
            e.Property(x => x.CantidadInformada).HasPrecision(12, 2);
            e.Property(x => x.MotivoValidacion).HasMaxLength(500);
            e.HasIndex(x => new { x.Validacion, x.ObraId });
            e.HasOne(x => x.ValidadoPorCapataz).WithMany()
                .HasForeignKey(x => x.ValidadoPorCapatazId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.PuntoFoto).WithMany()
                .HasForeignKey(x => x.PuntoFotoId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasIndex(x => x.Ref).IsUnique().HasFilter("[Ref] IS NOT NULL");
        });

        // ---- Circuito de datos de obra ----
        m.Entity<RecepcionMaterial>(e =>
        {
            e.ToTable("RecepcionesMaterial");
            e.Property(x => x.Proveedor).HasMaxLength(150);
            e.Property(x => x.NumeroRemito).HasMaxLength(50);
            e.Property(x => x.Nota).HasMaxLength(1000);
            e.HasIndex(x => x.Ref).IsUnique();
            e.HasIndex(x => new { x.ObraId, x.Fecha });
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Pedido).WithMany().HasForeignKey(x => x.PedidoMaterialId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Albanil).WithMany().HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Capataz).WithMany().HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Registro).WithMany().HasForeignKey(x => x.RegistroAvanceId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<RecepcionItem>(e =>
        {
            e.ToTable("RecepcionItems");
            e.Property(x => x.Material).HasMaxLength(150).IsRequired();
            e.Property(x => x.Unidad).HasMaxLength(20).IsRequired();
            e.Property(x => x.Cantidad).HasPrecision(12, 2);
            e.HasOne(x => x.Recepcion).WithMany(r => r.Items).HasForeignKey(x => x.RecepcionMaterialId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PedidoItem).WithMany().HasForeignKey(x => x.PedidoMaterialItemId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<ControlEtapa>(e =>
        {
            e.ToTable("ControlesEtapa");
            e.Property(x => x.Texto).HasMaxLength(300).IsRequired();
            e.HasIndex(x => new { x.EtapaObraId, x.Orden });
            e.HasOne(x => x.Etapa).WithMany().HasForeignKey(x => x.EtapaObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<RespuestaControl>(e =>
        {
            e.ToTable("RespuestasControl");
            e.Property(x => x.Nota).HasMaxLength(1000);
            e.HasIndex(x => x.Ref).IsUnique();
            e.HasOne(x => x.Control).WithMany(c => c.Respuestas).HasForeignKey(x => x.ControlEtapaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Albanil).WithMany().HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Capataz).WithMany().HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Registro).WithMany().HasForeignKey(x => x.RegistroAvanceId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<AsientoLibro>(e =>
        {
            e.ToTable("LibroObra");
            e.Property(x => x.Texto).HasMaxLength(4000).IsRequired();
            e.HasIndex(x => new { x.ObraId, x.Tipo, x.Numero }).IsUnique();
            e.HasIndex(x => x.Ref).IsUnique().HasFilter("[Ref] IS NOT NULL");
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Capataz).WithMany().HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Albanil).WithMany().HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Registro).WithMany().HasForeignKey(x => x.RegistroAvanceId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.RespondeA).WithMany().HasForeignKey(x => x.RespondeAId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<LecturaAsiento>(e =>
        {
            e.ToTable("LecturasLibro");
            e.HasIndex(x => new { x.AsientoLibroId, x.CapatazId }).IsUnique().HasFilter("[CapatazId] IS NOT NULL");
            e.HasIndex(x => new { x.AsientoLibroId, x.AlbanilId }).IsUnique().HasFilter("[AlbanilId] IS NOT NULL");
            e.HasOne(x => x.Asiento).WithMany(a => a.Lecturas).HasForeignKey(x => x.AsientoLibroId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Capataz).WithMany().HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Albanil).WithMany().HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<ClimaDiario>(e =>
        {
            e.ToTable("ClimaDiario");
            e.HasIndex(x => new { x.ObraId, x.Fecha }).IsUnique();
            e.Ignore(x => x.Desfavorable);
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<PuntoFoto>(e =>
        {
            e.ToTable("PuntosFoto");
            e.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
            e.Property(x => x.Indicacion).HasMaxLength(500);
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<Certificado>(e =>
        {
            e.ToTable("Certificados");
            e.Property(x => x.PorcentajeAnterior).HasPrecision(7, 3);
            e.Property(x => x.PorcentajeAcumulado).HasPrecision(7, 3);
            e.Property(x => x.Detalle).IsRequired();
            e.Property(x => x.Notas).HasMaxLength(2000);
            e.HasIndex(x => new { x.ObraId, x.Numero }).IsUnique();
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Pago).WithMany().HasForeignKey(x => x.PagoId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        // ---- App del cliente ----
        m.Entity<AccesoCliente>(e =>
        {
            e.ToTable("AccesosCliente");
            e.Property(x => x.Codigo).HasMaxLength(20).IsRequired();
            e.Property(x => x.PinHash).HasMaxLength(500).IsRequired();
            e.Property(x => x.Sello).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Codigo).IsUnique();
            e.HasIndex(x => x.ObraId).IsUnique();
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<DocumentoObra>(e =>
        {
            e.ToTable("DocumentosObra");
            e.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
            e.Property(x => x.Ruta).HasMaxLength(400).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.NombreOriginal).HasMaxLength(260);
            e.HasIndex(x => new { x.ObraId, x.Categoria });
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<MensajeObra>(e =>
        {
            e.ToTable("MensajesObra");
            e.Property(x => x.Autor).HasMaxLength(100);
            e.Property(x => x.Texto).HasMaxLength(4000).IsRequired();
            e.HasIndex(x => new { x.ObraId, x.Fecha });
            e.HasIndex(x => x.Ref).IsUnique().HasFilter("[Ref] IS NOT NULL");
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<AprobacionCliente>(e =>
        {
            e.ToTable("AprobacionesCliente");
            e.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(4000);
            e.Property(x => x.Opciones).HasMaxLength(2000);
            e.Property(x => x.OpcionElegida).HasMaxLength(200);
            e.Property(x => x.Comentario).HasMaxLength(2000);
            e.Property(x => x.RespondidoPor).HasMaxLength(100);
            e.Property(x => x.RespondidoDesdeIp).HasMaxLength(64);
            e.Ignore(x => x.ListaOpciones);
            e.HasIndex(x => new { x.ObraId, x.Estado });
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Documento).WithMany().HasForeignKey(x => x.DocumentoObraId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        // ---- App de albañiles ----
        // Regla para SQL Server: solo la obra borra en cascada (y la cuadrilla, sus sesiones y asignaciones).
        // El resto de las relaciones no cascadea: no se borran albañiles ni tareas con historia (se desactivan / cancelan).
        m.Entity<Albanil>(e =>
        {
            e.ToTable("Albaniles");
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.Property(x => x.Usuario).HasMaxLength(60).IsRequired();
            e.Property(x => x.Telefono).HasMaxLength(50);
            e.Property(x => x.Oficio).HasMaxLength(60);
            e.Property(x => x.Empresa).HasMaxLength(150);
            e.Property(x => x.PinHash).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.Usuario).IsUnique();
            e.HasOne(x => x.Capataz).WithMany().HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<AlbanilObra>(e =>
        {
            e.ToTable("AlbanilObras");
            e.HasKey(x => new { x.AlbanilId, x.ObraId });
            e.HasOne(x => x.Albanil).WithMany(a => a.Obras).HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<SesionAlbanil>(e =>
        {
            e.ToTable("SesionesAlbanil");
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Dispositivo).HasMaxLength(120);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.Albanil).WithMany(a => a.Sesiones).HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<TareaObra>(e =>
        {
            e.ToTable("TareasObra");
            e.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
            e.Property(x => x.Descripcion).HasMaxLength(2000);
            e.Property(x => x.Unidad).HasMaxLength(20);
            e.Property(x => x.Oficio).HasMaxLength(60);
            e.Property(x => x.Rubro).HasMaxLength(80);
            e.Property(x => x.CantidadObjetivo).HasPrecision(12, 2);
            e.Property(x => x.CantidadHecha).HasPrecision(12, 2);
            e.Property(x => x.CantidadAprobada).HasPrecision(12, 2);
            e.Property(x => x.Peso).HasPrecision(9, 3);
            e.HasIndex(x => new { x.ObraId, x.Estado });
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Etapa).WithMany().HasForeignKey(x => x.EtapaObraId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Albanil).WithMany().HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<JornadaAlbanil>(e =>
        {
            e.ToTable("JornadasAlbanil");
            e.HasIndex(x => x.Ref).IsUnique();
            e.HasIndex(x => new { x.AlbanilId, x.Entrada });
            e.HasIndex(x => new { x.ObraId, x.Entrada });
            e.Ignore(x => x.Horas);
            e.Property(x => x.EppFaltante).HasMaxLength(300);
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Albanil).WithMany().HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Capataz).WithMany().HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<PedidoMaterial>(e =>
        {
            e.ToTable("PedidosMaterial");
            e.Property(x => x.Nota).HasMaxLength(1000);
            e.Property(x => x.Respuesta).HasMaxLength(1000);
            e.HasIndex(x => x.Ref).IsUnique();
            e.HasIndex(x => new { x.Estado, x.Fecha });
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Albanil).WithMany().HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Capataz).WithMany().HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Tarea).WithMany().HasForeignKey(x => x.TareaObraId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<PedidoMaterialItem>(e =>
        {
            e.ToTable("PedidoMaterialItems");
            e.Property(x => x.Material).HasMaxLength(150).IsRequired();
            e.Property(x => x.Unidad).HasMaxLength(20).IsRequired();
            e.Property(x => x.Cantidad).HasPrecision(12, 2);
            e.HasOne(x => x.Pedido).WithMany(p => p.Items).HasForeignKey(x => x.PedidoMaterialId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<ProblemaObra>(e =>
        {
            e.ToTable("ProblemasObra");
            e.Property(x => x.Descripcion).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Respuesta).HasMaxLength(1000);
            e.HasIndex(x => x.Ref).IsUnique();
            e.HasIndex(x => new { x.Estado, x.Fecha });
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Albanil).WithMany().HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Capataz).WithMany().HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Tarea).WithMany().HasForeignKey(x => x.TareaObraId).OnDelete(DeleteBehavior.ClientSetNull);
            e.HasOne(x => x.Registro).WithMany().HasForeignKey(x => x.RegistroAvanceId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<ParteDiario>(e =>
        {
            e.ToTable("PartesDiarios");
            e.Property(x => x.Resumen).HasMaxLength(2000);
            e.Property(x => x.HorasTotales).HasPrecision(5, 2);
            e.HasIndex(x => new { x.AlbanilId, x.ObraId, x.Fecha }).IsUnique();
            e.HasIndex(x => new { x.ObraId, x.Fecha });
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Albanil).WithMany().HasForeignKey(x => x.AlbanilId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Capataz).WithMany().HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<ParteDiarioItem>(e =>
        {
            e.ToTable("ParteDiarioItems");
            e.Property(x => x.Horas).HasPrecision(5, 2);
            e.Property(x => x.Detalle).HasMaxLength(300);
            e.HasOne(x => x.Parte).WithMany(p => p.Items).HasForeignKey(x => x.ParteDiarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Tarea).WithMany().HasForeignKey(x => x.TareaObraId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        m.Entity<MedioRegistro>(e =>
        {
            e.ToTable("MediosRegistro");
            e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Ruta).HasMaxLength(400).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.NombreOriginal).HasMaxLength(260);
            e.HasIndex(x => x.Ref).IsUnique();
            e.HasOne(x => x.Registro).WithMany(r => r.Medios)
                .HasForeignKey(x => x.RegistroAvanceId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<Capataz>(e =>
        {
            e.ToTable("Capataces");
            e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
            e.Property(x => x.Usuario).HasMaxLength(60).IsRequired();
            e.Property(x => x.Telefono).HasMaxLength(50);
            e.Property(x => x.PinHash).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.Usuario).IsUnique();
        });

        m.Entity<CapatazObra>(e =>
        {
            e.ToTable("CapatazObras");
            e.HasKey(x => new { x.CapatazId, x.ObraId });
            e.HasOne(x => x.Capataz).WithMany(c => c.Obras).HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Obra).WithMany().HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<SesionCapataz>(e =>
        {
            e.ToTable("SesionesCapataz");
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Dispositivo).HasMaxLength(120);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.Capataz).WithMany(c => c.Sesiones).HasForeignKey(x => x.CapatazId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<Pago>(e =>
        {
            e.Property(x => x.Concepto).HasMaxLength(200);
            e.HasOne(x => x.Obra).WithMany(o => o.Pagos)
                .HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.Cascade);
        });

        m.Entity<Publicacion>(e =>
        {
            e.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
            e.Property(x => x.Contenido).IsRequired();
            e.HasIndex(x => new { x.Estado, x.FechaProgramada });
            e.HasOne(x => x.Obra).WithMany()
                .HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.SetNull);
        });

        m.Entity<ArancelVersion>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
            e.Property(x => x.Contenido).IsRequired();
            e.Property(x => x.Notas).HasMaxLength(2000);
            e.HasIndex(x => x.VigenteDesde);
        });

        m.Entity<Presupuesto>(e =>
        {
            e.Property(x => x.Numero).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Numero).IsUnique();
            e.Property(x => x.Titulo).HasMaxLength(200).IsRequired();
            e.Property(x => x.Destinatario).HasMaxLength(200);
            e.Property(x => x.Ubicacion).HasMaxLength(300);
            e.Property(x => x.Notas).HasMaxLength(4000);
            e.Property(x => x.Parametros).IsRequired();
            e.Property(x => x.SuperficieTotal).HasPrecision(12, 2);
            e.HasIndex(x => new { x.Estado, x.Fecha });
            e.Ignore(x => x.VenceEl);
            e.Ignore(x => x.NombreDestinatario);
            e.HasOne(x => x.Cliente).WithMany()
                .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Obra).WithMany()
                .HasForeignKey(x => x.ObraId).OnDelete(DeleteBehavior.SetNull);
            // Una versión de arancel usada por presupuestos no se puede borrar.
            e.HasOne(x => x.ArancelVersion).WithMany()
                .HasForeignKey(x => x.ArancelVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<PresupuestoItem>(e =>
        {
            e.Property(x => x.Seccion).HasMaxLength(60).IsRequired();
            e.Property(x => x.Concepto).HasMaxLength(200).IsRequired();
            e.Property(x => x.Detalle).HasMaxLength(500);
            e.HasIndex(x => new { x.PresupuestoId, x.Orden });
            e.HasOne(x => x.Presupuesto).WithMany(p => p.Items)
                .HasForeignKey(x => x.PresupuestoId).OnDelete(DeleteBehavior.Cascade);
        });

        ConfigurarDatosProceso(m);
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            switch (entry.Entity)
            {
                case Cliente c:
                    if (entry.State == EntityState.Added) c.CreadoEn = ahora;
                    c.ActualizadoEn = ahora;
                    break;
                case Obra o:
                    if (entry.State == EntityState.Added) o.CreadoEn = ahora;
                    o.ActualizadoEn = ahora;
                    break;
                case Consulta q when entry.State == EntityState.Added:
                    q.CreadoEn = ahora;
                    break;
                case Publicacion p when entry.State == EntityState.Added:
                    p.CreadoEn = ahora;
                    break;
                case ArancelVersion a:
                    if (entry.State == EntityState.Added) a.CreadoEn = ahora;
                    a.ActualizadoEn = ahora;
                    break;
                case Capataz cap when entry.State == EntityState.Added:
                    cap.CreadoEn = ahora;
                    break;
                case CapatazObra co when entry.State == EntityState.Added:
                    co.AsignadoEn = ahora;
                    break;
                case MedioRegistro me when entry.State == EntityState.Added:
                    me.CreadoEn = ahora;
                    break;
                case Albanil al when entry.State == EntityState.Added:
                    al.CreadoEn = ahora;
                    break;
                case AlbanilObra ao when entry.State == EntityState.Added:
                    ao.AsignadoEn = ahora;
                    break;
                case TareaObra t:
                    if (entry.State == EntityState.Added) t.CreadoEn = ahora;
                    t.ActualizadoEn = ahora;
                    break;
                case PedidoMaterial pm:
                    pm.ActualizadoEn = ahora;
                    break;
                case Certificado ce when entry.State == EntityState.Added:
                    ce.CreadoEn = ahora;
                    break;
                case AccesoCliente ac when entry.State == EntityState.Added:
                    ac.CreadoEn = ahora;
                    break;
                case DocumentoObra d when entry.State == EntityState.Added:
                    d.CreadoEn = ahora;
                    break;
                case AprobacionCliente ap when entry.State == EntityState.Added:
                    ap.CreadoEn = ahora;
                    break;
                case ParteDiario pd:
                    if (entry.State == EntityState.Added) pd.CreadoEn = ahora;
                    pd.ActualizadoEn = ahora;
                    break;
                case Presupuesto pr:
                    if (entry.State == EntityState.Added) pr.CreadoEn = ahora;
                    pr.ActualizadoEn = ahora;
                    break;
                case Relevamiento rl:
                    if (entry.State == EntityState.Added) rl.CreadoEn = ahora;
                    rl.ActualizadoEn = ahora;
                    break;
                case Adjunto ad when entry.State == EntityState.Added:
                    ad.CreadoEn = ahora;
                    break;
                case GastoObra g when entry.State == EntityState.Added:
                    g.CreadoEn = ahora;
                    break;
                case MaterialSobrante ms when entry.State == EntityState.Added:
                    ms.CreadoEn = ahora;
                    break;
                case TramiteObra tr when entry.State == EntityState.Added:
                    tr.CreadoEn = ahora;
                    break;
                case MovimientoTramite mt when entry.State == EntityState.Added:
                    mt.CreadoEn = ahora;
                    break;
                case ActaEntrega ae when entry.State == EntityState.Added:
                    ae.CreadoEn = ahora;
                    break;
                case ObservacionEntrega oe when entry.State == EntityState.Added:
                    oe.CreadoEn = ahora;
                    break;
                case PermisoDifusion pd2:
                    pd2.ActualizadoEn = ahora;
                    break;
            }
        }
        return base.SaveChangesAsync(ct);
    }
}
